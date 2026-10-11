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
        // TValue 用 int?：空集合时 MAX 返回 NULL，与 EF 时代 (int?) + ?? 0 的语义一致
        var maxVersion = await db.MaxAsync<FileVersionRow, int?>(
            $"FileItemId = {fileItemId}", cancellationToken);
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
