// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemRow.cs
// FileItem 的持久化行模型（影子模型）
// =============================================================================

using NasSystem.Domain.Enums;
using PalORM;

namespace NasSystem.Infrastructure.Persistence;

/// <summary>
/// FileItems 表的行模型。
/// </summary>
/// <remarks>
/// 为什么领域实体不能直接挂 PalORM 注解：领域实体是私有 setter 加私有无参构造的 DDD 形状，
/// PalORM 的生成器要求公共无参构造与可写属性（编译器报 CS0310）。
/// 因而在基础设施层建影子模型承接映射，Domain 保持零外部依赖，
/// CONVENTIONS §1.1 与 gate NAS-G1 的分层红线不受影响。
/// 列名一律显式声明：存量库是 PascalCase，而 PalORM 6.0 起删除了 NamingConvention，
/// 默认按属性名映射，显式写出可读且不依赖默认行为。
/// 索引随模型声明（而非手写 DDL）：迁移时漏声明会让新建库缺索引，
/// 按 OwnerId/Hash/ParentId 的查询退化成全表 SCAN，而功能完全正常、基准也测不出来。
/// </remarks>
[Table("FileItems")]
// Path 唯一部分索引与 Path 前缀 NOCASE 部分索引带 WHERE 过滤条件，生成器不产出，
// 由 InitializeDatabaseAsync 手工补；这里若再声明同名索引会重名冲突
[Index("IX_FileItems_Hash", nameof(Hash))]
[Index("IX_FileItems_IsDeleted", nameof(IsDeleted))]
[Index("IX_FileItems_OwnerId", nameof(OwnerId))]
[Index("IX_FileItems_ParentId", nameof(ParentId))]
[Index("IX_FileItems_ParentId_Name", nameof(ParentId), nameof(Name))]
[Index("IX_FileItems_StoragePoolId", nameof(StoragePoolId))]
public partial class FileItemRow
{
    /// <summary>Guid 主键必须显式 AutoIncrement=false，否则 PALORM022 报错且实体被静默跳过。</summary>
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Name")] public string Name { get; set; } = string.Empty;
    [Column("Path")] public string Path { get; set; } = string.Empty;
    [Column("PhysicalPath")] public string PhysicalPath { get; set; } = string.Empty;
    // 原生枚举属性（PalORM 6.1+）：库里存 INTEGER，与存量数据零迁移；
    // 省掉 RowMapper 里手写 int 强转，也消掉一类映射出错面
    [Column("ItemType", StoreAs = StoreAs.AsInt32)]
    public FileItemType ItemType { get; set; }
    [Column("SizeBytes")] public long SizeBytes { get; set; }
    [Column("MimeType")] public string? MimeType { get; set; }
    [Column("Extension")] public string? Extension { get; set; }
    [Column("Hash")] public string? Hash { get; set; }
    [Column("StoragePoolId")]
    [ForeignKey("StoragePools", "Id", OnDelete = DeleteAction.Restrict)]
    public Guid StoragePoolId { get; set; }

    [Column("OwnerId")]
    [ForeignKey("Users", "Id", OnDelete = DeleteAction.Restrict)]
    public Guid OwnerId { get; set; }

    [Column("ParentId")]
    [ForeignKey("FileItems", "Id", OnDelete = DeleteAction.Restrict)]
    public Guid? ParentId { get; set; }
    [Column("IsDeleted")] public bool IsDeleted { get; set; }
    [Column("DeletedAt")] public DateTime? DeletedAt { get; set; }
    [Column("IsHidden")] public bool IsHidden { get; set; }
    [Column("IsSystem")] public bool IsSystem { get; set; }
    [Column("IsReadOnly")] public bool IsReadOnly { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
