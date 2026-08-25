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

    public async Task<IReadOnlyList<FileItem>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(f => !f.IsDeleted)
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
        string? basePath = null,
        string? extension = null,
        bool? isFolder = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking()
            .Where(f => !f.IsDeleted && f.Name.Contains(searchTerm));

        if (ownerId.HasValue)
            query = query.Where(f => f.OwnerId == ownerId.Value);

        if (storagePoolId.HasValue)
            query = query.Where(f => f.StoragePoolId == storagePoolId.Value);

        if (!string.IsNullOrEmpty(basePath))
        {
            var normalizedBasePath = NormalizePath(basePath);
            query = query.Where(f => f.Path.StartsWith(normalizedBasePath));
        }

        if (isFolder.HasValue)
        {
            query = isFolder.Value
                ? query.Where(f => f.ItemType == Domain.Enums.FileItemType.Folder)
                : query.Where(f => f.ItemType == Domain.Enums.FileItemType.File);
        }

        // 先获取结果，再在内存中过滤扩展名（因为 EF Core 对复杂扩展名过滤支持有限）
        var items = await query
            .OrderByDescending(f => f.ItemType)
            .ThenBy(f => f.Name)
            .Take(500)
            .ToListAsync(cancellationToken);

        // 扩展名过滤（支持多个扩展名，用逗号分隔）
        if (!string.IsNullOrEmpty(extension))
        {
            var extensions = extension.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().StartsWith('.') ? e.Trim().ToLowerInvariant() : "." + e.Trim().ToLowerInvariant())
                .ToHashSet();
            
            items = items.Where(f => f.Extension != null && extensions.Contains(f.Extension.ToLowerInvariant())).ToList();
        }

        return items.Take(200).ToList();
    }

    public async Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking().Where(f => !f.IsDeleted);

        if (ownerId.HasValue)
            query = query.Where(f => f.OwnerId == ownerId.Value);

        var items = await query.ToListAsync(cancellationToken);
        
        var files = items.Where(f => f.IsFile).ToList();
        var folders = items.Where(f => f.IsFolder).ToList();

        // 按扩展名分类
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico" };
        var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv" };
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma" };
        var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".json", ".xml" };

        var recentDate = DateTime.UtcNow.AddDays(-7);

        return new FileStatistics
        {
            TotalFiles = files.Count,
            TotalFolders = folders.Count,
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            ImageCount = files.Count(f => imageExtensions.Contains(f.Extension?.ToLower())),
            VideoCount = files.Count(f => videoExtensions.Contains(f.Extension?.ToLower())),
            AudioCount = files.Count(f => audioExtensions.Contains(f.Extension?.ToLower())),
            DocumentCount = files.Count(f => documentExtensions.Contains(f.Extension?.ToLower())),
            OtherCount = files.Count(f => !imageExtensions.Contains(f.Extension?.ToLower()) 
                && !videoExtensions.Contains(f.Extension?.ToLower())
                && !audioExtensions.Contains(f.Extension?.ToLower())
                && !documentExtensions.Contains(f.Extension?.ToLower())),
            RecentFilesCount = files.Count(f => f.CreatedAt >= recentDate)
        };
    }

    public async Task<IReadOnlyList<FileItem>> GetDescendantsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default)
    {
        // 获取父路径——必须 NoTracking：调用方常已 Attach 同 Id 实体做更新，
        // FindAsync 的 tracking 加载会触发 IdentityMap 键冲突（软删除子树场景实测）
        var parent = await QueryNoTracking()
            .FirstOrDefaultAsync(f => f.Id == parentId, cancellationToken);
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

    public async Task<long> CalculateStoragePoolUsedSpaceAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(f => f.StoragePoolId == storagePoolId && !f.IsDeleted && f.ItemType == Domain.Enums.FileItemType.File)
            .SumAsync(f => f.SizeBytes, cancellationToken);
    }

    /// <summary>
    /// 标准化路径（单一真源委托，见 FilePath.Normalize 的保留论证）
    /// </summary>
    private static string NormalizePath(string path) => Domain.ValueObjects.FilePath.Normalize(path);
}
