// =============================================================================
// NasSystem.Api - FileShareEndpoints.cs
// 文件分享 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
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

        // 公开访问 API（无需登录）- 注册到根路由
        return group;
    }

    /// <summary>
    /// 注册公开分享访问端点（无需登录）
    /// </summary>
    public static WebApplication MapPublicShareEndpoints(this WebApplication app)
    {
        // API 端点放在 /api/v1/public-shares 下
        var publicShares = app.MapGroup("/api/v1/public-shares").WithTags("公开分享访问");

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
        IFileShareService fileShareService)
    {
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


    private static async Task<IResult> DownloadShareFile(
        string shareCode,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetDownloadStreamAsync(shareCode, token, null);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var (stream, fileName, contentType) = result.Value;
        return Results.File(stream, contentType, fileName);
    }

    private static async Task<IResult> DownloadShareSubFile(
        string shareCode,
        string subPath,
        string? token,
        IFileShareService fileShareService)
    {
        var result = await fileShareService.GetDownloadStreamAsync(shareCode, token, subPath);
        if (!result.IsSuccess)
            return result.ToHttpResult();

        var (stream, fileName, contentType) = result.Value;
        return Results.File(stream, contentType, fileName);
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
