// =============================================================================
// NasSystem.Api - AuthenticationExtensions.cs
// 认证配置扩展
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NasSystem.Application.Common;

/// <summary>
/// 认证配置扩展方法
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// 添加 JWT 认证服务
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // 绑定 JWT 配置
        var jwtSettings = new JwtSettings();
        configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        // ITM-N1：密钥只能来自环境变量/用户机密。占位符或缺失时：
        // 生产环境 fail-fast；非生产环境自动生成进程级临时随机密钥并告警。
        // 必须在服务注册期执行（AddJwtBearer 的 options 委托是惰性的，届时容器已只读）。
        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey == "<SET-VIA-ENV>")
        {
            if (!environment.IsProduction())
            {
                jwtSettings.SecretKey = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                Serilog.Log.Warning("JWT SecretKey 未配置，已生成临时随机密钥（重启后所有令牌失效）。生产环境请设置环境变量 Jwt__SecretKey");
            }
            else
            {
                throw new InvalidOperationException(
                    "JWT SecretKey 未配置：请通过环境变量 Jwt__SecretKey 注入至少 32 字符的强密钥");
            }
        }

        // 解析后的密钥回灌 DI Options——JwtService(IOptions<JwtSettings>) 与认证中间件同源
        services.Configure<JwtSettings>(o => o.SecretKey = jwtSettings.SecretKey);

        // 配置认证
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey));

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = jwtSettings.ValidateIssuer,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = jwtSettings.ValidateAudience,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = jwtSettings.ValidateLifetime,
                ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds),
                RequireExpirationTime = true
            };

            // SignalR 和文件下载支持: 从查询参数获取令牌
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    // 也支持 "token" 参数（前端使用）
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        accessToken = context.Request.Query["token"];
                    }
                    
                    var path = context.HttpContext.Request.Path;

                    // 如果是 SignalR Hub 或文件下载请求，从查询参数获取令牌
                    if (!string.IsNullOrEmpty(accessToken) && 
                        (path.StartsWithSegments("/hubs") || 
                         path.StartsWithSegments("/api/v1/files/download") ||
                         path.StartsWithSegments("/api/v1/thumbnails")))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    // 令牌过期时添加特殊头
                    if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                    {
                        context.Response.Headers.Append("Token-Expired", "true");
                    }
                    return Task.CompletedTask;
                }
            };
        });

        // 配置授权策略
        services.AddAuthorizationBuilder()
            .AddPolicy("RequireAdmin", policy =>
                policy.RequireRole("SuperAdmin", "Admin"))
            .AddPolicy("RequireSuperAdmin", policy =>
                policy.RequireRole("SuperAdmin"))
            .AddPolicy("RequireUser", policy =>
                policy.RequireRole("SuperAdmin", "Admin", "User"));

        return services;
    }

    /// <summary>
    /// 使用认证和授权中间件
    /// </summary>
    public static IApplicationBuilder UseJwtAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
