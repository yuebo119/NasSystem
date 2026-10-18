// =============================================================================
// NasSystem.Domain - Enums/SnapshotStatus.cs
// 目录快照状态（ROADMAP Phase 2 清单快照）
// =============================================================================

namespace NasSystem.Domain.Enums;

/// <summary>
/// 目录快照的生命周期状态。快照只记清单不搬内容：Running 期间条目陆续
/// 写入，全部完成后落 Completed；中途失败落 Failed 并保留已写入条目
/// （失败快照的条目仍可对比，缺多少一目了然）
/// </summary>
public enum SnapshotStatus
{
    /// <summary>正在记录条目</summary>
    Running = 0,

    /// <summary>已完成</summary>
    Completed = 1,

    /// <summary>记录过程中失败</summary>
    Failed = 2,
}
