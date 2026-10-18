// =============================================================================
// NasSystem.Api - Endpoints/TranscodeEndpoints.cs
// 视频 HLS 流端点（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Media;
using NasSystem.Domain.Repositories;

/// <summary>
/// HLS 播放：取播放列表（按需触发转码）与分片文件。
/// </summary>
/// <remarks>
/// 防穿越：分片端点只服务转码缓存目录内的 .ts 文件（缓存目录由服务端
/// 管，文件名来自白名单校验），虚拟路径经库内记录换物理路径——
/// 不接受客户端直传物理路径。ffmpeg 不可用时转码端点返回 501 诚实
/// 响应（开发机常见），不做假流。
/// </remarks>
public static class TranscodeEndpoints
{
    public static RouteGroupBuilder MapTranscodeEndpoints(this RouteGroupBuilder group)
    {
        var stream = group.MapGroup("/media/stream").WithTags("视频转码");

        // 单 catch-all 端点按后缀分派：.ts 走分片，其余走播放列表。
        // 不用 /{*path}/segments/{segment} 双段形态——catch-all 只能
        // 出现在路由模板最后一段（RoutePatternException 实测）
        stream.MapGet("/{*path}", GetStream)
              .WithName("GetHlsStream")
              .WithSummary("获取视频 HLS 播放列表或分片")
              .WithDescription("path 以 .ts 结尾返回分片，否则返回 m3u8 播放列表（首次请求按需触发 FFmpeg 转码，幂等缓存）")
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
        IFileItemRepository fileRepository,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // 分片路径 = 视频路径 + /seg%03d.ts（m3u8 内相对引用基于播放列表
        // URL 解析）。按最后一段拆分：段名过白名单、余下的是视频路径
        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = path.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var segment = path[(lastSlash + 1)..];
                var videoPath = path[..lastSlash];
                return await ServeSegmentAsync(
                    videoPath, segment, transcodeService, fileRepository, user, cancellationToken);
            }
        }

        var normalized = "/" + path.Trim('/');
        var item = await fileRepository.GetByPathAsync(normalized, cancellationToken);
        if (item is null)
            return Results.NotFound(new { message = $"路径不存在: {normalized}" });

        var userId = GetUserId(user);
        if (item.OwnerId != userId)
            return Results.Forbid();

        var result = await transcodeService.GetOrCreateHlsAsync(item.PhysicalPath, cancellationToken);
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

        var playlistPath = Path.Combine(result.Value.Directory, result.Value.PlaylistFileName);
        var content = await File.ReadAllTextAsync(playlistPath, cancellationToken);
        return Results.Content(content, "application/vnd.apple.mpegurl");
    }

    private static async Task<IResult> ServeSegmentAsync(
        string path,
        string segment,
        ITranscodeService transcodeService,
        IFileItemRepository fileRepository,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // 段名白名单：seg%03d.ts 形态，挡住 ../ 与任意文件名
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                segment, "^seg[0-9]{3}\\.ts$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            return Results.BadRequest(new { message = "非法分片名" });

        var normalized = "/" + path.Trim('/');
        var item = await fileRepository.GetByPathAsync(normalized, cancellationToken);
        if (item is null)
            return Results.NotFound(new { message = $"路径不存在: {normalized}" });

        var userId = GetUserId(user);
        if (item.OwnerId != userId)
            return Results.Forbid();

        // 复用 GetOrCreateHlsAsync 的缓存键口径（同路径同目录），不重复转码
        var result = await transcodeService.GetOrCreateHlsAsync(item.PhysicalPath, cancellationToken);
        if (result.IsFailure)
            return Results.Json(
                new { message = result.Error.Message },
                statusCode: StatusCodes.Status501NotImplemented);

        var segmentPath = Path.Combine(result.Value.Directory, segment);
        if (!File.Exists(segmentPath))
            return Results.NotFound(new { message = "分片不存在" });

        return Results.File(segmentPath, "video/mp2t", enableRangeProcessing: true);
    }

    private static async Task<IResult> ClearCache(
        ITranscodeService transcodeService,
        CancellationToken cancellationToken)
    {
        var freed = await transcodeService.ClearCacheAsync(cancellationToken);
        return Results.Ok(new { freedBytes = freed });
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
