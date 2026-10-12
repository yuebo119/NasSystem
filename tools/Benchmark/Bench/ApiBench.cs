// =============================================================================
// NasSystem.Benchmark - ApiBench.cs
// 端到端 HTTP 基准：起真实 API 进程，并发打 8 个端点
// =============================================================================

namespace NasSystem.Benchmark.Bench;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>端到端基准结果。</summary>
public sealed record ApiResult(
    string BaseUrl,
    int Concurrency,
    double DurationSec,
    int TotalRequests,
    int Errors,
    double Rps,
    Stats LatencyMs,
    Dictionary<string, Stats> ByEndpoint);

/// <summary>
/// 以子进程方式启动真实 API（非 TestServer），度量冷启动之后的稳态吞吐与延迟。
/// </summary>
/// <remarks>
/// 用子进程而不是 WebApplicationFactory：要量的就是完整 Kestrel 管线与真实启动路径，
/// 同时也为 proc 基准的冷启动指标复用同一套启动逻辑。
/// </remarks>
public static class ApiBench
{
    private const string HealthPath = "/api/v1/system/health";
    private const string LoginPath = "/api/v1/users/login";

    public static async Task<ApiResult> RunAsync(
        string seedDb, int concurrency, double durationSec, int port, IProgress<string>? progress = null)
    {
        var workDb = Paths.WorkDb("api");
        foreach (var suffix in new[] { "", "-shm", "-wal" })
            if (File.Exists(workDb + suffix))
                File.Delete(workDb + suffix);
        File.Copy(seedDb, workDb);

        var storagePath = Path.Combine(Path.GetTempPath(), $"nas-bench-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storagePath);

        var baseUrl = $"http://127.0.0.1:{port}";
        using var process = StartApiProcess(port, workDb, storagePath);

        try
        {
            var ready = await WaitForReadyAsync(baseUrl, TimeSpan.FromSeconds(60));
            if (!ready)
                throw new InvalidOperationException($"API 未在 60 秒内就绪: {baseUrl}{HealthPath}");

            var token = await LoginAsync(baseUrl);

            // 分享码从种子库取：公开分享访问是真实 NAS 里最热的只读路径之一，
            // 且它正是仓储里唯一还有 3 次按 Id 直查的路径（B3 的优化对象），必须进基准
            var shareCode = await FirstShareCodeAsync(workDb) ?? "share000000";

            var endpoints = new[]
            {
                $"{baseUrl}/api/v1/system/health",
                $"{baseUrl}/api/v1/files/browse/%2Ffolder000",
                $"{baseUrl}/api/v1/files/search?query=report",
                $"{baseUrl}/api/v1/files/statistics",
                $"{baseUrl}/api/v1/storage/overview",
                $"{baseUrl}/api/v1/file-shares",
                $"{baseUrl}/api/v1/users?page=1&pageSize=20",
                $"{baseUrl}/api/v1/public-shares/{shareCode}",
            };

            progress?.Report($"  并发={concurrency} 时长={durationSec}s 端点={endpoints.Length}");
            var result = await DriveLoadAsync(baseUrl, token, endpoints, concurrency, durationSec);
            return result with { BaseUrl = baseUrl, Concurrency = concurrency, DurationSec = durationSec };
        }
        finally
        {
            TryKill(process);
            TryDelete(workDb);
            TryDeleteDirectory(storagePath);
        }
    }

    private static async Task<ApiResult> DriveLoadAsync(
        string baseUrl, string token, string[] endpoints, int concurrency, double durationSec)
    {
        var samples = new System.Collections.Concurrent.ConcurrentBag<double>();
        var byEndpoint = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<double>>();
        var errors = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSec));
        var start = Stopwatch.GetTimestamp();

        var workers = new Task[concurrency];
        for (var w = 0; w < concurrency; w++)
        {
            var seed = w;
            workers[w] = Task.Run(async () =>
            {
                using var client = CreateClient(token);
                var i = seed;
                while (!cts.IsCancellationRequested)
                {
                    var endpoint = endpoints[i++ % endpoints.Length];
                    var key = EndpointKey(endpoint);
                    var sw = Stopwatch.GetTimestamp();
                    try
                    {
                        using var response = await client.GetAsync(endpoint, cts.Token);
                        response.EnsureSuccessStatusCode();
                        var elapsed = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
                        samples.Add(elapsed);
                        byEndpoint.GetOrAdd(key, _ => new()).Add(elapsed);
                    }
                    catch
                    {
                        Interlocked.Increment(ref errors);
                    }
                }
            });
        }

        await Task.WhenAll(workers);
        var totalSec = Stopwatch.GetElapsedTime(start).TotalSeconds;

        var dict = byEndpoint.ToDictionary(kvp => kvp.Key, kvp => Stats.From(kvp.Value.ToList()));
        return new ApiResult(
            baseUrl, concurrency, totalSec, samples.Count, errors,
            samples.Count / Math.Max(0.001, totalSec),
            Stats.From(samples.ToList()), dict);
    }

    /// <summary>从种子库取一个分享码，供公开分享端点基准使用。</summary>
    private static async Task<string?> FirstShareCodeAsync(string dbPath)
    {
        try
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ShareCode FROM FileShares ORDER BY ShareCode LIMIT 1";
            var result = await command.ExecuteScalarAsync();
            return result as string;
        }
        catch
        {
            return null;
        }
    }

    private static string EndpointKey(string url)
    {
        var path = url[(url.IndexOf("/api/", StringComparison.Ordinal))..];
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
            path = path[..queryIndex];
        // 归一化动态段，让同类请求聚合
        if (path.StartsWith("/api/v1/files/browse/", StringComparison.Ordinal))
            return "/api/v1/files/browse/*";
        if (path.StartsWith("/api/v1/users/", StringComparison.Ordinal))
            return "/api/v1/users/*";
        if (path.StartsWith("/api/v1/public-shares/", StringComparison.Ordinal))
        {
            // 分享码是动态段：/public-shares/{code} 与 /public-shares/{code}/files 分开统计
            var rest = path["/api/v1/public-shares/".Length..];
            var slash = rest.IndexOf('/');
            return slash < 0 ? "/api/v1/public-shares/*" : $"/api/v1/public-shares/*{rest[slash..]}";
        }
        return path;
    }

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAsync(string baseUrl)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var payload = JsonSerializer.Serialize(new { username = "admin", password = "admin123" });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync($"{baseUrl}{LoginPath}", content);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        // 令牌可能直接在根、data 或 value 里，逐层向下找第一个 token 属性
        foreach (var propertyName in new[] { "token", "data", "value", "result" })
        {
            if (document.RootElement.TryGetProperty(propertyName, out var node))
            {
                if (propertyName == "token")
                    return node.GetString()!;
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("token", out var nested))
                    return nested.GetString()!;
            }
        }

        throw new InvalidOperationException($"登录响应里找不到 token: {body[..Math.Min(200, body.Length)]}");
    }

    private static Process StartApiProcess(int port, string dbPath, string storagePath)
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

    private static async Task<bool> WaitForReadyAsync(string baseUrl, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(baseUrl + HealthPath);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // 进程尚未监听
            }
            await Task.Delay(300);
        }
        return false;
    }

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

    private static void TryDelete(string path)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                if (File.Exists(path + suffix))
                    File.Delete(path + suffix);
            }
            catch
            {
                // 文件被占用，留给下次运行清理
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 目录被占用
        }
    }
}
