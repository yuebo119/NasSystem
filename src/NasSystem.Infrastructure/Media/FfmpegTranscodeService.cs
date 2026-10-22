// =============================================================================
// NasSystem.Infrastructure - Media/FfmpegTranscodeService.cs
// FFmpeg HLS 自适应转码实现（ROADMAP Phase 3，多码率阶梯 + 硬件编码）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Media;
using NasSystem.Shared;

/// <summary>
/// FFmpeg 子进程 HLS 自适应转码。2026-10-19 从单档 1080p 升级为码率阶梯：
/// 一次调用完成解码一次、split 多路、每路独立编码，输出 master playlist
/// 加每档子列表（hls.js 自动 ABR 切换）。
/// </summary>
/// <remarks>
/// 参数取舍：
/// 一、阶梯档位高度不超过源（ffmpeg -i 探测），不上放大；源低于 480p
///    出单档且高度=源高度。质量仍由 CRF 23 锚定，每档加 maxrate/bufsize
///    把码率锁进 ABR 车道（播放器切档依据带宽承诺，不是实际峰值）。
/// 二、硬件编码探测（h264_nvenc/qsv/amf/vaapi/videotoolbox）用真实小编码
///    测试而非 -encoders 列表：编译进 FFmpeg 不等于运行时可达（实测某台
///    开发机 qsv 列表可见但 MFX session 创建失败）。探测结果 TTL 缓存，
///    探测到硬件用 -b:v 车道码率，未命中回退 libx264 + CRF。
/// 三、硬件编码启动失败（如容器忘透传设备）自动降级软件重试一次：
///    探测通过但运行时不可用是部署事实，重试失败才如实报错。
/// 四、缓存键 = 路径 + 阶梯签名 + 编码器：换档/换编码器自然产生新缓存，
///    不与旧产物混用。探测结果进程内 TTL 缓存，分片请求不重复探测。
/// 五、HLS 分片 6 秒：播放器起播缓冲与 seek 粒度的平衡点。
/// 六、缓存目录内一次只转一个视频（进程内闸），并发转码互抢 CPU 无收益。
/// </remarks>
public sealed class FfmpegTranscodeService : ITranscodeService
{
    /// <summary>进程内并发闸：同一时刻只允许一个转码（CPU 密集，互抢无收益）</summary>
    private static readonly SemaphoreSlim TranscodeGate = new(1, 1);

    /// <summary>媒体探测缓存 TTL：覆盖一次播放会话内的分片请求风暴</summary>
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>硬件编码探测缓存 TTL：硬件可达性是部署事实，不需要每请求探</summary>
    private static readonly TimeSpan EncoderProbeTtl = TimeSpan.FromMinutes(10);

    /// <summary>硬件编码候选（按优先级）。全部不可用回落 libx264</summary>
    private static readonly string[] HardwareEncoders =
        ["h264_nvenc", "h264_qsv", "h264_amf", "h264_vaapi", "h264_videotoolbox"];

    private readonly TranscodeOptions _options;
    private readonly ILogger<FfmpegTranscodeService> _logger;

    /// <summary>缓存目录绝对路径。构造期解析：File.Exists 按进程 CWD 解析，
    /// 而 Results.File 的相对路径按 wwwroot 文件提供程序解析——两套基准
    /// 不同，混用会让"文件明明存在却 404"。绝对路径消掉整个歧义类</summary>
    private readonly string _cacheRoot;

    /// <summary>媒体探测缓存：物理路径 → （结果， 探测时间）</summary>
    // 元组字段名不叫 Result：门禁 NAS-G7 的 .Result 正则无法区分元组字段与
    // Task.Result，曾把这里误报成同步阻塞异步（2026-10-22 审计实测）
    private readonly Dictionary<string, (MediaProbeResult Probe, DateTime At)> _probeCache = new();

    /// <summary>硬件编码探测缓存：编码器名 → （可用， 探测时间）</summary>
    private readonly Dictionary<string, (bool Usable, DateTime At)> _encoderCache = new();

    public FfmpegTranscodeService(
        TranscodeOptions options,
        ILogger<FfmpegTranscodeService> logger,
        NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        _options = options;
        _logger = logger;
        _dateTime = dateTime;
        _cacheRoot = Path.GetFullPath(options.CacheDirectory);
    }

