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
    /// 获取目录下的子项摘要（浏览专用）
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GetChildrenAsync"/> 的区别只在取数列：浏览 DTO 只要 14 个字段，
    /// 实体行有 19 列，其中 PhysicalPath 是绝对路径长串，千项目录下就是几十 KB 的白读白分配。
    /// 摘要查询按需取列，并把 IsHidden 过滤下推 SQL（原先载回全部再内存过滤）。
    /// 排序不在此做：前端 store 的 sortedFiles 会按用户选择重排，服务端 ORDER BY
    /// 的产物被完整丢弃，省掉这次排序。
    /// 软删过滤仍是硬约束：WHERE 必须带 IsDeleted = 0，否则回收站记录会出现在浏览结果里。
    /// </remarks>
    Task<IReadOnlyList<FileItemSummary>> GetChildrenSummariesAsync(
        Guid? parentId,
        bool includeHidden = false,
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
    /// 搜索文件（服务端分页）
    /// </summary>
    /// <remarks>
    /// 分页在 SQL 侧完成：LIMIT/OFFSET 加一条同 WHERE 的 COUNT。
    /// 原实现取满 500 条再在内存里 Skip/Take，翻到第 2 页要把整个 LIKE 扫描重跑一遍，
    /// 且 TotalCount 报的是窗口计数而非真实总数（前端分页器总页数是错的）。
    /// 排序在此从"可选"变成"必需"：没有确定的全序，LIMIT/OFFSET 跨页可能重复或漏行。
    /// </remarks>
    Task<FileSearchResult> SearchAsync(
        string searchTerm,
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        string? basePath = null,
        string? extension = null,
        bool? isFolder = null,
        int page = 1,
        int pageSize = 50,
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

/// <summary>
/// 文件项摘要：浏览目录列表所需的字段子集。
/// </summary>
/// <remarks>
/// 与 <see cref="Entities.FileItem"/> 同字段但少了 PhysicalPath、StoragePoolId、
/// IsDeleted、DeletedAt、IsSystem、Hash 六项，浏览 DTO 一个都不用它们
/// （Hash 于 2026-10-17 移除：前端零使用，万行目录下是白读白传，见 FileItemDto 注释）。
/// 定义在 Domain 而非 Application：它是仓储查询的返回形状，按 FileStatistics
/// 的同一先例放在仓储接口文件里，Domain 零外部依赖不受影响。
/// </remarks>
public sealed record FileItemSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required Enums.FileItemType ItemType { get; init; }
    public required long SizeBytes { get; init; }
    public string? MimeType { get; init; }
    public string? Extension { get; init; }
    public required Guid OwnerId { get; init; }
    public Guid? ParentId { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsReadOnly { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// 文件搜索结果：当前页条目 + 匹配总数。
/// </summary>
/// <remarks>
/// 总数必须来自同 WHERE 的 COUNT，不能是取回条数：原实现把"窗口计数"当 TotalCount
/// 报给前端，分页器的总页数从第二页起就是错的。
/// </remarks>
public sealed record FileSearchResult
{
    public required IReadOnlyList<Entities.FileItem> Items { get; init; }
    public required int TotalCount { get; init; }
}
