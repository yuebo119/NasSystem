// =============================================================================
// NasSystem.Infrastructure - Media/FfmpegTranscodeService.cs
// FFmpeg HLS 转码实现（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Media;
using NasSystem.Shared;

/// <summary>
/// FFmpeg 子进程 HLS 转码。参数取舍：
/// 一、libx264 + CRF 23：通用兼容与体积的平衡点；preset veryfast 是
///    NAS 侧 CPU 编码的甜点（再慢的 preset 收益递减、CPU 占用翻倍）。
/// 二、-vf scale=-2:1080：高度锁 1080p、宽度自动取偶数（x264 要求
///    偶数维度，-2 是"按比例缩到最接近的偶数"）。原视频低于 1080p 时
///    scale 是放大——加 min 判断？不放大：个人 NAS 片源以 1080p/4K
///    为主，低分辨率片源放大无收益且费 CPU。
/// 三、HLS 分片 6 秒：播放器起播缓冲与 seek 粒度的平衡点。
/// 四、缓存目录内一次只转一个视频（进程内闸），并发转码会互抢 CPU
///    且 NAS 上通常只有一个用户在播。
/// </summary>
public sealed class FfmpegTranscodeService : ITranscodeService
{
    /// <summary>进程内并发闸：同一时刻只允许一个转码（CPU 密集，互抢无收益）</summary>
    private static readonly SemaphoreSlim TranscodeGate = new(1, 1);

    private readonly TranscodeOptions _options;
    private readonly ILogger<FfmpegTranscodeService> _logger;

