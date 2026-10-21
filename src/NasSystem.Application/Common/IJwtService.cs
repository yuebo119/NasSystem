// =============================================================================
// NasSystem.Application - IJwtService.cs
// JWT 服务接口
// =============================================================================

namespace NasSystem.Application.Common;

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
    /// <param name="lifetime">
    /// 令牌有效期。传 null 用配置的 AccessTokenExpirationMinutes；
    /// "记住我"登录由调用方传更长的有效期（见 <see cref="JwtSettings.RememberMeExpirationDays"/>）
    /// </param>
    /// <returns>JWT 令牌字符串</returns>
    string GenerateAccessToken(User user, TimeSpan? lifetime = null);

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    /// <returns>刷新令牌字符串</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// 获取令牌过期时间
    /// </summary>
    /// <param name="token">JWT 令牌</param>
    /// <returns>过期时间</returns>
    DateTime? GetTokenExpiration(string token);
}
