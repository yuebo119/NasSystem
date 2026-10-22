// =============================================================================
// NasSystem.IntegrationTests - BenchmarkFixtureSchemaTests.cs
// 压测夹具 schema 与生产 schema 的一致性锁定（2026-10-22 优化轮）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// 锁 benchmark 夹具（tools/Benchmark/Seeding/Schema.cs）与生产 schema 的
/// 索引集合一致。
///
/// 2026-10-22 优化轮实测发现两者漂移：夹具把 IX_FileItems_Path 建成
/// 「唯一部分索引 WHERE IsDeleted=0」，而生产里这个名字是全量 BINARY 索引
/// （唯一部分索引另名 UX_FileItems_Path_Active），且夹具缺 IX_FileItems_Path_Nocase
/// 与 IX_FileItems_OwnerId_CreatedAt。
///
/// 漂移的后果不是「夹具跑得慢」这么轻——它会让**用夹具做的性能判断得出与生产
/// 相反的结论**：同一子树查询在漂移夹具上实测 12.9ms 全表扫、在生产同构索引下
/// 只要 0.17ms。本轮优化已被误导过一次（先误判成生产缺陷）。
///
/// 生产侧本来就有 schema 锁定测试（SchemaAndEnumTests），夹具侧此前没有，
/// 这正是漂移能长期存在的原因。本类补上。
/// </summary>
public class BenchmarkFixtureSchemaTests : ApiTestBase
{
    public BenchmarkFixtureSchemaTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public void BenchmarkFixture_DeclaredTables_HaveSameIndexesAsProduction()
    {
        var fixtureTables = ExtractFixtureTableNames();
        var fixtureIndexes = ExtractFixtureIndexes();

        fixtureTables.Should().NotBeEmpty("夹具声明必须解析得到");
        fixtureIndexes.Should().NotBeEmpty("夹具索引声明必须解析得到");

        // 只对夹具声明的表逐表比对：夹具是有意的最小集（5 张热路径表），
        // 生产还有备份设备/快照/OCR 等表，那些不在基准范围内
        foreach (var table in fixtureTables.OrderBy(t => t, StringComparer.Ordinal))
        {
            var fixtureForTable = fixtureIndexes
                .Where(i => string.Equals(i.Table, table, StringComparison.Ordinal))
                .Select(i => i.Name)
                .ToHashSet(StringComparer.Ordinal);
            var productionForTable = ReadProductionIndexNames(table);

            var missing = productionForTable.Except(fixtureForTable, StringComparer.Ordinal).OrderBy(x => x).ToList();
            var extra = fixtureForTable.Except(productionForTable, StringComparer.Ordinal).OrderBy(x => x).ToList();

            missing.Should().BeEmpty(
                $"夹具的 {table} 索引必须与生产一致：缺索引会让基准把「生产能走索引的查询」测成全表扫，得出相反结论");
            extra.Should().BeEmpty($"夹具的 {table} 不得声明生产不存在的索引");
        }
    }

    private readonly record struct FixtureIndex(string Name, string Table);

    /// <summary>从夹具源码提取表名（正则匹配 CREATE TABLE "NAME"）</summary>
    private static HashSet<string> ExtractFixtureTableNames()
    {
        var text = File.ReadAllText(ResolveFixtureSchemaPath());
        return Regex.Matches(text, @"CREATE\s+TABLE\s+\\?""([A-Za-z0-9_]+)\\?""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>从夹具源码提取索引名与其目标表。夹具的 DDL 是
    /// <c>CREATE [UNIQUE] INDEX "IX_X" ON "Table" (...)</c> 形态</summary>
    private static List<FixtureIndex> ExtractFixtureIndexes()
    {
        var text = File.ReadAllText(ResolveFixtureSchemaPath());
        return Regex.Matches(
                text,
                @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+\\?""([A-Za-z0-9_]+)\\?""\s+ON\s+\\?""([A-Za-z0-9_]+)\\?""")
            .Select(m => new FixtureIndex(m.Groups[1].Value, m.Groups[2].Value))
            .ToList();
    }

    private static string ResolveFixtureSchemaPath()
    {
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "Benchmark", "Seeding", "Schema.cs");
        File.Exists(schemaPath).Should().BeTrue($"应能在仓库根定位夹具 schema，实际找的是 {Path.GetFullPath(schemaPath)}");
        return schemaPath;
    }

    /// <summary>读生产库某张表的索引名。用原始连接而非仓储：
    /// PalORM 只认已注册的 Row 类型，sqlite_master 不在其行模型体系内</summary>
    private HashSet<string> ReadProductionIndexNames(string table)
    {
        using var scope = Factory.Services.CreateScope();
        var configuration = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = @t AND name NOT LIKE 'sqlite_%'";
        command.Parameters.AddWithValue("@t", table);

        var names = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }
}