// =============================================================================
// NasSystem.Infrastructure - Files/ThumbnailCacheCleanupService.cs
// 缩略图缓存定期清理宿主服务（F-105）
// =============================================================================

namespace NasSystem.Infrastructure.Files;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// 启动 5 分钟后先清一次，之后每小时一次。单次失败只记日志等下一轮，
/// 缓存清理不该让宿主停摆。
/// </summary>
internal sealed class ThumbnailCacheCleanupService(
    ThumbnailCachePruner pruner,
    ILogger<ThumbnailCacheCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                pruner.PruneOnce();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "缩略图缓存清理异常（已降级，等待下一轮）");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}