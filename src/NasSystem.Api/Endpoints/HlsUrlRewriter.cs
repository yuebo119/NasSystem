// =============================================================================
// NasSystem.Api - Endpoints/HlsUrlRewriter.cs
// HLS 播放列表 URL 重写（多码率阶梯的 HTTP 寻址方案）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 把 ffmpeg 产出的磁盘相对 URI 重写为本站流路由可寻址的 URI。
/// </summary>
/// <remarks>
/// 背景：master 与分片由同一个 catch-all 路由 <c>/media/stream/{*path}</c>
/// 服务，磁盘上是 v{N}/ 子目录布局。若直接把相对 URI 吐给播放器，
/// hls.js 会按播放列表 URL 解析出 /media/stream/v0/... 这类错误路径
/// （视频路径前缀被吃掉）。方案：variant 与 segment 走 query 参数，
/// 路径段始终是视频自身路径：
///   master（无参数）→ v0/index.m3u8 重写为 {视频路径}?variant=0
///   variant N        → seg000.ts 重写为 {视频文件名}?variant=N&amp;segment=seg000.ts
/// 段名仍在校验白名单内，query 只是寻址方式，不放开任意文件访问
/// </remarks>
internal static partial class HlsUrlRewriter
{
    /// <summary>variant 子列表行：v0/index.m3u8（%v 替换后的磁盘形态）</summary>
    [GeneratedRegex(@"^v(\d+)/index\.m3u8$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VariantLineRegex();

    /// <summary>分片行：seg000.ts</summary>
    [GeneratedRegex(@"^seg(\d{3})\.ts$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SegmentLineRegex();

    /// <summary>逐段转义保留路径分隔符（中文/空格文件名进 query 前必须转义）</summary>
    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>
    /// 重写 master 播放列表：variant 行替换为带 variant query 的视频路径 URI。
    /// 非 variant 行（#EXT 头、注释、空行）原样保留
    /// </summary>
    public static string RewriteMaster(string masterContent, string videoPath)
    {
        var escaped = EscapePath(videoPath);
        var sb = new StringBuilder(masterContent.Length + 64);
        foreach (var line in masterContent.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var match = VariantLineRegex().Match(trimmed);
            sb.AppendLine(match.Success
                ? $"{escaped}?variant={match.Groups[1].Value}"
                : trimmed);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 重写 variant 子播放列表：分片行替换为带 variant 与 segment query 的
    /// 视频文件名 URI。videoBaseName 是视频路径的末段（如 a.mp4）——
    /// 相对 URI 以子列表 URL（.../{视频路径}?variant=N）为基准解析，
    /// 末段即可拼回完整视频路径
    /// </summary>
    public static string RewriteVariant(string variantContent, string videoBaseName, int variant)
    {
        var escaped = Uri.EscapeDataString(videoBaseName);
        var sb = new StringBuilder(variantContent.Length + 64);
        foreach (var line in variantContent.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var match = SegmentLineRegex().Match(trimmed);
            sb.AppendLine(match.Success
                ? $"{escaped}?variant={variant}&segment={match.Value}"
                : trimmed);
        }
        return sb.ToString();
    }
}
