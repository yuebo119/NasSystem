// =============================================================================
// NasSystem.Api - Program.cs
// NAS System API 入口点
// =============================================================================

using NasSystem.Api.Extensions;
using NasSystem.Api.Hubs;
using NasSystem.Api.Middleware;
using NasSystem.Application;
using NasSystem.Infrastructure;
using Serilog;

// 配置 Serilog 早期启动日志
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("NAS System 正在启动...");
    
    var builder = WebApplication.CreateBuilder(args);

    // 配置 Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/nas-system-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30));

    // 注册服务
    builder.Services
        .AddSharedServices()
        .AddApplicationServices()
        .AddInfrastructureServices(builder.Configuration)
        .AddJwtAuthentication(builder.Configuration)
        .AddApiServices();

    var app = builder.Build();

    // 初始化数据库
    await app.Services.InitializeDatabaseAsync();

    // 配置中间件管道
    app.UseRequestLogging();
    app.UseGlobalExceptionHandler();
    app.UseJwtAuthentication();
    app.UseApiMiddleware();

    // 配置 SignalR (需要认证)
    app.MapHub<NasHub>("/hubs/nas").RequireAuthorization();

    Log.Information("NAS System 启动完成，监听端口: {Urls}", app.Urls);
    
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NAS System 启动失败");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// 为集成测试公开 Program 类
public partial class Program { }
