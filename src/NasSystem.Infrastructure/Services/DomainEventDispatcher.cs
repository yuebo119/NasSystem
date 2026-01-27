// =============================================================================
// NasSystem.Infrastructure - DomainEventDispatcher.cs
// 领域事件分发器实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Domain.Common;
using NasSystem.Domain.Services;

/// <summary>
/// 领域事件分发器实现
/// </summary>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

        // 获取所有处理器
        var handlers = serviceProvider.GetServices(handlerType);

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