    public FfmpegTranscodeService(
        TranscodeOptions options,
        ILogger<FfmpegTranscodeService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => ResolveFfmpegPath() is not null;

    /// <summary>解析 ffmpeg 可执行：配置优先，其次 PATH</summary>
    private string? ResolveFfmpegPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.FfmpegPath) && File.Exists(_options.FfmpegPath))
            return _options.FfmpegPath;
        // PATH 查找：Windows 开发机装没装都能被检出（没装即 null，诚实降级）
        var names = OperatingSystem.IsWindows()
            ? new[] { "ffmpeg.exe" }
            : new[] { "ffmpeg" };
        foreach (var name in names)
        {
            var pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable)) continue;
            foreach (var dir in pathVariable.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (IOException)
                {
                    // PATH 条目含非法字符时跳过该条
                }
            }
        }
        return null;
    }

    public async Task<Result<HlsPlaylist>> GetOrCreateHlsAsync(
        string physicalPath, CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveFfmpegPath();
        if (ffmpeg is null)
            return Result<HlsPlaylist>.Failure(
                new Error("Transcode.FfmpegMissing", "转码不可用：ffmpeg 未安装"));

        if (!File.Exists(physicalPath))
            return Result<HlsPlaylist>.Failure(
                new Error("Transcode.SourceMissing", "视频文件不存在"));

        var cacheKey = HashPath(physicalPath);
        var outputDir = Path.Combine(_options.CacheDirectory, cacheKey);
        var playlistPath = Path.Combine(outputDir, "index.m3u8");
        if (File.Exists(playlistPath))
            return Result<HlsPlaylist>.Success(
                new HlsPlaylist(outputDir, "index.m3u8", DirectorySize(outputDir)));

        await TranscodeGate.WaitAsync(cancellationToken);
        try
        {
            // 双检：等闸期间另一个请求可能已转完
            if (File.Exists(playlistPath))
                return Result<HlsPlaylist>.Success(
                    new HlsPlaylist(outputDir, "index.m3u8", DirectorySize(outputDir)));

            Directory.CreateDirectory(outputDir);
            var segmentPattern = Path.Combine(outputDir, "seg%03d.ts");

            // -y 覆盖、-hide_banner 静默、-loglevel error 只留错误行。
            // -movflags +faststart 对 fmp4 无效，HLS 不需要；ts 分片模式
            // 不需要 playlist type 参数（默认 event 语义的点播场景用 vod 更准，
            // 显式写 vod 让播放器知道可任意 seek）
            var arguments =
                $"-y -hide_banner -loglevel error -i \"{physicalPath}\" " +
                $"-c:v libx264 -preset veryfast -crf 23 -vf scale=-2:1080 " +
                $"-c:a aac -b:a 128k " +
                $"-f hls -hls_time 6 -hls_playlist_type vod -hls_segment_filename \"{segmentPattern}\" " +
                $"\"{playlistPath}\"";

            _logger.LogInformation("HLS 转码开始: {Path} → {Cache}", physicalPath, cacheKey);
            var sw = Stopwatch.StartNew();
            var (exitCode, stderr) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);
            if (exitCode != 0 || !File.Exists(playlistPath))
            {
                // 失败清半成品：下次重试从干净状态开始
                TryDeleteDirectory(outputDir);
                _logger.LogError("HLS 转码失败 (exit={Exit}): {Stderr}", exitCode, Truncate(stderr));
                return Result<HlsPlaylist>.Failure(
                    new Error("Transcode.Failed", "视频转码失败"));
            }

            sw.Stop();
            _logger.LogInformation(
                "HLS 转码完成: {Cache} ({Ms}ms, {Bytes} 字节)", cacheKey, sw.ElapsedMilliseconds, DirectorySize(outputDir));
            return Result<HlsPlaylist>.Success(
                new HlsPlaylist(outputDir, "index.m3u8", DirectorySize(outputDir)));
        }
        finally
        {
            TranscodeGate.Release();
        }
    }

    public async Task<byte[]?> ExtractVideoFrameAsync(
        string physicalPath, CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveFfmpegPath();
        if (ffmpeg is null || !File.Exists(physicalPath))
            return null;

        // 帧落临时文件再读字节：ffmpeg 输出到 stdout 的图片流对 image2
        // 管线支持不稳（容器里尤为明显），临时文件是可靠形态。用完即删
        var tempFrame = Path.Combine(Path.GetTempPath(), $"nas-frame-{Guid.NewGuid():N}.png");
        try
        {
            // -ss 1 跳过片头（黑帧/logo 高发区）；-frames:v 1 单帧；
            // 失败（视频短于 1 秒/无视频流）时退到 -ss 0 再试一次
            var arguments =
                $"-y -hide_banner -loglevel error -ss 1 -i \"{physicalPath}\" " +
                $"-frames:v 1 -f image2 \"{tempFrame}\"";
            var (exitCode, _) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);
            if (exitCode != 0 || !File.Exists(tempFrame))
            {
                arguments =
                    $"-y -hide_banner -loglevel error -i \"{physicalPath}\" " +
                    $"-frames:v 1 -f image2 \"{tempFrame}\"";
                (exitCode, _) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);
                if (exitCode != 0 || !File.Exists(tempFrame))
                    return null;
            }
            return await File.ReadAllBytesAsync(tempFrame, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "视频抽帧失败: {Path}", physicalPath);
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFrame)) File.Delete(tempFrame);
            }
            catch (IOException)
            {
                // 临时帧清理失败不阻断（系统临时目录兜底）
            }
        }
    }

    public Task<long> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.CacheDirectory))
            return Task.FromResult(0L);

        var freed = 0L;
        foreach (var dir in Directory.EnumerateDirectories(_options.CacheDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            freed += DirectorySize(dir);
            TryDeleteDirectory(dir);
        }
        return Task.FromResult(freed);
    }

    private static async Task<(int ExitCode, string Stderr)> RunProcessAsync(
        string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动 {fileName}");
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stderr);
    }

    /// <summary>缓存键：物理路径的 SHA256 前 16 字节（32 hex）。不含 mtime：
    /// 同路径覆盖上传产新内容但路径不变，缩略图缓存同款已知口径</summary>
    private static string HashPath(string physicalPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(physicalPath)))[..32].ToLowerInvariant();

    private static long DirectorySize(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        return Directory.EnumerateFiles(directory).Sum(f => new FileInfo(f).Length);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 播放中的分片被占用：留着，清理入口或下次转码兜底
        }
    }

    private static string Truncate(string text, int max = 500) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>转码运行期配置（appsettings 绑定）</summary>
public sealed class TranscodeOptions
{
    /// <summary>转码缓存目录（相对数据目录或绝对路径）</summary>
    public string CacheDirectory { get; set; } = "temp/transcodes";

    /// <summary>ffmpeg 可执行路径（空则查 PATH）</summary>
    public string? FfmpegPath { get; set; }
}
