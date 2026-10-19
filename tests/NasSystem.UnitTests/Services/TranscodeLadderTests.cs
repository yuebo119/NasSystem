// =============================================================================
// NasSystem.UnitTests - Services/TranscodeLadderTests.cs
// 码率阶梯、媒体探测解析与 HLS 参数构建（纯逻辑，无 IO）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using NasSystem.Application.Media;
using NasSystem.Infrastructure.Media;
using Xunit;

/// <summary>
/// 锁三件纯逻辑：阶梯选择（不上放大）、ffmpeg -i stderr 解析、
/// 多码率参数构建（split/var_stream_map/编码器分支/音频分支）。
/// 参数构建是真实 ffmpeg 命令的字符串源，集成测试只验产物结构，
/// 分支正确性（无音频、硬件编码、单档）由这里逐条锁
/// </summary>
public class TranscodeLadderTests
{
    // ---- 阶梯选择 ----

    [Theory]
    [InlineData(2160, new[] { 1080, 720, 480 })]   // 4K 源出全阶梯
    [InlineData(1080, new[] { 1080, 720, 480 })]   // 1080p 源出全阶梯
    [InlineData(900, new[] { 720, 480 })]          // 900p 源不出 1080 档（不上放大）
    [InlineData(480, new[] { 480 })]               // 恰好最低档只出一档
    public void Select_NeverUpscales(int sourceHeight, int[] expectedHeights)
    {
        var rungs = TranscodeLadder.Select(sourceHeight);
        rungs.Select(r => r.Height).Should().BeEquivalentTo(expectedHeights, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Select_SourceBelowLowestRung_AnchorsSourceHeight()
    {
        // 360p 源：单档且高度=源高度，放大低清片源无收益
        var rungs = TranscodeLadder.Select(360);
        rungs.Should().HaveCount(1);
        rungs[0].Height.Should().Be(360);
        rungs[0].MaxBitrateKbps.Should().Be(1000, "兜底档码率取最低档");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Select_UnknownHeight_ReturnsEmpty(int? sourceHeight)
    {
        // 探测失败不猜源分辨率：空列表让调用方走降级路径
        TranscodeLadder.Select(sourceHeight).Should().BeEmpty();
    }

    // ---- ffmpeg -i 解析 ----

    [Fact]
    public void Parse_ReadsResolutionAndAudioFromRealStderrShape()
    {
        // 形状对齐真实 ffmpeg -i 输出（static build 实测行）
        var stderr = """
            Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'sample.mp4':
              Duration: 00:00:03.00, start: 0.000000, bitrate: 234 kb/s
                Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1920x1080 [SAR 1:1 DAR 16:9], 164 kb/s, 30 fps, 30 tbr, 15360 tbn (default)
                Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, mono, fltp, 69 kb/s (default)
            """;

        var result = MediaProbeParser.Parse(stderr);
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.HasAudio.Should().BeTrue();
    }

    [Fact]
    public void Parse_VideoOnlyStream_HasAudioFalse()
    {
        var stderr = """
                Stream #0:0(und): Video: h264, yuv420p, 640x360, 100 kb/s, 15 fps
            """;

        var result = MediaProbeParser.Parse(stderr);
        result.Height.Should().Be(360);
        result.HasAudio.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ffmpeg version n7.0\n  libavcodec 61.0")]
    public void Parse_NoStreamInfo_ReturnsNulls(string stderr)
    {
        var result = MediaProbeParser.Parse(stderr);
        result.Height.Should().BeNull();
        result.Width.Should().BeNull();
        result.HasAudio.Should().BeFalse();
    }

    // ---- 参数构建 ----

    [Fact]
    public void BuildArguments_ThreeRungsWithAudio_SplitsOnceAndMapsAudioPerVariant()
    {
        var rungs = TranscodeLadder.Select(2160);

        var args = FfmpegTranscodeService.BuildTranscodeArguments(
            "ffmpeg", "/v.mp4", "/cache", rungs, hardwareEncoder: null, hasAudio: true);

        // 一次解码 split 三路（不是三次 -i）
        args.Should().Contain("[0:v]split=3[s0][s1][s2]");
        args.Should().Contain("scale=-2:1080").And.Contain("scale=-2:720").And.Contain("scale=-2:480");
        // 音频每档一份映射 + var_stream_map 的 a:N 与视频同序
        args.Should().Contain("-map 0:a -map 0:a -map 0:a");
        args.Should().Contain("-var_stream_map \"v:0,a:0 v:1,a:1 v:2,a:2\"");
        // 软件编码：CRF 锚质量 + 每档 maxrate 锁车道
        args.Should().Contain("-c:v libx264 -preset veryfast -crf 23");
        args.Should().Contain("-maxrate:0 5000k").And.Contain("-maxrate:1 2500k").And.Contain("-maxrate:2 1000k");
        // HLS 输出形态：%v 子目录 + 纯文件名的 master
        args.Should().Contain("-hls_segment_filename \"/cache/v%v/seg%03d.ts\"");
        args.Should().Contain("-master_pl_name \"master.m3u8\"");
        args.Should().EndWith("\"/cache/v%v/index.m3u8\"");
    }

    [Fact]
    public void BuildArguments_NoAudio_SkipsAudioMappingEntirely()
    {
        // 无音频源不能 -map 0:a（ffmpeg 直接失败），var_stream_map 只声明视频
        var rungs = TranscodeLadder.Select(720);

        var args = FfmpegTranscodeService.BuildTranscodeArguments(
            "ffmpeg", "/v.mp4", "/cache", rungs, hardwareEncoder: null, hasAudio: false);

        args.Should().NotContain("-map 0:a");
        args.Should().NotContain("-c:a aac");
        args.Should().Contain("-var_stream_map \"v:0 v:1\"", "无音频时 stream map 只声明视频");
    }

    [Fact]
    public void BuildArguments_HardwareEncoder_UsesBitrateLanesNotCrf()
    {
        var rungs = TranscodeLadder.Select(1080);

        var args = FfmpegTranscodeService.BuildTranscodeArguments(
            "ffmpeg", "/v.mp4", "/cache", rungs, hardwareEncoder: "h264_nvenc", hasAudio: true);

        args.Should().Contain("-c:v h264_nvenc -preset p2");
        args.Should().Contain("-b:v:0 5000k").And.Contain("-b:v:1 2500k").And.Contain("-b:v:2 1000k");
        args.Should().NotContain("libx264", "硬件编码不走软件编码器");
        args.Should().NotContain("-crf", "硬件编码不用 CRF，用车道码率");
    }
}
