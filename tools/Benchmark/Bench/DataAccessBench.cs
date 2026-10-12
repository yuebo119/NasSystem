// =============================================================================
// NasSystem.Benchmark - DataAccessBench.cs
// 仓储级微基准：ORM 臂与 ADO.NET 地板臂同进程交错
// =============================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;

namespace NasSystem.Benchmark.Bench;

/// <summary>单个基准用例的度量结果。Ratio = ORM臂P50 / 地板臂P50。</summary>
public sealed record CaseResult(
    string Name, bool IsWrite, Stats Orm, Stats Floor, double OrmAllocKbPerOp, double FloorAllocKbPerOp)
{
    public double Ratio => Floor.P50 > 0 ? Orm.P50 / Floor.P50 : 0;
}

/// <summary>
/// 仓储级基准。ORM 臂经 Domain 仓储接口调用（实现是 PalORM，harness 只依赖 Domain 接口，
/// 换实现时零改动），地板臂为裸 ADO.NET。
/// </summary>
/// <remarks>
/// 交错执行的原因：分段计时只能定位热点，量化收益必须墙钟交替。
/// 读用例在同一份库上两臂交替，每轮交换起始臂以抵消顺序偏差；
/// 写用例每次迭代换一份新库副本（复制耗时不计入测量），避免两臂互相干扰。
/// 度量不含 DI 与 HTTP 管线，那部分由 api 基准覆盖。
/// </remarks>
public static class DataAccessBench
{
    /// <summary>写用例专用的小库：表规模不影响单行写成本，但决定每次迭代的复制开销。</summary>
    private static string SmallSeedDb => Paths.SeedDb(1000);

    /// <summary>写用例的预热与测量次数。复制库的 I/O 占大头，样本数比读用例少一个量级。</summary>
    private const int WriteWarmup = 50;
    private const int WriteIterations = 200;

    public static async Task<List<CaseResult>> RunAsync(
        string seedDb, int warmup, int iterations, int rounds, IProgress<string>? progress = null)
    {
        SweepWorkDbs();

        // 写用例固定用 1k 库；缺失则现场生成
        if (!File.Exists(SmallSeedDb))
            await Seeding.Seeder.SeedAsync(SmallSeedDb, 1000, verbose: false);

        // 写阶段的两份可复用库在此统一准备一次；各用例靠重置动作维持状态
        var writeProbesForCopy = await DiscoverProbesAsync(SmallSeedDb);
        CopyDb(SmallSeedDb, Paths.WorkDb("write-orm"));
        CopyDb(SmallSeedDb, Paths.WorkDb("write-floor"));

        var workDb = Paths.WorkDb("read");
        CopyDb(seedDb, workDb);
        ApplyWal(workDb);

        var probes = await DiscoverProbesAsync(seedDb);

        await using var floor = await AdoNetFloor.OpenAsync(workDb);
        using var orm = OrmContainer.Create(workDb);

        await VerifyArmsAgreeAsync(orm, floor, BuildReadCases(probes));

        var results = new List<CaseResult>();

        foreach (var readCase in BuildReadCases(probes))
        {
            progress?.Report($"  读用例 {readCase.Name}");
            results.Add(await MeasureAsync(
                readCase.Name, false, warmup, iterations, rounds,
                () => readCase.Orm(orm), () => readCase.Floor(floor)));
        }

        // 写用例固定用 1k 小库：写成本由单次写入行数主导，与表规模基本无关，
        // 而复制库文件的开销随表规模线性增长，小库足以分辨两臂差异
        var writeProbes = probes.SeedDb == SmallSeedDb
            ? probes
            : writeProbesForCopy;

        foreach (var writeCase in BuildWriteCases(writeProbes))
        {
            progress?.Report($"  写用例 {writeCase.Name}");
            results.Add(await MeasureWriteAsync(writeCase, writeProbes, WriteWarmup, WriteIterations, rounds));
        }

        // 写用例的临时库在每个迭代后删除，句柄释放有延迟，结束时再扫一遍
        SweepWorkDbs();

        return results;
    }

