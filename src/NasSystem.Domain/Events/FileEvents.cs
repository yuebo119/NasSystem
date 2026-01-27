// =============================================================================
// NasSystem.Domain - FileEvents.cs
// 文件相关领域事件
// =============================================================================

namespace NasSystem.Domain.Events;

using NasSystem.Domain.Common;

/// <summary>
/// 文件创建事件
/// </summary>
public sealed record FileCreatedEvent(Guid FileId, string Path, long SizeBytes) : DomainEvent;

/// <summary>
/// 文件夹创建事件
/// </summary>
public sealed record FolderCreatedEvent(Guid FolderId, string Path) : DomainEvent;

/// <summary>
/// 文件重命名事件
/// </summary>
public sealed record FileRenamedEvent(Guid FileId, string OldName, string NewName) : DomainEvent;

/// <summary>
/// 文件移动事件
/// </summary>
public sealed record FileMovedEvent(Guid FileId, string OldPath, string NewPath) : DomainEvent;

/// <summary>
/// 文件删除事件
/// </summary>
public sealed record FileDeletedEvent(Guid FileId, string Path) : DomainEvent;

/// <summary>
/// 文件上传完成事件
/// </summary>
public sealed record FileUploadCompletedEvent(Guid FileId, string Path, long SizeBytes, TimeSpan Duration) : DomainEvent;

/// <summary>
/// 文件下载事件
/// </summary>
public sealed record FileDownloadedEvent(Guid FileId, string Path, Guid UserId) : DomainEvent;
