// =============================================================================
// NasSystem.IntegrationTests - SystemInfoIsolationTests.cs
// 信息面角色过滤（用户裁决 2026-10-22）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using NasSystem.Domain.Enums;
using Xunit;

/// <summary>
/// 普通用户不应拿到主机名、操作系统用户名、数据库文件路径、网卡地址与进程细节；
/// 存储池列表含物理挂载路径，只给管理员（Dashboard 走不含路径的 /storage/overview）。
/// </summary>
public class SystemInfoIsolationTests : ApiTestBase
{
    public SystemInfoIsolationTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task SystemInfo_NonAdmin_OmitsSensitiveSections()
    {
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        SetAuthToken(await GetAuthTokenAsync("info-user-" + suffix, UserRole.User));

        var response = await Client.GetAsync("/api/v1/system/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "端点仍对登录用户开放，只是内容收窄");
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"cpu\"").And.Contain("\"memory\"", "非敏感子集仍返回");
        body.Should().NotContain("\"database\"", "数据库文件路径不给普通用户");
        body.Should().NotContain("\"network\"", "网卡地址与主机名不给普通用户");
        body.Should().NotContain("\"process\"", "进程细节不给普通用户");
        body.Should().NotContain("\"machineName\"", "主机名不给普通用户");
        body.Should().NotContain("\"userName\"", "操作系统用户名不给普通用户");
    }

    [Fact]
    public async Task SystemInfo_Admin_IncludesSensitiveSections()
    {
        await SeedDefaultStoragePoolAsync();
        // 用唯一名 + 显式 SuperAdmin，避免与共享工厂里其他测试类并发创建 "admin" 撞用户名
        SetAuthToken(await GetAuthTokenAsync(
            "info-admin-" + Guid.NewGuid().ToString("N")[..6], UserRole.SuperAdmin));

        var response = await Client.GetAsync("/api/v1/system/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Contain("\"database\"").And.Contain("\"network\"").And.Contain("\"process\"");
    }

    [Fact]
    public async Task StoragePools_NonAdmin_ReturnsForbidden()
    {
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        SetAuthToken(await GetAuthTokenAsync("pool-user-" + suffix, UserRole.User));

        var response = await Client.GetAsync("/api/v1/storage/pools");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "存储池列表含物理挂载路径，只给管理员");
    }
}