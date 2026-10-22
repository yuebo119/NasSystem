// =============================================================================
// NasSystem.Api - Endpoints/TranscodeEndpoints.cs
// 视频 HLS 流端点（ROADMAP Phase 3，多码率阶梯寻址）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Files;
using NasSystem.Application.Media;
using NasSystem.Shared;

/// <summary>
/// HLS 播放：master 播放列表、variant 子列表与分片文件，按 query 参数分派。
/// </summary>
/// <remarks>
/// 防穿越：分片只服务转码缓存目录内 v{N}/ 下的 .ts 文件（段名过白名单，
/// variant 序号有上限），虚拟路径经库内记录换物理路径——不接受客户端
/// 直传物理路径。ffmpeg 不可用时转码端点返回 501 诚实响应（开发机常见），
/// 不做假流。URL 重写方案见 HlsUrlRewriter：磁盘的 v{N}/ 布局对播放器
/// 透明，播放器只见 {视频路径}?variant=N&amp;segment=xxx 的寻址形态
/// </remarks>
public static class TranscodeEndpoints
{
    /// <summary>variant 序号上限：挡异常 query 值，正常阶梯最多几档</summary>
    private const int MaxVariant = 8;

    public static RouteGroupBuilder MapTranscodeEndpoints(this RouteGroupBuilder group)
    {
        var stream = group.MapGroup("/media/stream").WithTags("视频转码");

        // 单 catch-all 端点按 query 分派：无参数 master、variant=N 子列表、
        // variant=N + segment=NAME 分片。不用 /{*path}/segments/{segment}
        // 双段形态——catch-all 只能出现在路由模板最后一段（RoutePatternException 实测）
        stream.MapGet("/{*path}", GetStream)
              .WithName("GetHlsStream")
              .WithSummary("获取视频 HLS master 播放列表、子列表或分片")
              .WithDescription("无 query 参数返回 master 播放列表（首次请求按需触发 FFmpeg 转码，幂等缓存）；variant=N 返回第 N 档子列表；再加 segment=NAME 返回分片")
              .RequireAuthorization();

        stream.MapGet("/status", GetStatus)
              .WithName("GetTranscodeStatus")
              .WithSummary("转码能力状态")
              .WithDescription("ffmpeg 可用性、当前生效编码器（硬件/软件）、码率阶梯档位")
              .RequireAuthorization();

        stream.MapDelete("/cache", ClearCache)
              .WithName("ClearTranscodeCache")
              .WithSummary("清空转码缓存")
              .WithDescription("删除全部 HLS 转码产物；下次播放按需重新转码")
              .RequireAuthorization("RequireAdmin");

        return stream;
    }

    private static async Task<IResult> GetStream(
        string path,
        ITranscodeService transcodeService,
        IFileAccessResolver accessResolver,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // query 寻址：variant / segment 决定拿哪一层产物
        var variantText = httpContext.Request.Query["variant"];
        var segment = httpContext.Request.Query["segment"].ToString();

        var normalized = "/" + path.Trim('/');
        var userId = GetUserId(user);
        var access = await accessResolver.ResolveAsync(normalized, userId, cancellationToken);
        if (access.Status == FileAccessStatus.NotFound)
            return Results.NotFound(new { message = $"路径不存在: {normalized}" });
        if (access.Status == FileAccessStatus.Forbidden)
            return Results.Forbid();

        // 复用 GetOrCreateHlsAsync 的缓存键口径（同路径同目录），不重复转码
        var result = await transcodeService.GetOrCreateHlsAsync(access.File!.PhysicalPath, cancellationToken);
        if (result.IsFailure)
        {
            // ffmpeg 缺失与源缺失都是诚实 501/404：客户端展示"转码不可用"
            // 而非假装播放
            return result.Error.Code == "Transcode.SourceMissing"
                ? Results.NotFound(new { message = result.Error.Message })
                : Results.Json(
                    new { message = result.Error.Message },
                    statusCode: StatusCodes.Status501NotImplemented);
        }

        var dir = result.Value.Directory;

        // ① 分片：variant=N + segment=NAME，段名白名单挡 ../ 与任意文件名
        if (!string.IsNullOrEmpty(segment))
        {
            if (!int.TryParse(variantText, out var v) || v < 0 || v >= MaxVariant
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    segment, "^seg[0-9]{3}\\.ts$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
                return Results.BadRequest(new { message = "非法分片寻址" });

            var segmentPath = Path.Combine(dir, $"v{v}", segment);
            if (!File.Exists(segmentPath))
                return Results.NotFound(new { message = "分片不存在" });

            return Results.File(segmentPath, "video/mp2t", enableRangeProcessing: true);
        }

        // ② variant 子列表：读磁盘 v{N}/index.m3u8，分片行重写为 query 寻址
        if (int.TryParse(variantText, out var variant)
            && variant >= 0 && variant < MaxVariant)
        {
            var variantPath = Path.Combine(dir, $"v{variant}", "index.m3u8");
            if (!File.Exists(variantPath))
                return Results.NotFound(new { message = "子播放列表不存在" });

            var content = await File.ReadAllTextAsync(variantPath, cancellationToken);
            var baseName = normalized.Split('/')[^1];
            return Results.Content(
                HlsUrlRewriter.RewriteVariant(content, baseName, variant),
                "application/vnd.apple.mpegurl");
        }

        // ③ master：读磁盘 master.m3u8，variant 行重写为 query 寻址。
        // 传末段文件名而非完整路径：相对 URI 以本 URL 为基准解析，
        // 完整路径会把父目录段拼重复（movies/a.mp4 → movies/movies/a.mp4）
        var masterPath = Path.Combine(dir, result.Value.PlaylistFileName);
        var masterContent = await File.ReadAllTextAsync(masterPath, cancellationToken);
        return Results.Content(
            HlsUrlRewriter.RewriteMaster(masterContent, normalized.Split('/')[^1]),
            "application/vnd.apple.mpegurl");
    }

    private static async Task<IResult> GetStatus(
        ITranscodeService transcodeService,
        CancellationToken cancellationToken)
    {
        var result = await transcodeService.GetStatusAsync(cancellationToken);
        if (result.IsFailure)
            return Results.Json(
                ApiResponse<object>.Fail(result.Error),
                statusCode: StatusCodes.Status501NotImplemented);

        var status = result.Value;
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            ffmpegAvailable = status.FfmpegAvailable,
            encoder = status.Encoder,
            isHardwareEncoder = status.IsHardwareEncoder,
            rungs = status.Rungs.Select(r => new { height = r.Height, maxBitrateKbps = r.MaxBitrateKbps, name = r.Name })
        }));
    }

    private static async Task<IResult> ClearCache(
        ITranscodeService transcodeService,
        CancellationToken cancellationToken)
    {
        var freed = await transcodeService.ClearCacheAsync(cancellationToken);
        // freedBytes 让前端告知用户实际释放了多少空间（清空是慢动作，
        // 大缓存下数值可观）；包装形态与其余端点一致
        return Results.Ok(ApiResponse<object>.Ok(new { freedBytes = freed }));
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
