// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemEntryRow.cs
// 子树条目（路径 + 类型）的投影行
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using NasSystem.Domain.Enums;
using PalORM;

/// <summary>
/// 子树轻量清单查询的结果行：路径 + 条目类型。
/// </summary>
/// <remarks>
/// 与 <see cref="ExtensionCountRow"/> 同一先例：<c>QueryAsync&lt;T&gt;</c> 要求 T 已注册，
/// 非表投影挂 <c>[Projection]</c>。子树批量操作（软删/恢复/路径重写）只需要路径和类型
/// 做副作用处理（缩略图缓存清理、目录版本号递增），物化完整实体是纯浪费。
/// 属性名与 SQL 列别名对应，列序契约同 FileItemBrowseRow。
/// </remarks>
[Projection]
public partial class FileItemEntryRow
{
    public string Path { get; set; } = string.Empty;

    // 与 FileItemRow 同规格：库里存 INTEGER，PalORM 6.1+ 原生枚举属性
    [Column("ItemType", StoreAs = StoreAs.AsInt32)]
    public FileItemType ItemType { get; set; }
}
