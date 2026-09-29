// =============================================================================
// NasSystem.Application - Notifications/IHubNotificationService.cs
// 实时通知门面（实现位于 Api 层 HubNotificationService，经 DI 注入）
// =============================================================================

namespace NasSystem.Application.Notifications;

/// <summary>
/// Hub 推送通知抽象——Application 层只依赖此接口；
/// 变更事件由用例成功路径发出，前端经 SignalR 订阅目录粒度刷新
/// </summary>
public interface IHubNotificationService
{
    /// <summary>通知文件变更（推送到父目录订阅组）</summary>
    Task NotifyFileChangedAsync(string path, FileChangeType changeType, object? data = null);
}

/// <summary>
/// 文件变更类型
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Moved
}
