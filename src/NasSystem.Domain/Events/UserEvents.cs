// =============================================================================
// NasSystem.Domain - UserEvents.cs
// 用户相关领域事件（当前无订阅者——扩展点预留，见 FileEvents.cs 声明）
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 用户创建事件
/// </summary>
public sealed record UserCreatedEvent(Guid UserId, string Username) : DomainEvent;

/// <summary>
/// 用户密码变更事件
/// </summary>
public sealed record UserPasswordChangedEvent(Guid UserId) : DomainEvent;

/// <summary>
/// 用户角色变更事件
/// </summary>
public sealed record UserRoleChangedEvent(Guid UserId, UserRole OldRole, UserRole NewRole) : DomainEvent;



