// =============================================================================
// NasSystem.Infrastructure - Persistence/SnapshotEntryRow.cs
// SnapshotEntries 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>SnapshotEntries 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
/// <remarks>
/// 无外键：SnapshotId 不建外键（删快照走仓储显式删条目，不用级联——
/// 大条目表上级联删除在 SQLite 里是逐行触发，批量删反而慢）。
/// (SnapshotId, RelativePath) 是逻辑唯一，但不建唯一索引：diff 查询
/// 已按 SnapshotId 走索引，唯一约束的写入代价（每批插入多一次查重）
/// 对大条目表不划算；条目由服务层一次性生成，无并发写同快照的场景。
/// </remarks>
[Table("SnapshotEntries")]
[Index("IX_SnapshotEntries_SnapshotId", nameof(SnapshotId))]
public partial class SnapshotEntryRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("SnapshotId")]
    public Guid SnapshotId { get; set; }

    [Column("RelativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [Column("IsFolder")]
    public bool IsFolder { get; set; }

    [Column("SizeBytes")]
    public long SizeBytes { get; set; }

    [Column("ContentHash")]
    public string? ContentHash { get; set; }

    [Column("LastModified")]
    public DateTime LastModified { get; set; }
}
