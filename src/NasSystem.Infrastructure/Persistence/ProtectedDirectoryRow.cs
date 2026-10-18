// =============================================================================
// NasSystem.Infrastructure - Persistence/ProtectedDirectoryRow.cs
// ProtectedDirectories 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>ProtectedDirectories 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
/// <remarks>
/// FileItemId 唯一：一个目录只能标记一次（重复标记是幂等操作，
/// 由服务层转 AlreadyExists 或直接返回现有标记）。不建 FileItems
/// 外键：文件永久删除时若级联删保护标记，删除动作本身就被保护逻辑
/// 拦住了，级联走不到；反过来标记悬空无害（IsPathProtectedAsync 按
/// 路径前缀查，条目对应的目录不存在时天然不命中）。
/// </remarks>
[Table("ProtectedDirectories")]
[Index("IX_ProtectedDirectories_FileItemId", nameof(FileItemId), Unique = true)]
public partial class ProtectedDirectoryRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("FileItemId")]
    public Guid FileItemId { get; set; }

    [Column("MarkedByUserId")]
    public Guid MarkedByUserId { get; set; }

    [Column("Note")]
    public string? Note { get; set; }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }

    [Column("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
