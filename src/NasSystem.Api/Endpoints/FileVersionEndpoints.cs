// =============================================================================
// NasSystem.Api - FileVersionEndpoints.cs
// 文件版本 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.FileVersions;

/// <summary>
/// 文件版本端点扩展
/// </summary>
public static class FileVersionEndpoints
{
    /// <summary>
    /// 注册文件版本相关端点
    /// </summary>
    public static RouteGroupBuilder MapFileVersionEndpoints(this RouteGroupBuilder group)
    {
        var versions = group.MapGroup("/file-versions").WithTags("文件版本");

        // 获取文件的所有版本
        versions.MapGet("/{fileItemId:guid}", GetVersions)
             .WithName("GetFileVersions")
             .WithSummary("获取文件的所有版本")
             .WithDescription("获取指定文件的所有历史版本列表")
             .RequireAuthorization();

        // 获取指定版本详情
        versions.MapGet("/{fileItemId:guid}/{versionNumber:int}", GetVersion)
             .WithName("GetFileVersion")
             .WithSummary("获取指定版本详情")
             .WithDescription("获取文件的指定版本详细信息")
             .RequireAuthorization();

        // 下载指定版本
        versions.MapGet("/{fileItemId:guid}/{versionNumber:int}/download", DownloadVersion)
             .WithName("DownloadFileVersion")
             .WithSummary("下载指定版本")
             .WithDescription("下载文件的指定历史版本")
             .RequireAuthorization();

        // 恢复到指定版本
        versions.MapPost("/{fileItemId:guid}/{versionNumber:int}/restore", RestoreVersion)
             .WithName("RestoreFileVersion")
             .WithSummary("恢复到指定版本")
             .WithDescription("将文件恢复到指定的历史版本")
             .RequireAuthorization();

        // 比较两个版本
        versions.MapGet("/{fileItemId:guid}/compare", CompareVersions)
             .WithName("CompareFileVersions")
             .WithSummary("比较两个版本")
             .WithDescription("比较文件的两个版本之间的差异")
             .RequireAuthorization();

        // 删除指定版本
        versions.MapDelete("/{fileItemId:guid}/{versionNumber:int}", DeleteVersion)
             .WithName("DeleteFileVersion")
             .WithSummary("删除指定版本")
             .WithDescription("删除文件的指定历史版本（不能删除当前版本）")
             .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetVersions(
        Guid fileItemId,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.GetVersionsAsync(fileItemId, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetVersion(
        Guid fileItemId,
        int versionNumber,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.GetVersionAsync(
            fileItemId, versionNumber, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DownloadVersion(
        Guid fileItemId,
        int versionNumber,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.DownloadVersionAsync(
            fileItemId, versionNumber, userId, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        var (stream, fileName, contentType) = result.Value;
        return Results.File(stream, contentType, fileName);
    }

    private static async Task<IResult> RestoreVersion(
        Guid fileItemId,
        int versionNumber,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.RestoreVersionAsync(
            fileItemId, versionNumber, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CompareVersions(
        Guid fileItemId,
        int version1,
        int version2,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.CompareVersionsAsync(
            fileItemId, version1, version2, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteVersion(
        Guid fileItemId,
        int versionNumber,
        IFileVersionService versionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await versionService.DeleteVersionAsync(
            fileItemId, versionNumber, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
