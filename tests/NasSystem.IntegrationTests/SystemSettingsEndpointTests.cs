// =============================================================================
// NasSystem.IntegrationTests - SystemSettingsEndpointTests.cs
// GET /system/settings 的真实性（2026-10-21 全量审计 P1 回归）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Repositories;
using Xunit;

/// <summary>
/// 锁 2026-10-21 审计发现的 P1：该端点曾把 6 个字段里 5 个写成硬编码常量
/// （DefaultStoragePool 恒 null、压缩/去重/维护模式恒 false、会话超时恒 30），
/// 不反映系统真实状态。修复后只返回有真实来源的字段：默认池查库、
/// 会话超时读 JWT 配置、上传上限取 Kestrel 生效值；三个无 backing 的
/// 虚构字段按 ITM-N9 删除。这条测试同时锁"假字段不复活"
/// </summary>
public class SystemSettingsEndpointTests : ApiTestBase
{
    public SystemSettingsEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task GetSettings_ReturnsRealDefaultPoolAndConfiguredTimeout()
    {
        // 种子 admin（EnsureDatabaseCreated 已跑初始化路径）
        var login = await Client.PostAsJsonAsync("/api/v1/users/login",
            new { username = "admin", password = "admin123" });
        var loginResult = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        SetAuthToken(loginResult?.Data?.Token ?? throw new InvalidOperationException("admin 登录失败"));

        await SeedDefaultStoragePoolAsync();
        var expectedPoolId = await GetDefaultPoolIdAsync();

        var response = await Client.GetAsync("/api/v1/system/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        // 默认池必须是库里的真池，不再是恒 null
        data.GetProperty("defaultStoragePool").GetString().Should().Be(
            expectedPoolId.ToString(), "默认池字段必须反映库里真实默认池");

        // 会话超时读 JWT 配置（appsettings 的 60 分钟），不再是写死的 30
        data.GetProperty("sessionTimeoutMinutes").GetInt32().Should().Be(60);

        // 上传上限是 Kestrel 真实生效值，必须是个正经正数
        data.GetProperty("maxUploadSizeMB").GetInt32().Should().BeGreaterThan(0);

        // 三个虚构字段不许复活：系统里没有压缩/去重/维护模式这些能力，
        // 返回它们就是假数据（PUT /settings 本就是诚实 501）
        body.Should().NotContain("enableCompression");
        body.Should().NotContain("enableDeduplication");
        body.Should().NotContain("maintenanceMode");
    }

    private async Task<Guid> GetDefaultPoolIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var pool = await pools.GetDefaultAsync();
        return pool?.Id ?? throw new InvalidOperationException("种子默认池应存在");
    }
}
