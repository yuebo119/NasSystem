// =============================================================================
// NasSystem.IntegrationTests - ForwardedHeadersClientKeyTests.cs
// 反代真实客户端 IP（审计 F-072）锁定测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 F-072：WebDAV 失败锁定以客户端 IP 为键，反代后全部
/// 请求共享同一个键（对端是反代容器，或 RemoteIpAddress 为空归到 "unknown"），
/// 一个攻击者的失败轰炸会把同反代下的所有正常用户一起锁掉。
///
/// 修复形态：配置驱动的 ForwardedHeaders 中间件。默认不启用（无条件信任
/// X-Forwarded-For 等于允许伪造客户端键绕过锁定，比共享桶更糟），
/// 只有配置了可信代理（IP/CIDR）时才在对端匹配的请求上采纳转发头。
///
/// 两条测试分别锁两个方向：
/// 一、默认不信任：客户端自报的 X-Forwarded-For 不改变锁定键（防伪造）
/// 二、配置信任后：不同 X-Forwarded-For 各自独立计锁（反代场景按真实客户端隔离）
/// </summary>
public class ForwardedHeadersClientKeyTests : ApiTestBase
{
    public ForwardedHeadersClientKeyTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task DefaultConfiguration_ClientSuppliedForwardedFor_DoesNotChangeLockoutKey()
    {
        // 未配置可信代理：换 X-Forwarded-For 不换键。若这条红，说明无条件
        // 信任了客户端自报头，任何人都能伪造键绕过失败锁定
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var response = await SendFailedDavAsync($"10.0.0.{attempt}");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"第 {attempt} 次失败应仍回 401");
        }

        // 换一个"客户端"再失败：键未变（仍在同一桶），必须已被锁
        var afterSwitch = await SendFailedDavAsync("203.0.113.9");
        afterSwitch.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "默认不信任转发头：客户端自报的 X-Forwarded-For 不得用来逃避锁定");
    }

    [Fact]
    public async Task TrustedProxy_DistinctClients_GetIsolatedLockoutBuckets()
    {
        // 同一工厂但换成"配信"实例：TestServer 对端是回环，把回环加进
        // 可信代理后 X-Forwarded-For 才会被采纳
        using var factory = new TrustedProxyFactory();
        factory.EnsureDatabaseCreated();
        var client = factory.CreateClient();

        // 客户端 A 打满阈值
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var response = await SendFailedDavAsync(client, "198.51.100.7");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"A 第 {attempt} 次失败应仍回 401");
        }

        // A 第 11 次：锁
        var lockedA = await SendFailedDavAsync(client, "198.51.100.7");
        lockedA.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "A 已达阈值，必须锁");

        // 客户端 B 同反代下首次失败：必须还能拿到 401 而不是被 A 连带锁掉
        var firstB = await SendFailedDavAsync(client, "198.51.100.8");
        firstB.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "反代后必须按真实客户端 IP 隔离锁定桶，否则一个攻击者锁掉同反代下所有用户（F-072）");
    }

    private static async Task<HttpResponseMessage> SendFailedDavAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/dav/");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("no-such-user:bad-password"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        // 模拟 nginx 反代形态：TLS 在反代侧终结，X-Forwarded-Proto=https。
        // 不带头时应用看到的是明文 HTTP + 非回环客户端 IP，会被 §4 的明文
        // 暴露守卫正确拦下（403）——那正是转发头让真实 IP 可见的直接证据
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        return await client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendFailedDavAsync(string forwardedFor) =>
        SendFailedDavAsync(Client, forwardedFor);

    /// <summary>配了可信代理的工厂：TestServer 对端是回环，信任回环后转发头生效</summary>
    private sealed class TrustedProxyFactory : NasSystemWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NasSystem:ForwardedHeaders:KnownProxies:0"] = "127.0.0.1",
                    ["NasSystem:ForwardedHeaders:KnownProxies:1"] = "::1",
                });
            });
        }
    }
}