    /// <summary>时间源注入（CONVENTIONS §5.1 / 审计 F-028）：探测缓存 TTL 可测试</summary>
    private readonly NasSystem.Domain.Services.IDateTimeProvider _dateTime;

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

    // -------------------------------------------------------------------------
    // 探测：媒体信息（ffmpeg -i）与硬件编码（真实小编码）
    // -------------------------------------------------------------------------

    /// <summary>
    /// 探测媒体信息。ffmpeg -i 的 stderr 含流描述，不依赖 ffprobe 二进制
    /// （apt 的 ffmpeg 包与 static build 都保证 ffmpeg 本体在位）。
    /// 进程内 TTL 缓存：分片请求与播放列表请求共享同一次探测结果
    /// </summary>
    private async Task<MediaProbeResult> ProbeMediaAsync(
        string ffmpeg, string physicalPath, CancellationToken cancellationToken)
    {
        if (_probeCache.TryGetValue(physicalPath, out var cached)
            && _dateTime.UtcNow - cached.At < ProbeCacheTtl)
        {
            return cached.Probe;
        }

        // -i 不带输出：ffmpeg 打印流信息到 stderr 后以错误码退出（无输出目标），
        // 这里的 stderr 就是探测结果，退出码无意义
        var (_, stderr) = await RunProcessAsync(ffmpeg, $"-hide_banner -i \"{physicalPath}\"", cancellationToken);
        var result = MediaProbeParser.Parse(stderr);

        _probeCache[physicalPath] = (result, _dateTime.UtcNow);
        return result;
    }

    /// <summary>
    /// 硬件编码可用性：对候选编码器各跑一次 64x64 瞬时编码。编译进 FFmpeg
    /// 不等于运行时可达（实测 qsv 列表可见但 MFX session 创建失败），
    /// 只有真编码出非空产物才算可用。TTL 缓存探测结果
    /// </summary>
    private async Task<string?> ResolveHardwareEncoderAsync(
        string ffmpeg, CancellationToken cancellationToken)
    {
        foreach (var encoder in HardwareEncoders)
        {
            if (_encoderCache.TryGetValue(encoder, out var cached))
            {
                if (_dateTime.UtcNow - cached.At < EncoderProbeTtl)
                {
                    if (cached.Usable) return encoder;
                    continue;
                }
            }

            var usable = await ProbeEncoderAsync(ffmpeg, encoder, cancellationToken);
            _encoderCache[encoder] = (usable, _dateTime.UtcNow);
            if (usable)
            {
                _logger.LogInformation("硬件编码器可用: {Encoder}", encoder);
                return encoder;
            }
        }
        return null;
    }

