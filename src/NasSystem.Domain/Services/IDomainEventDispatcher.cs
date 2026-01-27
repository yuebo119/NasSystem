// =============================================================================
// NasSystem.Domain - IDomainEventDispatcher.cs
// 领域事件分发器接口
// =============================================================================

namespace NasSystem.Domain.Services;

using NasSystem.Domain.Common;

/// <summary>
/// 领域事件分发器接口
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// 分发领域事件
    /// </summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分发多个领域事件
    /// </summary>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// 领域事件处理器接口
/// </summary>
/// <typeparam name="TEvent">事件类型</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>
    /// 处理领域事件
    /// </summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
