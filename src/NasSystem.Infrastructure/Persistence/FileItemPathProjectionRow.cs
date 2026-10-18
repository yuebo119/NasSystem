// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemPathProjectionRow.cs
// 文件路径投影行（不对应表，语义索引用）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>
/// 语义索引的文件路径投影：Id + 虚拟路径 + 物理路径。
/// 与 <see cref="FileItemScanRow"/> 同族：非表投影挂 <c>[Projection]</c>。
/// </summary>
[Projection]
public partial class FileItemPathProjectionRow
{
    [Column("Id")] public Guid Id { get; set; }
    [Column("Path")] public string Path { get; set; } = string.Empty;
    [Column("PhysicalPath")] public string PhysicalPath { get; set; } = string.Empty;
}
