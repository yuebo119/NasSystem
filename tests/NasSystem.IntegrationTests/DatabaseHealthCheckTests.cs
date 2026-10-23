// =============================================================================
// NasSystem.IntegrationTests - DatabaseHealthCheckTests.cs
// /health 的数据库探测（F-102）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NasSystem.Api.Health;
using NasSystem.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// 健康检查必须能反映数据库不可用：只读库或损坏库时进程还能起，
/// 但所有写入失败。正常库走真实 /health，故障库直接构造探测。
/// </summary>
public class DatabaseHealthCheckTests : ApiTestBase
{
    public DatabaseHealthCheckTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task HealthEndpoint_WithHealthyDatabase_ReturnsOk()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CheckHealthAsync_DatabaseResolutionFails_ReturnsUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddScoped<PalormSessionHolder>(_ =>
            throw new InvalidOperationException("数据库不可用"));
        using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy,
            "数据库探测失败必须让 /health 变 503，而不是继续报健康");
    }
}