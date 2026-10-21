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
    /// 根据路径获取文件项（含软删记录）。回收站条目的永久删除按路径查找，
    /// 可见口径（GetByPathAsync 排已删）会让回收站"永久删除"全数落入
    /// "路径不存在"——2026-10-19 E2E 走查实证
    /// </summary>
    Task<FileItem?> GetByPathIncludingDeletedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 Id 集合批量取文件项（单条 IN 查询）。语义搜索等"先拿 Id 集合
    /// 再取详情"的场景用它消 N+1：topK=200 时逐条 GetByIdAsync 是 200 次
    /// 单行查询
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

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
    /// OwnerId 过滤同为硬约束：多用户隔离的库层防线，调方传入当前用户 Id。
    /// </remarks>
    Task<IReadOnlyList<FileItemSummary>> GetChildrenSummariesAsync(
        Guid? parentId,
        Guid ownerId,
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
        bool ocrOnly = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 媒体聚合查询（ROADMAP Phase 3）：按扩展名集合取文件，
    /// 按创建时间倒序，可选月份过滤。与 SearchAsync 的差别是
    /// 无关键词条件（媒体库是浏览不是搜索），月份用
    /// strftime 在 SQL 侧过滤（不在内存里筛）
    /// </summary>
    Task<FileSearchResult> GetMediaAsync(
        IReadOnlyList<string> extensions,
        Guid ownerId,
        string? month = null,
        int page = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件统计信息
    /// </summary>
    Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取路径下所有后代 (递归)，不论删除态，按路径倒序（最深的先）。
    /// 倒序与含软删都是为永久删除服务：父是子的路径前缀，父先删撞
    /// ParentId 外键 RESTRICT；回收站里的子树整棵是软删态，排已删会
    /// 取回空集同样删不掉
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetDescendantsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取根路径自身加全部后代的轻量清单（路径 + 类型，不物化实体）。
    /// </summary>
    /// <remarks>
    /// 供子树批量操作（软删/恢复/路径重写）的副作用处理：文件路径用于缩略图
    /// 缓存清理，文件夹路径用于目录版本号递增。不含 IsDeleted 过滤：软删态的
    /// 后代同样要跟随恢复与重写，见各批量方法的注释。
    /// </remarks>
    Task<IReadOnlyList<FileItemEntry>> GetSubtreeEntriesAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同上，额外按属主收敛。备份 diff 用：路径前缀只保证"在这棵目录下"，
    /// 不保证"是这个人建的"（扫描同步会把无主物理文件记给触发者），
    /// 跨用户场景下前缀内可能混入他人记录
    /// </summary>
    Task<IReadOnlyList<FileItemEntry>> GetSubtreeEntriesAsync(
        string rootPath,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按扩展名计数（语义索引进度用：图片总量）。含软删过滤
    /// </summary>
    Task<int> CountByExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按扩展名取全部文件的轻量清单（Id + 虚拟路径 + 物理路径，
    /// 供语义索引逐张编码）。含软删过滤，不分页
    /// </summary>
    Task<IReadOnlyList<FileItemPathEntry>> GetPathsByExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取根路径自身加全部后代（不论删除态）的快照采集投影：路径 + 类型 +
    /// 大小 + 内容 hash + 修改时间。供清单快照生成条目（ROADMAP Phase 2）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GetSubtreeEntriesAsync"/> 同查询形态，多取 Hash 与
    /// 时间戳：快照条目要内容指纹与最后修改时间做时间点对比。删除态记录
    /// 也入快照：回收站里的文件同样是"目录在那一刻的形态"的一部分，
    /// 对比时如实呈现（恢复/清理决策由用户看 diff 后做）。
    /// </remarks>
    Task<IReadOnlyList<FileItemSnapshotEntry>> GetSnapshotEntriesAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单条 SQL 软删整个子树（根自身加全部后代，不论当前删除态）。
    /// 命令经 UnitOfWork 意图队列登记，随同批次其他写入在同一事务提交。
    /// </summary>
    Task SoftDeleteSubtreeAsync(
        string rootPath,
        DateTime deletedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单条 SQL 恢复整个子树（根自身加全部后代，不论当前删除态）。
    /// </summary>
    /// <remarks>
    /// 不含 IsDeleted 过滤是有意的：软删一个文件夹时整棵子树都进了回收站，
    /// 恢复必须把后代一并拉起。按 Id 取后代再逐条恢复的实现受
    /// <see cref="GetDescendantsAsync"/> 的 IsDeleted = 0 过滤所限，实际只恢复根。
    /// </remarks>
    Task RestoreSubtreeAsync(
        string rootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单条 SQL 重写子树全部路径（虚拟路径与物理路径同趟，按旧基路径长度截断拼接）。
    /// 不含 IsDeleted 过滤：软删后代同样要跟随重写，否则其 Path 停在旧位置，
    /// 恢复时可能撞上旧路径上的新记录（Path 唯一部分索引）。
    /// </summary>
    Task RewriteSubtreePathsAsync(
        string oldVirtualBase,
        string newVirtualBase,
        string oldPhysicalBase,
        string newPhysicalBase,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算用户已使用空间（上传配额预检取实时聚合，不依赖滞后的 UsedBytes 字段）
    /// </summary>
    Task<long> CalculateUserUsedSpaceAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按用户 Id 集合批量计算已使用空间，一条 GROUP BY 代替 N 次单用户聚合。
    /// 返回字典只含有文件的用户，无文件的用户不在字典里（调用方按 0 处理）。
    /// 用户列表展示口径用这个方法：User.UsedBytes 字段没有生产维护者，
    /// 直接展示它永远是 0
    /// </summary>
    Task<IReadOnlyDictionary<Guid, long>> CalculateUsersUsedSpaceAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 全站已使用空间（所有用户的未删文件字节和）。管理页统计卡用：
    /// 曾经前端把当前分页页的 usedBytes 求和冒充全站总量
    /// </summary>
    Task<long> CalculateAllUsersUsedSpaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查路径是否存在
    /// </summary>
    Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取当前用户最近修改的文件（按 CreatedAt 倒序，含文件夹）。
    /// 供 Dashboard"最近文件"面板使用：与统计同口径按 OwnerId 过滤，
    /// 不做分页（面板只要前几条，count 由调用方夹取）
    /// </summary>
    Task<IReadOnlyList<FileItem>> GetRecentAsync(
        Guid ownerId,
        int count,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 取存储池全部记录的轻量清单（含软删），供扫描同步建对比表。
    /// 不排序：调用方建字典并按需自行排序（删除集按路径倒序确保子先于父）。
    /// </summary>
    Task<IReadOnlyList<FileItemScanEntry>> GetScanEntriesAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入（单条多值 SQL，经 UnitOfWork 意图队列与同批次写入同事务）。
    /// 列表顺序即插入顺序：调用方必须保证父行在子行之前（ParentId 外键 RESTRICT）。
    /// </summary>
    Task BulkInsertAsync(
        IReadOnlyList<FileItem> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量更新（逐行执行但复用命令与参数，经意图队列同事务）。
    /// </summary>
    Task BulkUpdateAsync(
        IReadOnlyList<FileItem> entities,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 Id 批量删除（每 500 个主键 IN 一批，经意图队列同事务）。
    /// 调用方必须保证子行 Id 在父行之前（ParentId 外键 RESTRICT）。
    /// </summary>
    Task BulkDeleteAsync(
        IReadOnlyList<Guid> ids,
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

/// <summary>
/// 子树条目的轻量投影：路径 + 类型 + 大小。子树批量操作的副作用处理用，
/// 不物化完整实体（万级子树下实体化是纯浪费）。
/// 大小 2026-10-18 加入：备份计划 diff 按体积判断文件是否变化
/// （ROADMAP Phase 1），软删链路只消费路径与类型，不受影响
/// </summary>
public sealed record FileItemEntry(string Path, Enums.FileItemType ItemType, long SizeBytes);

/// <summary>
/// 扫描同步的轻量投影：路径 + 类型 + 大小 + 父 + 删除态 + Id。
/// </summary>
/// <remarks>
/// 扫描对比只需要这六个字段：路径做对比表键、大小做变更检测、父 Id 做树链接
/// 修复检测、删除态做跳过判定、Id 做批量删除。含软删记录（ITM-N26：软删记录的
/// 物理文件可能仍在挂载点，必须进对比表防止被当新文件重建）。物化完整实体是纯浪费：
/// 十万文件的池就是十万个实体同时驻留。
/// </remarks>
public sealed record FileItemScanEntry(
    Guid Id,
    string Path,
    Enums.FileItemType ItemType,
    long SizeBytes,
    Guid? ParentId,
    bool IsDeleted);

/// <summary>
/// 快照采集的轻量投影：路径 + 类型 + 大小 + 内容 hash + 时间戳。
/// 供清单快照生成条目（ROADMAP Phase 2），物化完整实体是纯浪费。
/// </summary>
public sealed record FileItemSnapshotEntry(
    string Path,
    Enums.FileItemType ItemType,
    long SizeBytes,
    string? Hash,
    DateTime? UpdatedAt,
    DateTime CreatedAt);

/// <summary>语义索引的文件路径投影：Id + 虚拟路径 + 物理路径</summary>
public sealed record FileItemPathEntry(Guid Id, string Path, string PhysicalPath);
