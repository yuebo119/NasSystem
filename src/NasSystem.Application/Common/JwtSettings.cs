// =============================================================================
// NasSystem.Application - JwtSettings.cs
// JWT 认证配置
// =============================================================================

namespace NasSystem.Application.Common;

/// <summary>
/// JWT 认证配置
/// </summary>
public sealed class JwtSettings
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// 密钥 (最少 32 字符)
    /// </summary>
    public string SecretKey { get; set; } = "NasSystem_Super_Secret_Key_2025_Must_Be_At_Least_32_Characters";

    /// <summary>
    /// 发行者
    /// </summary>
    public string Issuer { get; set; } = "NasSystem";

    /// <summary>
    /// 受众
    /// </summary>
    public string Audience { get; set; } = "NasSystemUsers";

    /// <summary>
    /// 访问令牌过期时间 (分钟)
    /// </summary>
    public int AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// 刷新令牌过期时间 (天)
    /// </summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;

    /// <summary>
    /// 是否验证发行者
    /// </summary>
    public bool ValidateIssuer { get; set; } = true;

    /// <summary>
    /// 是否验证受众
    /// </summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>
    /// 是否验证生命周期
    /// </summary>
    public bool ValidateLifetime { get; set; } = true;

    /// <summary>
    /// 时钟偏移 (秒)
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
