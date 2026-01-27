// =============================================================================
// NasSystem.Api - ThumbnailEndpoints.cs
// 缩略图 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Thumbnails;
using NasSystem.Shared;

/// <summary>
/// 缩略图端点扩展
/// </summary>
public static class ThumbnailEndpoints
{
    /// <summary>
    /// 注册缩略图相关端点
    /// </summary>
    public static RouteGroupBuilder MapThumbnailEndpoints(this RouteGroupBuilder group)
    {
        var thumbnails = group.MapGroup("/thumbnails").WithTags("缩略图");

        // 获取缩略图
        thumbnails.MapGet("/{*path}", GetThumbnail)
             .WithName("GetThumbnail")
             .WithSummary("获取文件缩略图")
             .WithDescription("获取指定文件的缩略图，支持自定义尺寸。支持图片和视频文件。")
             .RequireAuthorization();

        // 检查是否支持缩略图
        thumbnails.MapGet("/supported/{*path}", CheckThumbnailSupport)
             .WithName("CheckThumbnailSupport")
             .WithSummary("检查文件是否支持缩略图")
             .WithDescription("检查指定文件是否支持生成缩略图")
             .RequireAuthorization();

        // 清除缩略图缓存
        thumbnails.MapDelete("/cache/{*path}", ClearThumbnailCache)
             .WithName("ClearThumbnailCache")
             .WithSummary("清除缩略图缓存")
             .WithDescription("清除指定文件的缩略图缓存")
             .RequireAuthorization();

        // 清除所有缩略图缓存（管理员）
        thumbnails.MapDelete("/cache", ClearAllThumbnailCache)
             .WithName("ClearAllThumbnailCache")
             .WithSummary("清除所有缩略图缓存")
             .WithDescription("清除系统中所有的缩略图缓存。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static async Task<IResult> GetThumbnail(
        string path,
        int? width,
        int? height,
        IThumbnailService thumbnailService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var targetWidth = width ?? 200;
        var targetHeight = height ?? 200;

        var result = await thumbnailService.GetThumbnailAsync(
            path,
            targetWidth,
            targetHeight,
            userId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        var thumbnail = result.Value;
        
        // 设置缓存头
        return Results.Bytes(
            thumbnail.Data,
            thumbnail.ContentType,
            enableRangeProcessing: false);
    }

    private static IResult CheckThumbnailSupport(
        string path,
        IThumbnailService thumbnailService)
    {
        var isSupported = thumbnailService.IsThumbnailSupported(path);
        
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Path = path,
            IsSupported = isSupported,
            Extension = Path.GetExtension(path)
        }));
    }

    private static async Task<IResult> ClearThumbnailCache(
        string path,
        IThumbnailService thumbnailService)
    {
        await thumbnailService.ClearCacheAsync(path);
        
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Message = $"已清除文件 '{path}' 的缩略图缓存"
        }));
    }

    private static async Task<IResult> ClearAllThumbnailCache(
        IThumbnailService thumbnailService)
    {
        await thumbnailService.ClearAllCacheAsync();
        
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            Message = "已清除所有缩略图缓存"
        }));
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
