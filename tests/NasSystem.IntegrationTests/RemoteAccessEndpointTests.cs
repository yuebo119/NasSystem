// =============================================================================
// NasSystem.IntegrationTests - RemoteAccessEndpointTests.cs
// 外网访问配置端点（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using NasSystem.Application.Users.Dtos;
using Xunit;

/// <summary>
/// 锁住端点的两件事：管理员能拿到含 hostname 的配置；
/// 未知方案回 400 而不是 500 或静默回落。
/// 非管理员 403 由 RequireAdmin 策略与 UserEndpointsIntegrationTests
/// 的同款断言共同覆盖，这里不重复
/// </summary>
public class RemoteAccessEndpointTests : ApiTestBase
{
    public RemoteAccessEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Config_Cloudflare_ReturnsGeneratedConfig()
    {
        var token = await GetAuthTokenAsync("raadmin");
        // 种子用户是 SuperAdmin；GetAuthTokenAsync 建的是普通用户，
        // 端点要 RequireAdmin，故直接用种子的 admin 账号登录
        var adminToken = await LoginAsAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/remote-access/config?provider=cloudflare&hostname=nas.test.com");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("nas.test.com");
        body.Should().Contain("localhost:6050");
        body.Should().Contain("config.yml", "响应要带建议文件名");

        // 普通用户令牌不应有用（策略是 RequireAdmin）
        var forbidden = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/remote-access/config?provider=cloudflare");
        forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await Client.SendAsync(forbidden)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Config_UnknownProvider_Returns400()
    {
        var adminToken = await LoginAsAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/remote-access/config?provider=ngrok");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "未知方案是客户端错误，不能静默回落成某个默认方案");
    }

    [Fact]
    public async Task Config_LangEn_ReturnsEnglishSteps()
    {
        // 2026-10-19 E2E 走查实证的回归：英文界面下配置注释与步骤全是中文。
        // 生成器按 lang 出双语，端点透传，缺省仍 zh（既有调方行为不变）
        var adminToken = await LoginAsAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/remote-access/config?provider=cloudflare&hostname=nas.test.com&lang=en");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Install cloudflared", "lang=en 应出英文步骤");
        body.Should().Contain("nas.test.com", "插值（生效域名）与语言无关，都要在");
        body.Should().NotContain("安装 cloudflared", "英文分支不应混中文步骤");
    }

    [Fact]
    public async Task Config_DefaultLang_StaysChinese()
    {
        // 不传 lang 的既有调方必须还是中文（契约不变）
        var adminToken = await LoginAsAdminAsync();

        var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/system/remote-access/config?provider=cloudflare&hostname=nas.test.com");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("安装 cloudflared", "缺省 lang 保持中文");
    }

    /// <summary>用种子 admin/admin123 登录换令牌（种子见 Infrastructure 的 SeedDataAsync）</summary>
    private async Task<string> LoginAsAdminAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/users/login",
            new { username = "admin", password = "admin123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "种子 admin 账号应可登录");

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("admin 登录失败");
    }
}
