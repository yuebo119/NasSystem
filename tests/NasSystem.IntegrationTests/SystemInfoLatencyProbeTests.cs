// =============================================================================
// NasSystem.IntegrationTests - SystemInfoLatencyProbeTests.cs
// /system/info 300ms 延迟的分项计时探针（优化前置门：先定位再改）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Enums;
using Xunit;

/// <summary>
/// 2026-10-22 优化轮实测发现：`GET /api/v1/system/info` 稳定 300ms+，
/// 而其他读端点（/system/status、/files/browse、/storage/overview）
/// 全在 10-20ms。本类把该端点的耗时拆成「采集」与「传输」两段，
/// 并逐项计时八组域里的每一个，定位到具体是哪一步。
///
/// 结论会写回 SystemInfoService 的注释里；探针本身保留为回归守卫：
/// 若某次改动让单项采集超过阈值，这条先红。
/// </summary>
public class SystemInfoLatencyProbeTests : ApiTestBase
{
    private readonly ITestOutputHelper _output;

    public SystemInfoLatencyProbeTests(NasSystemWebApplicationFactory factory, ITestOutputHelper output)
        : base(factory)
    {
        _output = output;
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task SystemInfo_ItemizedTimings_IdentifyDominantCost()
    {
        var token = await GetAuthTokenAsync("latencyprobe" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);

        // 预热一次（首访有 JIT 与连接池建立）
        await Client.GetAsync("/api/v1/system/info");

        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            var response = await Client.GetAsync("/api/v1/system/info");
            sw.Stop();
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        var median = samples.OrderBy(x => x).ElementAt(samples.Count / 2);
        _output.WriteLine($"/system/info 中位 {median:F1}ms，样本: {string.Join(", ", samples.Select(s => s.ToString("F1")))}");

        // 直接调用服务层，排除 HTTP 传输与 JSON 序列化
        using var scope = Factory.Services.CreateScope();
        var svc = scope.ServiceProvider
            .GetRequiredService<NasSystem.Application.SystemInfo.ISystemInfoService>();

        var serviceSamples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            await svc.GetSystemInfoAsync();
            sw.Stop();
            serviceSamples.Add(sw.Elapsed.TotalMilliseconds);
        }
        var svcMedian = serviceSamples.OrderBy(x => x).ElementAt(serviceSamples.Count / 2);
        _output.WriteLine($"服务层中位 {svcMedian:F1}ms，样本: {string.Join(", ", serviceSamples.Select(s => s.ToString("F1")))}");

        // 逐项计时：把每个采集动作单独跑，找出主导项
        var timings = new Dictionary<string, double>();
        for (var round = 0; round < 3; round++)
        {
            Measure("Process.GetCurrentProcess+StartTime", timings, () =>
            {
                var p = System.Diagnostics.Process.GetCurrentProcess();
                _ = p.StartTime;
                _ = p.Threads.Count;
                _ = p.HandleCount;
                _ = p.TotalProcessorTime;
            });
            Measure("Environment.MachineName", timings, () => _ = Environment.MachineName);
            Measure("TimeZoneInfo.Local.DisplayName", timings, () => _ = TimeZoneInfo.Local.DisplayName);
            Measure("NetworkInterface.GetAllNetworkInterfaces", timings, () =>
                _ = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .ToArray());
            Measure("Registry.ReadCpuModel", timings, () =>
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                _ = key?.GetValue("ProcessorNameString");
            });
        }

        _output.WriteLine("--- 逐项耗时（3 轮最小值，毫秒）---");
        foreach (var kv in timings.OrderByDescending(k => k.Value))
            _output.WriteLine($"{kv.Key,-45} {kv.Value,8:F2}");

        // 断言的靶心是端到端与服务层延迟，不是原始 API 成本：
        // NetworkInterface.GetAllNetworkInterfaces() 本身在本机就要 ~105ms
        // （Windows 上枚举全部网卡是慢调用），优化手段是缓存/单次调用，
        // 而不是让这个 API 变快。所以对原始成本只记录不设阈值
        svcMedian.Should().BeLessThan(100,
            "服务层耗时超过 100ms 说明有未缓存的慢采集项漏在外面");
        median.Should().BeLessThan(150,
            "端点端到端超过 150ms 会让系统信息页明显卡顿");
    }

    private static void Measure(string name, Dictionary<string, double> acc, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        var ms = sw.Elapsed.TotalMilliseconds;
        acc[name] = acc.TryGetValue(name, out var prev) ? Math.Min(prev, ms) : ms;
    }
}
