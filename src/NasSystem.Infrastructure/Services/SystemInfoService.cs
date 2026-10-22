// =============================================================================
// NasSystem.Infrastructure - Services/SystemInfoService.cs
// 系统信息采集实现（运行时自省 + 数据库版本 + 网卡统计）
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NasSystem.Application.SystemInfo;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>
/// 系统信息采集。全部走托管 API，不 P/Invoke：
/// 物理内存用 <see cref="GC.GetGCMemoryInfo"/>（.NET 5+ 跨平台），
/// 网卡用 <see cref="NetworkInterface"/>（本地枚举，不触发 DNS 查询），
/// CPU 型号按平台分流（Windows 注册表 / Linux /proc/cpuinfo）。
/// </summary>
public sealed class SystemInfoService(
    PalormSessionHolder sessions,
    IConfiguration configuration,
    NasSystem.Domain.Services.IDateTimeProvider dateTime) : ISystemInfoService
{
    private const string DefaultConnectionString = "Data Source=data/nas.db";

    public async Task<SystemInfoDto> GetSystemInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var process = Process.GetCurrentProcess();
        var startedAt = process.StartTime.ToUniversalTime();

        return new SystemInfoDto
        {
            Name = "NAS System",
            Version = "1.0.0",
            MachineName = Environment.MachineName,
            StartTime = startedAt,
            RunningInContainer = IsRunningInContainer(),
            Runtime = CollectRuntime(),
            OperatingSystem = CollectOs(),
            Cpu = CollectCpu(process),
            Memory = CollectMemory(process),
            Database = await CollectDatabaseAsync(cancellationToken),
            Network = CollectNetwork(),
            Process = new ProcessInfo
            {
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                PagedMemoryBytes = process.PagedMemorySize64,
                TotalProcessorSeconds = Math.Round(process.TotalProcessorTime.TotalSeconds, 1)
            }
        };
    }

    private static RuntimeInfo CollectRuntime() => new()
    {
        Framework = RuntimeInformation.FrameworkDescription,
        Version = Environment.Version.ToString(),
        RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
        OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Is64BitProcess = Environment.Is64BitProcess,
        GcMode = GCSettings.IsServerGC ? "Server" : "Workstation",
        GcGen0Collections = GC.CollectionCount(0),
        GcGen1Collections = GC.CollectionCount(1),
        GcGen2Collections = GC.CollectionCount(2)
    };

    private static OsInfo CollectOs() => new()
    {
        Description = RuntimeInformation.OSDescription,
        Version = Environment.OSVersion.ToString(),
        Architecture = RuntimeInformation.OSArchitecture.ToString(),
        Is64Bit = Environment.Is64BitOperatingSystem,
        TimeZone = TimeZoneInfo.Local.DisplayName,
        // 不变区域（Invariant）的 Name 是空串，回落 DisplayName 免得展示空白
        Culture = string.IsNullOrEmpty(CultureInfo.CurrentCulture.Name)
            ? CultureInfo.CurrentCulture.EnglishName
            : CultureInfo.CurrentCulture.Name,
        UserName = Environment.UserName
    };

    private CpuInfo CollectCpu(Process process)
    {
        // 进程 CPU 占用 = 进程 CPU 时间 /（核数 × 进程运行时长）。
        // 整机 CPU 需要性能计数器（Windows 专有）或解析 /proc/stat，
        // 跨平台口径不诚实，这里只报应用自身占用，展示层如实标注
        var elapsedSeconds = (dateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds;
        double cpuPercent = elapsedSeconds > 0
            ? process.TotalProcessorTime.TotalSeconds / (Environment.ProcessorCount * elapsedSeconds) * 100
            : 0;

        return new CpuInfo
        {
            LogicalCores = Environment.ProcessorCount,
            Model = ReadCpuModel(),
            ProcessCpuPercent = Math.Round(Math.Min(cpuPercent, 100), 1)
        };
    }

    /// <summary>
    /// CPU 型号：Windows 读注册表 CentralProcessor\0，Linux 读 /proc/cpuinfo，
    /// 其余平台（或读取失败）返回空串，由展示层显示"未知"
    /// </summary>
    private static string ReadCpuModel()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString") as string;
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            }

            if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
            {
                var model = File.ReadLines("/proc/cpuinfo")
                    .FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                var value = model?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
                return value ?? string.Empty;
            }
        }
        catch
        {
            // 采集失败不致命：型号只是展示信息，回落空串
        }

        return string.Empty;
    }

    private static MemoryInfo CollectMemory(Process process) => new()
    {
        TotalPhysicalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        ProcessWorkingSetBytes = process.WorkingSet64,
        GcHeapBytes = GC.GetTotalMemory(false)
    };

    private async Task<DatabaseInfo> CollectDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? DefaultConnectionString;

        var filePath = new SqliteConnectionStringBuilder(connectionString).DataSource ?? string.Empty;
        long fileSize = 0;
        try
        {
            if (File.Exists(filePath))
                fileSize = new FileInfo(filePath).Length;
        }
        catch
        {
            // 文件不可达时大小记 0，版本与 journal mode 仍从连接取
        }

        var version = "未知";
        var journalMode = "未知";
        try
        {
            var db = await sessions.GetAsync(cancellationToken);
            version = await db.ScalarAsync<string>($"SELECT sqlite_version()", false, cancellationToken) ?? "未知";
            journalMode = await db.ScalarAsync<string>($"PRAGMA journal_mode", false, cancellationToken) ?? "未知";
        }
        catch
        {
            // 数据库不可达时保留"未知"：系统信息端点不该因采集失败而 500
        }

        return new DatabaseInfo
        {
            Provider = "SQLite",
            Version = version,
            FilePath = filePath,
            FileSizeBytes = fileSize,
            JournalMode = journalMode
        };
    }

    /// <summary>
    /// 网卡枚举缓存。2026-10-22 优化轮实测：NetworkInterface.GetAllNetworkInterfaces()
    /// 在本机耗时 112ms（/system/info 服务层总耗时 233ms 的一半，另一次
    /// GetIsNetworkAvailable 同量级），是整个系统信息端点唯一的主导项。
    ///
    /// 缓存 2 秒而不是更长：吞吐量（ReceivedBytes/SentBytes）要能反映近期变化，
    /// 但网卡集合与 IP 不会在秒级频繁变动。2s 覆盖「系统信息页连续刷新/重试」
    /// 这批重复调用，又不会让流量数字看起来冻住。
    /// 静态字段而非实例：服务是 Scoped（内部查 SQLite 版本），缓存必须跨请求存活
    /// </summary>
    private static readonly (NetworkInfo Info, DateTime At)[] NetworkCacheSlot = new (NetworkInfo, DateTime)[1];
    private static readonly TimeSpan NetworkCacheTtl = TimeSpan.FromSeconds(2);
    private static readonly Lock NetworkCacheLock = new();

    private NetworkInfo CollectNetwork()
    {
        lock (NetworkCacheLock)
        {
            var cached = NetworkCacheSlot[0];
            if (cached.Info is not null && dateTime.UtcNow - cached.At < NetworkCacheTtl)
                return cached.Info;
        }

        var info = CollectNetworkUncached();
        lock (NetworkCacheLock)
        {
            NetworkCacheSlot[0] = (info, dateTime.UtcNow);
        }
        return info;
    }

    private static NetworkInfo CollectNetworkUncached()
    {
        var hostName = Environment.MachineName;
        try
        {
            // 取首个可用（ operational ）网卡：本地枚举，不触发 DNS 解析。
            // 只枚举一次：原实现在无匹配网卡与正常分支各调一次
            // GetIsNetworkAvailable()（内部又会枚举一遍全部网卡）
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            var nic = nics.FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.Wireless80211
                        or NetworkInterfaceType.GigabitEthernet);

            if (nic is null)
            {
                return new NetworkInfo
                {
                    IsAvailable = nics.Any(n => n.OperationalStatus == OperationalStatus.Up),
                    HostName = hostName,
                    PrimaryInterface = string.Empty,
                    InterfaceType = string.Empty,
                    Ipv4Addresses = Array.Empty<string>(),
                    ReceivedBytes = 0,
                    SentBytes = 0
                };
            }

            var ipv4 = nic.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToList();

            var stats = nic.GetIPv4Statistics();
            return new NetworkInfo
            {
                // 用同一次枚举结果判定，不再触发第二轮网卡扫描
                IsAvailable = true,
                HostName = hostName,
                PrimaryInterface = nic.Name,
                InterfaceType = nic.NetworkInterfaceType.ToString(),
                Ipv4Addresses = ipv4,
                ReceivedBytes = stats.BytesReceived,
                SentBytes = stats.BytesSent
            };
        }
        catch
        {
            return new NetworkInfo
            {
                IsAvailable = false,
                HostName = hostName,
                PrimaryInterface = string.Empty,
                InterfaceType = string.Empty,
                Ipv4Addresses = Array.Empty<string>(),
                ReceivedBytes = 0,
                SentBytes = 0
            };
        }
    }

    private static bool IsRunningInContainer() =>
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") is "true" or "1";
}
