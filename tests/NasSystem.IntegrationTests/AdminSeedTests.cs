// =============================================================================
// NasSystem.IntegrationTests - AdminSeedTests.cs
// 初始管理员种子的环境差异化（2026-10-19 数据库层审计 P1 回归）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 锁生产环境的初始管理员不再是开箱即 admin123：
/// 一、Production 无配置密码时生成随机密码，且只在启动日志出现一次
///     （测试用捕获型 logger 把它读出来，反向验证库里存的就是它）；
/// 二、Production 配置了 NasSystem:AdminPassword 时用配置值；
/// 三、开发环境保持 admin123 的既有便利（既有测试全依赖它）。
/// 另外锁两个种子独立判定：admin 已存在时不再整体早退，池缺失能补种
/// </summary>
public class AdminSeedTests
{
    /// <summary>捕获 LogWarning 及以上级别的消息（随机密码只打一次，必须抓住）</summary>
    private sealed class CapturingLogger :
        Microsoft.Extensions.Logging.ILogger<NasSystem.Infrastructure.Persistence.PalormSessionHolder>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.Contains("初始管理员"))
                Messages.Add(message);
        }
    }

    [Fact]
    public async Task Production_WithoutConfiguredPassword_SeedsRandomPassword()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"SeedProd_{Guid.NewGuid():N}.db");
        var logger = new CapturingLogger();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        // 覆盖 InitializeDatabaseAsync 取的 ILogger<PalormSessionHolder>：
        // AddLogging 注册的是真 logger，随机密码的一次性打印必须被测试抓住
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<NasSystem.Infrastructure.Persistence.PalormSessionHolder>>(_ => logger);
        services.AddInfrastructureServices(config);
        var provider = services.BuildServiceProvider();

        try
        {
            provider.InitializeDatabaseAsync("Production").GetAwaiter().GetResult();

            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var admin = await users.GetByUsernameAsync("admin");
            admin.Should().NotBeNull("首次启动必须种出管理员");

            // admin123 绝不能再是生产实例的钥匙
            new NasSystem.Infrastructure.Services.PasswordHasher()
                .VerifyPassword("admin123", admin!.PasswordHash, admin.PasswordSalt)
                .Should().BeFalse("生产环境不得播种 admin123");

            // 日志里一次性打印的随机密码，必须就是库里存的那个
            logger.Messages.Should().ContainSingle("随机密码应只在启动日志出现一次");
            var match = System.Text.RegularExpressions.Regex.Match(
                logger.Messages[0], @"随机密码 (\S+?)。");
            match.Success.Should().BeTrue("启动日志应打印一次性随机密码");
            var password = match.Groups[1].Value;
            new NasSystem.Infrastructure.Services.PasswordHasher()
                .VerifyPassword(password, admin.PasswordHash, admin.PasswordSalt)
                .Should().BeTrue("日志打印的随机密码必须能登录（否则种子就是把用户关在门外）");
        }
        finally
        {
            provider.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task Production_WithConfiguredPassword_UsesConfiguredValue()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"SeedCfg_{Guid.NewGuid():N}.db");
        const string Configured = "MyStrongInitialPass123";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                ["NasSystem:AdminPassword"] = Configured,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        var provider = services.BuildServiceProvider();

        try
        {
            provider.InitializeDatabaseAsync("Production").GetAwaiter().GetResult();

            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var admin = await users.GetByUsernameAsync("admin");
            new NasSystem.Infrastructure.Services.PasswordHasher()
                .VerifyPassword(Configured, admin!.PasswordHash, admin.PasswordSalt)
                .Should().BeTrue("配置了初始密码就用配置值");
        }
        finally
        {
            provider.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task Development_SeedsDefaultAdminPassword()
    {
        // 开发环境保持 admin123：既有测试与本地开发都依赖这个便利
        var dbPath = Path.Combine(Path.GetTempPath(), $"SeedDev_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        var provider = services.BuildServiceProvider();

        try
        {
            provider.InitializeDatabaseAsync("Development").GetAwaiter().GetResult();

            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var admin = await users.GetByUsernameAsync("admin");
            new NasSystem.Infrastructure.Services.PasswordHasher()
                .VerifyPassword("admin123", admin!.PasswordHash, admin.PasswordSalt)
                .Should().BeTrue("开发环境保持 admin123");
        }
        finally
        {
            provider.Dispose();
            DeleteDb(dbPath);
        }
    }

    [Fact]
    public async Task AdminExistsButPoolMissing_BackfillsPool()
    {
        // 曾经的早退条件是"admin 存在就整体跳过"：池被删后永远不补种。
        // 两个种子必须独立判定
        var dbPath = Path.Combine(Path.GetTempPath(), $"SeedPool_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        var provider = services.BuildServiceProvider();

        try
        {
            provider.InitializeDatabaseAsync("Development").GetAwaiter().GetResult();

            using (var scope = provider.CreateScope())
            {
                var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
                var pool = await pools.GetDefaultAsync();
                pool.Should().NotBeNull();
                await pools.DeleteAsync(pool!);
            }

            // 再来一次初始化：admin 还在，池必须补回来
            provider.InitializeDatabaseAsync("Development").GetAwaiter().GetResult();

            using (var scope = provider.CreateScope())
            {
                var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
                var pool = await pools.GetDefaultAsync();
                pool.Should().NotBeNull("池被删后重启必须补种（曾经的早退逻辑永久留下无池状态）");
            }
        }
        finally
        {
            provider.Dispose();
            DeleteDb(dbPath);
        }
    }

    private static void DeleteDb(string dbPath)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                if (File.Exists(dbPath + suffix))
                    File.Delete(dbPath + suffix);
            }
            catch
            {
                // 句柄未释放时留下，由系统临时目录清理
            }
        }
    }
}
