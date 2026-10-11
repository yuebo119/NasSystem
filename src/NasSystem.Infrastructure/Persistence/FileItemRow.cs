// =============================================================================
// NasSystem.Infrastructure - Persistence/FileItemRow.cs
// FileItem 的持久化行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

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
/// </remarks>
[Table("FileItems")]
public partial class FileItemRow
{
    /// <summary>Guid 主键必须显式 AutoIncrement=false，否则 PALORM022 报错且实体被静默跳过。</summary>
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Name")] public string Name { get; set; } = string.Empty;
    [Column("Path")] public string Path { get; set; } = string.Empty;
    [Column("PhysicalPath")] public string PhysicalPath { get; set; } = string.Empty;
    [Column("ItemType")] public int ItemType { get; set; }
    [Column("SizeBytes")] public long SizeBytes { get; set; }
    [Column("MimeType")] public string? MimeType { get; set; }
    [Column("Extension")] public string? Extension { get; set; }
    [Column("Hash")] public string? Hash { get; set; }
    [Column("StoragePoolId")] public Guid StoragePoolId { get; set; }
    [Column("OwnerId")] public Guid OwnerId { get; set; }
    [Column("ParentId")] public Guid? ParentId { get; set; }
    [Column("IsDeleted")] public bool IsDeleted { get; set; }
    [Column("DeletedAt")] public DateTime? DeletedAt { get; set; }
    [Column("IsHidden")] public bool IsHidden { get; set; }
    [Column("IsSystem")] public bool IsSystem { get; set; }
    [Column("IsReadOnly")] public bool IsReadOnly { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
