// =============================================================================
// NasSystem.Application - Semantic/ISemanticSearchService.cs
// 语义搜图服务（ROADMAP Phase 2，探针结论见 ADR-0003）
// =============================================================================

namespace NasSystem.Application.Semantic;

using NasSystem.Shared;

/// <summary>
/// 语义搜图：CLIP 双塔编码（图→向量、文→向量）把图片与自然语言查询
/// 映射到同一向量空间，sqlite-vec 做 KNN。模型四件套不入库（约 606MB），
/// 首次启用经设置页下载到数据目录；模型未就位时全部能力诚实降级
/// （状态可查、搜索返回明确错误），不做假 UI。
/// </summary>
/// <remarks>
/// 连接形态：向量索引与业务表同库文件但独立连接——vec0 是 loadable
/// 扩展，PalORM 的 DataSession 不暴露扩展加载入口（其 API 无
/// load_extension），故语义域自持 SqliteConnection（打开时加载 vec0），
/// 检索两步走：vec0 KNN 取 FileItemId 集合，再经业务仓储查详情。
/// </remarks>
public interface ISemanticSearchService
{
    /// <summary>模型状态：四件套是否就位、各文件大小、向量索引规模</summary>
    Task<SemanticModelStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>下载模型四件套（幂等：已就位直接返回）。progress 报 0-1</summary>
    Task<Result> DownloadModelAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>建索引进度（已索引/应索引总数）</summary>
    Task<SemanticIndexProgress> GetProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 库内全部图片的轻量清单（供后台索引用）。单独方法而不复用
    /// 媒体库的分页浏览查询：建索引要全量无序遍历
    /// </summary>
    Task<IReadOnlyList<NasSystem.Domain.Repositories.FileItemPathEntry>> ListImageItemsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 语义搜图。模型未就位返回明确失败（调用方展示"模型未下载"），
    /// 不返回空列表假装搜过
    /// </summary>
    Task<Result<IReadOnlyList<SemanticHit>>> SearchAsync(
        string query, int topK = 50, CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>模型文件就位状态</summary>
public sealed record SemanticModelFile(string Name, bool Present, long SizeBytes);

/// <summary>模型状态</summary>
public sealed record SemanticModelStatus(
    bool Ready,
    IReadOnlyList<SemanticModelFile> Files,
    string? LastError);

/// <summary>建索引进度</summary>
public sealed record SemanticIndexProgress(
    int TotalImages,
    int IndexedImages,
    bool IsIndexing,
    string? LastError);

/// <summary>语义命中：FileItemId + 余弦距离（越小越相似）</summary>
public sealed record SemanticHit(Guid FileItemId, double Distance);
