// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemSnapshotEntryRow.cs
// 快照条目采集的投影行（不对应表）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using NasSystem.Domain.Enums;
using PalORM;

/// <summary>
/// 快照采集的投影行：路径 + 类型 + 大小 + 内容 hash + 两个时间戳。
/// </summary>
/// <remarks>
/// 与 <see cref="FileItemEntryRow"/> 同族：非表投影挂 <c>[Projection]</c>，
/// 属性名与 SQL 列别名对应、列序契约同前。比 FileItemEntryRow 多三列
/// （Hash/UpdatedAt/CreatedAt）：快照条目要内容指纹与最后修改时间做
/// diff 判定，这两列子树批量操作（软删/恢复）用不到，故不并到那个投影。
/// </remarks>
[Projection]
public partial class FileItemSnapshotEntryRow
{
    public string Path { get; set; } = string.Empty;

    [Column("ItemType", StoreAs = StoreAs.AsInt32)]
    public FileItemType ItemType { get; set; }

    public long SizeBytes { get; set; }

    public string? Hash { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
