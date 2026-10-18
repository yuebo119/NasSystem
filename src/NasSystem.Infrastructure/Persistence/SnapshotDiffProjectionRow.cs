// =============================================================================
// NasSystem.Infrastructure - Persistence/SnapshotDiffProjectionRow.cs
// 快照 diff 查询的投影行（不对应表）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>
/// diff 查询的投影行。与 <see cref="FileItemScanRow"/> 同族：无 [Table]，
/// 只承接 SQL 侧对齐结果的列。Kind 是 diff 分类的整数形态
/// （0=Added/1=Removed/2=Modified，与 SnapshotDiffKind 逐值对应）。
/// </summary>
[Projection]
public partial class SnapshotDiffProjectionRow
{
    [Column("RelativePath")] public string RelativePath { get; set; } = string.Empty;
    [Column("IsFolder")] public bool IsFolder { get; set; }
    [Column("Kind")] public int Kind { get; set; }
    [Column("BaseSizeBytes")] public long BaseSizeBytes { get; set; }
    [Column("TargetSizeBytes")] public long TargetSizeBytes { get; set; }
    [Column("BaseHash")] public string? BaseHash { get; set; }
    [Column("TargetHash")] public string? TargetHash { get; set; }
}
