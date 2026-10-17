// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemBrowseRow.cs
// 浏览目录列表的只读投影行模型
// =============================================================================

using NasSystem.Domain.Enums;
using PalORM;

namespace NasSystem.Infrastructure.Persistence;

/// <summary>
/// FileItems 表的浏览专用投影：只含目录列表 DTO 用得到的 14 列。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 FileItemRow</b>：<c>From&lt;T&gt;()</c> 走全列 SELECT，而浏览 DTO
/// 用不到 PhysicalPath（绝对路径长串）、StoragePoolId、IsDeleted、DeletedAt、IsSystem。
/// 千项目录下 PhysicalPath 一项就是几十 KB 的白读加白分配。PalORM 的
/// <c>QueryBuilder.Select</c> 目前只支持 DryRun/ToSql，实体执行会明确失败，
/// 因而投影必须走 <c>[Projection]</c> 类型加 <c>QueryAsync&lt;T&gt;</c> 这条路。
/// </para>
/// <para>
/// <b>列序契约</b>：PalORM 投影按属性声明序做序号映射，SELECT 的列序必须与这里
/// 逐列对齐，错位不会报错、只会静默交换数据。仓储里的 SQL 已按同一顺序书写，
/// 新增列时必须同时改两处。
/// </para>
/// <para>
/// <b>可写属性</b>：源生成器要为投影生成 RowFactory，形状与 FileItemRow 保持一致
/// （公共无参构造 + 可写属性）最稳。它是基础设施内部类型，出仓储即转成 Domain 的
/// FileItemSummary，可写性不外泄。
/// </para>
/// </remarks>
[Projection]
public partial class FileItemBrowseRow
{
    [Column("Id")] public Guid Id { get; set; }
    [Column("Name")] public string Name { get; set; } = string.Empty;
    [Column("Path")] public string Path { get; set; } = string.Empty;
    // 与 FileItemRow 同规格：库里存 INTEGER，PalORM 6.1+ 原生枚举属性
    [Column("ItemType", StoreAs = StoreAs.AsInt32)]
    public FileItemType ItemType { get; set; }
    [Column("SizeBytes")] public long SizeBytes { get; set; }
    [Column("MimeType")] public string? MimeType { get; set; }
    [Column("Extension")] public string? Extension { get; set; }
    [Column("OwnerId")] public Guid OwnerId { get; set; }
    [Column("ParentId")] public Guid? ParentId { get; set; }
    [Column("IsHidden")] public bool IsHidden { get; set; }
    [Column("IsReadOnly")] public bool IsReadOnly { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
