// =============================================================================
// NasSystem.IntegrationTests - HlsUrlRewriterTests.cs
// 播放列表 URL 重写（多码率阶梯寻址方案，纯函数）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using NasSystem.Api.Extensions;
using Xunit;

/// <summary>
/// 锁重写契约：磁盘的 v{N}/ 布局必须被翻译成 {视频路径}?variant=N 寻址，
/// 否则 hls.js 按播放列表 URL 解析会产出 /media/stream/v0/... 错误路径
/// （视频路径前缀被吃掉）。中文/空格文件名走 query 前必须转义
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

        var result = HlsUrlRewriter.RewriteMaster(master, "movies/a.mp4");

        result.Should().Contain("movies/a.mp4?variant=0");
        result.Should().Contain("movies/a.mp4?variant=1");
        // EXT 头与结构行原样保留
        result.Should().Contain("#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080");
        // 磁盘形态不残留
        result.Should().NotContain("v0/index.m3u8");
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
    public void Rewrite_EscapesNonAsciiAndSpacesInPath()
    {
        // 中文/空格文件名：query 前逐段转义，但路径分隔符保留
        var master = "v0/index.m3u8";
        var result = HlsUrlRewriter.RewriteMaster(master, "电影/我的 视频.mp4");

        result.Trim().Should().Be("%E7%94%B5%E5%BD%B1/%E6%88%91%E7%9A%84%20%E8%A7%86%E9%A2%91.mp4?variant=0");
    }

    [Fact]
    public void RewriteVariant_EscapesBaseName()
    {
        var result = HlsUrlRewriter.RewriteVariant("seg000.ts", "我的视频.mp4", 0);
        result.Trim().Should().Be("%E6%88%91%E7%9A%84%E8%A7%86%E9%A2%91.mp4?variant=0&segment=seg000.ts");
    }
}
