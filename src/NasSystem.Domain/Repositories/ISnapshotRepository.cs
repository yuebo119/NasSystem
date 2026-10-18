// =============================================================================
// NasSystem.Domain - Repositories/ISnapshotRepository.cs
// 目录快照仓储接口（ROADMAP Phase 2 清单快照）
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// 目录清单快照仓储。查询形态说明：
/// 条目按快照批量写入（一个快照可达数万行），diff 在 SQL 层按
/// RelativePath 对齐两个快照的条目集合，不在内存里做全量比对。
/// </summary>
public interface ISnapshotRepository
{
    // ---- 快照头 ----

    Task AddAsync(DirectorySnapshot snapshot, CancellationToken cancellationToken = default);

    void Update(DirectorySnapshot snapshot);

    void Delete(DirectorySnapshot snapshot);

    Task<DirectorySnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>快照列表（按创建时间倒序分页）</summary>
    Task<(IReadOnlyList<DirectorySnapshot> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>保留策略用：最旧的超出保留数的快照（按创建时间升序取前 N 个）</summary>
    Task<IReadOnlyList<DirectorySnapshot>> GetOldestAsync(
        int keepCount, CancellationToken cancellationToken = default);

    // ---- 快照条目 ----

    /// <summary>批量登记条目（意图队列形式，与快照头同一事务提交）</summary>
    Task AddEntriesAsync(
        IReadOnlyList<SnapshotEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>取快照条目（按相对路径排序分页，diff 前端展示用）</summary>
    Task<(IReadOnlyList<SnapshotEntry> Items, int TotalCount)> GetEntriesPagedAsync(
        Guid snapshotId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>快照内条目总数（创建完成时对账用）</summary>
    Task<int> CountEntriesAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    // ---- diff（SQL 层对齐两个快照的条目集合）----

    /// <summary>
    /// 对比两个快照的条目。base=旧快照，target=新快照。
    /// 结果含三类：仅在 target（新增）、仅在 base（删除）、
    /// 两边都有但 hash 或大小不同（修改）
    /// </summary>
    Task<SnapshotDiffResult> DiffAsync(
        Guid baseSnapshotId, Guid targetSnapshotId,
        int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>diff 单条结果的行内投影（仓储层形态，服务层转 DTO）</summary>
public sealed record SnapshotDiffRow(
    string RelativePath,
    bool IsFolder,
    SnapshotDiffKind Kind,
    long BaseSizeBytes,
    long TargetSizeBytes,
    string? BaseHash,
    string? TargetHash);

/// <summary>diff 条目类别</summary>
public enum SnapshotDiffKind
{
    /// <summary>仅新快照有（相对旧快照新增）</summary>
    Added = 0,

    /// <summary>仅旧快照有（相对旧快照删除）</summary>
    Removed = 1,

    /// <summary>两边都有但 hash 或大小不同（内容变了）</summary>
    Modified = 2,

    /// <summary>两边都有且 hash 与大小都相同（未变）</summary>
    Unchanged = 3,
}

/// <summary>diff 分页结果（含分类计数）</summary>
public sealed record SnapshotDiffResult(
    IReadOnlyList<SnapshotDiffRow> Items,
    int TotalCount,
    int AddedCount,
    int RemovedCount,
    int ModifiedCount,
    int UnchangedCount);
