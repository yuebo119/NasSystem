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

    public async Task<IReadOnlyList<FileItem>> GetByStoragePoolAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        // 不含软删记录：调用方（扫描同步）另取软删集并入对比表，双集concat后按 Path 建字典
        return await QueryNoTracking()
            .Where(f => f.StoragePoolId == storagePoolId && !f.IsDeleted)
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileItem>> GetDeletedItemsAsync(
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking()
            .Where(f => f.IsDeleted);

        if (ownerId.HasValue)
            query = query.Where(f => f.OwnerId == ownerId.Value);

        if (storagePoolId.HasValue)
            query = query.Where(f => f.StoragePoolId == storagePoolId.Value);

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

        // 扩展名过滤（支持多个扩展名，用逗号分隔）——下推 SQL。
        // 原实现先 Take(500) 截断再内存过滤：截断后的过滤既丢结果也让分页失真
        if (!string.IsNullOrEmpty(extension))
        {
            var extensions = extension.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().StartsWith('.') ? e.Trim().ToLowerInvariant() : "." + e.Trim().ToLowerInvariant())
                .ToHashSet();

            query = query.Where(f => f.Extension != null && extensions.Contains(f.Extension.ToLower()));
        }

        // 结果集上限（服务端分页为独立事项：当前服务层做客户端分页，TotalCount
        // 取的是本窗口计数；此上限防止无参搜索拖全表）
        return await query
            .OrderByDescending(f => f.ItemType)
            .ThenBy(f => f.Name)
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = QueryNoTracking().Where(f => !f.IsDeleted);

        if (ownerId.HasValue)
            baseQuery = baseQuery.Where(f => f.OwnerId == ownerId.Value);

        // 分类表（与原内存版口径一致：空扩展名归 Other）
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico" };
        var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv" };
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma" };
        var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".json", ".xml" };

        var files = baseQuery.Where(f => f.ItemType == Domain.Enums.FileItemType.File);
        var recentDate = DateTime.UtcNow.AddDays(-7);

        // 全部聚合下推 SQL：原实现 ToListAsync 全表载入后内存 Count/Sum，
        // 文件数上万时单接口即全表传输；此处每项均为服务端单行聚合
        var totalFiles = await files.CountAsync(cancellationToken);
        var totalFolders = await baseQuery.CountAsync(f => f.ItemType == Domain.Enums.FileItemType.Folder, cancellationToken);
        var totalSizeBytes = await files.SumAsync(f => f.SizeBytes, cancellationToken);
        var recentFilesCount = await files.CountAsync(f => f.CreatedAt >= recentDate, cancellationToken);

        // 扩展名分组一次拿全（distinct 集合小，内存分类无放大风险）
        var extensionCounts = await files
            .GroupBy(f => f.Extension == null ? null : f.Extension.ToLower())
            .Select(g => new { Extension = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountIn(string[] set) =>
            extensionCounts.Where(e => e.Extension != null && set.Contains(e.Extension)).Sum(e => e.Count);

        var imageCount = CountIn(imageExtensions);
        var videoCount = CountIn(videoExtensions);
        var audioCount = CountIn(audioExtensions);
        var documentCount = CountIn(documentExtensions);

        return new FileStatistics
        {
            TotalFiles = totalFiles,
            TotalFolders = totalFolders,
            TotalSizeBytes = totalSizeBytes,
            ImageCount = imageCount,
            VideoCount = videoCount,
            AudioCount = audioCount,
            DocumentCount = documentCount,
            OtherCount = totalFiles - imageCount - videoCount - audioCount - documentCount,
            RecentFilesCount = recentFilesCount
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

    public async Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        return await QueryNoTracking()
            .AnyAsync(f => f.Path == normalizedPath && !f.IsDeleted, cancellationToken);
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
