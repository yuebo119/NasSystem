// =============================================================================
// NasSystem.Application - System/ISystemInfoService.cs
// 系统信息用例
// =============================================================================

namespace NasSystem.Application.SystemInfo;

/// <summary>
/// 系统信息采集：运行时、操作系统、处理器、内存、数据库、网络、进程七个域。
/// </summary>
public interface ISystemInfoService
{
    Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken cancellationToken = default);
}
