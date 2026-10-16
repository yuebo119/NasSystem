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
    /// <summary>
    /// 向订阅了 <paramref name="path"/> 的客户端推送变更。
    /// </summary>
    /// <remarks>
    /// <b>参数 path 就是订阅组名本身，不再取它的父目录</b>。调用方
    /// （FileService.NotifyChangedAsync）传进来的已经是受影响的父目录，
    /// 这里再取一次 GetDirectoryName 会得到祖父目录：在 /docs 里建文件夹时
    /// 组名算成 path:/，而浏览 /docs 的客户端订阅的是 path:/docs，永远收不到。
    /// 该缺陷让"实时刷新闭环"只在根目录生效，其余目录全靠 mutation 后前端
    /// 主动 reload 兜底。根目录文件不受影响是因为 GetDirectoryName("/") 返回 null，
    /// 被归一回 "/"，恰好等于原值。
    /// </remarks>
    public async Task NotifyFileChangedAsync(string path, FileChangeType changeType, object? data = null)
    {
        var groupPath = string.IsNullOrEmpty(path) ? "/" : path.Replace('\\', '/');

        await hubContext.Clients.Group($"path:{groupPath}").SendAsync("FileChanged", new
        {
            Path = path,
            ChangeType = changeType.ToString(),
            Data = data,
            Timestamp = DateTime.UtcNow
        });
    }
}
