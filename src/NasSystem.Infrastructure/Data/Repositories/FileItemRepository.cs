// =============================================================================
// NasSystem.Infrastructure - FileItemRepository.cs
// 文件项仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 文件项仓储实现
/// </summary>
public sealed class FileItemRepository(NasDbContext context) : RepositoryBase<FileItem>(context), IFileItemRepository
{
    public async Task<FileItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        return await QueryNoTracking()
            .FirstOrDefaultAsync(f => f.Path == normalizedPath && !f.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetChildrenAsync(
        Guid? parentId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking()
            .Where(f => f.ParentId == parentId);

        if (!includeDeleted)
            query = query.Where(f => !f.IsDeleted);

        return await query
            .OrderByDescending(f => f.ItemType) // 文件夹优先
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetChildrenByPathAsync(
        string parentPath,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(parentPath);
        
        // 先获取父文件夹
        var parent = await QueryNoTracking()
            .FirstOrDefaultAsync(f => f.Path == normalizedPath, cancellationToken);

        if (parent is null)
            return [];

        return await GetChildrenAsync(parent.Id, includeDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted)
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetDeletedItemsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking()
            .Where(f => f.IsDeleted);

        if (ownerId.HasValue)
            query = query.Where(f => f.OwnerId == ownerId.Value);

        return await query
            .OrderByDescending(f => f.DeletedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> SearchAsync(
        string searchTerm,
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking()
            .Where(f => !f.IsDeleted && f.Name.Contains(searchTerm));

        if (ownerId.HasValue)
            query = query.Where(f => f.OwnerId == ownerId.Value);

        if (storagePoolId.HasValue)
            query = query.Where(f => f.StoragePoolId == storagePoolId.Value);

        return await query
            .OrderBy(f => f.Name)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetDescendantsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default)
    {
        // 获取父路径
        var parent = await DbSet.FindAsync([parentId], cancellationToken);
        if (parent is null)
            return [];

        var parentPath = parent.Path;

        // 获取所有以该路径开头的项
        return await QueryNoTracking()
            .Where(f => f.Path.StartsWith(parentPath + "/") && !f.IsDeleted)
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CalculateFolderSizeAsync(
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var descendants = await GetDescendantsAsync(folderId, cancellationToken);
        return descendants.Where(f => f.IsFile).Sum(f => f.SizeBytes);
    }

    public async Task<long> CalculateUserUsedSpaceAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(f => f.OwnerId == userId && !f.IsDeleted && f.IsFile)
            .SumAsync(f => f.SizeBytes, cancellationToken);
    }

    public async Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        return await QueryNoTracking()
            .AnyAsync(f => f.Path == normalizedPath && !f.IsDeleted, cancellationToken);
    }

    public async Task AddRangeAsync(
        IEnumerable<FileItem> items,
        CancellationToken cancellationToken = default)
    {
        await DbSet.AddRangeAsync(items, cancellationToken);
    }

    public Task DeleteRangeAsync(
        IEnumerable<FileItem> items,
        CancellationToken cancellationToken = default)
    {
        DbSet.RemoveRange(items);
        return Task.CompletedTask;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }
}
