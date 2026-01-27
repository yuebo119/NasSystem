// =============================================================================
// NasSystem.Domain - StorageEvents.cs
// 存储相关领域事件
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 存储池创建事件
/// </summary>
public sealed record StoragePoolCreatedEvent(Guid PoolId, string Name) : DomainEvent;

/// <summary>
/// 存储池状态变更事件
/// </summary>
public sealed record StoragePoolStatusChangedEvent(
    Guid PoolId,
    StoragePoolStatus OldStatus,
    StoragePoolStatus NewStatus) : DomainEvent;

/// <summary>
/// 存储池删除事件
/// </summary>
public sealed record StoragePoolDeletedEvent(Guid PoolId, string Name) : DomainEvent;

/// <summary>
/// 磁盘添加事件
/// </summary>
public sealed record DiskAddedEvent(Guid PoolId, Guid DiskId, string SerialNumber) : DomainEvent;

/// <summary>
/// 磁盘移除事件
/// </summary>
public sealed record DiskRemovedEvent(Guid PoolId, Guid DiskId, string SerialNumber) : DomainEvent;

/// <summary>
/// 磁盘状态变更事件
/// </summary>
public sealed record DiskStatusChangedEvent(
    Guid DiskId,
    DiskStatus OldStatus,
    DiskStatus NewStatus) : DomainEvent;

/// <summary>
/// 存储空间不足警告事件
/// </summary>
public sealed record StorageLowSpaceWarningEvent(
    Guid PoolId,
    string Name,
    long AvailableBytes,
    double UsagePercentage) : DomainEvent;
