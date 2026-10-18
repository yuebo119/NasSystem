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
using NasSystem.Api.Security;
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

            // 源生生成上下文排在前，反射解析器兜底（匿名对象与 object 声明成员走后者）。
            // 上下文的 [JsonSourceGenerationOptions] 与上面三项逐项一致，见 NasJsonContext 注释。
            options.SerializerOptions.TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                Serialization.NasJsonContext.Default,
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
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

        // CORS：只注册开发用默认策略（localhost 前端开发服务器）。
        // 生产白名单在 UseApiMiddleware 里按 Cors:AllowedOrigins 就地构造，
        // 因为注册阶段拿不到 IConfiguration
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:6060", "http://localhost:3000")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials(); // SignalR 需要
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

        // 上传大小限制（ITM-N2）：Kestrel 全局 500MB，防未限制上传耗尽磁盘。
        // 提为常量并注入 DI：系统设置端点要把它报给前端，
        // 两边各写一个数字必然漂移（原先这里是 500MB、端点返回 1024MB）
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = UploadLimit.MaxRequestBodySize;
        });
        services.AddSingleton(UploadLimit.Instance);

        // 【S5】上传解析从 ReadFormAsync 改 MultipartReader 流式（见
        // FileEndpoints.UploadFile）：不再有 multipart 表单缓冲，原
        // FormOptions 的 MemoryBufferThreshold/MultipartBodyLengthLimit
        // 配置随之删除（ReadFormAsync 无调用方后它们是死配置）。
        // 完全流式放弃的两个代价与补偿：拿不到 part 声明大小，容量预检改
        // 余量判定加边写边检；中断上传的半成品临时文件由扫描同步跳过
        // （.uploading- 后缀）并在下次上传成功后由 File.Move 覆盖

        // 注册 Hub 通知服务
        services.AddScoped<IHubNotificationService, HubNotificationService>();

        // WebDAV Basic 认证失败锁定（ROADMAP Phase 0）：单例，全进程共享失败计数
        services.AddSingleton<IWebDavAuthGuard, WebDavAuthGuard>();

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

        // CORS：生产环境按配置白名单构造策略，开发环境用 localhost 默认策略。
        // 原先无参调用恒走 default，生产实际放行 localhost 白名单，
        // 而注册的 "Production" 策略从未被引用（其注释里的 ConfigureCorsOrigins 也不存在）。
        // 这里在中间件阶段读配置并就地构造，避开注册阶段拿不到 IConfiguration 的问题
        if (app.Environment.IsProduction())
        {
            var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            app.UseCors(policy =>
            {
                if (allowedOrigins.Length == 0)
                    policy.SetIsOriginAllowed(_ => false);
                else
                    policy.WithOrigins(allowedOrigins);

                policy.AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        }
        else
        {
            app.UseCors("Default");
        }

        // 限流（必须在路由解析后、端点执行前）
        app.UseRateLimiter();

        // 静态文件（前端 SPA）
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // 注册 API 端点
        app.MapApiEndpoints();

        // 健康检查端点
        app.MapHealthChecks("/health");

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

        // SPA 回退（单一 fallback 内部分流：注册多个 fallback 会互相冲突触发
        // AmbiguousMatchException——fallback 端点共享 {*path:nonfile} 兜底模式）：
        // 1. 未匹配的 API/hubs 路径返回 404 JSON，不能吞进 SPA HTML 回退，
        //    否则前端把错误页当接口响应解析
        // 2. 其余未匹配路径（/files、/users、/storage、/shares、/s/* 公开分享链接
        //    等前端路由）返回 index.html 交由前端路由处理。此前仅 /s/* 有回退，
        //    其余深链接刷新/直联均 404
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = "端点不存在" });
                return;
            }

            // 走 WebRootFileProvider 显式定位：SendFileAsync(string) 按内容根解析，
            // 会去找 {ContentRoot}/index.html 而非 wwwroot 下的产物
            var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var indexFile = env.WebRootFileProvider.GetFileInfo("index.html");
            if (!indexFile.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = "前端未构建（wwwroot/index.html 不存在）" });
                return;
            }

            await context.Response.SendFileAsync(indexFile);
        });

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

        // 移动备份端点（ROADMAP Phase 1）
        apiGroup.MapBackupEndpoints();

        // 媒体库端点（ROADMAP Phase 3）
        apiGroup.MapMediaEndpoints();

        // 系统动态配置端点（配置页）
        apiGroup.MapSystemConfigEndpoints();

        // 清单快照端点（ROADMAP Phase 2）
        apiGroup.MapSnapshotEndpoints();

        // 受保护目录（只读保护）端点（ROADMAP Phase 2）
        apiGroup.MapProtectedDirectoryEndpoints();

        // 3-2-1 导出端点（ROADMAP Phase 2）
        apiGroup.MapExportEndpoints();

        // 语义搜图端点（ROADMAP Phase 2）
        apiGroup.MapSemanticEndpoints();
        
        // 公开分享访问端点（无需登录）
        app.MapPublicShareEndpoints();

        // WebDAV 协议端点（ROADMAP Phase 0）：挂 /dav，与 /api/v1 平级。
        // 必须早于 SPA fallback 注册，且动词全集在路由内显式列出，
        // 否则未匹配的 WebDAV 动词会落进 fallback 返回 index.html
        app.MapWebDavEndpoints();
    }
}
