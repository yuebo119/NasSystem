// =============================================================================
// NasSystem.Application - UserDtos.cs
// 用户相关 DTO
// =============================================================================

namespace NasSystem.Application.Users.Dtos;

/// <summary>
/// 用户信息 DTO
/// </summary>
public sealed record UserDto(
    Guid Id,
    string Username,
    string? DisplayName,
    string? Email,
    string Role,
    bool IsEnabled,
    DateTime? LastLoginAt,
    long? QuotaBytes,
    long UsedBytes,
    string? QuotaDisplay,
    string? UsedDisplay,
    double? UsagePercentage,
    DateTime CreatedAt);

/// <summary>
/// 用户简要信息 DTO
/// </summary>
public sealed record UserSummaryDto(
    Guid Id,
    string Username,
    string? DisplayName,
    string Role,
    bool IsEnabled);

/// <summary>
/// 创建用户请求
/// </summary>
public sealed record CreateUserDto(
    string Username,
    string Password,
    string? DisplayName = null,
    string? Email = null,
    string Role = "User",
    long? QuotaBytes = null);

/// <summary>
/// 更新用户请求
/// </summary>
public sealed record UpdateUserDto(
    string? DisplayName = null,
    string? Email = null,
    string? Role = null,
    bool? IsEnabled = null,
    long? QuotaBytes = null);

/// <summary>
/// 登录请求
/// </summary>
public sealed record LoginDto(
    string Username,
    string Password,
    bool RememberMe = false);

/// <summary>
/// 登录结果
/// </summary>
public sealed record LoginResultDto(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);
