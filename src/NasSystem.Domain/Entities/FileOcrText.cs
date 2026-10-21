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

    /// <summary>
    /// 按行的位置区域（2026-10-21 OCR 文字叠加定位）。与 <see cref="Text"/>
    /// 的行同序：前端把文字按坐标叠回图片原位（底色采样自图片），用户可直接
    /// 对照"哪些字识别好了"并选中复制。坐标为归一化四角（0..1，相对图片
    /// 宽高），显示尺寸变化时按比例换算，与存储分辨率解耦。
    /// 存量行（识别于本特性之前）为空列表，前端降级为纯文本展示
    /// </summary>
    public IReadOnlyList<OcrTextRegion> Regions { get; private set; } = [];

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
        int elapsedMs,
        IReadOnlyList<OcrTextRegion>? regions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new FileOcrText
        {
            FileItemId = fileItemId,
            Text = text ?? string.Empty,
            Regions = regions ?? [],
            CharCount = text?.Length ?? 0,
            Model = model,
            ElapsedMs = elapsedMs,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>重新识别：同文件换文本（模型升级或首次识别失败后重试）</summary>
    public void UpdateText(string text, string model, int elapsedMs, IReadOnlyList<OcrTextRegion>? regions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        Text = text ?? string.Empty;
        Regions = regions ?? [];
        CharCount = text?.Length ?? 0;
        Model = model;
        ElapsedMs = elapsedMs;
        MarkAsModified();
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）。
    /// regionsJson 为存量库的 null/空串时降级为空列表（识别于叠加特性之前）</summary>
    internal static FileOcrText Restore(
        Guid id,
        Guid fileItemId,
        string text,
        int charCount,
        string model,
        int elapsedMs,
        DateTime createdAt,
        string? regionsJson = null)
    {
        return new FileOcrText
        {
            Id = id,
            FileItemId = fileItemId,
            Text = text,
            CharCount = charCount,
            Model = model,
            ElapsedMs = elapsedMs,
            CreatedAt = createdAt,
            Regions = OcrTextRegion.ParseMany(regionsJson)
        };
    }
}

/// <summary>
/// OCR 行区域：一行文字与其在图片中的归一化四角坐标。
/// Points 为 8 个 float（x1,y1,x2,y2,x3,y3,x4,y4，均 0..1，原点左上）。
/// 存归一化而非像素：图片显示尺寸随布局变化，坐标按比例换算即用
/// </summary>
/// <param name="Text">该行识别出的文字</param>
/// <param name="Points">归一化四角坐标（8 值）</param>
public sealed record OcrTextRegion(string Text, float[] Points)
{
    /// <summary>
    /// 从持久化 JSON 解析区域列表。null/空/损坏一律返回空列表——
    /// 位置信息是增强，缺了不能影响文本主流程
    /// </summary>
    public static IReadOnlyList<OcrTextRegion> ParseMany(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            var rows = System.Text.Json.JsonSerializer.Deserialize<List<OcrTextRegion>>(json);
            return rows?
                .Where(r => !string.IsNullOrEmpty(r.Text) && r.Points is { Length: 8 })
                .Select(r => new OcrTextRegion(r.Text, r.Points!))
                .ToList() ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
