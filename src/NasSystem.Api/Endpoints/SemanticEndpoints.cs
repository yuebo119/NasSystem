// =============================================================================
// NasSystem.Api - Endpoints/SemanticEndpoints.cs
// 语义搜图 API 端点（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Configuration;
using NasSystem.Application.Semantic;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 语义搜图端点：模型状态/下载、索引进度、语义检索、手动触发建索引。
/// 模型未就位时搜索返回明确错误（Semantic.ModelNotReady → 404 语义由
/// 调用方展示"请先下载模型"），不返回空列表假装搜过。
/// </summary>
public static class SemanticEndpoints
{
    public static RouteGroupBuilder MapSemanticEndpoints(this RouteGroupBuilder group)
    {
        var semantic = group.MapGroup("/semantic").WithTags("语义搜图");

        semantic.MapGet("/status", GetStatus)
                .WithName("GetSemanticStatus")
                .WithSummary("语义模型与索引状态")
                .WithDescription("四件套就位情况、向量索引规模（已索引/总数）")
                .RequireAuthorization("RequireAdmin");

        semantic.MapPost("/model/download", DownloadModel)
                .WithName("DownloadSemanticModel")
                .WithSummary("下载 CLIP 模型四件套")
                .WithDescription("约 606MB，从 hf-mirror 下载到数据目录。幂等：已就位直接返回")
                .RequireAuthorization("RequireAdmin");

        semantic.MapPost("/index/trigger", TriggerIndex)
                .WithName("TriggerSemanticIndex")
                .WithSummary("手动触发一轮语义索引")
                .WithDescription("唤醒后台建库服务立即执行一轮增量编码；auto-index 关闭时的手动入口。功能未启用或模型未就位返回明确错误，否则 202 由后台异步执行")
                .RequireAuthorization("RequireAdmin");

        semantic.MapPost("/search", Search)
                .WithName("SemanticSearch")
                .WithSummary("语义搜图")
                .WithDescription("自然语言查询图片（CLIP 图文向量 + sqlite-vec KNN）。返回 FileItemId 与距离，详情由媒体库列表按 Id 呈现")
                .RequireAuthorization();

        return semantic;
    }

    private static async Task<IResult> GetStatus(
        ISemanticSearchService semanticService,
        IFileItemRepository fileRepository,
        CancellationToken cancellationToken)
    {
        var status = await semanticService.GetStatusAsync(cancellationToken);
        var progress = await semanticService.GetProgressAsync(cancellationToken);
        // ApiResponse 包装不是可选装饰：前端统一按 success/data 判定，
        // 裸对象会让整个语义卡（模型四件套/索引进度/手动触发）静默不渲染
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            model = new
            {
                ready = status.Ready,
                files = status.Files.Select(f => new { name = f.Name, present = f.Present, sizeBytes = f.SizeBytes }),
            },
            index_ = new
            {
                totalImages = progress.TotalImages,
                indexedImages = progress.IndexedImages,
                isIndexing = progress.IsIndexing,
                error = progress.LastError,
            },
        }));
    }

    private static async Task<IResult> DownloadModel(
        ISemanticSearchService semanticService,
        CancellationToken cancellationToken)
    {
        // 进度不上送到 HTTP 响应（流式进度需要 SSE，超出本端点定位）；
        // 下载是幂等慢动作，前端轮询 /status 观察文件就位即可
        var result = await semanticService.DownloadModelAsync(null, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// 手动触发一轮索引：enabled + 模型就位才 202（否则明确错误），
    /// 实际编码交给后台服务——单轮是分钟级容量动作，HTTP 同步跑必超时。
    /// 前端经 /status 轮询观察索引进度（isIndexing 与已索引数）
    /// </summary>
    private static async Task<IResult> TriggerIndex(
        ISemanticIndexTrigger indexTrigger,
        ISemanticSearchService semanticService,
        ISystemConfigService configService,
        CancellationToken cancellationToken)
    {
        if (!await configService.GetBoolAsync(ConfigDescriptors.Keys.SemanticEnabled, cancellationToken))
        {
            return Result.Failure(
                new Error("Semantic.Disabled", "语义搜图未启用，请在系统配置中先开启")).ToHttpResult();
        }

        var status = await semanticService.GetStatusAsync(cancellationToken);
        if (!status.Ready)
        {
            return Result.Failure(
                new Error("Semantic.ModelNotReady", "语义模型未下载，请在设置页先下载模型")).ToHttpResult();
        }

        indexTrigger.RequestRun();
        return Results.Accepted("/api/v1/semantic/status", ApiResponse<object>.Ok(new
        {
            Message = "已触发一轮语义索引，后台执行中，进度见状态查询"
        }));
    }

    private static async Task<IResult> Search(
        SemanticSearchRequest request,
        ISemanticSearchService semanticService,
        IFileItemRepository fileRepository,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await semanticService.SearchAsync(request.Query, request.TopK, cancellationToken);
        if (result.IsFailure)
            return result.ToHttpResult();

        // 融合：KNN 命中的 Id → 媒体库图片详情（保持距离排序）。
        // 过滤非本人/不存在的条目（语义索引可能先于删除同步）。
        // 批量取详情：曾经的逐条 GetByIdAsync 在 topK=200 时是 200 次
        // 单行查询（2026-10-19 数据库层审计 P2）
        var hits = new List<object>();
        var hitIds = result.Value.Select(h => h.FileItemId).Distinct().ToList();
        var items = await fileRepository.GetByIdsAsync(hitIds, cancellationToken);
        var byId = items.ToDictionary(i => i.Id);
        foreach (var hit in result.Value)
        {
            if (!byId.TryGetValue(hit.FileItemId, out var item))
                continue;
            if (item.OwnerId != userId || item.IsDeleted)
                continue;
            hits.Add(new
            {
                id = item.Id,
                name = item.Name,
                path = item.Path,
                sizeBytes = item.SizeBytes,
                distance = hit.Distance,
            });
        }
        // 与 /status 同缘由包 ApiResponse：前端按 success/data 统一判定，
        // 裸对象会让搜索静默无结果（媒体库看不到任何反馈）
        return Results.Ok(ApiResponse<object>.Ok(new { items = hits }));
    }

    private sealed record SemanticSearchRequest(string Query, int TopK = 50);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
