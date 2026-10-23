// =============================================================================
// NasSystem.Api - Health/DatabaseHealthCheck.cs
// /health 的 SQLite 探测（F-102）
// =============================================================================

namespace NasSystem.Api.Health;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using NasSystem.Infrastructure.Persistence;

/// <summary>
/// 健康检查里的 SQLite 探测：只读库、库文件损坏或连接失败时必须让 /health 变 503，
/// 否则 Docker 与编排层会看到一个"健康"的进程，而所有写入都在失败。
/// </summary>
/// <remarks>
/// 用 IServiceScopeFactory 而不是直接注入 <see cref="PalormSessionHolder"/>：
/// 后者是 Scoped，健康检查由根容器解析，直接注入会变成俘虏依赖。
/// 探测只跑 SELECT 1，不写库，不会与业务写事务争锁。
/// </remarks>
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessions = scope.ServiceProvider.GetRequiredService<PalormSessionHolder>();
            var db = await sessions.GetAsync(cancellationToken);
            var value = await db.ScalarAsync<long>(
                System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT 1"),
                false,
                cancellationToken);

            return value == 1
                ? HealthCheckResult.Healthy("SQLite 可连接")
                : HealthCheckResult.Unhealthy($"SQLite 探测返回 {value}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQLite 探测失败", ex);
        }
    }
}