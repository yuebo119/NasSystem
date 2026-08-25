// =============================================================================
// NasSystem.Api - NasHub.cs
// SignalR 实时通信 Hub
// =============================================================================

namespace NasSystem.Api.Hubs;

using Microsoft.AspNetCore.SignalR;
using NasSystem.Application.Notifications;

/// <summary>
/// NAS 系统实时通信 Hub
/// </summary>
public sealed class NasHub : Hub
{
    private readonly ILogger<NasHub> _logger;

    public NasHub(ILogger<NasHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("客户端连接: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客户端断开: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 订阅文件变更通知
    /// </summary>
    public async Task SubscribeToPath(string path)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"path:{path}");
        _logger.LogDebug("客户端 {ConnectionId} 订阅路径: {Path}", Context.ConnectionId, path);
    }

    /// <summary>
    /// 取消订阅文件变更通知
    /// </summary>
    public async Task UnsubscribeFromPath(string path)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"path:{path}");
        _logger.LogDebug("客户端 {ConnectionId} 取消订阅路径: {Path}", Context.ConnectionId, path);
    }

    /// <summary>
    /// 订阅任务进度通知
    /// </summary>
    public async Task SubscribeToTask(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"task:{taskId}");
    }

    /// <summary>
    /// 取消订阅任务进度通知
    /// </summary>
    public async Task UnsubscribeFromTask(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"task:{taskId}");
    }
}


/// <summary>
/// Hub 通知服务实现。
/// </summary>
/// <remarks>
/// 【分层论证】为什么实现放在 Api 层而接口在 Application/Notifications：
/// 推送的技术载体（IHubContext/SignalR）是表现层基础设施，Domain/Application 不应感知；
/// Application 服务仅依赖抽象接口在用例成功路径发出事件，实现经 DI 注入。
/// 这与 ILogger/MailKit 等横切关注点的"接口在内、实现在外"布局一致。
/// </remarks>
public sealed class HubNotificationService(IHubContext<NasHub> hubContext) : IHubNotificationService
{
    public async Task NotifyFileChangedAsync(string path, FileChangeType changeType, object? data = null)
    {
        // 通知订阅该路径的客户端
        var parentPath = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parentPath))
            parentPath = "/"; // 根目录文件的父目录归一为 /（与前端订阅串一致）
        
        await hubContext.Clients.Group($"path:{parentPath}").SendAsync("FileChanged", new
        {
            Path = path,
            ChangeType = changeType.ToString(),
            Data = data,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyUploadProgressAsync(string taskId, long uploaded, long total)
    {
        var percentage = total > 0 ? (double)uploaded / total * 100 : 0;

        await hubContext.Clients.Group($"task:{taskId}").SendAsync("UploadProgress", new
        {
            TaskId = taskId,
            Uploaded = uploaded,
            Total = total,
            Percentage = Math.Round(percentage, 2),
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyTaskCompletedAsync(string taskId, bool success, string? message = null)
    {
        await hubContext.Clients.Group($"task:{taskId}").SendAsync("TaskCompleted", new
        {
            TaskId = taskId,
            Success = success,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifySystemMessageAsync(string message, string level = "info")
    {
        await hubContext.Clients.All.SendAsync("SystemMessage", new
        {
            Message = message,
            Level = level,
            Timestamp = DateTime.UtcNow
        });
    }

    public async Task NotifyStorageChangedAsync(long totalBytes, long usedBytes, long availableBytes)
    {
        await hubContext.Clients.All.SendAsync("StorageChanged", new
        {
            TotalBytes = totalBytes,
            UsedBytes = usedBytes,
            AvailableBytes = availableBytes,
            UsagePercentage = totalBytes > 0 ? Math.Round((double)usedBytes / totalBytes * 100, 2) : 0,
            Timestamp = DateTime.UtcNow
        });
    }
}
