// =============================================================================
// NasSystem.Application - SystemInfo/ISystemControlService.cs
// 系统控制服务（重启/关机）
// =============================================================================

namespace NasSystem.Application.SystemInfo;

/// <summary>
/// 系统控制：重启与关机。原先两个端点直接返回"将在 5 秒后重启"的
/// 计划消息但不做任何事（对 API 消费者是假动作），改为真实调度：
/// 响应先flush，再停应用进程。进程退出后能否回来由宿主决定——
/// Docker 的 restart: unless-stopped 会拉起重启，systemd/裸进程
/// 则不回来（关机的预期语义）
/// </summary>
public interface ISystemControlService
{
    /// <summary>调度重启：延迟数秒让响应写出后停止应用进程</summary>
    void ScheduleRestart();

    /// <summary>调度关机：同上，停止后不期待宿主拉回</summary>
    void ScheduleShutdown();
}
