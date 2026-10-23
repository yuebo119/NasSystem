// =============================================================================
// NasSystem.IntegrationTests - ShareEnumerationGuardTests.cs
// 全局分享枚举闸覆盖整组匿名端点（F-103）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

/// <summary>
/// 分享码是存在性预言机，不只 info 端点：download 也按 404 与 401/400 的差异
/// 泄漏存在性。修复前只有 info 计入全局枚举闸，download 只受按 IP 的窗口限制，
/// 分布式枚举可以绕过总速率。这里用 limit=1 的独立工厂锁住整组 filter。
/// </summary>
public class ShareEnumerationGuardTests
{
    [Fact]
    public async Task PublicShareDownload_SecondProbe_Returns429()
    {
        using var factory = new NasSystemWebApplicationFactory();
        factory.EnsureDatabaseCreated();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NasSystem:RateLimiting:ShareGlobalPerMinute"] = "1"
                })));
        using var client = configured.CreateClient();

        var first = await client.GetAsync("/api/v1/public-shares/nonexistent/download");
        var second = await client.GetAsync("/api/v1/public-shares/nonexistent/download");

        first.StatusCode.Should().Be(HttpStatusCode.NotFound);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "download 与 info 同样是存在性预言机，必须计入全局枚举闸");
    }
}