// =============================================================================
// NasSystem.Domain - IFileItemRepository.cs
// 文件项仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// 文件项仓储接口
/// </summary>
public interface IFileItemRepository : IRepository<FileItem>
{
    /// <summary>
    /// 根据路径获取文件项
    /// </summary>
    Task<FileItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取目录下的子项
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetChildrenAsync(
        Guid? parentId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定存储池的文件列表（扫描同步按池过滤，不载全表）
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetByStoragePoolAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已删除的文件 (回收站)
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetDeletedItemsAsync(
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索文件
    /// </summary>
    Task<IReadOnlyList<FileItem>> SearchAsync(
        string searchTerm,
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        string? basePath = null,
        string? extension = null,
        bool? isFolder = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件统计信息
    /// </summary>
    Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取路径下所有后代 (递归)
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetDescendantsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算用户已使用空间（上传配额预检取实时聚合，不依赖滞后的 UsedBytes 字段）
    /// </summary>
    Task<long> CalculateUserUsedSpaceAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查路径是否存在
    /// </summary>
    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除文件项
    /// </summary>
    Task DeleteRangeAsync(
        IEnumerable<FileItem> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算存储池内所有文件的总大小
    /// </summary>
    Task<long> CalculateStoragePoolUsedSpaceAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件统计信息
/// </summary>
public sealed record FileStatistics
{
    public int TotalFiles { get; init; }
    public int TotalFolders { get; init; }
    public long TotalSizeBytes { get; init; }
    public int ImageCount { get; init; }
    public int VideoCount { get; init; }
    public int AudioCount { get; init; }
    public int DocumentCount { get; init; }
    public int OtherCount { get; init; }
    public int RecentFilesCount { get; init; }
}
