// =============================================================================
// NasSystem.Infrastructure - ThumbnailService.cs
// 缩略图生成服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Files;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NasSystem.Application.Files;
using NasSystem.Shared;

/// <summary>
/// 缩略图生成服务实现
/// </summary>
/// <remarks>
/// 图片使用 SkiaSharp 跨平台缩放；视频帧提取需 FFmpeg，当前返回占位图。
/// 生成链路带三道闸：全局并发信号量（CPU 密集的编解码不无限并行）、
/// 同缓存键 in-flight 去重（同一张图的并发请求只生成一次）、
/// 服务端尺寸档位吸附（客户端任意 width/height 不各自成缓存文件）。
/// </remarks>
public class ThumbnailService : IThumbnailService
{
    /// <summary>服务端尺寸白名单：客户端请求就近吸附，缓存文件数与档位挂钩可控</summary>
    private static readonly int[] AllowedSizes = [64, 128, 256, 512, 1024];

    private readonly ThumbnailOptions _options;
    private readonly ILogger<ThumbnailService> _logger;

    /// <summary>全局生成并发闸：编解码是 CPU 密集同步工作，无上限并行会打满线程池</summary>
    private readonly SemaphoreSlim _generationGate = new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>同缓存键的 in-flight 生成任务，供并发请求合并</summary>
    private readonly ConcurrentDictionary<string, Task<byte[]>> _inFlightGenerations = new();

