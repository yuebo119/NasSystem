// =============================================================================
// NasSystem.Api - SystemEndpoints.cs
// 系统管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using Microsoft.Extensions.Options;
using NasSystem.Application.Common;
using NasSystem.Application.Storage;
using NasSystem.Application.SystemInfo;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

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

        // 浏览目录（用于选择存储路径）
        system.MapGet("/browse-directories", BrowseDirectories)
              .WithName("BrowseDirectories")
              .WithSummary("浏览系统目录")
              .RequireAuthorization("RequireAdmin");

        // 获取系统驱动器列表
        system.MapGet("/drives", GetDrives)
              .WithName("GetDrives")
              .WithSummary("获取系统驱动器列表")
              .RequireAuthorization("RequireAdmin");

        // 生成外网访问配置（ROADMAP Phase 3）
        system.MapGet("/remote-access/config", GetRemoteAccessConfig)
              .WithName("GetRemoteAccessConfig")
              .WithSummary("生成外网访问配置")
              .WithDescription("按方案（cloudflare/frp/tailscale）生成配置文件与操作步骤，免公网 IP 也能外网访问")
              .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static IResult GetHealthCheck(NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Status = "Healthy",
            Timestamp = dateTime.UtcNow
        }));
    }

    private static async Task<IResult> GetSystemInfo(
        NasSystem.Application.SystemInfo.ISystemInfoService systemInfoService,
        CancellationToken cancellationToken)
    {
        var result = await systemInfoService.GetSystemInfoAsync(cancellationToken);
        return Results.Ok(ApiResponse<object>.Ok(result));
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

    /// <summary>
    /// 获取系统设置。只返回有真实来源的字段：上传上限取 Kestrel 实际生效值、
    /// 默认池与会话超时取库/配置真值。曾经另外三个字段（压缩/去重/维护模式）
    /// 是硬编码常量，系统里没有对应能力也不落库，2026-10-21 审计按
    /// ITM-N9（假数据比报错更危险）删除——PUT /settings 本就是诚实 501，
    /// GET 再返回虚构字段与同一文件的自我矛盾
    /// </summary>
    private static async Task<IResult> GetSystemSettings(
        UploadLimit uploadLimit,
        IStorageService storageService,
        IOptions<JwtSettings> jwtSettings)
    {
        var pools = await storageService.GetPoolsAsync();
        var defaultPool = pools.IsSuccess
            ? pools.Value.FirstOrDefault(p => p.IsDefault)
            : null;

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            DefaultStoragePool = (Guid?)defaultPool?.Id,
            MaxUploadSizeMB = uploadLimit.MaxUploadSizeMB,
            SessionTimeoutMinutes = jwtSettings.Value.AccessTokenExpirationMinutes
        }));
    }

    private static IResult UpdateSystemSettings(UpdateSettingsRequest request)
    {
        // ITM-N9：假成功比报错更危险（前端显示已保存但数据被丢弃）——诚实返回未实现
        return Results.Json(ApiResponse<object>.Fail("System.NotImplemented", "系统设置持久化尚未实现"),
            statusCode: StatusCodes.Status501NotImplemented);
    }

    private static IResult GetSystemLogs(int page = 1, int pageSize = 50, string? level = null)
    {
        // ITM-N9：恒空数组是假数据——诚实返回未实现
        return Results.Json(ApiResponse<object>.Fail("System.NotImplemented", "系统日志查询尚未实现"),
            statusCode: StatusCodes.Status501NotImplemented);
    }

    private static IResult RestartSystem(
        ISystemControlService controlService,
        NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        // 真实调度：响应先返回，3 秒后进程停止。Docker 的
        // restart: unless-stopped 会把它拉起来；裸进程需外部守护
        controlService.ScheduleRestart();
        return Results.Accepted($"/api/v1/system/status", ApiResponse<object>.Ok(new
        {
            Message = "系统正在重启，约 3 秒后停止进程",
            ScheduledTime = dateTime.UtcNow.AddSeconds(3)
        }));
    }

    private static IResult ShutdownSystem(
        ISystemControlService controlService,
        NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        controlService.ScheduleShutdown();
        return Results.Accepted($"/api/v1/system/status", ApiResponse<object>.Ok(new
        {
            Message = "系统正在关机，约 3 秒后停止进程",
            ScheduledTime = dateTime.UtcNow.AddSeconds(3)
        }));
    }

    private static IResult GetDrives()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new
                {
                    Name = d.Name.TrimEnd('\\'),
                    Label = string.IsNullOrEmpty(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel,
                    DriveType = d.DriveType.ToString(),
                    TotalSize = d.TotalSize,
                    AvailableSpace = d.AvailableFreeSpace,
                    TotalDisplay = FormatSize(d.TotalSize),
                    AvailableDisplay = FormatSize(d.AvailableFreeSpace)
                })
                .ToList();

            return Results.Ok(ApiResponse<object>.Ok(drives));
        }
        catch (Exception ex)
        {
            return Results.Ok(ApiResponse<object>.Fail("System.Error", $"获取驱动器列表失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 外网访问配置生成（ROADMAP Phase 3）。provider 非法时回 400——
    /// 生成器对未知方案抛 ArgumentException，这里转成客户端错误。
    /// lang 透传界面语言（zh/en）：配置注释与操作步骤按它出，
    /// 缺省 zh（端点既有调方不传时行为不变）
    /// </summary>
    private static IResult GetRemoteAccessConfig(
        string provider, string? hostname, int localPort = 6050, string lang = "zh")
    {
        try
        {
            var config = NasSystem.Application.SystemInfo.RemoteAccessConfigGenerator.Generate(
                provider, hostname, localPort, lang);
            return Results.Ok(ApiResponse<object>.Ok(config));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ApiResponse<object>.Fail(
                "System.InvalidProvider", ex.Message));
        }
    }

    private static IResult BrowseDirectories(string? path)
    {
        try
        {
            // 如果没有指定路径，返回驱动器列表
            if (string.IsNullOrEmpty(path))
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DirectoryItemDto
                    {
                        Name = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})",
                        Path = d.Name,
                        IsRoot = true
                    })
                    .ToList();

                return Results.Ok(ApiResponse<BrowseDirectoriesResult>.Ok(new BrowseDirectoriesResult
                {
                    CurrentPath = "",
                    ParentPath = null,
                    Directories = drives
                }));
            }

            // 规范化路径
            var normalizedPath = Path.GetFullPath(path);
            
            if (!Directory.Exists(normalizedPath))
            {
                return Results.Ok(ApiResponse<object>.Fail("System.Error", "目录不存在"));
            }

            // 获取父目录
            var parentDir = Directory.GetParent(normalizedPath);
            string? parentPath = parentDir?.FullName;

            // 获取子目录
            var directories = new List<DirectoryItemDto>();
            try
            {
                var dirs = Directory.GetDirectories(normalizedPath);
                foreach (var dir in dirs)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        // 跳过系统和隐藏目录
                        if ((dirInfo.Attributes & FileAttributes.System) != 0 ||
                            (dirInfo.Attributes & FileAttributes.Hidden) != 0)
                            continue;

                        directories.Add(new DirectoryItemDto
                        {
                            Name = dirInfo.Name,
                            Path = dirInfo.FullName,
                            IsRoot = false
                        });
                    }
                    catch
                    {
                        // 跳过无法访问的目录
                    }
                }
            }
            catch
            {
                // 无法读取目录内容
            }

            return Results.Ok(ApiResponse<BrowseDirectoriesResult>.Ok(new BrowseDirectoriesResult
            {
                CurrentPath = normalizedPath,
                ParentPath = parentPath,
                Directories = directories.OrderBy(d => d.Name).ToList()
            }));
        }
        catch (Exception ex)
        {
            return Results.Ok(ApiResponse<object>.Fail("System.Error", $"浏览目录失败: {ex.Message}"));
        }
    }

}

/// <summary>
/// 目录浏览结果
/// </summary>
public record BrowseDirectoriesResult
{
    public string CurrentPath { get; init; } = "";
    public string? ParentPath { get; init; }
    public List<DirectoryItemDto> Directories { get; init; } = [];
}

/// <summary>
/// 目录项
/// </summary>
public record DirectoryItemDto
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsRoot { get; init; }
}

/// <summary>
/// 更新设置请求
/// </summary>
public record UpdateSettingsRequest(Dictionary<string, object> Settings);
