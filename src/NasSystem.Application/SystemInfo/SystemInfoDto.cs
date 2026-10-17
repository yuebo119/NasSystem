// =============================================================================
// NasSystem.Application - System/SystemInfoDto.cs
// 系统信息 DTO（分组结构）
// =============================================================================

namespace NasSystem.Application.SystemInfo;

/// <summary>
/// 系统信息全量 DTO。按域分组而非平铺：Dashboard 的系统信息模块与
/// Settings 页都按组渲染，平铺三十多个字段没法组织展示。
/// </summary>
public sealed record SystemInfoDto
{
    // ─── 应用（保留原扁平字段的语义，Settings 页直接用）───
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string MachineName { get; init; }
    public required DateTime StartTime { get; init; }
    public required bool RunningInContainer { get; init; }

    // ─── .NET 运行时 ───
    public required RuntimeInfo Runtime { get; init; }

    // ─── 操作系统 ───
    public required OsInfo OperatingSystem { get; init; }

    // ─── 处理器 ───
    public required CpuInfo Cpu { get; init; }

    // ─── 内存 ───
    public required MemoryInfo Memory { get; init; }

    // ─── 数据库 ───
    public required DatabaseInfo Database { get; init; }

    // ─── 网络 ───
    public required NetworkInfo Network { get; init; }

    // ─── 进程 ───
    public required ProcessInfo Process { get; init; }
}

public sealed record RuntimeInfo
{
    public required string Framework { get; init; }
    public required string Version { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required string OsArchitecture { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required bool Is64BitProcess { get; init; }
    public required string GcMode { get; init; }
    public required long GcGen0Collections { get; init; }
    public required long GcGen1Collections { get; init; }
    public required long GcGen2Collections { get; init; }
}

public sealed record OsInfo
{
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required string Architecture { get; init; }
    public required bool Is64Bit { get; init; }
    public required string TimeZone { get; init; }
    public required string Culture { get; init; }
    public required string UserName { get; init; }
}

public sealed record CpuInfo
{
    public required int LogicalCores { get; init; }
    /// <summary>CPU 型号。Windows 读注册表，Linux 读 /proc/cpuinfo，其余平台为空</summary>
    public required string Model { get; init; }
    /// <summary>应用进程的 CPU 占用百分比（跨平台可算；整机 CPU 需性能计数器，不在此采集）</summary>
    public required double ProcessCpuPercent { get; init; }
}

public sealed record MemoryInfo
{
    /// <summary>物理内存总量（GC.GetGCMemoryInfo 跨平台可取）</summary>
    public required long TotalPhysicalBytes { get; init; }
    /// <summary>进程工作集（WorkingSet）</summary>
    public required long ProcessWorkingSetBytes { get; init; }
    /// <summary>GC 托管堆已用</summary>
    public required long GcHeapBytes { get; init; }
}

public sealed record DatabaseInfo
{
    public required string Provider { get; init; }
    /// <summary>SQLite 版本（SELECT sqlite_version()）</summary>
    public required string Version { get; init; }
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required string JournalMode { get; init; }
}

public sealed record NetworkInfo
{
    public required bool IsAvailable { get; init; }
    public required string HostName { get; init; }
    /// <summary>首个可用网卡名称（以太网/无线等），无则空</summary>
    public required string PrimaryInterface { get; init; }
    public required string InterfaceType { get; init; }
    /// <summary>首个可用网卡上的 IPv4 地址列表</summary>
    public required IReadOnlyList<string> Ipv4Addresses { get; init; }
    public required long ReceivedBytes { get; init; }
    public required long SentBytes { get; init; }
}

public sealed record ProcessInfo
{
    public required int ThreadCount { get; init; }
    public required int HandleCount { get; init; }
    public required long PagedMemoryBytes { get; init; }
    public required double TotalProcessorSeconds { get; init; }
}
