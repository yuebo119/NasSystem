// =============================================================================
// NasSystem.Api - SystemEndpoints.cs
// 系统管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

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

    private static IResult GetSystemSettings(UploadLimit uploadLimit)
    {
        // 返回系统配置（实际应从配置服务获取）。
        // MaxUploadSizeMB 取 Kestrel 实际生效的上限：两边必须同源，
        // 否则前端按这里的数字做校验会被服务端真实限制打回
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            DefaultStoragePool = (Guid?)null,
            MaxUploadSizeMB = uploadLimit.MaxUploadSizeMB,
            EnableCompression = false,
            EnableDeduplication = false,
            SessionTimeoutMinutes = 30,
            MaintenanceMode = false
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
