// =============================================================================
// NasSystem.Infrastructure - Media/MediaMetadataExtractor.cs
// 媒体元数据提取（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

using NasSystem.Infrastructure.Files;

/// <summary>
/// 托管媒体元数据提取。三种格式族各自的最小解析：
/// 图片走 SkiaSharp（复用已有解码器），MP4/MOV 与 MKV/WebM 走
/// 盒/EBML 结构直读，MP3 走帧头统计。
/// </summary>
/// <remarks>
/// 为什么是手写解析而不是 FFmpeg：NAS 场景要的是时长与尺寸这两个数，
/// 引 FFmpeg 子进程要多背一个 100MB 级系统依赖与进程管理成本，
/// 而三种容器各自读几十个字节就能拿到数。覆盖不到格式
/// （FLAC/OGG/WMV 等）返回 null，展示层降级为"无元数据"，
/// 需要全格式时再上 FFmpeg（ROADMAP 的转码二期本来就要引）。
/// 全部只读文件头/盒结构，不做全文件解码：大文件也是毫秒级
/// </remarks>
public static class MediaMetadataExtractor
{
    /// <summary>
    /// 按扩展名分流提取。未覆盖的格式返回全 null 的条目（不是失败）
    /// </summary>
    public static MediaMetadata Extract(string filePath, string? extension)
    {
        var ext = extension?.ToLowerInvariant() ?? System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif" => ExtractImage(filePath),
                ".mp4" or ".mov" or ".m4v" => Mp4Parser.Extract(filePath),
                ".mkv" or ".webm" => MkvParser.Extract(filePath),
                ".mp3" => Mp3Parser.Extract(filePath),
                _ => MediaMetadata.Empty
            };
        }
        catch
        {
            // 解析失败（截断文件、非标准变种）按无元数据处理：
            // 元数据是增强信息，不该让列表页因为一个怪文件整页 500
            return MediaMetadata.Empty;
        }
    }

    /// <summary>
    /// 图片：只读文件头取尺寸（SKCodec.Info），不解码像素。
    /// 原实现 SKBitmap.Decode 为两个数字解全图，实测一张 6000x4000 JPEG
    /// 在工作集上多占 94MB；媒体页一次请求最多 200 个路径，是成比例的分配浪费。
    /// </summary>
    private static MediaMetadata ExtractImage(string filePath)
    {
        var dimensions = ImageDecoder.ReadDimensions(filePath);
        return dimensions is { } size
            ? new MediaMetadata(size.Width, size.Height, null)
            : MediaMetadata.Empty;
    }
}

/// <summary>媒体元数据。未取到的字段为 null（格式不支持或解析失败）</summary>
/// <param name="Width">像素宽（图片/视频）</param>
/// <param name="Height">像素高（图片/视频）</param>
/// <param name="DurationSeconds">时长秒（视频/音频）</param>
public sealed record MediaMetadata(int? Width, int? Height, double? DurationSeconds)
{
    public static readonly MediaMetadata Empty = new(null, null, null);

    public bool HasAny => Width is not null || Height is not null || DurationSeconds is not null;
}