    /// <summary>真实小编码探测：产物非空才判可用。
    /// 尺寸取 320x240：NVENC 有最小维度限制（实测 64x64 直接
    /// InitializeEncoder failed），320x240 在所有硬件编码器
    /// 最小值之上且仍是瞬时编码</summary>
    private async Task<bool> ProbeEncoderAsync(
        string ffmpeg, string encoder, CancellationToken cancellationToken)
    {
        var probeFile = Path.Combine(Path.GetTempPath(), $"nas-encprobe-{Guid.NewGuid():N}.mp4");
        try
        {
            var arguments =
                $"-y -hide_banner -loglevel error -f lavfi -i \"testsrc=size=320x240:rate=10:duration=1\" " +
                $"-c:v {encoder} -b:v 500k \"{probeFile}\"";
            var (exitCode, _) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);
            return exitCode == 0 && File.Exists(probeFile) && new FileInfo(probeFile).Length > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "硬件编码探测异常: {Encoder}", encoder);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probeFile)) File.Delete(probeFile);
            }
            catch (IOException)
            {
                // 探测临时文件清理失败不阻断
            }
        }
    }

    // -------------------------------------------------------------------------
    // 主流程
    // -------------------------------------------------------------------------

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

        // 探测与阶梯先于缓存键：阶梯签名进键，换源分辨率/档位/编码器不混用旧产物
        var probe = await ProbeMediaAsync(ffmpeg, physicalPath, cancellationToken);
        var rungs = TranscodeLadder.Select(probe.Height);
        if (rungs.Count == 0)
        {
            // 探测失败降级：单档 1080p（Phase 3 原行为），不猜源分辨率
            rungs = [new TranscodeRung(1080, TranscodeLadder.Standard[0].MaxBitrateKbps)];
        }
        var encoder = await ResolveHardwareEncoderAsync(ffmpeg, cancellationToken);
        var encoderName = encoder ?? "libx264";

        var cacheKey = HashKey(physicalPath, TranscodeLadder.Signature(probe.Height, rungs), encoderName);
        var outputDir = Path.Combine(_cacheRoot, cacheKey);
        var playlistPath = Path.Combine(outputDir, "master.m3u8");
        if (File.Exists(playlistPath))
            return Result<HlsPlaylist>.Success(
                new HlsPlaylist(outputDir, "master.m3u8", DirectorySize(outputDir)));

        await TranscodeGate.WaitAsync(cancellationToken);
        try
        {
            // 双检：等闸期间另一个请求可能已转完
            if (File.Exists(playlistPath))
                return Result<HlsPlaylist>.Success(
                    new HlsPlaylist(outputDir, "master.m3u8", DirectorySize(outputDir)));

            Directory.CreateDirectory(outputDir);
            var arguments = BuildTranscodeArguments(ffmpeg, physicalPath, outputDir, rungs, encoder, probe.HasAudio);

            _logger.LogInformation(
                "HLS 转码开始: {Path} → {Cache}（{Rungs} 档, {Encoder}）",
                physicalPath, cacheKey, rungs.Count, encoderName);
            var sw = Stopwatch.StartNew();
            var (exitCode, stderr) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);

            // 硬件编码运行时失败（容器忘透传设备等）：降级软件重试一次。
            // 探测通过但运行时不可用是部署事实，不是请求错误
            if (exitCode != 0 && encoder is not null)
            {
                _logger.LogWarning(
                    "硬件编码 {Encoder} 运行时失败，降级 libx264 重试: {Stderr}", encoder, Truncate(stderr));
                arguments = BuildTranscodeArguments(ffmpeg, physicalPath, outputDir, rungs, null, probe.HasAudio);
                (exitCode, stderr) = await RunProcessAsync(ffmpeg, arguments, cancellationToken);
            }

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
                "HLS 转码完成: {Cache} ({Ms}ms, {Bytes} 字节, {Encoder})",
                cacheKey, sw.ElapsedMilliseconds, DirectorySize(outputDir), encoderName);
            return Result<HlsPlaylist>.Success(
                new HlsPlaylist(outputDir, "master.m3u8", DirectorySize(outputDir)));
        }
        finally
        {
            TranscodeGate.Release();
        }
    }

    public async Task<Result<TranscodeStatus>> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveFfmpegPath();
        if (ffmpeg is null)
            return Result<TranscodeStatus>.Success(
                new TranscodeStatus(false, "无", false, []));

        var encoder = await ResolveHardwareEncoderAsync(ffmpeg, cancellationToken);
        return Result<TranscodeStatus>.Success(
            new TranscodeStatus(true, encoder ?? "libx264", encoder is not null, TranscodeLadder.Standard));
    }

    // -------------------------------------------------------------------------
    // 参数构建（纯函数，单测直接覆盖分支）
    // -------------------------------------------------------------------------

    /// <summary>
    /// 构建多码率 HLS 参数：split 多路 scale，var_stream_map 声明档位，
    /// %v 占位使每档产物落 v{N}/ 子目录，master.m3u8 在缓存根。
    /// 音频按探测结果映射：有音频每档配一份（var_stream_map 的 a:N 指向
    /// 各自映射），无音频只映射视频（-map 0:a 会因无流直接失败）
    /// </summary>
    internal static string BuildTranscodeArguments(
        string ffmpeg,
        string physicalPath,
        string outputDir,
        IReadOnlyList<TranscodeRung> rungs,
        string? hardwareEncoder,
        bool hasAudio)
    {
        var n = rungs.Count;

        // split + scale 链：[0:v]split=N[s0][s1]...;[si]scale=-2:H[vi]
        var filter = new StringBuilder();
        filter.Append("[0:v]split=").Append(n);
        for (var i = 0; i < n; i++) filter.Append("[s").Append(i).Append(']');
        for (var i = 0; i < n; i++)
        {
            filter.Append(";[s").Append(i).Append("]scale=-2:").Append(rungs[i].Height).Append("[v").Append(i).Append(']');
        }

        var sb = new StringBuilder();
        sb.Append("-y -hide_banner -loglevel error -i \"").Append(physicalPath).Append("\" ");
        sb.Append("-filter_complex \"").Append(filter).Append("\" ");
        for (var i = 0; i < n; i++) sb.Append("-map \"[v").Append(i).Append("]\" ");
        if (hasAudio)
        {
            for (var i = 0; i < n; i++) sb.Append("-map 0:a ");
        }

        // var_stream_map：每档 "v:i" 或 "v:i,a:i"（音频映射与视频同序）
        var streamMap = new List<string>();
        for (var i = 0; i < n; i++)
        {
            streamMap.Add(hasAudio ? $"v:{i},a:{i}" : $"v:{i}");
        }
        sb.Append("-var_stream_map \"").Append(string.Join(' ', streamMap)).Append("\" ");

        // 编码器：硬件用车道码率（-b:v），软件用 CRF 锚质量 + maxrate 锁车道
        if (hardwareEncoder is not null)
        {
            sb.Append("-c:v ").Append(hardwareEncoder).Append(' ');
            if (hardwareEncoder == "h264_nvenc") sb.Append("-preset p2 ");
            for (var i = 0; i < n; i++)
            {
                sb.Append("-b:v:").Append(i).Append(' ').Append(rungs[i].MaxBitrateKbps).Append("k ");
            }
        }
        else
        {
            sb.Append("-c:v libx264 -preset veryfast -crf 23 ");
            for (var i = 0; i < n; i++)
            {
                var maxrate = rungs[i].MaxBitrateKbps;
                sb.Append("-maxrate:").Append(i).Append(' ').Append(maxrate).Append("k ")
                  .Append("-bufsize:").Append(i).Append(' ').Append(maxrate * 2).Append("k ");
            }
        }

        if (hasAudio)
        {
            sb.Append("-c:a aac -b:a 128k ");
        }

        // HLS 输出：%v 进子目录与文件名，master 落缓存根。
        // 路径统一正斜杠：%v 替换混在 Windows 反斜杠里会让 ffmpeg 的
        // 路径拼接出现歧义（实测 pattern 内的目录分隔符要正斜杠）。
        // master_pl_name 只能传纯文件名：带目录会被 ffmpeg 再拼一次
        // 输出目录（实测 out/master.m3u8 变 out/out/master.m3u8 直接失败）
        var dir = outputDir.Replace('\\', '/');
        sb.Append("-f hls -hls_time 6 -hls_playlist_type vod ")
          .Append("-hls_segment_filename \"").Append(dir).Append("/v%v/seg%03d.ts\" ")
          .Append("-master_pl_name \"master.m3u8\" ")
          .Append('"').Append(dir).Append("/v%v/index.m3u8\"");
        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // 进程与工具
    // -------------------------------------------------------------------------

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
        if (!Directory.Exists(_cacheRoot))
            return Task.FromResult(0L);

        var freed = 0L;
        foreach (var dir in Directory.EnumerateDirectories(_cacheRoot))
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

    /// <summary>缓存键：物理路径 + 阶梯签名 + 编码器的 SHA256 前 16 字节。
    /// 阶梯签名含源高度与档位，编码器进键：硬件可达性变化即换产物，
    /// 不与另一编码器的缓存混用</summary>
    private static string HashKey(string physicalPath, string ladderSignature, string encoder) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{physicalPath}|{ladderSignature}|{encoder}")))[..32].ToLowerInvariant();

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
    /// <summary>转码缓存目录（相对 data 目录或绝对路径）</summary>
    public string CacheDirectory { get; set; } = "temp/transcodes";

    /// <summary>ffmpeg 可执行路径（空则查 PATH）</summary>
    public string? FfmpegPath { get; set; }
}
