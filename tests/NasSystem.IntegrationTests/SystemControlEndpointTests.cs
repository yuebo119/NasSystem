// =============================================================================
// NasSystem.IntegrationTests - SystemControlEndpointTests.cs
// 重启/关机端点（真实调度 + 测试环境 no-op）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.SystemInfo;
using NasSystem.Application.Users.Dtos;
using Xunit;

/// <summary>
/// 锁住两件事：端点返回 202 + 计划消息；确实调度了控制服务
/// （不是原来的假动作）。真实服务在测试工厂被换成 no-op 计数替身，
/// 防止任何测试把宿主停掉
/// </summary>
public class SystemControlEndpointTests : ApiTestBase
{
    public SystemControlEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Restart_Returns202AndSchedules()
    {
        var token = await LoginAsAdminAsync();

        var request = CreateBearerRequest(HttpMethod.Post, "/api/v1/system/restart", token);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "重启是已接受的后台动作，202 而非 200");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("重启");

        Control.RestartCalls.Should().Be(1, "端点必须真实调度控制服务（原实现只返回假消息）");
    }

    [Fact]
    public async Task Shutdown_Returns202AndSchedules()
    {
        var token = await LoginAsAdminAsync();

        var request = CreateBearerRequest(HttpMethod.Post, "/api/v1/system/shutdown", token);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).Should().Contain("关机");

        Control.ShutdownCalls.Should().Be(1);
    }

    [Fact]
    public async Task Restart_NonAdmin_Forbidden()
    {
        var token = await GetAuthTokenAsync("ctluser" + Guid.NewGuid().ToString("N")[..8]);

        var request = CreateBearerRequest(HttpMethod.Post, "/api/v1/system/restart", token);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "重启/关机限管理员");
    }

    /// <summary>测试工厂注册的 no-op 控制服务（单例，与端点拿到同一个实例）</summary>
    private NoOpSystemControlService Control =>
        Factory.Services.GetRequiredService<ISystemControlService>() as NoOpSystemControlService
        ?? throw new InvalidOperationException("测试工厂应注册 NoOpSystemControlService");

    private async Task<string> LoginAsAdminAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/users/login",
            new { username = "admin", password = "admin123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "种子 admin 账号应可登录");

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("admin 登录失败");
    }

    private static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
