// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemScanRow.cs
// 扫描同步对比表的投影行
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using NasSystem.Domain.Enums;
using PalORM;

/// <summary>
/// 扫描同步对比表的投影行：Id + 路径 + 类型 + 大小 + 父 + 删除态。
/// </summary>
/// <remarks>
/// 与 <see cref="FileItemBrowseRow"/> 同一先例：<c>QueryAsync&lt;T&gt;</c> 走
/// <c>[Projection]</c> 类型，SELECT 列序必须与属性声明序逐列对齐（错位静默交换数据）。
/// 出仓储即转成 Domain 的 <c>FileItemScanEntry</c>，可写性不外泄。
/// </remarks>
[Projection]
public partial class FileItemScanRow
{
    [Column("Id")] public Guid Id { get; set; }
    [Column("Path")] public string Path { get; set; } = string.Empty;
    // 与 FileItemRow 同规格：库里存 INTEGER，PalORM 6.1+ 原生枚举属性
    [Column("ItemType", StoreAs = StoreAs.AsInt32)]
    public FileItemType ItemType { get; set; }
    [Column("SizeBytes")] public long SizeBytes { get; set; }
    [Column("ParentId")] public Guid? ParentId { get; set; }
    [Column("IsDeleted")] public bool IsDeleted { get; set; }
}
