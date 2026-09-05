// =============================================================================
// NasSystem.Domain - Events/StorageEvents.cs
// 存储池领域事件（当前无订阅者——扩展点预留，见 FileEvents.cs 声明）
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;

/// <summary>
/// 存储池创建事件
/// </summary>
public sealed record StoragePoolCreatedEvent(Guid PoolId, string Name) : DomainEvent;
