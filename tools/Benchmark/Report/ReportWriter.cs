// =============================================================================
// NasSystem.Benchmark - ReportWriter.cs
// 基准报告落盘：Markdown 供人读，JSON 供机器比对
// =============================================================================

using NasSystem.Benchmark.Bench;

namespace NasSystem.Benchmark.Report;

/// <summary>
/// 把一次运行写成 Markdown 与 JSON。
/// </summary>
/// <remarks>
/// 每个数字都带口径：同一进程内交错、每轮交换起始臂、读用例共享一份库、
/// 写用例每次迭代换库副本。没有口径的数字不允许进报告。
/// </remarks>
public static class ReportWriter
{
    public static string Write(RunRecord record)
    {
        Directory.CreateDirectory(Paths.ReportDir);
        var stamp = record.CapturedAtUtc.ToString("yyyyMMdd-HHmmss");
        var mdPath = Path.Combine(Paths.ReportDir, $"{record.Arm}-{stamp}.md");
        var jsonPath = Path.Combine(Paths.ReportDir, $"{record.Arm}-{stamp}.json");

        File.WriteAllText(mdPath, BuildMarkdown(record));
        File.WriteAllText(jsonPath, record.ToJson());

        return mdPath;
    }

    private static string BuildMarkdown(RunRecord r)
    {
        var lines = new List<string>
        {
            $"# 性能基准报告：{r.Arm}",
            "",
            $"- 采集时间（UTC）：{r.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}",
            $"- 目标框架：{r.TargetFramework}",
            $"- ORM：{r.OrmDescription}",
            $"- Git SHA：{r.GitSha}",
            $"- 机器：{r.Machine.Describe()}",
            $"- 种子行数：{r.SeedRows}",
            $"- 数据层口径：预热 {r.Warmup} 次，测量 {r.Iterations} 次 × {r.Rounds} 轮，同进程交错 A/B，每轮交换起始臂",
            $"- API 口径：并发 {r.ApiConcurrency}，时长 {r.ApiDurationSec:F0}s",
            "",
            "## 数据访问层（P50，毫秒）",
            "",
            "| 用例 | ORM 臂 | 地板臂 | 比值 | ORM 分配 KB/次 | 地板分配 KB/次 |",
            "|---|---:|---:|---:|---:|---:|",
        };

        foreach (var c in r.DataCases)
        {
            lines.Add($"| {c.Name}{(c.IsWrite ? "（写）" : string.Empty)} | {c.Orm.P50:F3} | {c.Floor.P50:F3} | {c.Ratio:F2}× | {c.OrmAllocKbPerOp:F1} | {c.FloorAllocKbPerOp:F1} |");
        }

        lines.Add("");
        lines.Add("分位数明细（ORM 臂 / 地板臂，毫秒）：");
        lines.Add("");
        lines.Add("| 用例 | ORM P95 | 地板 P95 | ORM P99 | 地板 P99 |");
        lines.Add("|---|---:|---:|---:|---:|");
        foreach (var c in r.DataCases)
            lines.Add($"| {c.Name} | {c.Orm.P95:F3} | {c.Floor.P95:F3} | {c.Orm.P99:F3} | {c.Floor.P99:F3} |");

        if (r.Api is not null)
        {
            lines.Add("");
            lines.Add("## API 端到端");
            lines.Add("");
            lines.Add($"总请求 {r.Api.TotalRequests}，错误 {r.Api.Errors}，吞吐 {r.Api.Rps:F1} req/s");
            lines.Add("");
            lines.Add("整体延迟（毫秒）：" + r.Api.LatencyMs.Describe());
            lines.Add("");
            lines.Add("| 端点 | 请求数 | P50 | P95 | P99 |");
            lines.Add("|---|---:|---:|---:|---:|");
            foreach (var (key, stats) in r.Api.ByEndpoint.OrderBy(k => k.Key, StringComparer.Ordinal))
                lines.Add($"| `{key}` | {stats.Count} | {stats.P50:F2} | {stats.P95:F2} | {stats.P99:F2} |");
        }

        if (r.Process is not null)
        {
            lines.Add("");
            lines.Add("## 进程级");
            lines.Add("");
            lines.Add($"冷启动中位：{r.Process.ColdStartMsMedian:F0} ms（最快 {r.Process.ColdStartMsMin:F0} ms，{r.Process.ColdStartRuns} 次）");
            lines.Add($"稳态工作集：{r.Process.SteadyStateWorkingSetMb} MB");
            lines.Add($"托管堆：{r.Process.ManagedHeapMb} MB");
            lines.Add($"Release 发布体积：{r.Process.PublishSizeMb} MB（{r.Process.PublishFileCount} 个文件）");
        }

        lines.Add("");
        lines.Add("## 口径声明");
        lines.Add("");
        lines.Add("- 绝对值仅在本机本轮有效，跨机器只比较比值。");
        lines.Add("- 读用例两臂共享同一份库；写用例在可复用库上进行，每次迭代前做一次不计入测量的重置。");
        lines.Add("- 数据访问层不含 DI 与 HTTP 管线，端到端差异看 API 段。");
        lines.Add("- 地板臂为裸 ADO.NET 等价 SQL，不构造领域对象，是上限参照而非竞品。");
        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }
}
