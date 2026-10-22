// =============================================================================
// NasSystem.Api - ThumbnailEndpoints.cs
// 缩略图 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Files;

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
        IFileAccessResolver accessResolver,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var targetWidth = width ?? 200;
        var targetHeight = height ?? 200;
        var userId = GetUserId(user);

        // 条件请求在查库之前判定：ETag 由 URL 三要素（路径加请求尺寸）加用户决定。
        // 含用户是因为缩略图现在按归属放行（多用户隔离）：ETag 不含用户时，
        // 浏览器缓存里他人文件的缩略图会被 304 重放给当前用户，归属校验被绕过
        // 路径经 EscapeDataString 转 ASCII：ETag 是 HTTP 头，头值不允许
        // 非 ASCII。含中文/空格的文件名直传会让 Kestrel 抛 Invalid
        // non-ASCII character，缩略图整个 500（2026-10-19 E2E 走查实证：
        // 媒体库 5 张图里带中文名的那个 400）
        var etag = $"\"{Uri.EscapeDataString(path)}:{userId}:{targetWidth}:{targetHeight}\"";
        httpContext.Response.Headers.ETag = etag;

        if (httpContext.Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
        {
            httpContext.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        // 端点收到的是虚拟路径，缩略图服务要的是物理路径。原先直接把虚拟路径交给
        // 服务，File.Exists 恒为 false，缩略图功能整个不工作（Thumbnail.FileNotFound）。
        // 多用户隔离：记录不存在、已软删或非本人时按不存在处理，不回退原值——
        // 回退会让服务拿裸路径去碰磁盘，既是越权面也是噪声错误
        var access = await accessResolver.ResolveAsync(path, userId, cancellationToken);
        if (access.Status != FileAccessStatus.Ok)
            return Results.NotFound(ApiResponse<object>.Fail("File.NotFound", $"文件不存在: {path}"));
        var physicalPath = access.File!.PhysicalPath;

        var result = await thumbnailService.GetThumbnailAsync(
            path,
            physicalPath,
            targetWidth,
            targetHeight,
            userId,
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
