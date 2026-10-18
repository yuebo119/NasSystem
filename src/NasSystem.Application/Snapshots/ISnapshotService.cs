// =============================================================================
// NasSystem.Application - Snapshots/ISnapshotService.cs
// 目录清单快照服务（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Application.Snapshots;

using NasSystem.Domain.Enums;
using NasSystem.Shared;

/// <summary>
/// 目录清单快照：记录目录子树在某一时刻的形态（路径/大小/hash/时间），
/// 支持按时间点对比。快照只记清单不搬内容——内容级备份是 3-2-1 导出的职责。
/// </summary>
/// <remarks>
/// 采集走库内目录树（FileItemRepository.GetSnapshotEntriesAsync），不做物理
/// 目录扫描：物理世界由 ScanAndSync 对齐，清单层基于库内状态的快照是确定性的。
/// 删除态记录也进快照（回收站形态同样是"那一刻的目录状态"），diff 如实呈现，
/// 恢复/清理决策由用户看 diff 后做。
/// </remarks>
public interface ISnapshotService
{
    /// <summary>创建快照（同步采集：库内查询 + 批量写条目，毫秒到秒级）</summary>
    Task<Result<SnapshotDto>> CreateAsync(
        string rootPath,
        string? name,
        Guid operatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>快照列表（倒序分页）</summary>
    Task<Result<PagedResult<SnapshotDto>>> GetListAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>快照详情</summary>
    Task<Result<SnapshotDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>快照条目（按相对路径排序分页）</summary>
    Task<Result<PagedResult<SnapshotEntryDto>>> GetEntriesAsync(
        Guid id, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>对比两个快照（base=旧，target=新），四态分类</summary>
    Task<Result<SnapshotDiffDto>> DiffAsync(
        Guid baseSnapshotId, Guid targetSnapshotId,
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>删除快照（连带条目）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保留策略：删除最旧的、超出 keepCount 的快照。返回删除条数。
    /// 由定时任务与手动创建后调用
    /// </summary>
    Task<int> ApplyRetentionAsync(int keepCount, CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>快照摘要</summary>
public sealed record SnapshotDto(
    Guid Id,
    string Name,
    string RootPath,
    SnapshotStatus Status,
    int FileCount,
    int FolderCount,
    long TotalSizeBytes,
    Guid CreatedByUserId,
    string? Error,
    DateTime CreatedAt);

/// <summary>快照条目</summary>
public sealed record SnapshotEntryDto(
    string RelativePath,
    bool IsFolder,
    long SizeBytes,
    string? ContentHash,
    DateTime LastModified);

/// <summary>diff 条目类别（与仓储层 SnapshotDiffKind 同值）</summary>
public enum SnapshotDiffKindDto
{
    Added = 0,
    Removed = 1,
    Modified = 2,
    Unchanged = 3,
}

/// <summary>diff 单条</summary>
public sealed record SnapshotDiffItemDto(
    string RelativePath,
    bool IsFolder,
    SnapshotDiffKindDto Kind,
    long BaseSizeBytes,
    long TargetSizeBytes,
    string? BaseHash,
    string? TargetHash);

/// <summary>diff 结果（分页条目 + 四态计数）</summary>
public sealed record SnapshotDiffDto(
    IReadOnlyList<SnapshotDiffItemDto> Items,
    int TotalCount,
    int AddedCount,
    int RemovedCount,
    int ModifiedCount,
    int UnchangedCount);
