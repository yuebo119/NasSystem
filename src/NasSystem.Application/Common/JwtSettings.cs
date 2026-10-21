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
    /// 密钥 (最少 32 字符)。只允许经环境变量/用户机密注入；
    /// 缺失时生产环境启动即失败，Development 环境自动生成临时随机密钥（ITM-N1）
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

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
    /// "记住我"登录的访问令牌过期时间 (天)。勾选时用这个更长的有效期，
    /// 不勾选回落到 AccessTokenExpirationMinutes。默认 7 天与刷新令牌同档
    /// </summary>
    public int RememberMeExpirationDays { get; set; } = 7;

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
