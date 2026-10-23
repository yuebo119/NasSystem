// =============================================================================
// NasSystem.Infrastructure - Files/ThumbnailCachePruner.cs
// 缩略图缓存过期清理（F-105）
// =============================================================================

namespace NasSystem.Infrastructure.Files;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NasSystem.Application.Files;
using NasSystem.Domain.Services;

/// <summary>
/// 按 <see cref="ThumbnailOptions.CacheExpirationHours"/> 清理过期缩略图。
/// </summary>
/// <remarks>
/// 原先 TTL 只在读取缓存时判定：过期文件不会被删，只是不再命中；缓存目录
/// 因此只增不减。这里用同一 TTL 做主动清理，删除失败（正在被读取/占用）
/// 留给下一轮，不阻断。
/// </remarks>
public sealed class ThumbnailCachePruner(
    IOptions<ThumbnailOptions> options,
    IDateTimeProvider dateTime,
    ILogger<ThumbnailCachePruner> logger)
{
    /// <summary>执行一次清理，返回删除的文件数。测试直接调用。</summary>
    public int PruneOnce()
    {
        var settings = options.Value;
        var directory = settings.CacheDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        var ttlHours = settings.CacheExpirationHours > 0 ? settings.CacheExpirationHours : 1;
        var cutoff = dateTime.UtcNow.AddHours(-ttlHours);
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*.jpg", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                    continue;

                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // 正在被读取/占用：下一轮再试
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "缩略图缓存清理失败: {File}", file);
            }
        }

        if (removed > 0)
        {
            logger.LogInformation(
                "缩略图缓存清理: 删除 {Removed} 个超过 {Hours} 小时的文件", removed, ttlHours);
        }

        return removed;
    }
}