    /// <summary>
    /// 开测前核对两臂结果一致。 Guid 在 SQLite 里按 TEXT 大小写敏感比较，
    /// 参数格式不一致会让某一臂静默返回空集，那样量到的不是性能而是空查询。
    /// </summary>
    private static async Task VerifyArmsAgreeAsync(OrmContainer orm, AdoNetFloor floor, List<ReadCase> cases)
    {
        var mismatches = new List<string>();
        foreach (var readCase in cases)
        {
            var ormValue = await readCase.OrmSignature(orm);
            var floorValue = await readCase.FloorSignature(floor);
            if (ormValue != floorValue)
                mismatches.Add($"{readCase.Name}: ORM={ormValue} 地板={floorValue}");
        }

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                "两臂结果不一致，基准无效：" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
        }
    }

    private static async Task<CaseResult> MeasureAsync(
        string name, bool isWrite, int warmup, int iterations, int rounds,
        Func<Task> ormAction, Func<Task> floorAction)
    {
        var ormSamples = new List<double>(iterations * rounds);
        var floorSamples = new List<double>(iterations * rounds);
        var ormAlloc = 0.0;
        var floorAlloc = 0.0;

        // 预热：两个臂各跑，避免首次 JIT 与连接建立算进测量
        for (var i = 0; i < warmup; i++)
        {
            await SampleAsync(ormAction, v => ormAlloc = v);
            await SampleAsync(floorAction, v => floorAlloc = v);
        }

        for (var round = 0; round < rounds; round++)
        {
            // 每轮交换起始臂，抵消缓存与热漂移的顺序偏差
            var ormFirst = round % 2 == 0;
            for (var i = 0; i < iterations; i++)
            {
                if (ormFirst)
                {
                    ormSamples.Add(await SampleAsync(ormAction, v => ormAlloc = v));
                    floorSamples.Add(await SampleAsync(floorAction, v => floorAlloc = v));
                }
                else
                {
                    floorSamples.Add(await SampleAsync(floorAction, v => floorAlloc = v));
                    ormSamples.Add(await SampleAsync(ormAction, v => ormAlloc = v));
                }
            }
        }

        return new CaseResult(
            name, isWrite,
            Stats.From(ormSamples), Stats.From(floorSamples),
            ormAlloc / Math.Max(1, ormSamples.Count),
            floorAlloc / Math.Max(1, floorSamples.Count));
    }

    private static async Task<CaseResult> MeasureWriteAsync(
        WriteCase writeCase, Probes probes, int warmup, int iterations, int rounds)
    {
        // 写用例在可复用库上进行：每次迭代前做一次不计入测量的重置，
        // 避免逐迭代或逐用例复制库文件（I/O 与句柄释放都不稳定）。
        // 库文件由 RunAsync 在写阶段开始前统一准备。
        var ormDb = Paths.WorkDb("write-orm");
        var floorDb = Paths.WorkDb("write-floor");

        var ormSamples = new List<double>(iterations * rounds);
        var floorSamples = new List<double>(iterations * rounds);
        var ormAlloc = 0.0;
        var floorAlloc = 0.0;

        for (var round = 0; round < warmup + iterations * rounds; round++)
        {
            var isWarmup = round < warmup;
            var ormFirst = (round - warmup) % 2 == 0;

            if (isWarmup || ormFirst)
            {
                var sample = await RunOrmWriteOnce(writeCase, ormDb, v => ormAlloc = v);
                if (!isWarmup) ormSamples.Add(sample);
            }

            if (isWarmup || !ormFirst)
            {
                var sample = await RunFloorWriteOnce(writeCase, floorDb, v => floorAlloc = v);
                if (!isWarmup) floorSamples.Add(sample);
            }
        }

        return new CaseResult(
            writeCase.Name, true,
            Stats.From(ormSamples), Stats.From(floorSamples),
            ormAlloc / Math.Max(1, ormSamples.Count),
            floorAlloc / Math.Max(1, floorSamples.Count));
    }

    private static async Task<double> RunOrmWriteOnce(WriteCase writeCase, string db, Action<double> setAlloc)
    {
        // 重置放在独立容器里：重置写入的实体会被 EF 跟踪，
        // 若与计时动作共用同一上下文，计时段的删除会撞上 IdentityMap 键冲突
        using (var resetContainer = OrmContainer.Create(db))
            await writeCase.ResetOrm(resetContainer);

        using var container = OrmContainer.Create(db);
        var before = GC.GetTotalAllocatedBytes(true);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await writeCase.Orm(container);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        setAlloc((GC.GetTotalAllocatedBytes(true) - before) / 1024.0);
        return elapsed;
    }

    private static async Task<double> RunFloorWriteOnce(WriteCase writeCase, string db, Action<double> setAlloc)
    {
        var floor = await AdoNetFloor.OpenAsync(db);
        await writeCase.ResetFloor(floor);

        var before = GC.GetTotalAllocatedBytes(true);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await writeCase.Floor(floor);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        setAlloc((GC.GetTotalAllocatedBytes(true) - before) / 1024.0);
        await floor.DisposeAsync();
        return elapsed;
    }

    /// <summary>清扫上一轮遗留的临时库，避免 data 目录无限增长。</summary>
    public static void SweepWorkDbs()
    {
        if (!Directory.Exists(Paths.DataDir))
            return;

        foreach (var file in Directory.GetFiles(Paths.DataDir, "work-*"))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // 被占用，跳过
            }
        }
    }

    private static async Task<double> RunFloorWriteOnce(WriteCase writeCase, Probes probes, int serial, Action<double> setAlloc)
    {
        var db = Paths.WorkDb($"wf{serial}");
        CopyDb(probes.SeedDb, db);

        var floor = await AdoNetFloor.OpenAsync(db);
        var before = GC.GetTotalAllocatedBytes(true);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await writeCase.Floor(floor);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        setAlloc((GC.GetTotalAllocatedBytes(true) - before) / 1024.0);
        await floor.DisposeAsync();

        return elapsed;
    }

    private static async Task<double> SampleAsync(Func<Task> action, Action<double> setAlloc)
    {
        var before = GC.GetTotalAllocatedBytes(true);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        await action();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        setAlloc((GC.GetTotalAllocatedBytes(true) - before) / 1024.0);
        return elapsed;
    }

    private static void CopyDb(string source, string target)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            if (File.Exists(target + suffix))
                File.Delete(target + suffix);
        }
        File.Copy(source, target);
    }

    private static void ApplyWal(string dbPath)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
    }

    private static async Task<Probes> DiscoverProbesAsync(string seedDb)
    {
        var topFolder = await FirstRowAsync(seedDb, """
            SELECT "Id","Path" FROM "FileItems" WHERE "ParentId" IS NULL AND "ItemType" = 1 ORDER BY "Path" LIMIT 1
            """);
        if (topFolder is null)
            throw new InvalidOperationException("种子库没有顶层目录，无法构造探测参数");

        var sampleChildPath = await ScalarAsync(seedDb, """
            SELECT "Path" FROM "FileItems" WHERE "ParentId" IS NOT NULL AND "IsDeleted" = 0 ORDER BY "Path" LIMIT 1
            """);
        if (sampleChildPath is null)
            throw new InvalidOperationException("种子库没有子项，无法构造探测参数");

        var userId = await ScalarAsync(seedDb, """SELECT "Id" FROM "Users" ORDER BY "Username" LIMIT 1""");
        var poolId = await ScalarAsync(seedDb, """SELECT "Id" FROM "StoragePools" ORDER BY "Name" LIMIT 1""");

        return new Probes(seedDb, topFolder.Value.Id, topFolder.Value.Path, sampleChildPath, Guid.Parse(userId!), Guid.Parse(poolId!));
    }

    private static async Task<(Guid Id, string Path)?> FirstRowAsync(string dbPath, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (Guid.Parse(reader.GetString(0)), reader.GetString(1));
    }

    private static async Task<string?> ScalarAsync(string dbPath, string sql)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private sealed record Probes(
        string SeedDb, Guid TopFolderId, string TopFolderPath, string SampleChildPath, Guid UserId, Guid PoolId);

    private sealed record ReadCase(
        string Name,
        Func<OrmContainer, Task> Orm,
        Func<AdoNetFloor, Task> Floor,
        Func<OrmContainer, Task<long>> OrmSignature,
        Func<AdoNetFloor, Task<long>> FloorSignature);

    private static List<ReadCase> BuildReadCases(Probes p) =>
    [
        new("GetByPath",
            o => o.FileItems.GetByPathAsync(p.SampleChildPath), f => f.GetByPathAsync(p.SampleChildPath),
            async o => (await o.FileItems.GetByPathAsync(p.SampleChildPath)) is null ? 0 : 1,
            async f => (await f.GetByPathAsync(p.SampleChildPath)) is null ? 0 : 1),
        new("GetChildren",
            o => o.FileItems.GetChildrenAsync(p.TopFolderId), f => f.GetChildrenAsync(p.TopFolderId),
            async o => (await o.FileItems.GetChildrenAsync(p.TopFolderId)).Count,
            async f => (await f.GetChildrenAsync(p.TopFolderId)).Count),
        new("Search_名称模糊",
            o => o.FileItems.SearchAsync("report"), f => f.SearchAsync("report"),
            async o => (await o.FileItems.SearchAsync("report")).Count,
            async f => (await f.SearchAsync("report")).Count),
        new("GetStatistics",
            o => o.FileItems.GetStatisticsAsync(), f => f.GetStatisticsAsync(DateTime.UtcNow.AddDays(-7)),
            async o => (await o.FileItems.GetStatisticsAsync()).TotalFiles,
            async f => (await f.GetStatisticsAsync(DateTime.UtcNow.AddDays(-7))).TotalFiles),
        new("GetDescendants",
            o => o.FileItems.GetDescendantsAsync(p.TopFolderId), f => f.GetDescendantsAsync(p.TopFolderId),
            async o => (await o.FileItems.GetDescendantsAsync(p.TopFolderId)).Count,
            async f => (await f.GetDescendantsAsync(p.TopFolderId)).Count),
        new("GetPagedUsers",
            o => o.Users.GetPagedAsync(1, 20), f => f.GetPagedUsersAsync(1, 20),
            async o => (await o.Users.GetPagedAsync(1, 20)).TotalCount,
            async f => (await f.GetPagedUsersAsync(1, 20)).Total),
        new("CalculateUserUsedSpace",
            o => o.FileItems.CalculateUserUsedSpaceAsync(p.UserId), f => f.SumUsedSpaceAsync(p.UserId),
            async o => await o.FileItems.CalculateUserUsedSpaceAsync(p.UserId),
            async f => await f.SumUsedSpaceAsync(p.UserId)),
        new("PathExists",
            o => o.FileItems.PathExistsAsync(p.SampleChildPath), f => f.PathExistsAsync(p.SampleChildPath),
            async o => await o.FileItems.PathExistsAsync(p.SampleChildPath) ? 1 : 0,
            async f => await f.PathExistsAsync(p.SampleChildPath) ? 1 : 0),
        new("GetByStoragePool",
            o => o.FileItems.GetByStoragePoolAsync(p.PoolId), f => f.GetByStoragePoolAsync(p.PoolId),
            async o => (await o.FileItems.GetByStoragePoolAsync(p.PoolId)).Count,
            async f => (await f.GetByStoragePoolAsync(p.PoolId)).Count),
        new("GetDeletedItems",
            o => o.FileItems.GetDeletedItemsAsync(), f => f.GetDeletedItemsAsync(),
            async o => (await o.FileItems.GetDeletedItemsAsync()).Count,
            async f => (await f.GetDeletedItemsAsync()).Count),
        new("CalculateStoragePoolUsedSpace",
            o => o.FileItems.CalculateStoragePoolUsedSpaceAsync(p.PoolId), f => f.SumUsedSpaceByPoolAsync(p.PoolId),
            async o => await o.FileItems.CalculateStoragePoolUsedSpaceAsync(p.PoolId),
            async f => await f.SumUsedSpaceByPoolAsync(p.PoolId)),
    ];

    private sealed record WriteCase(
        string Name,
        Func<OrmContainer, Task> ResetOrm,
        Func<OrmContainer, Task> Orm,
        Func<AdoNetFloor, Task> ResetFloor,
        Func<AdoNetFloor, Task> Floor);

    private static List<WriteCase> BuildWriteCases(Probes p) =>
    [
        new("Insert_20行",
            // 重置：删掉上一轮插入的行（不计时），保持表规模恒定
            async o =>
            {
                var stale = (await o.FileItems.GetChildrenAsync(p.TopFolderId))
                    .Where(i => i.Name.StartsWith("bench_", StringComparison.Ordinal))
                    .ToList();
                if (stale.Count > 0)
                {
                    await o.FileItems.DeleteRangeAsync(stale);
                    await o.UnitOfWork.SaveChangesAsync();
                }
            },
            async o =>
            {
                for (var i = 0; i < 20; i++)
                {
                    var unique = Guid.NewGuid().ToString("N");
                    await o.FileItems.AddAsync(NasSystem.Domain.Entities.FileItem.CreateFile(
                        $"bench_{unique}.txt", $"{p.TopFolderPath}/bench_{unique}.txt",
                        $"/storage{p.TopFolderPath}/bench_{unique}.txt", 1024, "text/plain",
                        p.PoolId, p.UserId, p.TopFolderId));
                }
                await o.UnitOfWork.SaveChangesAsync();
            },
            f => f.DeleteBenchRowsAsync(p.TopFolderPath, "bench_"),
            f => f.InsertBenchAsync(p.TopFolderPath, p.TopFolderId, p.PoolId, p.UserId, 20)),
        new("Update_20行",
            // 无需重置：每次改名都产生新的有效行，表规模不变
            o => Task.CompletedTask,
            async o =>
            {
                var items = await o.FileItems.GetChildrenAsync(p.TopFolderId);
                foreach (var item in items.Take(20))
                    item.Rename($"renamed_{Guid.NewGuid():N}.txt");
                await o.UnitOfWork.SaveChangesAsync();
            },
            f => Task.CompletedTask,
            f => f.UpdateBenchAsync(p.TopFolderId, 20)),
        new("Delete_20行",
            // 重置：把上一轮删掉的行按原路径补回（不计时），保证下一轮有 20 个可删目标
            async o =>
            {
                for (var i = 0; i < 20; i++)
                {
                    var unique = Guid.NewGuid().ToString("N");
                    await o.FileItems.AddAsync(NasSystem.Domain.Entities.FileItem.CreateFile(
                        $"deltarget_{unique}.txt", $"{p.TopFolderPath}/deltarget_{unique}.txt",
                        $"/storage{p.TopFolderPath}/deltarget_{unique}.txt", 1024, "text/plain",
                        p.PoolId, p.UserId, p.TopFolderId));
                }
                await o.UnitOfWork.SaveChangesAsync();
            },
            async o =>
            {
                var items = await o.FileItems.GetChildrenAsync(p.TopFolderId);
                await o.FileItems.DeleteRangeAsync(items.Take(20));
                await o.UnitOfWork.SaveChangesAsync();
            },
            f => f.ReinsertBenchRowsAsync(p.TopFolderPath, p.TopFolderId, p.PoolId, p.UserId, 20),
            f => f.DeleteBenchAsync(p.TopFolderId, 20)),
    ];

    /// <summary>ORM 臂的 DI 容器。仓储接口在此解析，实现随 Infrastructure 注册而变。</summary>
    private sealed class OrmContainer : IDisposable
    {
        private readonly IServiceScope _scope;
        private readonly ServiceProvider _provider;

        private OrmContainer(IServiceScope scope, ServiceProvider provider)
        {
            _scope = scope;
            _provider = provider;
        }

        public static OrmContainer Create(string dbPath)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddInfrastructureServices(config);
            var provider = services.BuildServiceProvider();
            return new OrmContainer(provider.CreateScope(), provider);
        }

        public IFileItemRepository FileItems => _scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        public IUserRepository Users => _scope.ServiceProvider.GetRequiredService<IUserRepository>();
        public IUnitOfWork UnitOfWork => _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public void Dispose()
        {
            // 先释放 scope（含 PalORM 会话与连接），再释放根容器，最后才允许删库文件
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
