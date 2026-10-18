// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/ProtectedDirectoryRepository.cs
// 受保护目录仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>受保护目录仓储实现。查询形态说明见 <see cref="FileItemRepository"/>的remarks。</summary>
/// <remarks>
/// IsPathProtectedAsync 把全部标记的目录路径取进内存做前缀比较：
/// 表是个位数行，一次全表查询换掉路径索引的维护成本。前缀比较一律
/// Ordinal（ITM-N15：文化敏感比较在特定 culture 下大小写折叠可致误判），
/// 且按路径段边界匹配——"/Doc" 不是 "/Docs" 的前缀保护，
/// 命中要求 path 等于标记路径或其以 "标记路径/" 开头。
/// </remarks>
public sealed class ProtectedDirectoryRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IProtectedDirectoryRepository
{
    public Task AddAsync(ProtectedDirectory protectedDirectory, CancellationToken cancellationToken = default)
    {
        var row = protectedDirectory.ToRow();
        unitOfWork.EnqueueInsert(protectedDirectory, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Delete(ProtectedDirectory protectedDirectory)
    {
        var id = protectedDirectory.Id;
        unitOfWork.EnqueueDelete(protectedDirectory, async (db, ct) => await db.DeleteAsync<ProtectedDirectoryRow>(id, ct));
    }

    public async Task<ProtectedDirectory?> GetByFileItemIdAsync(
        Guid fileItemId, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<ProtectedDirectoryRow>()
            .Where($"FileItemId = {fileItemId}")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<ProtectedDirectory>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<ProtectedDirectoryRow>()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> IsPathProtectedAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var db = await sessions.GetAsync(cancellationToken);

        // 空表短路：未配置任何保护（默认态）时一次 COUNT 直接放行。
        // 写路径（上传覆盖/移动/删除）每次操作都会过这里，短路让
        // 没有保护标记的部署零 JOIN 开销
        var markCount = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM ProtectedDirectories"), false, cancellationToken);
        if (markCount == 0)
            return false;

        // 取全部标记行的 FileItemId 与对应目录路径。目录路径不在本表
        // （保护表只存 FileItemId），按 Id 关联 FileItems 取 Path：
        // 个位数行的 JOIN 成本可忽略，换来不冗余存路径（路径会随
        // 重命名/移动变，冗余存即漂移源）
        var rows = await db.QueryAsync<ProtectedPathProjectionRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                """
                SELECT f.Path AS Path FROM ProtectedDirectories p
                JOIN FileItems f ON f.Id = p.FileItemId
                WHERE f.IsDeleted = 0
                """),
            false, cancellationToken);

        foreach (var row in rows)
        {
            if (IsUnderOrEqual(path, row.Path))
                return true;
        }
        return false;
    }

    /// <summary>
    /// path 是否等于标记路径或其子孙。段边界比较："/Docs2/x" 不算
    /// "/Docs" 的子孙（Ordinal 下逐字符比会在 "Docs" 后遇到 '2' ≠ '/' 而落空）
    /// </summary>
    private static bool IsUnderOrEqual(string path, string markedPath)
    {
        if (string.IsNullOrEmpty(markedPath))
            return false;
        return path.AsSpan().Equals(markedPath.AsSpan(), StringComparison.Ordinal)
            || (path.Length > markedPath.Length
                && path.StartsWith(markedPath, StringComparison.Ordinal)
                && path[markedPath.Length] == '/');
    }
}
