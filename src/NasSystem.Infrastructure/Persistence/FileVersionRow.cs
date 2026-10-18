// =============================================================================
// NasSystem.Infrastructure - Persistence/FileVersionRow.cs
// FileVersions 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>FileVersions 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
[Table("FileVersions")]
[Index("IX_FileVersions_FileItemId", nameof(FileItemId))]
[Index("IX_FileVersions_FileItemId_IsCurrent", nameof(FileItemId), nameof(IsCurrent))]
[Index("IX_FileVersions_CreatedByUserId", nameof(CreatedByUserId))]
[Index("IX_FileVersions_FileItemId_VersionNumber", nameof(FileItemId), nameof(VersionNumber), Unique = true)]
public partial class FileVersionRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    // FileItemId 不设外键（ROADMAP Phase 1）：版本快照的用途就是兜住不可逆操作，
    // 永久删除文件项后版本必须仍然存在、可下载。原 ON DELETE CASCADE 会在删
    // FileItems 时把版本一起带走，恰好摧毁这道安全网。存量库的级联外键由
    // InitializeDatabaseAsync 的迁移脚本重建表移除
    [Column("FileItemId")]
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
