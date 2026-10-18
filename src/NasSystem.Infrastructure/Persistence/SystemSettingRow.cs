// =============================================================================
// NasSystem.Infrastructure - Persistence/SystemSettingRow.cs
// SystemSettings 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>SystemSettings 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("SystemSettings")]
[Index("IX_SystemSettings_Key", nameof(Key), Unique = true)]
public partial class SystemSettingRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Key")]
    public string Key { get; set; } = string.Empty;

    [Column("Value")]
    public string Value { get; set; } = string.Empty;

    [Column("UpdatedAt")]
    public DateTime UpdatedAt { get; set; }
}
