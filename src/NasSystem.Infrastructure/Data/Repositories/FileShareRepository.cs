// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/FileShareRepository.cs
// 文件分享仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>文件分享仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class FileShareRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IFileShareRepository
{
    public async Task<FileShare?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
    {
        // 公开分享信息/密码校验/目录浏览都是只读路径；
        // 下载计数走显式 UpdateAsync
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileShareRow>()
            .Where($"ShareCode = {shareCode}")
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        // EF 时代的两个 Include：调用方映射 DTO 需要文件项与创建者显示名。
        // 影子模型没有导航属性，改用两次按 Id 直查（单行路径共 3 条查询）
        var fileItem = await db.GetAsync<FileItemRow>(row.FileItemId, cancellationToken);
        var creator = await db.GetAsync<UserRow>(row.CreatedByUserId, cancellationToken);

        return row.ToDomain(fileItem?.ToDomain(), creator?.ToDomain());
    }

    public async Task<IReadOnlyList<FileShare>> GetByCreatorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // 同时取文件项与创建者：调用方映射 DTO 需要两者。
        // 不逐行 GetAsync：那会触发 PALORM005 的 N+1 告警，
        // 改为先收集 Id 再一次 IN 查回（3 条查询，与 EF 的单条 JOIN 同数量级）
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<FileShareRow>()
            .Where($"CreatedByUserId = {userId}")
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var items = await LoadFileItemsByIdsAsync(db, rows.Select(r => r.FileItemId).Distinct(), cancellationToken);
        var creators = await LoadUsersByIdsAsync(db, rows.Select(r => r.CreatedByUserId).Distinct(), cancellationToken);

        return rows
            .Select(r => r.ToDomain(
                items.TryGetValue(r.FileItemId, out var item) ? item : null,
                creators.TryGetValue(r.CreatedByUserId, out var creator) ? creator : null))
            .ToList();
    }

    public async Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<FileShareRow>($"ShareCode = {shareCode}", cancellationToken);
        return count > 0;
    }

    /// <summary>按 Id 集合批量取文件项，避免逐行查询。</summary>
    internal static async Task<Dictionary<Guid, FileItem>> LoadFileItemsByIdsAsync(
        DataSession<SqliteProvider> db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        var placeholders = string.Join(", ", idList.Select((_, i) => $"{{{i}}}"));
        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"SELECT * FROM FileItems WHERE Id IN ({placeholders})",
                idList.Cast<object>().ToArray()),
            false,
            cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.ToDomain());
    }

    /// <summary>按 Id 集合批量取用户，避免逐行查询。</summary>
    internal static async Task<Dictionary<Guid, User>> LoadUsersByIdsAsync(
        DataSession<SqliteProvider> db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return [];

        var placeholders = string.Join(", ", idList.Select((_, i) => $"{{{i}}}"));
        var rows = await db.QueryAsync<UserRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"SELECT * FROM Users WHERE Id IN ({placeholders})",
                idList.Cast<object>().ToArray()),
            false,
            cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.ToDomain());
    }

    public async ValueTask<FileShare?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<FileShareRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<FileShare>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<FileShareRow>().ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public Task<FileShare> AddAsync(FileShare entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueInsert(entity, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(FileShare entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueUpdate(entity, async (db, ct) => await db.UpdateAsync(row, ct));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(FileShare entity, CancellationToken cancellationToken = default)
    {
        var id = entity.Id;
        unitOfWork.EnqueueDelete(entity, async (db, ct) => await db.DeleteAsync<FileShareRow>(id, ct));
        return Task.CompletedTask;
    }
}
