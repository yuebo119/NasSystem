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

    /// <summary>通知上传进度</summary>
    Task NotifyUploadProgressAsync(string taskId, long uploaded, long total);

    /// <summary>通知任务完成</summary>
    Task NotifyTaskCompletedAsync(string taskId, bool success, string? message = null);

    /// <summary>通知系统消息</summary>
    Task NotifySystemMessageAsync(string message, string level = "info");

    /// <summary>通知存储空间变更</summary>
    Task NotifyStorageChangedAsync(long totalBytes, long usedBytes, long availableBytes);
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
