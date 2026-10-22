// =============================================================================
// NasSystem.Api - FileShareEndpoints.cs
// 文件分享 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Api.Endpoints;
using NasSystem.Api.Security;
using NasSystem.Application.FileShares;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件分享端点扩展
/// </summary>
public static class FileShareEndpoints
{
    /// <summary>
    /// 注册文件分享相关端点
    /// </summary>
    public static RouteGroupBuilder MapFileShareEndpoints(this RouteGroupBuilder group)
    {
        // 管理 API（需要登录）
        var fileShares = group.MapGroup("/file-shares").WithTags("文件分享管理");

        fileShares.MapPost("/", CreateFileShare)
                  .WithName("CreateFileShare")
                  .WithSummary("创建文件分享")
                  .RequireAuthorization();

        fileShares.MapGet("/", GetMyFileShares)
                  .WithName("GetMyFileShares")
                  .WithSummary("获取我的分享列表")
                  .RequireAuthorization();

        fileShares.MapGet("/{id:guid}", GetFileShareById)
                  .WithName("GetFileShareById")
                  .WithSummary("获取分享详情")
                  .RequireAuthorization();

        fileShares.MapPut("/{id:guid}", UpdateFileShare)
                  .WithName("UpdateFileShare")
                  .WithSummary("更新分享设置")
                  .RequireAuthorization();

        fileShares.MapDelete("/{id:guid}", DeleteFileShare)
                  .WithName("DeleteFileShare")
                  .WithSummary("删除分享")
                  .RequireAuthorization();

        fileShares.MapPost("/{id:guid}/regenerate-code", RegenerateShareCode)
                  .WithName("RegenerateShareCode")
                  .WithSummary("重新生成分享码")
                  .RequireAuthorization();

        // ITM-N23：认证用户的分享下载/浏览入口——Internal 型分享的唯一合法通道
        // （公开组对 Internal 返回 LoginRequired；此处 isAuthenticated=true 放行）
        fileShares.MapGet("/{shareCode}/download", DownloadAuthenticatedShareFile)
                  .WithName("DownloadAuthenticatedShareFile")
                  .WithSummary("认证用户下载分享文件")
                  .Produces(404)
                  .RequireAuthorization();

        fileShares.MapGet("/{shareCode}/files/{*subPath}", GetAuthenticatedShareFolderContent)
                  .WithName("GetAuthenticatedShareFolderContent")
                  .WithSummary("认证用户浏览分享文件夹")
                  .RequireAuthorization();

        // 公开访问 API（无需登录）- 注册到根路由
        return group;
    }

    /// <summary>
    /// 注册公开分享访问端点（无需登录）
    /// </summary>
    public static WebApplication MapPublicShareEndpoints(this WebApplication app)
    {
        // API 端点放在 /api/v1/public-shares 下。
        // 整组挂 "share" 限流（按客户端 IP 分区）：这组是匿名面，
        // /verify 是口令判定端点，零限流即可在线爆破（CONVENTIONS §4.5）
        var publicShares = app.MapGroup("/api/v1/public-shares")
            .RequireRateLimiting("share")
            .WithTags("公开分享访问");

        // 信息端点是分享码存在性预言机（404 vs 200）。ASP.NET Core 的
        // RequireRateLimiting 是端点级元数据，同端点叠加第二个策略不会
        // 都生效（实测只有端点级那个生效），因此全局桶不走策略叠加，
        // 改由 ShareEnumerationGuard 在 handler 内显式检查，与组级的
        // 按 IP 窗口（对本端点仍生效）叠加成双闸
        publicShares.MapGet("/{shareCode}", GetPublicShareInfo)
                    .WithName("GetPublicShareInfo")
                    .WithSummary("获取公开分享信息");

        publicShares.MapPost("/{shareCode}/verify", VerifySharePassword)
                    .WithName("VerifySharePassword")
                    .WithSummary("验证分享密码");

        publicShares.MapGet("/{shareCode}/download", DownloadShareFile)
                    .WithName("DownloadShareFile")
                    .WithSummary("下载分享文件");

        publicShares.MapGet("/{shareCode}/download/{*subPath}", DownloadShareSubFile)
                    .WithName("DownloadShareSubFile")
                    .WithSummary("下载分享文件夹中的文件");

        publicShares.MapGet("/{shareCode}/files", GetShareFolderContent)
                    .WithName("GetShareFolderContent")
                    .WithSummary("获取分享文件夹内容");

        publicShares.MapGet("/{shareCode}/files/{*subPath}", GetShareSubFolderContent)
                    .WithName("GetShareSubFolderContent")
                    .WithSummary("获取分享子文件夹内容");

        return app;
    }

