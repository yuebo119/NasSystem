// =============================================================================
// NasSystem.Benchmark - Paths.cs
// 路径解析：定位仓库根与基准产物目录
// =============================================================================

namespace NasSystem.Benchmark;

/// <summary>
/// 基准产物路径。仓库根从程序集目录向上找 NasSystem.sln 定位，
/// 避免依赖进程工作目录（proc/api 基准会以不同 CWD 启动子进程）。
/// </summary>
public static class Paths
{
    public static string RepoRoot { get; } = LocateRepoRoot();

    /// <summary>种子库与运行记录落盘位置，随代码提交以保证可复现。</summary>
    public static string DataDir => Path.Combine(RepoRoot, "tools", "Benchmark", "data");

    public static string ReportDir => Path.Combine(RepoRoot, "docs", "perf");

    public static string ApiProjectDir => Path.Combine(RepoRoot, "src", "NasSystem.Api");

    /// <summary>
    /// 已构建的 API 程序集路径。不硬编码 TFM：net10 与 net11 阶段共用同一份 harness。
    /// </summary>
    public static string ApiDllPath
    {
        get
        {
            var binDir = Path.Combine(ApiProjectDir, "bin");
            if (!Directory.Exists(binDir))
                throw new InvalidOperationException($"未找到 API 构建产物目录 {binDir}，请先构建");

            var candidates = Directory.GetFiles(binDir, "NasSystem.Api.dll", SearchOption.AllDirectories)
                // 优先 Debug：基准默认构建形态，避免占用 Release 目录导致 dotnet publish 无法覆盖
                .OrderByDescending(p => p.Contains("Debug", StringComparison.Ordinal))
                .ThenByDescending(p => p.Contains($"net{Environment.Version.Major}.0", StringComparison.Ordinal))
                .ToList();

            if (candidates.Count == 0)
                throw new InvalidOperationException($"未在 {binDir} 下找到 NasSystem.Api.dll，请先构建 API");

            return candidates[0];
        }
    }

    public static string SeedDb(int rows) => Path.Combine(DataDir, $"bench-{rows}.db");

    /// <summary>每次运行的库副本，避免基准写入污染种子库。</summary>
    public static string WorkDb(string tag) => Path.Combine(DataDir, $"work-{tag}.db");

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NasSystem.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("未找到 NasSystem.sln，无法定位仓库根");
    }
}
