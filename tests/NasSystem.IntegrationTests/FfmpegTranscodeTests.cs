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

    /// <summary>用 ffmpeg 造一个 3 秒测试视频（testsrc 图案，无外部素材依赖）</summary>
    private static async Task<string> CreateTestVideoAsync(string ffmpegPath, string workDir)
    {
        var output = Path.Combine(workDir, "sample.mp4");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -hide_banner -loglevel error -f lavfi -i \"testsrc=size=320x240:rate=15\" -t 3 -pix_fmt yuv420p \"{output}\"",
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

        var video = await CreateTestVideoAsync(ffmpeg, _workDir);
        var options = new TranscodeOptions { CacheDirectory = _cacheDir, FfmpegPath = ffmpeg };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance);

        service.IsAvailable.Should().BeTrue();

        var result = await service.GetOrCreateHlsAsync(video);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
        var playlist = result.Value;

        var playlistPath = Path.Combine(playlist.Directory, playlist.PlaylistFileName);
        File.Exists(playlistPath).Should().BeTrue("index.m3u8 应存在");

        var content = await File.ReadAllTextAsync(playlistPath);
        content.Should().Contain("#EXTM3U", "m3u8 头");
        content.Should().Contain(".ts", "分片引用");
        content.Should().Contain("#EXT-X-ENDLIST", "vod 播放列表以 ENDLIST 收尾");

        var segments = Directory.EnumerateFiles(playlist.Directory, "seg*.ts").ToList();
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
    public async Task Transcode_MissingFfmpeg_FailsLoudly()
    {
        var video = Path.Combine(_workDir, "nonexistent.mp4");
        var options = new TranscodeOptions
        {
            CacheDirectory = _cacheDir,
            FfmpegPath = Path.Combine(_workDir, "no-such-ffmpeg.exe"),
        };
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance);

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
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance);

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
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance);

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
        var service = new FfmpegTranscodeService(options, NullLogger<FfmpegTranscodeService>.Instance);

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
