// =============================================================================
// NasSystem.Domain - FileOcrText.cs
// 图片 OCR 文本实体（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;

/// <summary>
/// 图片 OCR 识别文本。与 FileItem 一对一：只有识别过的图片有行，
/// 未识别的图片不占行（稀疏表）。识别文本进搜索后，
/// "搜扫描件/截图里的字" 才成立
/// </summary>
public sealed class FileOcrText : Entity
{
    /// <summary>关联的文件项（图片）</summary>
    public Guid FileItemId { get; private set; }

    /// <summary>识别出的全文（按行拼接）</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>文本字符数（展示与调试用，不入索引）</summary>
    public int CharCount { get; private set; }

    /// <summary>产出该文本的模型标识（如 ppocrv6-tiny-zh）。
    /// 模型升级后重新识别可据此判断是否过期</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>识别耗时（毫秒）。性能观测用：NAS 上是同步阻塞一次上传，
    /// 还是排队异步，决策需要这个数</summary>
    public int ElapsedMs { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static FileOcrText Create(
        Guid fileItemId,
        string text,
        string model,
        int elapsedMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new FileOcrText
        {
            FileItemId = fileItemId,
            Text = text ?? string.Empty,
            CharCount = text?.Length ?? 0,
            Model = model,
            ElapsedMs = elapsedMs,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>重新识别：同文件换文本（模型升级或首次识别失败后重试）</summary>
    public void UpdateText(string text, string model, int elapsedMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        Text = text ?? string.Empty;
        CharCount = text?.Length ?? 0;
        Model = model;
        ElapsedMs = elapsedMs;
        MarkAsModified();
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static FileOcrText Restore(
        Guid id,
        Guid fileItemId,
        string text,
        int charCount,
        string model,
        int elapsedMs,
        DateTime createdAt)
    {
        return new FileOcrText
        {
            Id = id,
            FileItemId = fileItemId,
            Text = text,
            CharCount = charCount,
            Model = model,
            ElapsedMs = elapsedMs,
            CreatedAt = createdAt
        };
    }
}
