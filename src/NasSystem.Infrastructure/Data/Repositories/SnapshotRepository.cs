// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/SnapshotRepository.cs
// 目录快照仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>目录快照仓储实现。查询形态说明见 <see cref="FileItemRepository"/>的remarks。</summary>
/// <remarks>
/// 条目写入全部走 BulkInsert（一批 500 行多值 INSERT，经意图队列与
/// 快照头同事务）：一个快照可达数万条目，逐条 Insert 的事务开销
/// 随条目数线性放大。diff 在 SQL 层按 RelativePath 对齐两个快照的
/// 条目集合（SQLite 3.39+ 的 FULL OUTER JOIN，本引擎 3.54 可用），
/// 不在内存里做全量比对。
/// </remarks>
public sealed class SnapshotRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : ISnapshotRepository
{
    /// <summary>批量插入的每批行数。PalORM 的 BulkInsertAsync 按此分多值 INSERT 语句</summary>
    private const int BulkBatchSize = 500;

    public Task AddAsync(DirectorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var row = snapshot.ToRow();
        unitOfWork.EnqueueInsert(snapshot, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Update(DirectorySnapshot snapshot)
    {
        var row = snapshot.ToRow();
        unitOfWork.EnqueueUpdate(snapshot, async (db, ct) => await db.UpdateAsync(row, ct));
    }

    public void Delete(DirectorySnapshot snapshot)
    {
        var id = snapshot.Id;
        // 条目随快照头同事务删（无外键级联，见 SnapshotEntryRow remarks）
        unitOfWork.EnqueueCommand(async (db, ct) =>
        {
            await db.ExecuteAsync(
                System.Runtime.CompilerServices.FormattableStringFactory.Create(
                    "DELETE FROM SnapshotEntries WHERE SnapshotId = {0}", id), ct);
            await db.DeleteAsync<DirectorySnapshotRow>(id, ct);
        });
    }

    public async Task<DirectorySnapshot?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<DirectorySnapshotRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<(IReadOnlyList<DirectorySnapshot> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var total = (int)await db.CountAsync<DirectorySnapshotRow>(null, cancellationToken);
        if (total == 0)
            return ([], 0);

        var rows = await db.From<DirectorySnapshotRow>()
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (rows.Select(r => r.ToDomain()).ToList(), total);
    }

    public async Task<IReadOnlyList<DirectorySnapshot>> GetOldestAsync(
        int keepCount, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // SQL 侧切片：先 COUNT 再 LIMIT，不再全表物化后内存 Take。
        // 原实现每轮保留都把 DirectorySnapshots 全表读进内存，
        // 行数随快照总量线性增长（2026-10-21 审计 P2）
        var total = (int)await db.CountAsync<DirectorySnapshotRow>(null, cancellationToken);
        // keepCount 之外的（最旧的）才是淘汰对象；keepCount<=0 时全删
        if (keepCount >= total)
            return [];

        var rows = await db.From<DirectorySnapshotRow>()
            .OrderBy(s => s.CreatedAt)
            .Take(total - keepCount)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public Task AddEntriesAsync(
        IReadOnlyList<SnapshotEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return Task.CompletedTask;

        var rows = entries.Select(e => e.ToRow()).ToList();
        unitOfWork.EnqueueCommand(async (db, ct) =>
            await db.BulkInsertAsync(rows, BulkBatchSize, ct));
        return Task.CompletedTask;
    }

    public async Task<(IReadOnlyList<SnapshotEntry> Items, int TotalCount)> GetEntriesPagedAsync(
        Guid snapshotId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var total = (int)await db.CountAsync<SnapshotEntryRow>(
            $"SnapshotId = {snapshotId}", cancellationToken);
        if (total == 0)
            return ([], 0);

        var rows = await db.From<SnapshotEntryRow>()
            .Where($"SnapshotId = {snapshotId}")
            .OrderBy(e => e.RelativePath)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (rows.Select(r => r.ToDomain()).ToList(), total);
    }

    public async Task<int> CountEntriesAsync(
        Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        return (int)await db.CountAsync<SnapshotEntryRow>(
            $"SnapshotId = {snapshotId}", cancellationToken);
    }

    public async Task<SnapshotDiffResult> DiffAsync(
        Guid baseSnapshotId, Guid targetSnapshotId,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 分类计数与分页数据同一口径。两侧先按 SnapshotId 过滤成子查询再
        // FULL OUTER JOIN（SQLite 3.39+ 支持；本引擎 3.54）：不过滤的话
        // JOIN 要拿全表条目两两配对，大表上慢一个量级。
        // 四条 SQL 各自完整，且参数一律走"普通字符串 {n} 占位 + args"形态：
        // PalORM 的参数化只认这个构造路径。$"" 内插 Guid 实测会被
        // ToString 拼进 SQL（unrecognized token），必须用
        // FormattableStringFactory.Create("...{0}", guid)（2026-10-18 实测）。
        // COALESCE 兜任一 side 缺失；Kind：0=Added（仅新）/ 1=Removed（仅旧）/
        // 2=两边都有（是否"修改"由调用方按 hash 与大小判定）
        var total = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) " +
                "FROM (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {0}) b " +
                "FULL OUTER JOIN (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {1}) t " +
                "ON b.RelativePath = t.RelativePath",
                baseSnapshotId, targetSnapshotId), false, cancellationToken);
        if (total == 0)
            return new SnapshotDiffResult([], 0, 0, 0, 0, 0);

        var added = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) " +
                "FROM (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {0}) b " +
                "FULL OUTER JOIN (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {1}) t " +
                "ON b.RelativePath = t.RelativePath " +
                "WHERE b.RelativePath IS NULL",
                baseSnapshotId, targetSnapshotId), false, cancellationToken);
        var removed = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) " +
                "FROM (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {0}) b " +
                "FULL OUTER JOIN (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {1}) t " +
                "ON b.RelativePath = t.RelativePath " +
                "WHERE t.RelativePath IS NULL",
                baseSnapshotId, targetSnapshotId), false, cancellationToken);
        var modified = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) " +
                "FROM (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {0}) b " +
                "FULL OUTER JOIN (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {1}) t " +
                "ON b.RelativePath = t.RelativePath " +
                "WHERE b.RelativePath IS NOT NULL AND t.RelativePath IS NOT NULL " +
                "AND (COALESCE(b.ContentHash, '') <> COALESCE(t.ContentHash, '') OR b.SizeBytes <> t.SizeBytes)",
                baseSnapshotId, targetSnapshotId), false, cancellationToken);

        var rows = await db.QueryAsync<SnapshotDiffProjectionRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COALESCE(t.RelativePath, b.RelativePath) AS RelativePath, " +
                "COALESCE(t.IsFolder, b.IsFolder) AS IsFolder, " +
                "CASE WHEN b.RelativePath IS NULL THEN 0 WHEN t.RelativePath IS NULL THEN 1 " +
                "WHEN COALESCE(b.ContentHash, '') <> COALESCE(t.ContentHash, '') OR b.SizeBytes <> t.SizeBytes THEN 2 " +
                "ELSE 3 END AS Kind, " +
                "COALESCE(b.SizeBytes, 0) AS BaseSizeBytes, " +
                "COALESCE(t.SizeBytes, 0) AS TargetSizeBytes, " +
                "b.ContentHash AS BaseHash, t.ContentHash AS TargetHash " +
                "FROM (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {0}) b " +
                "FULL OUTER JOIN (SELECT RelativePath, IsFolder, SizeBytes, ContentHash FROM SnapshotEntries WHERE SnapshotId = {1}) t " +
                "ON b.RelativePath = t.RelativePath " +
                "ORDER BY COALESCE(t.RelativePath, b.RelativePath) " +
                "LIMIT {2} OFFSET {3}",
                baseSnapshotId, targetSnapshotId, pageSize, (page - 1) * pageSize), false, cancellationToken);

        var items = rows.Select(r => new SnapshotDiffRow(
            r.RelativePath,
            r.IsFolder,
            (SnapshotDiffKind)r.Kind,
            r.BaseSizeBytes,
            r.TargetSizeBytes,
            string.IsNullOrEmpty(r.BaseHash) ? null : r.BaseHash,
            string.IsNullOrEmpty(r.TargetHash) ? null : r.TargetHash)).ToList();

        return new SnapshotDiffResult(
            items, (int)total, (int)added, (int)removed, (int)modified,
            (int)total - (int)added - (int)removed - (int)modified);
    }
}
