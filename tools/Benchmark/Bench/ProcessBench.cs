// =============================================================================
// NasSystem.Benchmark - ProcessBench.cs
// 进程级指标：冷启动、稳态内存、发布产物体积
// =============================================================================

namespace NasSystem.Benchmark.Bench;

using System.Diagnostics;

/// <summary>进程级指标结果。</summary>
public sealed record ProcessResult(
    double ColdStartMsMedian,
    double ColdStartMsMin,
    int ColdStartRuns,
    long SteadyStateWorkingSetMb,
    long ManagedHeapMb,
    long PublishSizeMb,
    int PublishFileCount);

/// <summary>
/// 冷启动到首个健康检查通过的耗时、稳态内存与 Release 发布体积。
/// </summary>
/// <remarks>
/// 冷启动是 EF 与 PalORM 差异最可能显现的地方：EF 首次要构建模型，
/// PalORM 的物化与 SQL 已在编译期生成。这一项单独度量，不与吞吐混在一起。
/// </remarks>
public static class ProcessBench
{
    private const string HealthPath = "/api/v1/system/health";

    public static async Task<ProcessResult> RunAsync(
        string seedDb, int coldStartRuns, int port, IProgress<string>? progress = null)
    {
        var workDb = Paths.WorkDb("proc");
        foreach (var suffix in new[] { "", "-shm", "-wal" })
            if (File.Exists(workDb + suffix))
                File.Delete(workDb + suffix);
        File.Copy(seedDb, workDb);

        var storagePath = Path.Combine(Path.GetTempPath(), $"nas-bench-proc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storagePath);

        var coldStarts = new List<double>();
        try
        {
            // 发布放在最前面：冷启动进程会占用 Debug/Release 产物目录，
            // 若先起进程再 publish，文件锁会让 publish 失败并量到 0 MB
            progress?.Report("  发布产物体积");
            var (publishMb, fileCount) = MeasurePublishSize();

            for (var i = 0; i < coldStartRuns; i++)
            {
                progress?.Report($"  冷启动 {i + 1}/{coldStartRuns}");
                coldStarts.Add(await MeasureColdStartAsync(port, workDb, storagePath));
                port++;
            }

            progress?.Report("  稳态内存采样");
            var (workingSet, heap) = await MeasureSteadyStateAsync(port, workDb, storagePath);

            return new ProcessResult(
                Median(coldStarts), coldStarts.Min(), coldStartRuns,
                workingSet, heap, publishMb, fileCount);
        }
        finally
        {
            try
            {
                if (Directory.Exists(storagePath))
                    Directory.Delete(storagePath, recursive: true);
            }
            catch
            {
                // 目录被占用
            }
        }
    }

    private static async Task<double> MeasureColdStartAsync(int port, string dbPath, string storagePath)
    {
        using var process = StartApi(port, dbPath, storagePath);
        var start = Stopwatch.GetTimestamp();
        var deadline = start + Stopwatch.Frequency * 60;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (Stopwatch.GetTimestamp() < deadline)
        {
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}{HealthPath}");
                if (response.IsSuccessStatusCode)
                    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            catch
            {
                // 进程尚未监听
            }
            await Task.Delay(50);
        }

        throw new InvalidOperationException("冷启动测量超时（60 秒）");
    }

    private static async Task<(long WorkingSetMb, long HeapMb)> MeasureSteadyStateAsync(int port, string dbPath, string storagePath)
    {
        using var process = StartApi(port, dbPath, storagePath);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var response = await client.GetAsync($"http://127.0.0.1:{port}{HealthPath}");
                    if (response.IsSuccessStatusCode)
                        break;
                }
                catch
                {
                    // 尚未就绪
                }
                await Task.Delay(200);
            }

            // 打一轮真实读负载，让内存进入稳态而不是仅启动态
            for (var i = 0; i < 50; i++)
            {
                try
                {
                    using var _ = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/files/statistics");
                }
                catch
                {
                    // 单次失败不影响采样
                }
            }

            await Task.Delay(500);
            process.Refresh();
            var workingSet = process.WorkingSet64 / 1024 / 1024;
            var heap = GC.GetGCMemoryInfo().HeapSizeBytes / 1024 / 1024;
            return (workingSet, heap);
        }
        finally
        {
            // Dispose Process 对象不会终止进程；不显式 Kill 会残留监听端口的宿主，
            // 既占用端口又锁住 bin 目录让后续 build/publish 失败
            TryKill(process);
        }
    }

    private static (long SizeMb, int FileCount) MeasurePublishSize()
    {
        var output = Path.Combine(Path.GetTempPath(), $"nas-bench-publish-{Guid.NewGuid():N}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Paths.RepoRoot,
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(Path.Combine(Paths.ApiProjectDir, "NasSystem.Api.csproj"));
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(output);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet publish 启动失败");
        process.WaitForExit(300000);

        if (!Directory.Exists(output))
            return (0, 0);

        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        var bytes = files.Sum(f => new FileInfo(f).Length);

        try
        {
            Directory.Delete(output, recursive: true);
        }
        catch
        {
            // 残留由系统临时目录清理
        }

        return (bytes / 1024 / 1024, files.Length);
    }

    /// <summary>
    /// 终止 API 子进程。<see cref="Process.Dispose"/> 只释放进程对象，
    /// 不会结束进程本身，故采样结束必须显式 Kill。
    /// </summary>
    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程已退出
        }
    }

    private static Process StartApi(int port, string dbPath, string storagePath)
    {
        var dll = Paths.ApiDllPath;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Paths.ApiProjectDir,
        };
        startInfo.ArgumentList.Add(dll);
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={dbPath}";
        startInfo.Environment["Jwt__SecretKey"] = "benchmark-only-secret-key-32chars-minimum!!";
        startInfo.Environment["NasSystem__StoragePath"] = storagePath;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("API 进程启动失败");
        _ = Task.Run(async () =>
        {
            while (!process.HasExited)
                await process.StandardOutput.ReadLineAsync();
        });
        _ = Task.Run(async () =>
        {
            while (!process.HasExited)
                await process.StandardError.ReadLineAsync();
        });
        return process;
    }

    private static double Median(List<double> values)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }
}
