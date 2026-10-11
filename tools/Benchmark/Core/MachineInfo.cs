// =============================================================================
// NasSystem.Benchmark - MachineInfo.cs
// 运行环境快照：跨轮对比时确认未换机器、未换 SDK
// =============================================================================

namespace NasSystem.Benchmark;

/// <summary>
/// 采集机器与工具链状态。写进每条运行记录，
/// 使"绝对值不可跨机器比较"这条纪律可被事后核查。
/// </summary>
public sealed record MachineInfo(
    string MachineName,
    string OsDescription,
    int ProcessorCount,
    string DotNetVersion,
    long WorkingSetMb,
    DateTime CapturedAtUtc)
{
    public static MachineInfo Capture()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new MachineInfo(
            Environment.MachineName,
            Environment.OSVersion.ToString(),
            Environment.ProcessorCount,
            Environment.Version.ToString(),
            process.WorkingSet64 / 1024 / 1024,
            DateTime.UtcNow);
    }

    public string Describe() =>
        $"{MachineName} | {OsDescription} | {ProcessorCount} 逻辑核 | .NET {DotNetVersion}";
}
