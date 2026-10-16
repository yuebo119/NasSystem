// =============================================================================
// NasSystem.Api - ThumbnailEndpoints.cs
// 缩略图 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Files;
using NasSystem.Domain.Repositories;

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
        IFileItemRepository fileItemRepository,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var targetWidth = width ?? 200;
        var targetHeight = height ?? 200;

        // 条件请求在查库之前判定：ETag 只由 URL 三要素（路径加请求尺寸）决定，
        // 同一 URL 的服务端吸附结果必然相同，故键稳定。命中 If-None-Match 直接 304，
        // 连一次 FileItems 查询都省掉。
        // 不用服务端算出的缓存键当 ETag：那需要先调服务，304 就失去了省成本的意义
        var etag = $"\"{path}:{targetWidth}:{targetHeight}\"";
        httpContext.Response.Headers.ETag = etag;

        if (httpContext.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // 端点收到的是虚拟路径，缩略图服务要的是物理路径。原先直接把虚拟路径交给
        // 服务，File.Exists 恒为 false，缩略图功能整个不工作（Thumbnail.FileNotFound）。
        // 查不到记录时退回原值，让服务给出原有的"源文件不存在"错误而不是崩
        var item = await fileItemRepository.GetByPathAsync(path, cancellationToken);
        var physicalPath = item?.PhysicalPath ?? path;

        var result = await thumbnailService.GetThumbnailAsync(
            physicalPath,
            targetWidth,
            targetHeight,
            GetUserId(user),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // 缓存头。前端经 fetch + blob + objectURL 引用（Files.vue），浏览器私有缓存
        // 能整条命中，重复进入同一目录时缩略图零请求。此前这里一个头都没设，
        // 而注释写着"设置缓存头"。max-age 1 天只影响多久不回源校验，
        // 不影响正确性：源文件变了 ETag 变，且服务端有 LastWriteTimeUtc 比对兜底
        httpContext.Response.Headers.CacheControl = "private, max-age=86400";

        return Results.Bytes(
            result.Value.Data,
            result.Value.ContentType,
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
