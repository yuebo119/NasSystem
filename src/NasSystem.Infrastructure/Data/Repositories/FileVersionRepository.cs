// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/FileVersionRepository.cs
// 文件版本仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>文件版本仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class FileVersionRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IFileVersionRepository
{
    public Task AddAsync(FileVersion version, CancellationToken cancellationToken = default)
    {
        var row = version.ToRow();
        unitOfWork.EnqueueInsert(version, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Update(FileVersion version)
    {
        var row = version.ToRow();
        unitOfWork.EnqueueUpdate(version, async (db, ct) => await db.UpdateAsync(row, ct));
    }

    public void Delete(FileVersion version)
    {
        var id = version.Id;
        unitOfWork.EnqueueDelete(version, async (db, ct) => await db.DeleteAsync<FileVersionRow>(id, ct));
    }

    public async Task<IReadOnlyList<FileVersion>> GetByFileItemIdAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<FileVersionRow>()
            .Where($"FileItemId = {fileItemId}")
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

        return await AttachCreatorsAsync(db, rows, cancellationToken);
    }

    public async Task<FileVersion?> GetCurrentVersionAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileVersionRow>()
            .Where($"FileItemId = {fileItemId} AND IsCurrent = 1")
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var creator = await db.GetAsync<UserRow>(row.CreatedByUserId, cancellationToken);
        return row.ToDomain(createdByUser: creator?.ToDomain());
    }

    public async Task<FileVersion?> GetByVersionNumberAsync(
        Guid fileItemId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileVersionRow>()
            .Where($"FileItemId = {fileItemId} AND VersionNumber = {versionNumber}")
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var creator = await db.GetAsync<UserRow>(row.CreatedByUserId, cancellationToken);
        return row.ToDomain(createdByUser: creator?.ToDomain());
    }

    public async Task<int> GetLatestVersionNumberAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        // 不用 MaxAsync<T,TValue>：它的 FormattableString 参数是聚合表达式本身，
        // 生成 `SELECT MAX(表达式) FROM 表`，没有 WHERE 通道——把 `FileItemId = {id}`
        // 传进去会算出 `MAX(FileItemId = @p0)`，返回值 1 或 0 而不是最大版本号
        // （实测：建 3 个版本，旧写法返回 1）。这里手写完整 SQL，语义与 EF 时代一致。
        // int? 承接空集合的 NULL，再 `?? 0` 与 EF 时代 (int?) + ?? 0 等价
        var maxVersion = await db.ScalarAsync<int?>(
            $"SELECT MAX(VersionNumber) FROM FileVersions WHERE FileItemId = {fileItemId}",
            false, cancellationToken);
        return maxVersion ?? 0;
    }

    /// <summary>
    /// 批量挂创建者。不逐行查：那会触发 PALORM005 的 N+1 告警，
    /// 先收集 Id 再一次 IN 查回（2 条查询）。
    /// </summary>
    private static async Task<IReadOnlyList<FileVersion>> AttachCreatorsAsync(
        DataSession<SqliteProvider> db, List<FileVersionRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];

        var creators = await FileShareRepository.LoadUsersByIdsAsync(
            db, rows.Select(r => r.CreatedByUserId).Distinct(), cancellationToken);

        return rows
            .Select(r => r.ToDomain(
                createdByUser: creators.TryGetValue(r.CreatedByUserId, out var creator) ? creator : null))
            .ToList();
    }
}
