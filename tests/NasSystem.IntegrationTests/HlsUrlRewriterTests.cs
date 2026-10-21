// =============================================================================
// NasSystem.IntegrationTests - HlsUrlRewriterTests.cs
// 播放列表 URL 重写（多码率阶梯寻址方案，纯函数）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using NasSystem.Api.Extensions;
using Xunit;

/// <summary>
/// 锁重写契约：磁盘的 v{N}/ 布局必须被翻译成 {视频文件名}?variant=N 寻址，
/// 否则 hls.js 按播放列表 URL 解析会产出 /media/stream/v0/... 错误路径
/// （视频路径前缀被吃掉）。中文/空格文件名走 query 前必须转义。
/// 2026-10-21 审计修正：master 与 variant 统一只传末段文件名——传完整
/// 路径会被 RFC 3986 相对解析把父目录段拼重复（子目录视频 404），
/// 下面的解析用例直接锁住这个契约
/// </summary>
public class HlsUrlRewriterTests
{
    [Fact]
    public void RewriteMaster_TranslatesVariantLinesKeepsHeaders()
    {
        var master = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
            v0/index.m3u8

            #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720
            v1/index.m3u8
            """;

        // 调用方传末段文件名（与 RewriteVariant 同口径）
        var result = HlsUrlRewriter.RewriteMaster(master, "a.mp4");

        result.Should().Contain("a.mp4?variant=0");
        result.Should().Contain("a.mp4?variant=1");
        // EXT 头与结构行原样保留
        result.Should().Contain("#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080");
        // 磁盘形态不残留
        result.Should().NotContain("v0/index.m3u8");
    }

    [Fact]
    public void RewriteMaster_VariantUriResolvesToSameVideoPath()
    {
        // 子目录视频的真实契约：master 播放列表服务在
        // /api/v1/media/stream/movies/a.mp4，重写出的相对 URI 必须
        // 解析回同一个 URL。修复前传完整路径 movies/a.mp4，RFC 3986
        // 合并后变成 /api/v1/media/stream/movies/movies/a.mp4（404）
        var master = "v0/index.m3u8";
        var rewritten = HlsUrlRewriter.RewriteMaster(master, "a.mp4").Trim();

        var masterUrl = new Uri("http://nas.local/api/v1/media/stream/movies/a.mp4");
        var resolved = new Uri(masterUrl, rewritten);

        resolved.AbsolutePath.Should().Be("/api/v1/media/stream/movies/a.mp4",
            "variant URI 必须解析回视频自身路径，父目录段不许拼重复");
        resolved.Query.Should().Be("?variant=0");
    }

    [Fact]
    public void RewriteVariant_TranslatesSegmentLinesWithBaseName()
    {
        var variant = """
            #EXTM3U
            #EXT-X-TARGETDURATION:6
            #EXTINF:6.0,
            seg000.ts
            #EXTINF:6.0,
            seg001.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsUrlRewriter.RewriteVariant(variant, "a.mp4", 1);

        result.Should().Contain("a.mp4?variant=1&segment=seg000.ts");
        result.Should().Contain("a.mp4?variant=1&segment=seg001.ts");
        result.Should().Contain("#EXT-X-ENDLIST");
        result.Should().NotContain("\nseg000.ts");
    }

    [Fact]
    public void Rewrite_EscapesNonAsciiAndSpacesInBaseName()
    {
        // 中文/空格文件名：query 前逐段转义，但路径分隔符保留。
        // 调用方传末段，父目录段不进 URI（相对解析由基准 URL 提供）
        var master = "v0/index.m3u8";
        var result = HlsUrlRewriter.RewriteMaster(master, "我的 视频.mp4");

        result.Trim().Should().Be("%E6%88%91%E7%9A%84%20%E8%A7%86%E9%A2%91.mp4?variant=0");
    }

    [Fact]
    public void RewriteVariant_EscapesBaseName()
    {
        var result = HlsUrlRewriter.RewriteVariant("seg000.ts", "我的视频.mp4", 0);
        result.Trim().Should().Be("%E6%88%91%E7%9A%84%E8%A7%86%E9%A2%91.mp4?variant=0&segment=seg000.ts");
    }
}
