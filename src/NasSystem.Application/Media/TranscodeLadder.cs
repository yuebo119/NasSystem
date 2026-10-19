// =============================================================================
// NasSystem.Application - Media/TranscodeLadder.cs
// 自适应码率阶梯策略（ROADMAP Phase 3 多码率升级）
// =============================================================================

namespace NasSystem.Application.Media;

/// <summary>码率阶梯的一档</summary>
/// <param name="Height">该档高度（宽度按比例取偶数）</param>
/// <param name="MaxBitrateKbps">该档视频码率上限（ABR 车道的带宽承诺）</param>
public sealed record TranscodeRung(int Height, int MaxBitrateKbps)
{
    public string Name => $"{Height}p";
}

/// <summary>
/// 码率阶梯选择：从标准阶梯里取高度不超过源的档位，不上放大。
/// 纯函数，无 IO——单元测试直接覆盖各源分辨率分支。
/// </summary>
/// <remarks>
/// 档位取“高度不超过源”而非“最接近源”：720p 片源出 720p+480p 两档，
/// 起播与弱网各有落脚点。源低于最低档（480p）时出单档且高度=源高度，
/// 放大低清片源只会浪费 CPU 且不增益画质（ROADMAP Phase 3 同款取舍）。
/// </remarks>
public static class TranscodeLadder
{
    /// <summary>标准阶梯（高度降序）。码率是 ABR 车道承诺，质量仍由 CRF 锚定</summary>
    public static IReadOnlyList<TranscodeRung> Standard { get; } =
    [
        new TranscodeRung(1080, 5000),
        new TranscodeRung(720, 2500),
        new TranscodeRung(480, 1000)
    ];

    /// <summary>源低于最低档时的兜底档高度（= 源高度，由调用方填入）</summary>
    public static int LowestRungHeight => Standard[^1].Height;

    /// <summary>
    /// 按源高度选档。<paramref name="sourceHeight"/> 为 null（探测失败）时
    /// 返回空列表——调用方按“探测失败”降级为单档，不猜测源分辨率
    /// </summary>
    public static IReadOnlyList<TranscodeRung> Select(int? sourceHeight)
    {
        if (sourceHeight is null || sourceHeight <= 0)
            return [];

        var rungs = Standard.Where(r => r.Height <= sourceHeight.Value).ToList();
        if (rungs.Count == 0)
        {
            // 源低于最低档：单档锚定源高度，码率取最低档（弱网民照顾）
            rungs.Add(new TranscodeRung(sourceHeight.Value, Standard[^1].MaxBitrateKbps));
        }
        return rungs;
    }

    /// <summary>阶梯签名：进缓存键。源高度或档位变化即换键重转（不混用旧产物）</summary>
    public static string Signature(int? sourceHeight, IReadOnlyList<TranscodeRung> rungs) =>
        $"h{sourceHeight?.ToString() ?? "?"}:" + string.Join(",", rungs.Select(r => $"{r.Height}@{r.MaxBitrateKbps}"));
}

/// <summary>ffmpeg -i 输出的媒体信息探测结果</summary>
public sealed record MediaProbeResult(int? Width, int? Height, bool HasAudio);

/// <summary>
/// 解析 <c>ffmpeg -i</c> 的 stderr（不依赖 ffprobe：apt 装的 ffmpeg 包
/// 与 static build 都带 ffmpeg 本体，少一个二进制依赖）。纯解析函数，
/// IO 在 Infrastructure 侧
/// </summary>
public static class MediaProbeParser
{
    /// <summary>
    /// 从 stderr 文本取首个视频流的宽高与是否存在音频流。
    /// 取不到返回全 null/false（调用方按“探测失败”降级，不猜）
    /// </summary>
    public static MediaProbeResult Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return new MediaProbeResult(null, null, false);

        int? width = null;
        int? height = null;
        foreach (var line in stderr.Split('\n'))
        {
            // 形如：Stream #0:0(und): Video: h264 (High), yuv420p, 1920x1080 [SAR...]
            var videoIdx = line.IndexOf(": Video:", StringComparison.Ordinal);
            if (videoIdx >= 0)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line, @"(\d{2,5})x(\d{2,5})", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                if (match.Success)
                {
                    width = int.Parse(match.Groups[1].Value);
                    height = int.Parse(match.Groups[2].Value);
                }
            }
        }

        var hasAudio = stderr.Contains(": Audio:", StringComparison.Ordinal);
        return new MediaProbeResult(width, height, hasAudio);
    }
}
