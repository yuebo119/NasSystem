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
    /// <param name="virtualPath">虚拟路径。缓存键的身份依据：所有清除入口
    /// （删除事件、回收站、端点）持有的都是虚拟路径，键必须同口径，
    /// 否则清除时前缀恒不匹配（2026-10-21 审计实证：删了文件缓存还在）</param>
    /// <param name="physicalPath">物理路径。只用于读源文件与新鲜度比对，
    /// 不进缓存键：物理路径随池迁移/移动会变，键要的是稳定身份</param>
    Task<Result<ThumbnailResult>> GetThumbnailAsync(
        string virtualPath,
        string physicalPath,
        int width,
        int height,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件是否支持生成缩略图
    /// </summary>
    bool IsThumbnailSupported(string filePath);

    /// <summary>
    /// 清除文件的缩略图缓存（按虚拟路径前缀，清掉该文件全部尺寸）
    /// </summary>
    Task ClearCacheAsync(string virtualPath);

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
    /// <summary>JPEG 编码质量（1-100）。原先硬编码在 ThumbnailService 里，
    /// appsettings 中的同名字键无人读取，两边必然漂移</summary>
    public int JpegQuality { get; set; } = 82;
    public string[] SupportedImageExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];
    public string[] SupportedVideoExtensions { get; set; } = [".mp4", ".avi", ".mkv", ".mov"];
}