    #region 管理 API 处理方法

    private static async Task<IResult> CreateFileShare(
        CreateFileShareRequest request,
        IFileShareService fileShareService,
        HttpContext httpContext,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var baseUrl = GetBaseUrl(httpContext);
        var result = await fileShareService.CreateAsync(request, userId, baseUrl);
        return result.IsSuccess
            ? Results.Created($"/api/v1/file-shares/{result.Value.Id}", ApiResponse<FileShareDto>.Ok(result.Value))
            : result.ToHttpResult();
    }

    private static async Task<IResult> GetMyFileShares(
        IFileShareService fileShareService,
        HttpContext httpContext,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var baseUrl = GetBaseUrl(httpContext);
        var result = await fileShareService.GetMySharesAsync(userId, baseUrl);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetFileShareById(
        Guid id,
        IFileShareService fileShareService,
        HttpContext httpContext,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var baseUrl = GetBaseUrl(httpContext);
        var result = await fileShareService.GetByIdAsync(id, userId, baseUrl);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdateFileShare(
        Guid id,
        UpdateFileShareRequest request,
        IFileShareService fileShareService,
        HttpContext httpContext,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var baseUrl = GetBaseUrl(httpContext);
        var result = await fileShareService.UpdateAsync(id, request, userId, baseUrl);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteFileShare(
        Guid id,
        IFileShareService fileShareService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await fileShareService.DeleteAsync(id, userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RegenerateShareCode(
        Guid id,
        IFileShareService fileShareService,
        HttpContext httpContext,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var baseUrl = GetBaseUrl(httpContext);
        var result = await fileShareService.RegenerateShareCodeAsync(id, userId, baseUrl);
        return result.ToHttpResult();
    }

    #endregion

    #region 公开访问 API 处理方法

    private static async Task<IResult> GetPublicShareInfo(
        string shareCode,
        IFileShareService fileShareService,
        ShareEnumerationGuard enumerationGuard)
    {
        // 全局枚举闸（F-073）：本端点是分享码存在性预言机，按 IP 的窗口
        // 挡不住分布式枚举，这里再钉一层总速率。超限回 429 且不查库
        if (!enumerationGuard.TryAcquire())
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        var result = await fileShareService.GetPublicShareInfoAsync(shareCode);
        return result.ToHttpResult();
    }

    private static async Task<IResult> VerifySharePassword(
        string shareCode,
        VerifySharePasswordRequest request,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.VerifyPasswordAsync(shareCode, request.Password);
        if (result.IsSuccess)
        {
            return Results.Ok(ApiResponse<object>.Ok(new { AccessToken = result.Value }));
        }
        return result.ToHttpResult();
    }


    /// <summary>
    /// 分享下载统一出口：单文件走 Range 流式响应，文件夹走 ZipDirectoryResult 边扫边压缩
    /// </summary>
    private static IResult ToShareDownloadResult(Result<ShareDownloadTarget> result)
    {
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var target = result.Value;
        return target.Stream is not null
            ? Results.File(target.Stream, target.ContentType, target.FileName, enableRangeProcessing: true)
            : new ZipDirectoryResult(target.ZipDirectory!, target.FileName);
    }

    private static async Task<IResult> DownloadShareFile(
        string shareCode,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetDownloadStreamAsync(shareCode, token, null);
        return ToShareDownloadResult(result);
    }

    private static async Task<IResult> DownloadShareSubFile(
        string shareCode,
        string? subPath,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetDownloadStreamAsync(shareCode, token, subPath);
        return ToShareDownloadResult(result);
    }

    /// <summary>
    /// 认证用户下载（ITM-N23）：isAuthenticated=true 使 Internal 型分享可用；
    /// 密码型分享仍要求 accessToken（认证身份不豁免密码校验）
    /// </summary>
    private static async Task<IResult> DownloadAuthenticatedShareFile(
        string shareCode,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetDownloadStreamAsync(shareCode, token, null, isAuthenticated: true);
        return ToShareDownloadResult(result);
    }

    private static async Task<IResult> GetAuthenticatedShareFolderContent(
        string shareCode,
        string? subPath,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetFolderContentAsync(shareCode, token, subPath, isAuthenticated: true);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetShareFolderContent(
        string shareCode,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetFolderContentAsync(shareCode, token, null);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetShareSubFolderContent(
        string shareCode,
        string subPath,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetFolderContentAsync(shareCode, token, subPath);
        return result.ToHttpResult();
    }

    #endregion

    #region 辅助方法

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private static string GetBaseUrl(HttpContext httpContext)
    {
        var request = httpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    #endregion
}
