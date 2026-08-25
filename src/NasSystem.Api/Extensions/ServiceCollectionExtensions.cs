// =============================================================================
// NasSystem.Api - ServiceCollectionExtensions.cs
// API 层服务注册扩展
// =============================================================================

namespace NasSystem.Api.Extensions;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Threading.RateLimiting;
using NasSystem.Api.Hubs;
using NasSystem.Application.Notifications;
using Scalar.AspNetCore;

/// <summary>
/// API 层服务注册扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 API 层服务
    /// </summary>
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        // 配置 JSON 序列化选项
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

        // .NET 10 原生 OpenAPI 支持
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info.Title = "NAS System API";
                document.Info.Version = "v1";
                document.Info.Description = """
                    ## Windows NAS 系统 RESTful API
                    
                    ### 功能模块
                    - **用户管理**: 用户注册、登录、认证、权限管理
                    - **文件管理**: 文件/文件夹 CRUD、上传下载、搜索
                    - **存储管理**: 存储池、磁盘、RAID 管理
                    - **共享管理**: SMB 共享、权限控制
                    - **系统管理**: 系统状态、健康检查
                    
                    ### 认证方式
                    使用 JWT Bearer Token 进行认证。登录成功后，将返回的 token 添加到请求头：
                    ```
                    Authorization: Bearer <your-token>
                    ```
                    """;
                document.Info.Contact = new()
                {
                    Name = "NAS System Team"
                };
                return Task.CompletedTask;
            });
        });

        // CORS 配置 (开发环境允许前端开发服务器访问)
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:6060", "http://localhost:3000")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials(); // SignalR 需要
            });
            
            // 生产环境策略（ITM-N2）：来源白名单来自配置 Cors:AllowedOrigins，禁止 AllowAnyOrigin
            options.AddPolicy("Production", policy =>
            {
                // 占位：实际来源在 UseApiMiddleware 阶段由 ConfigureCorsOrigins 动态装配
                policy.SetIsOriginAllowed(_ => false)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // 响应压缩
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
        });

        // SignalR 实时通信
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 64 * 1024; // 64 KB
        });

        // 限流（CONVENTIONS §4.5）：认证端点严格窗口，防暴力破解/用户名枚举
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("auth", limiterOptions =>
            {
                // 集成测试并发登录不受限（仅生产启用严格窗口防暴力破解）
                limiterOptions.PermitLimit = environment.IsProduction() ? 10 : int.MaxValue;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.QueueLimit = 0;
            });
        });

        // 上传大小限制（ITM-N2）：Kestrel 全局 500MB，防未限制上传耗尽磁盘
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
        });

        // 注册 Hub 通知服务
        services.AddScoped<IHubNotificationService, HubNotificationService>();

        // 健康检查
        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    /// 配置 API 中间件管道
    /// </summary>
    public static WebApplication UseApiMiddleware(this WebApplication app)
    {
        // 开发环境启用 OpenAPI 和 Scalar UI
        if (app.Environment.IsDevelopment())
        {
            // .NET 10 原生 OpenAPI 端点
            app.MapOpenApi();
            
            // Scalar UI - 现代化的 API 文档界面
            app.MapScalarApiReference(options =>
            {
                options.WithTitle("NAS System API")
                       .WithTheme(ScalarTheme.BluePlanet)
                       .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
            });
        }

        // 响应压缩
        app.UseResponseCompression();

        // CORS
        app.UseCors();

        // 限流（必须在路由解析后、端点执行前）
        app.UseRateLimiter();

        // 静态文件（前端 SPA）
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // 注册 API 端点
        app.MapApiEndpoints();

        // 健康检查端点
        app.MapHealthChecks("/health");

        // SPA 回退：对于 /s/* 路径，返回 index.html 让前端路由处理
        app.MapFallbackToFile("/s/{*path}", "index.html");

        // 版本信息端点
        app.MapGet("/api/version", () => Results.Ok(new
        {
            Name = "NAS System",
            Version = "1.0.0",
            Environment = app.Environment.EnvironmentName,
            Timestamp = DateTime.UtcNow
        }))
        .WithName("GetVersion")
        .WithTags("System")
        .WithSummary("获取系统版本信息");

        return app;
    }

    /// <summary>
    /// 注册所有 API 端点
    /// </summary>
    private static void MapApiEndpoints(this WebApplication app)
    {
        var apiGroup = app.MapGroup("/api/v1");

        // 文件管理端点
        apiGroup.MapFileEndpoints();
        
        // 缩略图端点
        apiGroup.MapThumbnailEndpoints();
        
        // 文件版本端点
        apiGroup.MapFileVersionEndpoints();
        
        // 用户管理端点
        apiGroup.MapUserEndpoints();
        
        // 文件分享端点
        apiGroup.MapFileShareEndpoints();
        
        // 存储池管理端点
        apiGroup.MapStorageEndpoints();
        
        // 系统管理端点
        apiGroup.MapSystemEndpoints();
        
        // 公开分享访问端点（无需登录）
        app.MapPublicShareEndpoints();
    }
}
