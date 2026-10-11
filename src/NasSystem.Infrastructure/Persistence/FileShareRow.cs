// =============================================================================
// NasSystem.Infrastructure - Persistence/FileShareRow.cs
// FileShares 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>FileShares 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("FileShares")]
public partial class FileShareRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("ShareCode")] public string ShareCode { get; set; } = string.Empty;
    [Column("FileItemId")] public Guid FileItemId { get; set; }
    [Column("AccessType")] public int AccessType { get; set; }
    [Column("PasswordHash")] public string? PasswordHash { get; set; }
    [Column("Permission")] public int Permission { get; set; }
    [Column("ExpiresAt")] public DateTime? ExpiresAt { get; set; }
    [Column("MaxDownloads")] public int? MaxDownloads { get; set; }
    [Column("DownloadCount")] public int DownloadCount { get; set; }
    [Column("CreatedByUserId")] public Guid CreatedByUserId { get; set; }
    [Column("IsEnabled")] public bool IsEnabled { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
