// =============================================================================
// NasSystem.Api - SystemEndpoints.cs
// 系统管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using NasSystem.Shared;

/// <summary>
/// 系统管理端点扩展
/// </summary>
public static class SystemEndpoints
{
    /// <summary>
    /// 注册系统管理相关端点
    /// </summary>
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        var system = group.MapGroup("/system").WithTags("系统管理");

        // 健康检查 (无需认证)
        system.MapGet("/health", GetHealthCheck)
              .WithName("HealthCheck")
              .WithSummary("健康检查")
              .AllowAnonymous();

        // 获取系统信息
        system.MapGet("/info", GetSystemInfo)
              .WithName("GetSystemInfo")
              .WithSummary("获取系统信息")
              .RequireAuthorization();

        // 获取系统状态
        system.MapGet("/status", GetSystemStatus)
              .WithName("GetSystemStatus")
              .WithSummary("获取系统运行状态")
              .RequireAuthorization();

        // 获取系统设置
        system.MapGet("/settings", GetSystemSettings)
              .WithName("GetSystemSettings")
              .WithSummary("获取系统设置")
              .RequireAuthorization("RequireAdmin");

        // 更新系统设置
        system.MapPut("/settings", UpdateSystemSettings)
              .WithName("UpdateSystemSettings")
              .WithSummary("更新系统设置")
              .RequireAuthorization("RequireAdmin");

        // 获取系统日志
        system.MapGet("/logs", GetSystemLogs)
              .WithName("GetSystemLogs")
              .WithSummary("获取系统日志")
              .RequireAuthorization("RequireAdmin");

        // 系统重启
        system.MapPost("/restart", RestartSystem)
              .WithName("RestartSystem")
              .WithSummary("重启 NAS 服务")
              .RequireAuthorization("RequireAdmin");

        // 系统关机
        system.MapPost("/shutdown", ShutdownSystem)
              .WithName("ShutdownSystem")
              .WithSummary("关闭 NAS 服务")
              .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static IResult GetHealthCheck()
    {
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow
        }));
    }

    private static IResult GetSystemInfo()
    {
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Name = "NAS System",
            Version = "1.0.0",
            Os = Environment.OSVersion.ToString(),
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            DotNetVersion = Environment.Version.ToString(),
            StartTime = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64)
        }));
    }

    private static IResult GetSystemStatus()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Status = "Running",
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"d\.hh\:mm\:ss"),
            MemoryUsageMB = process.WorkingSet64 / 1024 / 1024,
            CpuTime = process.TotalProcessorTime.ToString(@"hh\:mm\:ss"),
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount
        }));
    }

    private static IResult GetSystemSettings()
    {
        // 返回系统配置（实际应从配置服务获取）
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            DefaultStoragePool = (Guid?)null,
            MaxUploadSizeMB = 1024,
            EnableCompression = false,
            EnableDeduplication = false,
            SessionTimeoutMinutes = 30,
            MaintenanceMode = false
        }));
    }

    private static IResult UpdateSystemSettings(UpdateSettingsRequest request)
    {
        // TODO: 实现更新系统设置逻辑
        return Results.Ok(ApiResponse.Ok());
    }

    private static IResult GetSystemLogs(int page = 1, int pageSize = 50, string? level = null)
    {
        // TODO: 从日志文件或数据库读取日志
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Items = Array.Empty<object>(),
            Page = page,
            PageSize = pageSize,
            TotalCount = 0
        }));
    }

    private static IResult RestartSystem()
    {
        // 在实际应用中，这里会触发服务重启
        return Results.Accepted(null, ApiResponse<object>.Ok(new
        {
            Message = "系统将在 5 秒后重启",
            ScheduledTime = DateTime.UtcNow.AddSeconds(5)
        }));
    }

    private static IResult ShutdownSystem()
    {
        // 在实际应用中，这里会触发服务关闭
        return Results.Accepted(null, ApiResponse<object>.Ok(new
        {
            Message = "系统将在 5 秒后关闭",
            ScheduledTime = DateTime.UtcNow.AddSeconds(5)
        }));
    }
}

/// <summary>
/// 更新设置请求
/// </summary>
public record UpdateSettingsRequest(Dictionary<string, object> Settings);
