// =============================================================================
// NasSystem.Benchmark - Program.cs
// CLI 入口：seed / data / api / proc / report
// =============================================================================

using NasSystem.Benchmark;
using NasSystem.Benchmark.Bench;
using NasSystem.Benchmark.Report;
using NasSystem.Benchmark.Seeding;

// 用法：
//   dotnet run --project tools/Benchmark -- seed  --rows 10000 [--assert]
//   dotnet run --project tools/Benchmark -- data  --rows 10000 [--warmup 200] [--iterations 2000] [--rounds 5]
//   dotnet run --project tools/Benchmark -- api   --rows 10000 [--concurrency 8] [--duration 15] [--port 6100]
//   dotnet run --project tools/Benchmark -- proc  --rows 10000 [--coldstarts 10] [--port 6200]
//   dotnet run --project tools/Benchmark -- all   --rows 10000
var command = args.Length > 0 ? args[0] : "help";
var options = ParseOptions(args);

switch (command)
{
    case "seed":
        await SeedCommandAsync(options);
        break;

    case "data":
        await DataCommandAsync(options);
        break;

    case "api":
        await ApiCommandAsync(options);
        break;

    case "proc":
        await ProcCommandAsync(options);
        break;

    case "all":
        await SeedCommandAsync(options, assert: true);
        await DataCommandAsync(options);
        await ApiCommandAsync(options);
        await ProcCommandAsync(options);
        break;

    default:
        Console.WriteLine("""
            NAS 性能基准 harness

            用法：
              seed  --rows N [--assert]     生成确定性数据集并校验 schema
              data  --rows N [--warmup W] [--iterations I] [--rounds R]
              api   --rows N [--concurrency C] [--duration S] [--port P]
              proc  --rows N [--coldstarts N] [--port P]
              all   --rows N                依次跑完上面四项

            产物写入 docs/perf/<臂>-<时间戳>.md 与 .json
            """);
        return 1;
}

return 0;

static Dictionary<string, string> ParseOptions(string[] args)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        var key = args[i][2..];
        // 無值旗標（後面沒有參數或下一個也是 --開頭）記為 true
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            map[key] = "true";
        else
            map[key] = args[++i];
    }
    return map;
}

static int IntOption(Dictionary<string, string> options, string key, int fallback) =>
    options.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

static double DoubleOption(Dictionary<string, string> options, string key, double fallback) =>
    options.TryGetValue(key, out var value) && double.TryParse(value, out var parsed) ? parsed : fallback;

static bool BoolOption(Dictionary<string, string> options, string key) =>
    options.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;

static async Task SeedCommandAsync(Dictionary<string, string> options, bool assert = false)
{
    var rows = IntOption(options, "rows", 10000);
    var dbPath = Paths.SeedDb(rows);
    Console.WriteLine($"[seed] 目标 {rows} 行 → {dbPath}");
    var inserted = await Seeder.SeedAsync(dbPath, rows);

    if (assert || BoolOption(options, "assert"))
        await AssertSchemaAsync(dbPath);

    Console.WriteLine($"[seed] 完成，实际插入 {inserted} 行");
}

static async Task AssertSchemaAsync(string dbPath)
{
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
    connection.Open();

    var expected = new HashSet<string>(StringComparer.Ordinal);
    foreach (var ddl in Schema.Tables)
        expected.Add(ExtractName(ddl, "TABLE"));
    foreach (var ddl in Schema.Indexes)
        expected.Add(ExtractName(ddl, "INDEX"));

    var actual = new HashSet<string>(StringComparer.Ordinal);
    await using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            actual.Add(reader.GetString(0));
    }

    var missing = expected.Where(e => !actual.Contains(e)).ToList();
    var extra = actual.Where(a => !expected.Contains(a)).ToList();

    if (missing.Count > 0 || extra.Count > 0)
    {
        Console.WriteLine($"[assert] 失败：缺失 {string.Join(", ", missing)}；多出 {string.Join(", ", extra)}");
        throw new InvalidOperationException("种子库 schema 与 Schema.cs 声明不一致");
    }

    Console.WriteLine($"[assert] schema 一致（{actual.Count} 个对象）");
}

static string ExtractName(string ddl, string kind)
{
    var marker = $"CREATE {(kind == "INDEX" ? "UNIQUE INDEX" : "TABLE")} ";
    var start = ddl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
        marker = $"CREATE {kind} ";
    start = ddl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
        throw new InvalidOperationException($"无法解析 DDL 名称: {ddl[..Math.Min(60, ddl.Length)]}");
    start += marker.Length;
    var rest = ddl[start..].Trim();
    if (rest.StartsWith("\"", StringComparison.Ordinal))
    {
        var end = rest.IndexOf('"', 1);
        return rest[1..end];
    }
    return rest.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim('"');
}

