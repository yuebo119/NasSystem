// =============================================================================
// NasSystem.Api - MediaEndpoints.cs
// 媒体库 API 端点（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Media;

/// <summary>
/// 媒体库端点。列表与元数据分离：列表纯数据库（滚动不触发解码），
/// 元数据按页批量（一次一页，解析盒/帧头毫秒级）
/// </summary>
public static class MediaEndpoints
{
    public static RouteGroupBuilder MapMediaEndpoints(this RouteGroupBuilder group)
    {
        var media = group.MapGroup("/media").WithTags("媒体库");

        // 媒体列表（时间线）
        media.MapGet("/", GetMedia)
             .WithName("GetMedia")
             .WithSummary("获取媒体列表")
             .WithDescription("按类型（image/video/audio）聚合，创建时间倒序，可选月份过滤（yyyy-MM）")
             .RequireAuthorization();

        // 批量元数据
        media.MapPost("/metadata", GetMetadata)
             .WithName("GetMediaMetadata")
             .WithSummary("批量获取媒体元数据")
             .WithDescription("按路径批量提取元数据（图片尺寸、视频时长/尺寸、音频时长）。非本人或不存在路径静默跳过")
             .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetMedia(
        string type,
        string? month = null,
        int page = 1,
        int pageSize = 60,
        IMediaService mediaService = null!,
        ClaimsPrincipal user = null!,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        var query = new MediaQuery(type, month, page, pageSize);
        var result = await mediaService.GetMediaAsync(query, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetMetadata(
        MediaMetadataRequest request,
        IMediaService mediaService = null!,
        ClaimsPrincipal user = null!,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(user);
        var result = await mediaService.GetMetadataAsync(request.Paths, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private sealed record MediaMetadataRequest(IReadOnlyList<string> Paths);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
