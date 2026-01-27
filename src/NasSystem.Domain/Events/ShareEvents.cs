// =============================================================================
// NasSystem.Domain - ShareEvents.cs
// 共享相关领域事件
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;

/// <summary>
/// 共享创建事件
/// </summary>
public sealed record ShareCreatedEvent(Guid ShareId, string Name) : DomainEvent;

/// <summary>
/// 共享更新事件
/// </summary>
public sealed record ShareUpdatedEvent(Guid ShareId, string Name) : DomainEvent;

/// <summary>
/// 共享删除事件
/// </summary>
public sealed record ShareDeletedEvent(Guid ShareId, string Name) : DomainEvent;

/// <summary>
/// 共享权限变更事件
/// </summary>
public sealed record SharePermissionChangedEvent(Guid ShareId, Guid? UserId, string? GroupName) : DomainEvent;
