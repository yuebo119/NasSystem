// =============================================================================
// NasSystem.Infrastructure - Services/FileDeletedCacheCleanupHandler.cs
// 领域事件处理器：文件删除后清理其缩略图缓存
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using Microsoft.Extensions.Logging;
using NasSystem.Application.Files;
using NasSystem.Domain.Common;
using NasSystem.Domain.Events;
using NasSystem.Domain.Services;

/// <summary>
/// 文件删除事件的副作用处理：清除对应缩略图缓存条目。
/// </summary>
/// <remarks>
/// 【为什么这是领域事件机制的第一个订阅者】文件删除后其缩略图缓存成为磁盘泄漏源
/// （ITM-N5 审计确认缓存目录无 TTL/LRU）——经事件解耦使删除用例不必感知缩略图存在，
/// 后续新增副作用（审计日志/搜索索引移除）以新 handler 追加即可，不改删除用例。
/// </remarks>
public sealed class FileDeletedCacheCleanupHandler(
    IThumbnailService thumbnailService,
    ILogger<FileDeletedCacheCleanupHandler> logger) : IDomainEventHandler<FileDeletedEvent>
{
    public async Task HandleAsync(FileDeletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            // 缩略图缓存键以源文件路径派生，按前缀清除该文件全部尺寸的缓存
            await thumbnailService.ClearCacheAsync(domainEvent.Path);
        }
        catch (Exception ex)
        {
            // 缓存清理失败不阻断业务主流程——泄漏一条缓存的代价远小于删除失败
            logger.LogWarning(ex, "清理缩略图缓存失败: {Path}", domainEvent.Path);
        }
    }
}
