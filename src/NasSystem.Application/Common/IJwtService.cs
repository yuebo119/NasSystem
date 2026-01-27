// =============================================================================
// NasSystem.Application - IJwtService.cs
// JWT 服务接口
// =============================================================================

namespace NasSystem.Application.Common;

using System.Security.Claims;
using NasSystem.Domain.Entities;

/// <summary>
/// JWT 服务接口
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// 生成访问令牌
    /// </summary>
    /// <param name="user">用户实体</param>
    /// <returns>JWT 令牌字符串</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    /// <returns>刷新令牌字符串</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// 验证令牌并返回 ClaimsPrincipal
    /// </summary>
    /// <param name="token">JWT 令牌</param>
    /// <returns>ClaimsPrincipal 或 null (验证失败)</returns>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>
    /// 从令牌获取用户 ID
    /// </summary>
    /// <param name="token">JWT 令牌</param>
    /// <returns>用户 ID 或 null</returns>
    Guid? GetUserIdFromToken(string token);

    /// <summary>
    /// 获取令牌过期时间
    /// </summary>
    /// <param name="token">JWT 令牌</param>
    /// <returns>过期时间</returns>
    DateTime? GetTokenExpiration(string token);

    /// <summary>
    /// 检查令牌是否过期
    /// </summary>
    /// <param name="token">JWT 令牌</param>
    /// <returns>是否过期</returns>
    bool IsTokenExpired(string token);
}

/// <summary>
/// 令牌结果
/// </summary>
public sealed record TokenResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);
