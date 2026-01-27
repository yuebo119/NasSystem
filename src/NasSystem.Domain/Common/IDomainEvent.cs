// =============================================================================
// NasSystem.Domain - IDomainEvent.cs
// 领域事件接口
// =============================================================================

namespace NasSystem.Domain.Common;

/// <summary>
/// 领域事件标记接口
/// </summary>
public interface IDomainEvent
{
    /// <summary>
    /// 事件发生时间 (UTC)
    /// </summary>
    DateTime OccurredAt { get; }

    /// <summary>
    /// 事件唯一标识
    /// </summary>
    Guid EventId { get; }
}

/// <summary>
/// 领域事件基类
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public Guid EventId { get; } = Guid.NewGuid();
}
