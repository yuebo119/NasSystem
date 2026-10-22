// =============================================================================
// NasSystem.IntegrationTests - ShareEnumerationRateLimitTests.cs
// 分享码枚举面加固（审计 F-073）锁定测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 F-073：公开分享信息端点是分享码存在性预言机
/// （404 vs 200），此前只有按 IP 的限流，挡不住分布式枚举。
///
/// 修复是双闸，两条各自锁定：
/// 一、ShareEnumerationGuard 全局速率闸（handler 内显式检查）——
///    不管多少台机器，总探测速率被钉住
/// 二、组级 "share" 限流策略（按客户端 IP 的固定窗口）——
///    单来源轰炸仍被拦，且确认组级策略对本端点确实生效
///     （ASP.NET Core 的 RequireRateLimiter 写端点元数据，同端点叠加
///      第二个策略不都生效，所以全局闸不走策略叠加）
/// </summary>
public class ShareEnumerationRateLimitTests : ApiTestBase
{
    public ShareEnumerationRateLimitTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task ShareInfo_BeyondGlobalLimit_Returns429()
    {
        // 全局闸 3：第 4 次探测必须被拦，且不查库（429 与 404 不同，
        // 证明闸在 handler 入口而非查询之后）
        using var factory = new LowLimitFactory(sharePerMinute: 1000, shareGlobalPerMinute: 3);
        factory.EnsureDatabaseCreated();
        var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var response = await client.GetAsync($"/api/v1/public-shares/nosuchcode{attempt}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"第 {attempt} 次探测应正常走查询");
        }

        var limited = await client.GetAsync("/api/v1/public-shares/nosuchcode4");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "分享码枚举必须有全局速率上限：按 IP 的限流挡不住分布式探测");
    }

    [Fact]
    public async Task ShareInfo_BeyondPerIpLimit_Returns429()
    {
        // 单 IP 闸 2、全局闸放到很大：第三次必须触发按 IP 的窗口。
        // 这条同时验证组级 "share" 策略对信息端点仍然生效——
        // 若组级策略被端点级元数据覆盖，这条会红
        using var factory = new LowLimitFactory(sharePerMinute: 2, shareGlobalPerMinute: 1000);
        factory.EnsureDatabaseCreated();
        var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/public-shares/perip1")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/v1/public-shares/perip2")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var third = await client.GetAsync("/api/v1/public-shares/perip3");
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "单来源的探测速率仍须被按 IP 的窗口拦住，且组级策略不得被全局闸挤掉");
    }

    /// <summary>把两个闸的阈值压到可测范围的工厂（生产缺省值不变，走配置覆盖）</summary>
    private sealed class LowLimitFactory : NasSystemWebApplicationFactory
    {
        private readonly int _sharePerMinute;
        private readonly int _shareGlobalPerMinute;

        public LowLimitFactory(int sharePerMinute, int shareGlobalPerMinute)
        {
            _sharePerMinute = sharePerMinute;
            _shareGlobalPerMinute = shareGlobalPerMinute;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NasSystem:RateLimiting:SharePerMinute"] = _sharePerMinute.ToString(),
                    ["NasSystem:RateLimiting:ShareGlobalPerMinute"] = _shareGlobalPerMinute.ToString(),
                });
            });
        }
    }
}
