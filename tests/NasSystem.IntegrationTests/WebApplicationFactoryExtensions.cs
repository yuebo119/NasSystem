// =============================================================================
// NasSystem.IntegrationTests - WebApplicationFactoryExtensions.cs
// Web 应用工厂扩展
// =============================================================================

namespace NasSystem.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Persistence;

/// <summary>
/// 自定义 Web 应用工厂 - 每个测试类使用独立的数据库实例
/// </summary>
/// <remarks>
/// EF 时代这里按服务名摘除 EF 注册再挂 InMemory provider。PalORM 没有内存 provider，
/// 改用每个工厂一个独立的临时 SQLite 文件库：隔离语义不变（唯一库名），
/// 且测的是真实 SQLite 行为（大小写敏感的 Guid 比较、部分索引、WAL）。
/// 代价是每个测试类多几次文件 I/O，对本项目 54 个集成测试可接受。
/// </remarks>
public class NasSystemWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;
    private bool _databaseCreated;

    public NasSystemWebApplicationFactory()
    {
        // 每个工厂实例使用唯一的库文件
        _dbPath = Path.Combine(Path.GetTempPath(), $"NasSystemTestDb_{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 摘掉按生产连接串注册的会话持有者，换成指向本工厂临时库的实例
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(PalormSessionHolder))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            services.AddScoped(_ => new PalormSessionHolder(new PalORM.DbOptions
            {
                ConnectionString = $"Data Source={_dbPath}",
            }));

            // 系统控制服务换 no-op：真实的 ScheduleRestart/Shutdown 会在
            // 数秒后调用 StopApplication 停掉测试宿主，同一程序集里其他
            // 测试类虽各有工厂，但停宿主是不可逆副作用，测试环境一律禁真
            var controlDescriptors = services
                .Where(d => d.ServiceType == typeof(NasSystem.Application.SystemInfo.ISystemControlService))
                .ToList();
            foreach (var descriptor in controlDescriptors)
                services.Remove(descriptor);
            services.AddSingleton<NasSystem.Application.SystemInfo.ISystemControlService>(
                new NoOpSystemControlService());
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            });
        });
    }

    public void EnsureDatabaseCreated()
    {
        if (_databaseCreated) return;

        // 走与生产相同的初始化路径：建表 + 补两个部分索引 + 种子
        InitializeDatabaseAsync().GetAwaiter().GetResult();
        _databaseCreated = true;
    }

    private async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.InitializeDatabaseAsync();
    }

    /// <summary>
    /// 重置数据库状态 - 用于测试之间的隔离
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<PalormSessionHolder>();
        var db = await sessions.GetAsync();

        await db.ExecuteAsync(System.Runtime.CompilerServices.FormattableStringFactory.Create(
            """
            DELETE FROM "FileShares";
            DELETE FROM "FileVersions";
            DELETE FROM "FileItems";
            DELETE FROM "StoragePools";
            DELETE FROM "Users";
            """));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                if (File.Exists(_dbPath + suffix))
                    File.Delete(_dbPath + suffix);
            }
            catch
            {
                // 句柄未释放时留下，由系统临时目录清理
            }
        }
    }
}

/// <summary>
/// 测试用 no-op 系统控制：记录调用但不停进程。
/// 单独计数供断言"端点确实调度了"（比 mock 框架轻）
/// </summary>
public sealed class NoOpSystemControlService : NasSystem.Application.SystemInfo.ISystemControlService
{
    public int RestartCalls { get; private set; }
    public int ShutdownCalls { get; private set; }

    public void ScheduleRestart() => RestartCalls++;

    public void ScheduleShutdown() => ShutdownCalls++;
}
