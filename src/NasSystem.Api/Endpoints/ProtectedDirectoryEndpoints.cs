// =============================================================================
// NasSystem.Api - Endpoints/ProtectedDirectoryEndpoints.cs
// 受保护目录 API 端点（ROADMAP Phase 2 关键目录只读保护）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.ProtectedDirectories;

/// <summary>
/// 受保护目录管理端点。只读保护的执行不在端点层：拦截点在 FileService
/// 的写路径（上传覆盖/Move/Delete/Rename）与 WebDAV 对应动词，
/// 两者都走 FileService，一处接线全覆盖。
/// </summary>
public static class ProtectedDirectoryEndpoints
{
    public static RouteGroupBuilder MapProtectedDirectoryEndpoints(this RouteGroupBuilder group)
    {
        var protectedDirs = group.MapGroup("/protected-directories").WithTags("只读保护");

        protectedDirs.MapPost("/", MarkProtected)
                      .WithName("MarkProtectedDirectory")
                      .WithSummary("标记目录为只读保护")
                      .WithDescription("标记后该目录及其全部子孙在应用层禁止覆盖/删除（网页端与 WebDAV 同时生效）。幂等：重复标记返回现有标记")
                      .RequireAuthorization("RequireAdmin");

        protectedDirs.MapGet("/", ListProtected)
                      .WithName("ListProtectedDirectories")
                      .WithSummary("受保护目录列表")
                      .RequireAuthorization("RequireAdmin");

        protectedDirs.MapDelete("/{fileItemId:guid}", UnmarkProtected)
                      .WithName("UnmarkProtectedDirectory")
                      .WithSummary("取消只读保护")
                      .RequireAuthorization("RequireAdmin");

        return protectedDirs;
    }

    private static async Task<IResult> MarkProtected(
        MarkProtectedRequest request,
        IProtectedDirectoryService protectedDirectoryService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await protectedDirectoryService.MarkAsync(
            request.FileItemId, userId, request.Note, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ListProtected(
        IProtectedDirectoryService protectedDirectoryService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await protectedDirectoryService.ListAsync(cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UnmarkProtected(
        Guid fileItemId,
        IProtectedDirectoryService protectedDirectoryService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await protectedDirectoryService.UnmarkAsync(fileItemId, cancellationToken);
        return result.ToHttpResult();
    }

    private sealed record MarkProtectedRequest(Guid FileItemId, string? Note);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
