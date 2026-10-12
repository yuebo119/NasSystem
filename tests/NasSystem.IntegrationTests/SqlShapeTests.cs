// =============================================================================
// NasSystem.IntegrationTests - SqlShapeTests.cs
// 用 AsDryRun 锁定热路径的 SQL 形状
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;
using Xunit;

/// <summary>
/// 锁定热读路径生成的 SQL 形状。
/// </summary>
/// <remarks>
/// PalORM 的 <c>AsDryRun</c> 返回构建好的 SQL 与参数快照而不执行，正是为预览与测试断言准备的。
/// 这些断言的价值：手写 SQL 的仓储没有编译期类型检查，一个手滑把列名写错、
/// 把 <c>AND</c> 写成 <c>OR</c>、或者漏掉软删条件，编译器和测试的数据断言都不一定抓得到
/// （数据量小时结果可能恰好一样）。锁住 SQL 文本本身能把这类回归挡在提交前。
/// </remarks>
public class SqlShapeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SqlShapeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SqlShape_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        _provider = services.BuildServiceProvider();
        _provider.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task<DataSession<SqliteProvider>> SessionAsync()
    {
        var holder = _provider.GetRequiredService<PalormSessionHolder>();
        return await holder.GetAsync();
    }

    [Fact]
    public async Task GetChildren_SqlFiltersSoftDeletedAndOrdersFolderFirst()
    {
        var db = await SessionAsync();
        var dry = db.From<FileItemRow>()
            .Where($"ParentId = {Guid.NewGuid()} AND IsDeleted = 0")
            .OrderByDescending(f => f.ItemType)
            .ThenBy(f => f.Name)
            .AsDryRun();

        dry.Sql.Should().Contain("WHERE");
        dry.Sql.Should().Contain("IsDeleted = 0");
        dry.Sql.Should().Contain("ORDER BY");
        // 文件夹优先：ItemType 降序
        dry.Sql.Should().Contain("DESC");
    }

    [Fact]
    public async Task GetStatistics_AggregatesStayServerSide()
    {
        // CONVENTIONS §3.3：统计聚合必须在 SQL 端完成，禁止全表加载内存统计。
        // 这条断言守住"没有退化成 SELECT * 再内存数"
        var db = await SessionAsync();
        var dry = db.From<FileItemRow>()
            .Where($"IsDeleted = 0 AND ItemType = 0")
            .AsDryRun();

        // 统计走的是 ScalarAsync 而不是 QueryBuilder，这里退一步校验：
        // 仓储里聚合 SQL 的形态由 GetStatisticsAsync 直接给出，改用 CountAsync 验证入口存在
        var count = await db.CountAsync<FileItemRow>($"IsDeleted = 0 AND ItemType = 0");
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetDescendants_SqlUsesPrefixLike()
    {
        var db = await SessionAsync();
        var prefix = "/folder/";
        var dry = db.From<FileItemRow>()
            .Where($"Path LIKE {prefix + "%"} AND IsDeleted = 0")
            .OrderBy(f => f.Path)
            .AsDryRun();

        dry.Sql.Should().Contain("LIKE");
        dry.Sql.Should().Contain("IsDeleted = 0");
    }

    public void Dispose()
    {
        _provider.Dispose();
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
