// =============================================================================
// NasSystem.Application - Files/IThumbnailService.cs
// 缩略图服务接口（自 Thumbnails/ 独立目录归位至 Files 域，ITM-N33）
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Domain.Services;
using NasSystem.Shared;

/// <summary>
/// 缩略图生成服务接口——实现位于 Infrastructure/Files/ThumbnailService（SkiaSharp）
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// 获取文件缩略图（带缓存）
    /// </summary>
    Task<Result<ThumbnailResult>> GetThumbnailAsync(
        string filePath,
        int width,
        int height,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件是否支持生成缩略图
    /// </summary>
    bool IsThumbnailSupported(string filePath);

    /// <summary>
    /// 清除文件的缩略图缓存
    /// </summary>
    Task ClearCacheAsync(string filePath);

    /// <summary>
    /// 清除全部缩略图缓存
    /// </summary>
    Task ClearAllCacheAsync();
}

/// <summary>
/// 缩略图生成结果
/// </summary>
public sealed record ThumbnailResult(
    byte[] Data,
    string ContentType,
    int Width,
    int Height,
    bool FromCache);

/// <summary>
/// 缩略图配置选项
/// </summary>
public sealed class ThumbnailOptions
{
    public string CacheDirectory { get; set; } = "temp/thumbnails";
    public int DefaultWidth { get; set; } = 200;
    public int DefaultHeight { get; set; } = 200;
    public int MaxWidth { get; set; } = 1920;
    public int MaxHeight { get; set; } = 1080;
    public int CacheExpirationHours { get; set; } = 168;
    public string[] SupportedImageExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];
    public string[] SupportedVideoExtensions { get; set; } = [".mp4", ".avi", ".mkv", ".mov"];
}
