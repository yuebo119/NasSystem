// =============================================================================
// NasSystem.Infrastructure - Semantic/SemanticSearchService.cs
// 语义搜图服务实现（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using Microsoft.Extensions.Logging;
using NasSystem.Application.Semantic;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 语义搜图编排：模型状态/下载、建索引进度、KNN 检索融合。
/// </summary>
/// <remarks>
/// 建索引不在本服务的同步路径里：由 <see cref="SemanticIndexBackgroundService"/>
/// 增量消费（模型就位 + 开关打开时扫描缺向量图片逐张编码）。
/// 本服务的 EnqueueIndexBuildAsync 只是把"待办"标记为脏，
/// 真正编码在后台——上传即时编码会让大图上传等 60ms 推理，
/// 与 OCR 自动识别的取舍不同：那个是配置开关选同步，
/// 这个是容量动作（10 万张外推 97 分钟，探针口径）必须后台。
/// </remarks>
public sealed class SemanticSearchService : ISemanticSearchService
{
    /// <summary>图片扩展名（与媒体库图片口径一致）</summary>
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"];

    private readonly SemanticIndexStore _indexStore;
    private readonly ClipEngineHolder _engineHolder;
    private readonly ClipModelDownloader _downloader;
    private readonly SemanticOptions _options;
    private readonly IFileItemRepository _fileItems;
    private readonly ILogger<SemanticSearchService> _logger;

    public SemanticSearchService(
        SemanticIndexStore indexStore,
        ClipEngineHolder engineHolder,
        ClipModelDownloader downloader,
        SemanticOptions options,
        IFileItemRepository fileItems,
        ILogger<SemanticSearchService> logger)
    {
        _indexStore = indexStore;
        _engineHolder = engineHolder;
        _downloader = downloader;
        _options = options;
        _fileItems = fileItems;
        _logger = logger;
    }

    public Task<SemanticModelStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var files = ClipModelDownloader.RequiredFiles.Select(f =>
        {
            var path = Path.Combine(_options.ModelDirectory, f);
            var present = File.Exists(path) && new FileInfo(path).Length > 0;
            return new SemanticModelFile(f, present, present ? new FileInfo(path).Length : 0);
        }).ToList();
        var ready = files.All(f => f.Present);
        return Task.FromResult(new SemanticModelStatus(ready, files, null));
    }

    public async Task<Result> DownloadModelAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _downloader.DownloadAsync(
                _options.ModelDirectory, _options.ModelMirror, progress, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "CLIP 模型下载失败");
            return Result.Failure(new Error("Semantic.ModelDownloadFailed", $"模型下载失败: {ex.Message}"));
        }
    }

    public async Task<SemanticIndexProgress> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        var modelReady = ClipModelDownloader.IsReady(_options.ModelDirectory);
        if (!modelReady)
            return new SemanticIndexProgress(0, 0, false, "模型未下载");

        var vecReady = _indexStore.EnsureCreated();
        if (!vecReady)
            return new SemanticIndexProgress(0, 0, false, "向量索引不可用（sqlite-vec 扩展缺失）");

        var indexed = _indexStore.CountIndexed();
        var total = await CountImageItemsAsync(cancellationToken);
        return new SemanticIndexProgress(total, indexed, SemanticIndexBackgroundService.IsIndexing, null);
    }

    public async Task<Result<IReadOnlyList<SemanticHit>>> SearchAsync(
        string query, int topK = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result<IReadOnlyList<SemanticHit>>.Failure(Error.Validation("查询词不能为空"));

        if (!ClipModelDownloader.IsReady(_options.ModelDirectory))
            return Result<IReadOnlyList<SemanticHit>>.Failure(
                new Error("Semantic.ModelNotReady", "语义模型未下载，请在设置页先下载模型"));

        if (!_indexStore.EnsureCreated())
            return Result<IReadOnlyList<SemanticHit>>.Failure(
                new Error("Semantic.IndexUnavailable", "向量索引不可用（sqlite-vec 扩展缺失）"));

        try
        {
            var (_, textEncoder) = await _engineHolder.GetEnginesAsync(_options.ModelDirectory, cancellationToken);
            var queryVector = textEncoder.Encode(query);
            var hits = await _indexStore.KnnSearchAsync(queryVector, Math.Clamp(topK, 1, 200), cancellationToken);
            return Result<IReadOnlyList<SemanticHit>>.Success(
                hits.Select(h => new SemanticHit(h.FileItemId, h.Distance)).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "语义搜图失败: {Query}", query);
            return Result<IReadOnlyList<SemanticHit>>.Failure(
                new Error("Semantic.SearchFailed", $"语义检索失败: {ex.Message}"));
        }
    }

    /// <summary>库内图片总数（语义索引的目标全集）</summary>
    private async Task<int> CountImageItemsAsync(CancellationToken cancellationToken)
    {
        // 复用媒体库的扩展名口径，逐扩展名计数求和（一次 GROUP BY 更优，
        // 但仓储无现成方法；图片量级下逐扩展名 COUNT 走索引可接受）
        var total = 0;
        foreach (var extension in ImageExtensions)
        {
            var count = await _fileItems.CountByExtensionAsync(extension, cancellationToken);
            total += count;
        }
        return total;
    }

    /// <summary>
    /// 供后台索引用：库内全部图片的轻量清单（路径 + Id）。
    /// 单独方法而不复用 GetMediaAsync：那是有分页与排序的浏览语义，
    /// 建索引要全量无序遍历
    /// </summary>
    public async Task<IReadOnlyList<NasSystem.Domain.Repositories.FileItemPathEntry>> ListImageItemsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<NasSystem.Domain.Repositories.FileItemPathEntry>();
        foreach (var extension in ImageExtensions)
        {
            var items = await _fileItems.GetPathsByExtensionAsync(extension, cancellationToken);
            result.AddRange(items);
        }
        return result;
    }
}

/// <summary>语义搜图的运行期配置（appsettings 绑定，启动期定死的基础设施参数）</summary>
public sealed class SemanticOptions
{
    /// <summary>模型目录（四件套落点）</summary>
    public string ModelDirectory { get; set; } = "models/clip";

    /// <summary>模型下载镜像（默认 hf-mirror，国内可达）</summary>
    public string ModelMirror { get; set; } = "https://hf-mirror.com";
}
