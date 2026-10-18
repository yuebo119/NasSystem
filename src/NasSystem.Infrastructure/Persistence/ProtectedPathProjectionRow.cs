// =============================================================================
// NasSystem.Infrastructure - Persistence/ProtectedPathProjectionRow.cs
// 受保护目录路径投影行（不对应表）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>
/// 保护判定的路径投影：ProtectedDirectories JOIN FileItems 取目录虚拟路径。
/// 与 <see cref="FileItemScanRow"/> 同族：无 [Table]，只承接查询列。
/// </summary>
[Projection]
public partial class ProtectedPathProjectionRow
{
    [Column("Path")] public string Path { get; set; } = string.Empty;
}
