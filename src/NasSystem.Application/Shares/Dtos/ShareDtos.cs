// =============================================================================
// NasSystem.Application - ShareDtos.cs
// 共享相关 DTO
// =============================================================================

namespace NasSystem.Application.Shares.Dtos;

using NasSystem.Domain.Enums;

/// <summary>
/// 共享 DTO
/// </summary>
public sealed record ShareDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Path { get; init; }
    public required ShareType ShareType { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsPublic { get; init; }
    public required bool IsReadOnly { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public required Guid StoragePoolId { get; init; }
    public string? StoragePoolName { get; init; }
    public long? QuotaBytes { get; init; }
    public string? QuotaDisplay { get; init; }
    public string? SmbShareName { get; init; }
    public required bool EnableRecycleBin { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public IReadOnlyList<SharePermissionDto>? Permissions { get; init; }
}

/// <summary>
/// 共享权限 DTO
/// </summary>
public sealed record SharePermissionDto
{
    public required Guid Id { get; init; }
    public required Guid ShareId { get; init; }
    public Guid? UserId { get; init; }
    public string? UserName { get; init; }
    public string? GroupName { get; init; }
    public required PermissionLevel Permission { get; init; }
    public required bool IsDeny { get; init; }
}

/// <summary>
/// 创建共享请求
/// </summary>
public sealed record CreateShareRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Path { get; init; }
    public ShareType ShareType { get; init; } = ShareType.Smb;
    public required Guid StoragePoolId { get; init; }
    public bool IsPublic { get; init; }
    public bool IsReadOnly { get; init; }
    public long? QuotaBytes { get; init; }
    public bool EnableRecycleBin { get; init; } = true;
}

/// <summary>
/// 更新共享请求
/// </summary>
public sealed record UpdateShareRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsPublic { get; init; }
    public bool? IsReadOnly { get; init; }
    public long? QuotaBytes { get; init; }
    public bool? EnableRecycleBin { get; init; }
}

/// <summary>
/// 设置共享权限请求
/// </summary>
public sealed record SetSharePermissionsRequest
{
    public required IReadOnlyList<PermissionEntry> Permissions { get; init; }
}

/// <summary>
/// 权限条目
/// </summary>
public sealed record PermissionEntry
{
    public Guid? UserId { get; init; }
    public string? GroupName { get; init; }
    public required PermissionLevel Permission { get; init; }
    public bool IsDeny { get; init; }
}
