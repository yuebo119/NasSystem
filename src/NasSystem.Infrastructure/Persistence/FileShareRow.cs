// =============================================================================
// NasSystem.Infrastructure - Persistence/FileShareRow.cs
// FileShares 表的行模型（影子模型）
// =============================================================================

using NasSystem.Domain.Enums;
using PalORM;

namespace NasSystem.Infrastructure.Persistence;

/// <summary>FileShares 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("FileShares")]
[Index("IX_FileShares_FileItemId", nameof(FileItemId))]
[Index("IX_FileShares_CreatedByUserId", nameof(CreatedByUserId))]
[Index("IX_FileShares_ShareCode", nameof(ShareCode), Unique = true)]
public partial class FileShareRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("ShareCode")] public string ShareCode { get; set; } = string.Empty;

    [Column("FileItemId")]
    [ForeignKey("FileItems", "Id", OnDelete = DeleteAction.Cascade)]
    public Guid FileItemId { get; set; }
    [Column("AccessType", StoreAs = StoreAs.AsInt32)]
    public ShareAccessType AccessType { get; set; }
    [Column("PasswordHash")] public string? PasswordHash { get; set; }
    [Column("Permission", StoreAs = StoreAs.AsInt32)]
    public PermissionLevel Permission { get; set; }
    [Column("ExpiresAt")] public DateTime? ExpiresAt { get; set; }
    [Column("MaxDownloads")] public int? MaxDownloads { get; set; }
    [Column("DownloadCount")] public int DownloadCount { get; set; }
    [Column("CreatedByUserId")]
    [ForeignKey("Users", "Id", OnDelete = DeleteAction.Restrict)]
    public Guid CreatedByUserId { get; set; }
    [Column("IsEnabled")] public bool IsEnabled { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
