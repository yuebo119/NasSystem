// =============================================================================
// NasSystem.Benchmark - Stats.cs
// 延迟样本统计：分位数取排序后最近秩，不做插值
// =============================================================================

namespace NasSystem.Benchmark;

/// <summary>
/// 一组延迟样本的描述统计。分位数用最近秩法（ceil(p*n)），
/// 与基准报告的 P50/P95/P99 口径一致，不引入插值造成的虚假精度。
/// </summary>
public sealed record Stats(double Min, double P50, double P95, double P99, double Mean, double Max, int Count)
{
    public static Stats From(List<double> samples)
    {
        if (samples.Count == 0)
            return new Stats(0, 0, 0, 0, 0, 0, 0);

        var sorted = new List<double>(samples);
        sorted.Sort();
        return new Stats(
            sorted[0],
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            samples.Average(),
            sorted[^1],
            samples.Count);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        var rank = (int)Math.Ceiling(p * sorted.Count);
        if (rank < 1) rank = 1;
        if (rank > sorted.Count) rank = sorted.Count;
        return sorted[rank - 1];
    }

    public string Describe() =>
        $"n={Count} mean={Mean:F2} P50={P50:F2} P95={P95:F2} P99={P99:F2} min={Min:F2} max={Max:F2}";
}
