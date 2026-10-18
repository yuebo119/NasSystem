// =============================================================================
// NasSystem.Infrastructure - Persistence/DirectorySnapshotRow.cs
// DirectorySnapshots 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>DirectorySnapshots 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
/// <remarks>
/// 无外键：CreatedByUserId 与 Users 不建外键（快照是审计型数据，
/// 用户删除后历史快照仍需可读，与 FileVersions 同族取舍）。
/// Status 存 INTEGER，映射 SnapshotStatus 枚举。
/// </remarks>
[Table("DirectorySnapshots")]
[Index("IX_DirectorySnapshots_CreatedAt", nameof(CreatedAt))]
public partial class DirectorySnapshotRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Name")]
    public string Name { get; set; } = string.Empty;

    [Column("RootPath")]
    public string RootPath { get; set; } = string.Empty;

    [Column("Status")]
    public int Status { get; set; }

    [Column("FileCount")]
    public int FileCount { get; set; }

    [Column("FolderCount")]
    public int FolderCount { get; set; }

    [Column("TotalSizeBytes")]
    public long TotalSizeBytes { get; set; }

    [Column("CreatedByUserId")]
    public Guid CreatedByUserId { get; set; }

    [Column("Error")]
    public string? Error { get; set; }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
