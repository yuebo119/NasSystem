// =============================================================================
// NasSystem.Infrastructure - Persistence/FileVersionRow.cs
// FileVersions 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>FileVersions 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("FileVersions")]
public partial class FileVersionRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("FileItemId")]
    [ForeignKey("FileItems", "Id", OnDelete = DeleteAction.Cascade)]
    public Guid FileItemId { get; set; }
    [Column("VersionNumber")] public int VersionNumber { get; set; }
    [Column("StoragePath")] public string StoragePath { get; set; } = string.Empty;
    [Column("FileSize")] public long FileSize { get; set; }
    [Column("FileHash")] public string FileHash { get; set; } = string.Empty;
    [Column("Comment")] public string? Comment { get; set; }
    [Column("IsCurrent")] public bool IsCurrent { get; set; }
    [Column("CreatedByUserId")]
    [ForeignKey("Users", "Id", OnDelete = DeleteAction.Restrict)]
    public Guid CreatedByUserId { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("UpdatedAt")] public DateTime? UpdatedAt { get; set; }
}
