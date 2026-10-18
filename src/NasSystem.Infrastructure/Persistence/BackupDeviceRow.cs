// =============================================================================
// NasSystem.Infrastructure - Persistence/BackupDeviceRow.cs
// BackupDevices 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>BackupDevices 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
/// <remarks>
/// UserId 不设外键：与 FileVersions.FileItemId 同族的考虑——设备记录要在
/// 用户删除后仍可查（同步历史审计）。实际上用户删除当前会被 FileItems 的
/// 数据挡住，这里不设外键只是不为不存在的一致性检查付查询成本
/// </remarks>
[Table("BackupDevices")]
[Index("IX_BackupDevices_UserId", nameof(UserId))]
[Index("IX_BackupDevices_UserId_Name", nameof(UserId), nameof(Name), Unique = true)]
public partial class BackupDeviceRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("UserId")]
    public Guid UserId { get; set; }

    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    [Column("DeviceModel")]
    public string? DeviceModel { get; set; }

    [Column("LastSyncAt")]
    public DateTime? LastSyncAt { get; set; }

    [Column("LastSyncFileCount")]
    public int LastSyncFileCount { get; set; }

    [Column("IsRevoked")]
    public bool IsRevoked { get; set; }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
