// =============================================================================
// NasSystem.Infrastructure - Persistence/UserRow.cs
// Users 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>Users 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("Users")]
public partial class UserRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Username")] public string Username { get; set; } = string.Empty;
    [Column("PasswordHash")] public string PasswordHash { get; set; } = string.Empty;
    [Column("PasswordSalt")] public string PasswordSalt { get; set; } = string.Empty;
    [Column("DisplayName")] public string? DisplayName { get; set; }
    [Column("Email")] public string? Email { get; set; }
    [Column("Role")] public int Role { get; set; }
    [Column("IsEnabled")] public bool IsEnabled { get; set; }
    [Column("LastLoginAt")] public DateTime? LastLoginAt { get; set; }
    [Column("QuotaBytes")] public long? QuotaBytes { get; set; }
    [Column("UsedBytes")] public long UsedBytes { get; set; }
    [Column("HomeDirectory")] public string HomeDirectory { get; set; } = string.Empty;
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
