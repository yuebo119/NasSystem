// =============================================================================
// NasSystem.Api - AuthenticationExtensions.cs
// 认证配置扩展
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        IConfiguration configuration)
    {
        // 绑定 JWT 配置
        var jwtSettings = new JwtSettings();
        configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

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
