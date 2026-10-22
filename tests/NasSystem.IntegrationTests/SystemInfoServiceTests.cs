// =============================================================================
// NasSystem.UnitTests - Services/SystemInfoServiceTests.cs
// 系统信息采集的锁定测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NasSystem.Application.SystemInfo;
using NasSystem.Infrastructure.Services;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using Xunit;

/// <summary>
/// 锁定系统信息采集契约：七个域都要有值；数据库不可达时该域降级为"未知"
/// 而其余域不受影响（系统信息端点不该因单项采集失败而整体 500）
/// </summary>
public class SystemInfoTests
{
    private static IConfiguration BuildConfig(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();

    [Fact]
    public async Task GetSystemInfoAsync_WithReachableDatabase_ReturnsAllGroups()
    {
        // :memory: 内存库：sqlite_version 与 journal_mode 都能取到真值
        var holder = new PalormSessionHolder(new DbOptions { ConnectionString = "Data Source=:memory:" });
        var sut = new SystemInfoService(holder, BuildConfig("Data Source=:memory:"), new SystemDateTimeProvider());

        var result = await sut.GetSystemInfoAsync();

        result.Name.Should().Be("NAS System");
        result.Version.Should().Be("1.0.0");
        result.MachineName.Should().NotBeEmpty();

        result.Runtime.Framework.Should().NotBeEmpty();
        result.Runtime.RuntimeIdentifier.Should().NotBeEmpty();
        result.Runtime.GcMode.Should().BeOneOf("Server", "Workstation");

        result.OperatingSystem.Description.Should().NotBeEmpty();
        result.OperatingSystem.TimeZone.Should().NotBeEmpty();

        result.Cpu.LogicalCores.Should().BeGreaterThan(0);
        result.Cpu.ProcessCpuPercent.Should().BeInRange(0, 100);

        result.Memory.TotalPhysicalBytes.Should().BeGreaterThan(0);
        result.Memory.ProcessWorkingSetBytes.Should().BeGreaterThan(0);

        result.Database.Provider.Should().Be("SQLite");
        result.Database.Version.Should().NotBe("未知");

        result.Network.HostName.Should().NotBeEmpty();
        result.Network.IsAvailable.Should().Be(true);

        result.Process.ThreadCount.Should().BeGreaterThan(0);
        result.Process.TotalProcessorSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetSystemInfoAsync_WhenDatabaseUnavailable_DegradesToUnknownButKeepsOtherGroups()
    {
        // 指向不可能创建的路径：版本与 journal mode 落"未知"，
        // 运行时/OS/CPU/内存/网络/进程六域照常返回
        var badPath = "Z:\\no\\such\\directory\\nas.db";
        var holder = new PalormSessionHolder(new DbOptions { ConnectionString = $"Data Source={badPath}" });
        var sut = new SystemInfoService(holder, BuildConfig($"Data Source={badPath}"), new SystemDateTimeProvider());

        var result = await sut.GetSystemInfoAsync();

        result.Database.Version.Should().Be("未知");
        result.Database.JournalMode.Should().Be("未知");
        result.Database.FileSizeBytes.Should().Be(0);

        result.Runtime.Framework.Should().NotBeEmpty();
        result.Cpu.LogicalCores.Should().BeGreaterThan(0);
        result.Memory.TotalPhysicalBytes.Should().BeGreaterThan(0);
        result.Network.HostName.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSystemInfoAsync_WithMissingConnectionString_UsesDefaultPath()
    {
        // 连接串缺失时回落默认值 "Data Source=data/nas.db"（与 DI 注册同一默认），
        // 不抛异常；该文件在测试工作目录不存在，数据库域降级
        var holder = new PalormSessionHolder(new DbOptions { ConnectionString = "Data Source=data/nas.db" });
        var sut = new SystemInfoService(holder, new ConfigurationBuilder().Build(), new SystemDateTimeProvider());

        var result = await sut.GetSystemInfoAsync();

        result.Database.Provider.Should().Be("SQLite");
        result.Runtime.Framework.Should().NotBeEmpty();
    }
}
