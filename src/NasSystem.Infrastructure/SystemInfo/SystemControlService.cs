// =============================================================================
// NasSystem.Infrastructure - SystemInfo/SystemControlService.cs
// 系统控制服务实现（重启/关机）
// =============================================================================

namespace NasSystem.Infrastructure.SystemInfo;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NasSystem.Application.SystemInfo;

/// <summary>
/// 基于 IHostApplicationLifetime 的真实重启/关机。
/// </summary>
/// <remarks>
/// 延迟 3 秒再停：StopApplication 是同步触发优雅关停的，若在响应
/// 写出前调用，客户端拿到的是连接被重置而不是 202 + 计划消息。
/// 用 Task.Run 而不是 await：调用方（端点）要立刻返回响应，
/// 关停发生在后台。Docker 部署下 compose 的 restart: unless-stopped
/// 会把进程拉起来（重启语义）；裸进程/systemd 不配置守护则不回来
/// （关机语义）——两者行为一致，差别在宿主，服务只负责停自己
/// </remarks>
public sealed class SystemControlService(
    IHostApplicationLifetime lifetime,
    ILogger<SystemControlService> logger) : ISystemControlService
{
    private const int StopDelaySeconds = 3;

    public void ScheduleRestart()
    {
        logger.LogWarning("系统重启已调度，{Delay}s 后停止应用进程", StopDelaySeconds);
        ScheduleStop("重启");
    }

    public void ScheduleShutdown()
    {
        logger.LogWarning("系统关机已调度，{Delay}s 后停止应用进程", StopDelaySeconds);
        ScheduleStop("关机");
    }

    private void ScheduleStop(string reason)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(StopDelaySeconds));
                logger.LogWarning("正在停止应用进程（原因: {Reason}）", reason);
                lifetime.StopApplication();
            }
            catch (Exception ex)
            {
                // 关停失败只记日志：此时进程状态未知，再抛也无人接收
                logger.LogError(ex, "停止应用进程失败（原因: {Reason}）", reason);
            }
        });
    }
}
