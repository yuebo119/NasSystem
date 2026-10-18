// =============================================================================
// NasSystem.Api - Endpoints/SnapshotEndpoints.cs
// 目录清单快照 API 端点（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Snapshots;

/// <summary>
/// 清单快照端点。创建是同步采集（库内查询 + 批量写，毫秒到秒级），
/// 不做异步任务队列：快照的是库内目录树而非物理文件，没有慢 IO。
/// </summary>
public static class SnapshotEndpoints
{
    public static RouteGroupBuilder MapSnapshotEndpoints(this RouteGroupBuilder group)
    {
        var snapshots = group.MapGroup("/snapshots").WithTags("清单快照");

        snapshots.MapPost("/", CreateSnapshot)
                 .WithName("CreateSnapshot")
                 .WithSummary("创建目录快照")
                 .WithDescription("记录指定目录子树在当前的形态（路径/大小/hash/时间），供时间点对比。采集库内目录树，不做物理扫描")
                 .RequireAuthorization("RequireAdmin");

        snapshots.MapGet("/", GetSnapshots)
                 .WithName("GetSnapshots")
                 .WithSummary("快照列表")
                 .WithDescription("按创建时间倒序分页")
                 .RequireAuthorization("RequireAdmin");

        snapshots.MapGet("/{id:guid}", GetSnapshot)
                 .WithName("GetSnapshot")
                 .WithSummary("快照详情")
                 .RequireAuthorization("RequireAdmin");

        snapshots.MapGet("/{id:guid}/entries", GetSnapshotEntries)
                 .WithName("GetSnapshotEntries")
                 .WithSummary("快照条目")
                 .WithDescription("按相对路径排序分页")
                 .RequireAuthorization("RequireAdmin");

        snapshots.MapGet("/diff", GetSnapshotDiff)
                 .WithName("GetSnapshotDiff")
                 .WithSummary("对比两个快照")
                 .WithDescription("base=旧快照，target=新快照。四态分类：新增/删除/修改/未变（hash 或大小不同才算修改）")
                 .RequireAuthorization("RequireAdmin");

        snapshots.MapDelete("/{id:guid}", DeleteSnapshot)
                 .WithName("DeleteSnapshot")
                 .WithSummary("删除快照")
                 .RequireAuthorization("RequireAdmin");

        return snapshots;
    }

    private static async Task<IResult> CreateSnapshot(
        CreateSnapshotRequest request,
        ISnapshotService snapshotService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await snapshotService.CreateAsync(
            request.RootPath, request.Name, userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSnapshots(
        int page = 1,
        int pageSize = 50,
        ISnapshotService snapshotService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await snapshotService.GetListAsync(page, pageSize, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSnapshot(
        Guid id,
        ISnapshotService snapshotService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await snapshotService.GetAsync(id, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSnapshotEntries(
        Guid id,
        int page = 1,
        int pageSize = 100,
        ISnapshotService snapshotService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await snapshotService.GetEntriesAsync(id, page, pageSize, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSnapshotDiff(
        Guid baseSnapshotId,
        Guid targetSnapshotId,
        int page = 1,
        int pageSize = 100,
        ISnapshotService snapshotService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await snapshotService.DiffAsync(
            baseSnapshotId, targetSnapshotId, page, pageSize, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteSnapshot(
        Guid id,
        ISnapshotService snapshotService = null!,
        CancellationToken cancellationToken = default)
    {
        var result = await snapshotService.DeleteAsync(id, cancellationToken);
        return result.ToHttpResult();
    }

    private sealed record CreateSnapshotRequest(string RootPath, string? Name);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
