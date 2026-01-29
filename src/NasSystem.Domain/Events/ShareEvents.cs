// =============================================================================
// NasSystem.Domain - ShareEvents.cs
// 文件分享相关领域事件
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;

/// <summary>
/// 文件分享创建事件
/// </summary>
public sealed record FileShareCreatedEvent(Guid FileShareId, Guid FileItemId, string ShareCode) : DomainEvent;

/// <summary>
/// 文件分享访问事件
/// </summary>
public sealed record FileShareAccessedEvent(Guid FileShareId, string ShareCode) : DomainEvent;

/// <summary>
/// 文件分享下载事件
/// </summary>
public sealed record FileShareDownloadedEvent(Guid FileShareId, string ShareCode, int DownloadCount) : DomainEvent;

/// <summary>
/// 文件分享禁用事件
/// </summary>
public sealed record FileShareDisabledEvent(Guid FileShareId, string ShareCode) : DomainEvent;

/// <summary>
/// 文件分享过期事件
/// </summary>
public sealed record FileShareExpiredEvent(Guid FileShareId, string ShareCode) : DomainEvent;
