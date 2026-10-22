// =============================================================================
// NasSystem.IntegrationTests - FfmpegTranscodeTests.cs
// FFmpeg HLS 转码端到端测试（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NasSystem.Application.Media;
using NasSystem.Infrastructure.Media;
using Xunit;

/// <summary>
/// 真实 FFmpeg 的 HLS 转码端到端：产物结构（m3u8 + ts）、缓存命中
/// （第二次不重转）、诚实降级（ffmpeg 缺失 → 明确失败码）。
/// </summary>
/// <remarks>
/// ffmpeg 定位顺序：PATH → 仓库 tmp/probe/ffmpeg/ffmpeg.exe（探针期下载
/// 的 static build）。都找不到时跳过并说明——转码可用性是部署环境
/// 事实（生产镜像由 Dockerfile 装），开发机不强求。
/// </remarks>
public class FfmpegTranscodeTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _workDir;

    public FfmpegTranscodeTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"TranscodeTest_{Guid.NewGuid():N}");
        _cacheDir = Path.Combine(_workDir, "cache");
        Directory.CreateDirectory(_workDir);
    }

    private static string? ResolveFfmpeg()
    {
        // 1. PATH
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (var dir in pathVariable.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (IOException) { }
            }
        }
        // 2. 仓库探针位（相对测试运行目录回溯）
        var probePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tmp", "probe", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(probePath)) return Path.GetFullPath(probePath);
        return null;
    }

    /// <summary>用 ffmpeg 造一个测试视频（testsrc 图案，无外部素材依赖）。
    /// size 控制源分辨率（验证阶梯不上放大），withAudio 加正弦音轨（验证音频映射）</summary>
    private static async Task<string> CreateTestVideoAsync(
        string ffmpegPath, string workDir, string size = "320x240", bool withAudio = false)
    {
        var output = Path.Combine(workDir, $"sample-{size}-{withAudio}.mp4");
        var inputs = $"-f lavfi -i \"testsrc=size={size}:rate=15\"";
        var audio = withAudio ? $"-f lavfi -i \"sine=frequency=440\" -t 3" : "-t 3";
        var codec = withAudio ? "-c:a aac" : "-an";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -hide_banner -loglevel error {inputs} {audio} -pix_fmt yuv420p {codec} \"{output}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, "测试视频生成应成功");
        return output;
    }

    [Fact]
    public async Task Transcode_ProducesHlsPlaylistAndSegments_AndCaches()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用（PATH 与探针位均无），跳过真实转码测试");

        // 1080p 带音频源：验证三档阶梯（1080/720/480）与音频映射
        var video = await CreateTestVideoAsync(ffmpeg, _workDir, "1920x1080", withAudio: true);
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        service.IsAvailable.Should().BeTrue();

        var result = await service.GetOrCreateHlsAsync(video);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
        var playlist = result.Value;

        // master 播放列表在缓存根，含三档变体
        var playlistPath = Path.Combine(playlist.Directory, playlist.PlaylistFileName);
        playlist.PlaylistFileName.Should().Be("master.m3u8");
        File.Exists(playlistPath).Should().BeTrue("master.m3u8 应存在");

        var content = await File.ReadAllTextAsync(playlistPath);
        content.Should().Contain("#EXTM3U", "m3u8 头");
        content.Should().Contain("v0/index.m3u8", "master 引用 v0 子列表");
        content.Should().Contain("v1/index.m3u8", "1080p 源应出 720p 档");
        content.Should().Contain("v2/index.m3u8", "1080p 源应出 480p 档");

        // 每档子列表 + 分片，分片行是磁盘相对 URI（seg%03d.ts）
        for (var v = 0; v < 3; v++)
        {
            var variantPlaylist = Path.Combine(playlist.Directory, $"v{v}", "index.m3u8");
            File.Exists(variantPlaylist).Should().BeTrue($"v{v}/index.m3u8 应存在");
            var variantContent = await File.ReadAllTextAsync(variantPlaylist);
            variantContent.Should().Contain(".ts", "子列表引用分片");
            variantContent.Should().Contain("#EXT-X-ENDLIST", "vod 播放列表以 ENDLIST 收尾");
        }

        var segments = Directory.EnumerateFiles(playlist.Directory, "seg*.ts", SearchOption.AllDirectories).ToList();
        segments.Should().NotBeEmpty("至少一个 ts 分片");
        segments.All(s => new FileInfo(s).Length > 0).Should().BeTrue("分片非空");
        playlist.TotalSizeBytes.Should().BeGreaterThan(0);

        // 缓存命中：记下 mtime，第二次调用后内容与时间戳不变（不重转）
        var playlistBefore = File.GetLastWriteTimeUtc(playlistPath);
        await Task.Delay(50);
        var second = await service.GetOrCreateHlsAsync(video);
        second.IsSuccess.Should().BeTrue();
        File.GetLastWriteTimeUtc(playlistPath).Should().Be(playlistBefore, "缓存命中不重转");
    }

    [Fact]
    public async Task Transcode_LowResolutionSource_DoesNotUpscale()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过");
        // 360p 源（低于最低档 480p）：单档且高度=源高度，不上放大
        var video = await CreateTestVideoAsync(ffmpeg, _workDir, "640x360", withAudio: false);
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        var result = await service.GetOrCreateHlsAsync(video);
        result.IsSuccess.Should().BeTrue(result.Error.Message);

        var master = await File.ReadAllTextAsync(Path.Combine(result.Value.Directory, "master.m3u8"));
        master.Should().Contain("v0/index.m3u8", "单档仍有 v0");
        master.Should().NotContain("v1/index.m3u8", "低于最低档不出第二档");
        master.Should().Contain("RESOLUTION=640x360", "单档锚定源高度不上放大");
    }

    [Fact]
    public async Task GetStatus_ReportsEncoderAndLadder()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过");
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        var result = await service.GetStatusAsync();
        result.IsSuccess.Should().BeTrue();
        var status = result.Value;
        status.FfmpegAvailable.Should().BeTrue();
        status.Encoder.Should().NotBeNullOrEmpty("编码器名必须如实报告（硬件或 libx264）");
        status.Rungs.Should().HaveCount(3, "标准阶梯三档");
        status.Rungs.Select(r => r.Height).Should().BeEquivalentTo(new[] { 1080, 720, 480 });
    }

    [Fact]
    public async Task Transcode_MissingFfmpeg_FailsLoudly()
    {
        var video = Path.Combine(_workDir, "nonexistent.mp4");
        var options = new TranscodeOptions
        {
            CacheDirectory = _cacheDir,
            FfmpegPath = Path.Combine(_workDir, "no-such-ffmpeg.exe"),
        };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        // 注意：PATH 里可能有 ffmpeg（本机探针位不算 PATH），这里用显式
        // 坏路径覆盖。若 PATH 有 ffmpeg，IsAvailable 仍可能为 true——
        // 那这条跳过（该场景由服务层 IsAvailable=false 单测覆盖）
        if (service.IsAvailable)
            Assert.Skip("PATH 中存在 ffmpeg，无法构造缺失场景");

        var result = await service.GetOrCreateHlsAsync(video);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transcode.FfmpegMissing", "缺失是明确失败而非假装成功");
    }

    [Fact]
    public async Task Transcode_MissingSourceFile_FailsLoudly()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过");
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        var result = await service.GetOrCreateHlsAsync(Path.Combine(_workDir, "ghost.mp4"));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Transcode.SourceMissing");
    }

    [Fact]
    public async Task ExtractFrame_ProducesDecodableImage()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过");
        var video = await CreateTestVideoAsync(ffmpeg, _workDir);
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        var frame = await service.ExtractVideoFrameAsync(video);
        frame.Should().NotBeNull("抽帧应成功");
        frame!.Length.Should().BeGreaterThan(0);

        // PNG magic：抽帧输出是 image2 的 png
        frame[0].Should().Be(0x89);
        frame[1].Should().Be(0x50);
    }

    [Fact]
    public async Task ClearCache_RemovesTranscodedDirectories()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过");
        var video = await CreateTestVideoAsync(ffmpeg, _workDir);
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance,
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

        await service.GetOrCreateHlsAsync(video);
        var freed = await service.ClearCacheAsync();
        freed.Should().BeGreaterThan(0, "清理应报告释放的字节");
        Directory.EnumerateDirectories(_cacheDir).Should().BeEmpty("缓存目录应清空");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir))
                Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
            // 播放中的分片占用：留给系统临时目录
        }
    }
}
