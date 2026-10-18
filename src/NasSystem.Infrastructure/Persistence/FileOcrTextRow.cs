// =============================================================================
// NasSystem.Infrastructure - Persistence/FileOcrTextRow.cs
// FileOcrTexts 表的行模型（影子模型）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>FileOcrTexts 表的行模型。设计动因见 <see cref="FileItemRow"/> 的remarks。</summary>
/// <remarks>
/// FileItemId 不设外键：与 FileVersions 同族——图片永久删除后 OCR 文本
/// 理论上也可留作审计，且识别是稀疏行，不值得为不存在的一致性检查付查询成本。
/// FileItemId 唯一：一个文件一条文本（重复识别覆盖）
/// </remarks>
[Table("FileOcrTexts")]
[Index("IX_FileOcrTexts_FileItemId", nameof(FileItemId), Unique = true)]
public partial class FileOcrTextRow
{
    [Key(AutoIncrement = false)]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("FileItemId")]
    public Guid FileItemId { get; set; }

    [Column("Text")]
    public string Text { get; set; } = string.Empty;

    [Column("CharCount")]
    public int CharCount { get; set; }

    [Column("Model")]
    public string Model { get; set; } = string.Empty;

    [Column("ElapsedMs")]
    public int ElapsedMs { get; set; }

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }
}
