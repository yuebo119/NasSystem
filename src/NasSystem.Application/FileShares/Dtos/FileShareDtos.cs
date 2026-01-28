// =============================================================================
// NasSystem.Application - FileShareDtos.cs
// 文件分享相关 DTO
// =============================================================================

namespace NasSystem.Application.FileShares.Dtos;

using NasSystem.Domain.Enums;

/// <summary>
/// 文件分享 DTO
/// </summary>
public sealed record FileShareDto
{
    public required Guid Id { get; init; }
    public required string ShareCode { get; init; }
    public required string ShareUrl { get; init; }
    public required Guid FileItemId { get; init; }
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required bool IsFolder { get; init; }
    public long? SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public required ShareAccessType AccessType { get; init; }
    public required PermissionLevel Permission { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
    public required int DownloadCount { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsExpired { get; init; }
    public required bool IsDownloadLimitReached { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// 公开分享信息 DTO（不包含敏感信息）
/// </summary>
public sealed record PublicShareInfoDto
{
    public required string ShareCode { get; init; }
    public required string FileName { get; init; }
    public required bool IsFolder { get; init; }
    public long? SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public required ShareAccessType AccessType { get; init; }
    public required PermissionLevel Permission { get; init; }
    public required bool RequiresPassword { get; init; }
    public required bool IsExpired { get; init; }
    public required bool IsDownloadLimitReached { get; init; }
    public required bool IsAccessible { get; init; }
    public string? CreatedByUserName { get; init; }
}

/// <summary>
/// 创建文件分享请求
/// </summary>
public sealed record CreateFileShareRequest
{
    public required Guid FileItemId { get; init; }
    public ShareAccessType AccessType { get; init; } = ShareAccessType.Public;
    public string? Password { get; init; }
    public PermissionLevel Permission { get; init; } = PermissionLevel.Read;
    public DateTime? ExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
}

/// <summary>
/// 更新文件分享请求
/// </summary>
public sealed record UpdateFileShareRequest
{
    public ShareAccessType? AccessType { get; init; }
    public string? Password { get; init; }
    public bool? ClearPassword { get; init; }
    public PermissionLevel? Permission { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool? ClearExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
    public bool? ClearMaxDownloads { get; init; }
    public bool? IsEnabled { get; init; }
}

/// <summary>
/// 验证密码请求
/// </summary>
public sealed record VerifySharePasswordRequest
{
    public required string Password { get; init; }
}

/// <summary>
/// 分享文件夹内容 DTO
/// </summary>
public sealed record ShareFolderContentDto
{
    public required string ShareCode { get; init; }
    public required string CurrentPath { get; init; }
    public required IReadOnlyList<ShareFileItemDto> Items { get; init; }
}

/// <summary>
/// 分享文件项 DTO
/// </summary>
public sealed record ShareFileItemDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool IsFolder { get; init; }
    public long? SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public string? MimeType { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
