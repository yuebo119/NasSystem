// =============================================================================
// NasSystem.Infrastructure - DomainEventDispatcher.cs
// 领域事件分发器实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Domain.Common;
using NasSystem.Domain.Services;

/// <summary>
/// 领域事件分发器实现
/// </summary>
/// <remarks>
/// 带"已知无处理器事件类型"快路径：MakeGenericType 加 GetServices 加 GetMethod 加 Invoke
/// 这套反射组合每事件都要走一遍，而本项目已注册的处理器只有一个
/// （FileDeletedEvent 的缓存清理）。文件创建、重命名、移动、文件夹创建这些事件
/// 一个处理器都没有，每次扫描同步入库上万个文件就是上万次纯浪费的反射加 DI 查找。
/// 首次遇到某类型时照原路径走一遍，发现零处理器就记入快路径集合，后续直接返回。
/// 处理器注册只发生在启动阶段，运行期集合不变，缓存安全。
/// </remarks>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    /// <summary>已确认没有任何处理器的事件类型。命中即整条分发路径跳过。</summary>
    private readonly ConcurrentDictionary<Type, bool> _handlerlessEventTypes = new();

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var eventType = domainEvent.GetType();

        if (_handlerlessEventTypes.ContainsKey(eventType))
            return;

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        // 获取所有处理器
        var handlers = serviceProvider.GetServices(handlerType);

        if (!handlers.Any())
        {
            _handlerlessEventTypes[eventType] = true;
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                var handleMethod = handlerType.GetMethod("HandleAsync");
                if (handleMethod is not null)
                {
                    var task = (Task?)handleMethod.Invoke(handler, [domainEvent, cancellationToken]);
                    if (task is not null)
                        await task;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "处理领域事件 {EventType} 时发生错误", eventType.Name);
                // 继续处理其他处理器，不中断
            }
        }

        logger.LogDebug("领域事件 {EventType} 已分发, EventId: {EventId}",
            eventType.Name, domainEvent.EventId);
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            await DispatchAsync(domainEvent, cancellationToken);
        }
    }
}
