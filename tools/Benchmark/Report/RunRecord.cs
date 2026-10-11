// =============================================================================
// NasSystem.Benchmark - RunRecord.cs
// 一次完整基准运行的记录：所有数字可由此结构复现
// =============================================================================

using NasSystem.Benchmark.Bench;

namespace NasSystem.Benchmark.Report;

/// <summary>一次基准运行的完整记录。</summary>
public sealed record RunRecord(
    string Arm,
    string TargetFramework,
    string OrmDescription,
    int SeedRows,
    int Warmup,
    int Iterations,
    int Rounds,
    int ApiConcurrency,
    double ApiDurationSec,
    MachineInfo Machine,
    string GitSha,
    DateTime CapturedAtUtc,
    List<CaseResult> DataCases,
    ApiResult? Api,
    ProcessResult? Process)
{
    /// <summary>序列化为 JSON，落盘到 docs/perf/ 供跨轮比对。</summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, JsonOptions);

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}