static async Task DataCommandAsync(Dictionary<string, string> options)
{
    var rows = IntOption(options, "rows", 10000);
    var warmup = IntOption(options, "warmup", 200);
    var iterations = IntOption(options, "iterations", 2000);
    var rounds = IntOption(options, "rounds", 5);

    var seedDb = Paths.SeedDb(rows);
    if (!File.Exists(seedDb))
    {
        Console.WriteLine($"[data] 种子库不存在，先生成 {rows} 行");
        await Seeder.SeedAsync(seedDb, rows);
    }

    Console.WriteLine($"[data] 仓储级基准：预热 {warmup}，测量 {iterations}×{rounds}");
    var progress = new Progress<string>(m => Console.WriteLine(m));
    var cases = await DataAccessBench.RunAsync(seedDb, warmup, iterations, rounds, progress);

    foreach (var c in cases)
        Console.WriteLine($"  {c.Name,-28} ORM P50={c.Orm.P50,8:F3}ms  地板 P50={c.Floor.P50,8:F3}ms  比值={c.Ratio:F2}×");

    var record = BuildRecord(rows, warmup, iterations, rounds, cases, null, null);
    var path = ReportWriter.Write(record);
    Console.WriteLine($"[data] 报告已写入 {path}");
}

static async Task ApiCommandAsync(Dictionary<string, string> options)
{
    var rows = IntOption(options, "rows", 10000);
    var concurrency = IntOption(options, "concurrency", 8);
    var duration = DoubleOption(options, "duration", 15);
    var port = IntOption(options, "port", 6100);

    var seedDb = Paths.SeedDb(rows);
    if (!File.Exists(seedDb))
        await Seeder.SeedAsync(seedDb, rows);

    Console.WriteLine($"[api] 端到端基准：并发 {concurrency}，时长 {duration}s");
    var progress = new Progress<string>(m => Console.WriteLine(m));
    var result = await ApiBench.RunAsync(seedDb, concurrency, duration, port, progress);

    Console.WriteLine($"  总请求 {result.TotalRequests}，错误 {result.Errors}，吞吐 {result.Rps:F1} req/s");
    Console.WriteLine($"  整体延迟：{result.LatencyMs.Describe()}");

    var record = BuildRecord(rows, 0, 0, 0, [], result, null);
    var path = ReportWriter.Write(record);
    Console.WriteLine($"[api] 报告已写入 {path}");
}

static async Task ProcCommandAsync(Dictionary<string, string> options)
{
    var rows = IntOption(options, "rows", 10000);
    var coldStarts = IntOption(options, "coldstarts", 10);
    var port = IntOption(options, "port", 6200);

    var seedDb = Paths.SeedDb(rows);
    if (!File.Exists(seedDb))
        await Seeder.SeedAsync(seedDb, rows);

    Console.WriteLine($"[proc] 进程级基准：冷启动 {coldStarts} 次");
    var progress = new Progress<string>(m => Console.WriteLine(m));
    var result = await ProcessBench.RunAsync(seedDb, coldStarts, port, progress);

    Console.WriteLine($"  冷启动中位 {result.ColdStartMsMedian:F0} ms，稳态工作集 {result.SteadyStateWorkingSetMb} MB，发布体积 {result.PublishSizeMb} MB");

    var record = BuildRecord(rows, 0, 0, 0, [], null, result);
    var path = ReportWriter.Write(record);
    Console.WriteLine($"[proc] 报告已写入 {path}");
}

static RunRecord BuildRecord(
    int rows, int warmup, int iterations, int rounds,
    List<CaseResult> cases, ApiResult? api, ProcessResult? process)
{
    var sha = "unknown";
    try
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Paths.RepoRoot,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--short");
        startInfo.ArgumentList.Add("HEAD");
        using var git = System.Diagnostics.Process.Start(startInfo);
        if (git is not null)
        {
            var output = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit(5000);
            if (git.ExitCode == 0 && output.Length > 0)
                sha = output;
        }
    }
    catch
    {
        // git 不可用时保留 unknown，不影响基准本身
    }

    return new RunRecord(
        ArmLabel(),
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OrmLabel(),
        rows, warmup, iterations, rounds,
        api?.Concurrency ?? 0, api?.DurationSec ?? 0,
        MachineInfo.Capture(), sha, DateTime.UtcNow,
        cases, api, process);
}

/// <summary>臂标签由加载的 ORM 程序集自动判定，避免人为标错。</summary>
static string ArmLabel()
{
    var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.Ordinal);
    var hasPalorm = loaded.Any(n => n is not null && n.StartsWith("PalORM", StringComparison.Ordinal));
    var tfm = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Contains("11.", StringComparison.Ordinal)
        ? "net11"
        : "net10";
    return hasPalorm ? $"{tfm}-palorm" : $"{tfm}-ef";
}

static string OrmLabel()
{
    // 显式触达一次，确保程序集已加载（api/proc 命令本身不碰 EF）
    try
    {
        _ = typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly;
    }
    catch
    {
        // EF 程序集不在引用链里时忽略
    }

    var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.Ordinal);
    if (loaded.Any(n => n is not null && n.StartsWith("PalORM", StringComparison.Ordinal)))
    {
        var version = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "PalORM.Core")?
            .GetName().Version?.ToString() ?? "?";
        return $"PalORM {version}";
    }

    var ef = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name is "Microsoft.EntityFrameworkCore" or "Microsoft.EntityFrameworkCore.Sqlite")?
        .GetName().Version?.ToString() ?? "?";
    return $"EF Core {ef}";
}