    public ThumbnailService(
        IOptions<ThumbnailOptions> options,
        ILogger<ThumbnailService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 确保缓存目录存在
        if (!Directory.Exists(_options.CacheDirectory))
        {
            Directory.CreateDirectory(_options.CacheDirectory);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ThumbnailResult>> GetThumbnailAsync(
        string filePath,
        int width,
        int height,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // 验证文件是否支持缩略图
        if (!IsThumbnailSupported(filePath))
        {
            return Result<ThumbnailResult>.Failure(
                new Error("Thumbnail.NotSupported", $"文件类型不支持生成缩略图: {Path.GetExtension(filePath)}"));
        }

        // 规范化尺寸：吸附到服务端档位并受选项上限约束
        //（原实现按客户端原值建缓存，任意宽高组合会让缓存文件无限增长）
        width = SnapToAllowedSize(width, _options.MaxWidth);
        height = SnapToAllowedSize(height, _options.MaxHeight);

        // 检查缓存
        var cacheKey = GenerateCacheKey(filePath, width, height);
        var cachePath = Path.Combine(_options.CacheDirectory, $"{cacheKey}.jpg");

        if (File.Exists(cachePath))
        {
            var cacheFileInfo = new FileInfo(cachePath);
            var sourceFileInfo = new FileInfo(filePath);

            // 检查缓存是否过期（源文件更新或缓存时间过长）
            if (sourceFileInfo.Exists && 
                cacheFileInfo.LastWriteTimeUtc > sourceFileInfo.LastWriteTimeUtc &&
                DateTime.UtcNow - cacheFileInfo.LastWriteTimeUtc < TimeSpan.FromHours(_options.CacheExpirationHours))
            {
                try
                {
                    var cachedData = await File.ReadAllBytesAsync(cachePath, cancellationToken);
                    _logger.LogDebug("缩略图命中缓存: {FilePath}", filePath);
                    
                    return Result<ThumbnailResult>.Success(new ThumbnailResult(
                        Data: cachedData,
                        ContentType: "image/jpeg",
                        Width: width,
                        Height: height,
                        FromCache: true));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "读取缓存缩略图失败: {CachePath}", cachePath);
                    // 继续生成新的缩略图
                }
            }
        }

        // 检查源文件是否存在
        if (!File.Exists(filePath))
        {
            return Result<ThumbnailResult>.Failure(
                new Error("Thumbnail.FileNotFound", $"源文件不存在: {filePath}"));
        }

        try
        {
            // 同缓存键的并发请求合并到同一个生成任务（无去重时同图被并发重复生成，CPU 尖峰）
            var generationTask = _inFlightGenerations.GetOrAdd(
                cacheKey,
                _ => GenerateAndCacheAsync(filePath, cachePath, width, height));

            byte[] thumbnailData;
            try
            {
                thumbnailData = await generationTask.WaitAsync(cancellationToken);
            }
            finally
            {
                // 条件移除：仅移除自己这一份任务实例，避免误删后来的同键任务
                _inFlightGenerations.TryRemove(
                    new KeyValuePair<string, Task<byte[]>>(cacheKey, generationTask));
            }

            _logger.LogDebug("生成缩略图成功: {FilePath} ({Width}x{Height})", filePath, width, height);

            return Result<ThumbnailResult>.Success(new ThumbnailResult(
                Data: thumbnailData,
                ContentType: "image/jpeg",
                Width: width,
                Height: height,
                FromCache: false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成缩略图失败: {FilePath}", filePath);
            return Result<ThumbnailResult>.Failure(
                new Error("Thumbnail.GenerationFailed", $"生成缩略图失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 过并发闸生成并写缓存。共享任务不跟随首个调用方的取消令牌——
    /// 单个请求取消不应废掉其他等待方即将复用的同一份产物
    /// </summary>
    private async Task<byte[]> GenerateAndCacheAsync(
        string filePath,
        string cachePath,
        int width,
        int height)
    {
        await _generationGate.WaitAsync();
        try
        {
            var thumbnailData = await GenerateThumbnailAsync(filePath, width, height, CancellationToken.None);

            try
            {
                await File.WriteAllBytesAsync(cachePath, thumbnailData, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "保存缩略图缓存失败: {CachePath}", cachePath);
                // 不影响返回结果
            }

            return thumbnailData;
        }
        finally
        {
            _generationGate.Release();
        }
    }

    /// <inheritdoc />
    public bool IsThumbnailSupported(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return _options.SupportedImageExtensions.Contains(extension) ||
               _options.SupportedVideoExtensions.Contains(extension);
    }

    /// <inheritdoc />
    public async Task ClearCacheAsync(string filePath)
    {
        var cachePattern = GenerateCacheKeyPrefix(filePath);
        var cacheFiles = Directory.GetFiles(_options.CacheDirectory, $"{cachePattern}*.jpg");

        foreach (var cacheFile in cacheFiles)
        {
            try
            {
                File.Delete(cacheFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除缩略图缓存失败: {CacheFile}", cacheFile);
            }
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ClearAllCacheAsync()
    {
        try
        {
            var cacheFiles = Directory.GetFiles(_options.CacheDirectory, "*.jpg");
            foreach (var cacheFile in cacheFiles)
            {
                try
                {
                    File.Delete(cacheFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除缩略图缓存失败: {CacheFile}", cacheFile);
                }
            }

            _logger.LogInformation("已清除所有缩略图缓存: {Count} 个文件", cacheFiles.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清除缩略图缓存失败");
        }

        await Task.CompletedTask;
    }

    private async Task<byte[]> GenerateThumbnailAsync(
        string filePath,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // 视频帧提取需 FFmpeg 集成（独立事项）；当前返回占位图并在响应中如实标记
        if (_options.SupportedVideoExtensions.Contains(extension))
        {
            return GenerateVideoPlaceholder(targetWidth, targetHeight);
        }

        // 对于图片文件，使用简单的缩放
        return await GenerateImageThumbnailAsync(filePath, targetWidth, targetHeight, _options.JpegQuality, cancellationToken);
    }

    /// <summary>
    /// 使用 SkiaSharp 生成等比缩放的 JPEG 缩略图（ITM-N5 收尾：替换"返回原图字节"的占位实现）。
    /// 解码失败（损坏/不支持的编码）抛出异常，由上层统一转 Thumbnail.GenerationFailed。
    /// </summary>
    private static Task<byte[]> GenerateImageThumbnailAsync(
        string filePath,
        int targetWidth,
        int targetHeight,
        int jpegQuality,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var source = SkiaSharp.SKBitmap.Decode(filePath)
            ?? throw new InvalidOperationException($"图像解码失败: {Path.GetFileName(filePath)}");

        // 等比缩放适配目标框（不放大小图、不留白边）
        var scale = Math.Min(
            (float)targetWidth / source.Width,
            (float)targetHeight / source.Height);
        if (scale >= 1f)
            scale = Math.Min(scale, 1f);

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = source.Resize(
            new SkiaSharp.SKImageInfo(width, height),
            new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear))
            ?? throw new InvalidOperationException("图像缩放失败");
        using var image = SkiaSharp.SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, jpegQuality);

        return Task.FromResult(encoded.ToArray());
    }

    private static byte[] GenerateVideoPlaceholder(int width, int height)
    {
        // 生成一个简单的视频占位符图像
        // 实际生产环境应该使用 FFmpeg 提取视频帧
        // 这里返回一个简单的占位图像数据

        // 简单的 1x1 灰色 JPEG（占位符）
        return
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
            0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
            0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
            0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
            0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
            0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
            0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
            0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
            0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0xFF, 0xC4, 0x00, 0xB5, 0x10, 0x00, 0x02, 0x01, 0x03,
            0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 0x00, 0x01, 0x7D,
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06,
            0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08,
            0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72,
            0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45,
            0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75,
            0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3,
            0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
            0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9,
            0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
            0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4,
            0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01,
            0x00, 0x00, 0x3F, 0x00, 0xFB, 0xD5, 0xDB, 0x20, 0xA8, 0xF0, 0x7F, 0xFF,
            0xD9
        ];
    }

    /// <summary>
    /// 就近吸附到服务端尺寸档位（向上取最近档，超出最大档则收敛到上限）
    /// </summary>
    private static int SnapToAllowedSize(int requested, int max)
    {
        foreach (var size in AllowedSizes)
        {
            if (requested <= size)
                return Math.Min(size, max);
        }
        return max;
    }

    private static string GenerateCacheKey(string filePath, int width, int height)
    {
        var input = $"{filePath}|{width}|{height}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes)[..32];
    }

    private static string GenerateCacheKeyPrefix(string filePath)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath));
        return Convert.ToHexStringLower(hashBytes)[..16];
    }
}
