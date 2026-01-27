// =============================================================================
// NasSystem.Application - IThumbnailService.cs
// 缩略图服务接口
// =============================================================================

namespace NasSystem.Application.Thumbnails;

using NasSystem.Shared;

/// <summary>
/// 缩略图生成服务接口
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// 获取文件缩略图
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="width">目标宽度</param>
    /// <param name="height">目标高度</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缩略图字节数组和内容类型</returns>
    Task<Result<ThumbnailResult>> GetThumbnailAsync(
        string filePath,
        int width,
        int height,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件是否支持生成缩略图
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    bool IsThumbnailSupported(string filePath);

    /// <summary>
    /// 清除文件的缩略图缓存
    /// </summary>
    /// <param name="filePath">文件路径</param>
    Task ClearCacheAsync(string filePath);

    /// <summary>
    /// 清除所有缩略图缓存
    /// </summary>
    Task ClearAllCacheAsync();
}

/// <summary>
/// 缩略图结果
/// </summary>
public record ThumbnailResult(
    byte[] Data,
    string ContentType,
    int Width,
    int Height,
    bool FromCache);

/// <summary>
/// 缩略图生成选项
/// </summary>
public record ThumbnailOptions
{
    /// <summary>
    /// 缩略图缓存目录
    /// </summary>
    public string CacheDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "NasSystem", "thumbnails");

    /// <summary>
    /// 默认宽度
    /// </summary>
    public int DefaultWidth { get; init; } = 200;

    /// <summary>
    /// 默认高度
    /// </summary>
    public int DefaultHeight { get; init; } = 200;

    /// <summary>
    /// 最大宽度
    /// </summary>
    public int MaxWidth { get; init; } = 1920;

    /// <summary>
    /// 最大高度
    /// </summary>
    public int MaxHeight { get; init; } = 1080;

    /// <summary>
    /// 缓存过期时间（小时）
    /// </summary>
    public int CacheExpirationHours { get; init; } = 24 * 7; // 7 天

    /// <summary>
    /// JPEG 质量 (1-100)
    /// </summary>
    public int JpegQuality { get; init; } = 85;

    /// <summary>
    /// 支持的图片扩展名
    /// </summary>
    public IReadOnlySet<string> SupportedImageExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    /// <summary>
    /// 支持的视频扩展名（用于提取视频帧）
    /// </summary>
    public IReadOnlySet<string> SupportedVideoExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm"
    };
}
