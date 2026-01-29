// =============================================================================
// NasSystem.Domain - StorageEvents.cs
// 存储相关领域事件（简化版）
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;

/// <summary>
/// 存储池创建事件
/// </summary>
public sealed record StoragePoolCreatedEvent(Guid PoolId, string Name) : DomainEvent;

/// <summary>
/// 存储池删除事件
/// </summary>
public sealed record StoragePoolDeletedEvent(Guid PoolId, string Name) : DomainEvent;

/// <summary>
/// 存储空间不足警告事件
/// </summary>
public sealed record StorageLowSpaceWarningEvent(
    Guid PoolId,
    string Name,
    long AvailableBytes,
    double UsagePercentage) : DomainEvent;
