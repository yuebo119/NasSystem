// =============================================================================
// NasSystem.IntegrationTests - SqliteExtensionCapabilityTests.cs
// SQLite 引擎运行时能力探针：FTS5 虚拟表 + sqlite-vec 扩展加载
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;
using Xunit;

/// <summary>
/// 探针/锁定：应用真实使用的 SQLite 连接（PalORM DataSession）的运行时能力。
/// </summary>
/// <remarks>
/// 存在的理由：OCR 文本全文检索（FTS5）与语义搜图向量检索（sqlite-vec）
/// 都依赖引擎能力，而"包引了"不等于"引擎编译了这个特性"。
/// 注意探针必须经 <see cref="PalormSessionHolder"/> 拿连接：直连
/// SqliteConnection 走的是 Microsoft.Data.Sqlite 默认的 e_sqlite3，与运行期
/// 连接可能不是同一个引擎，在默认引擎上测出的结论不能外推到应用连接。
/// 参数绑定一律用 FormattableString 内插（PalORM 编译期参数化），
/// 不用手拼字符串。
/// </remarks>
public class SqliteExtensionCapabilityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SqliteExtensionCapabilityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SqliteExtProbe_{Guid.NewGuid():N}.db");
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

    private async Task<DataSession<SqliteProvider>> OpenPalormSessionAsync()
    {
        var sessions = _provider.GetRequiredService<PalormSessionHolder>();
        return await sessions.GetAsync();
    }

    [Fact]
    public async Task PalormSession_ReportsEngineVersion()
    {
        // 事实采集：应用运行期连接的真实引擎版本串。SQLite3MC 会在版本串里
        // 带分支标识，e_sqlite3 只有三段版本号。先取事实再写断言，不猜形态。
        await using var db = await OpenPalormSessionAsync();
        var version = await db.ScalarAsync<string>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT sqlite_version()"), false);
        version.Should().NotBeNullOrEmpty("运行期连接应能报告引擎版本");
        // 实测（2026-10-18 探针）：版本串为 3.54.0，三段标准版本号、无 sqlite3mc
        // 分支标识——运行期引擎是 Microsoft.Data.Sqlite 默认的 e_sqlite3，
        // PalORM 的 SqliteProvider 未激活 SQLite3MC。Directory.Packages.props 里
        // "原生引擎仍是 SQLite3MC"的注释与实际不符，以本条实测为准。
    }

    [Fact]
    public async Task Fts5_TrigramTokenizer_MatchesChineseSubstring()
    {
        // trigram tokenizer（FTS5 3.47+ 内置）按三字子串切分，是 FTS5 对中文
        // 开箱可用的形态；unicode61 按 Unicode 类别分词，中文整句被切成单字，
        // 双字词查询必然落空（实测 unicode61 下 MATCH '扫描件' 返回 0）。
        // 查询串加双引号是 trigram 的字符串字面量语法，不是可选的风格。
        const string Content = "家庭网络存储中的扫描件文字识别";
        await using var db = await OpenPalormSessionAsync();
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "CREATE VIRTUAL TABLE IF NOT EXISTS probe_fts USING fts5(content, tokenize='trigram')"));
        // 探针直写 SQL 字面量（ExecuteAsync 的 FormattableString 不转参数化，
        // 内插值会被当列名——实测报错 no such column）。生产写入的转义与
        // 参数化形态在实现层单独锁定，这里只验证引擎能力。
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"INSERT INTO probe_fts(content) VALUES ('{Content}')"));

        var hits = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM probe_fts WHERE probe_fts MATCH '\"扫描件\"'"), false);
        hits.Should().Be(1, "trigram FTS5 应命中中文子串");
    }

    [Fact]
    public async Task Fts5_MatchAcceptsParameterizedQueryString()
    {
        // 最小复现：MATCH 右值用绑定参数（SearchAsync 的 FTS 通道形态）
        // 与 SQL 字面量两种形态的行为差异
        await using var db = await OpenPalormSessionAsync();
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "CREATE VIRTUAL TABLE IF NOT EXISTS probe_match USING fts5(content, tokenize='trigram')"));
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "INSERT INTO probe_match(content) VALUES ('家庭网络存储中的扫描件文字识别')"));

        // 形态 A：绑定参数（含双引号的查询串）
        var paramHits = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM probe_match WHERE probe_match MATCH {0}", "\"扫描件\""),
            false);
        paramHits.Should().Be(1, "绑定参数形态应命中");

        // 形态 B：SQL 字面量
        var literalHits = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM probe_match WHERE probe_match MATCH '\"扫描件\"'"),
            false);
        literalHits.Should().Be(1, "字面量形态应命中");
    }

    [Fact]
    public void SqliteVec_LoadsAndRunsKnnQuery()
    {
        var vecDll = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "vec0.dll");
        File.Exists(vecDll).Should().BeTrue($"vec0.dll 应随 sqlite-vec 包落到输出目录（探针路径 {vecDll}）");

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        connection.EnableExtensions();
        try
        {
            connection.LoadExtension(Path.GetFullPath(vecDll));
        }
        finally
        {
            connection.EnableExtensions(false);
        }

        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS probe_vec USING vec0(id INTEGER PRIMARY KEY, embedding float[4])";
            create.ExecuteNonQuery();
        }
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO probe_vec(id, embedding) VALUES (1, $v)";
            insert.Parameters.AddWithValue("$v", "[1.0, 0.0, 0.0, 0.0]");
            insert.ExecuteNonQuery();
        }
        using (var knn = connection.CreateCommand())
        {
            knn.CommandText =
                "SELECT id FROM probe_vec WHERE embedding MATCH $q ORDER BY distance LIMIT 1";
            knn.Parameters.AddWithValue("$q", "[1.0, 0.1, 0.0, 0.0]");
            Convert.ToInt32(knn.ExecuteScalar()).Should().Be(1, "sqlite-vec KNN 查询应可用");
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
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
