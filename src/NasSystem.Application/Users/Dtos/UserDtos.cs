// =============================================================================
// NasSystem.Application - UserDtos.cs
// 用户相关 DTO
// =============================================================================

namespace NasSystem.Application.Users.Dtos;

using System.Text.Json.Serialization;

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
public sealed class LoginDto
{
    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;
    
    [JsonPropertyName("password")]
    public string Password { get; init; } = string.Empty;
    
    [JsonPropertyName("rememberMe")]
    public bool RememberMe { get; init; }
}

/// <summary>
/// 登录结果
/// </summary>
public sealed record LoginResultDto(
    string Token,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

/// <summary>
/// 用户全站统计（管理页统计卡）。与分页响应解耦：分页只含当前页数据，
/// 统计永远全站口径。2026-10-21 审计前前端对当前页 20 条聚合冒充总量，
/// 用户数超过一页时三张卡随翻页跳动
/// </summary>
public sealed record UserStatsDto(
    int TotalUsers,
    int AdminCount,
    int EnabledCount,
    long TotalUsedBytes);
