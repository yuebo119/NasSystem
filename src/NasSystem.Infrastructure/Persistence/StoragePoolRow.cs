// =============================================================================
// NasSystem.Infrastructure - Persistence/StoragePoolRow.cs
// StoragePools 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>StoragePools 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("StoragePools")]
public partial class StoragePoolRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Name")] public string Name { get; set; } = string.Empty;
    [Column("MountPath")] public string MountPath { get; set; } = string.Empty;
    [Column("Description")] public string? Description { get; set; }
    [Column("IsDefault")] public bool IsDefault { get; set; }
    [Column("IsEnabled")] public bool IsEnabled { get; set; }
    [Column("TotalBytes")] public long TotalBytes { get; set; }
    [Column("UsedBytes")] public long UsedBytes { get; set; }
    [Column("MaxCapacityBytes")] public long? MaxCapacityBytes { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
