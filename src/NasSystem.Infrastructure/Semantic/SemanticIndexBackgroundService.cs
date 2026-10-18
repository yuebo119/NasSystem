// =============================================================================
// NasSystem.Infrastructure - Semantic/SemanticIndexBackgroundService.cs
// 语义索引后台建库服务（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Configuration;

/// <summary>
/// 语义索引增量建库：模型就位 + semantic.enabled 打开时，周期性扫描
/// 库内图片，缺向量的逐张 CLIP 编码写入 vec0。
/// </summary>
/// <remarks>
/// 容量口径（ADR-0003 探针）：单张 58ms 中位，10 万张串行外推约
/// 97 分钟——这是必须后台而非同步的原因。断点续建天然成立：
/// vec0 表本身就是进度账本（已写入的不会重写），重启后从缺失集继续。
/// 并发闸 1：CLIP 编码是 CPU 密集（ONNX 内部已多线程），再并发只会
/// 抢核；与 OCR 引擎的 EnterRun 同款取舍。
/// </remarks>
public sealed class SemanticIndexBackgroundService : BackgroundService
{
    /// <summary>索引进行中标志（GetProgressAsync 展示用）</summary>
    internal static bool IsIndexing;

    /// <summary>扫描周期：无待办时 5 分钟一轮（新上传的图最多 5 分钟后进索引）</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SemanticIndexBackgroundService> _logger;

    public SemanticIndexBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SemanticIndexBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("语义索引服务已启动（模型就位且 semantic.enabled 后开始建库）");

        // 启动等 30 秒再首轮：给模型加载与迁移留时间，避免启动风暴
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunIndexPassAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单轮失败降级：记日志等下一轮，不杀宿主
                _logger.LogError(ex, "语义索引轮次异常（已降级，等待下一轮）");
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("语义索引服务已停止");
    }

    private async Task RunIndexPassAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();
        if (!await config.GetBoolAsync(ConfigDescriptors.Keys.SemanticEnabled, cancellationToken))
            return;

        var options = scope.ServiceProvider.GetRequiredService<SemanticOptions>();
        if (!ClipModelDownloader.IsReady(options.ModelDirectory))
            return;

        var store = scope.ServiceProvider.GetRequiredService<SemanticIndexStore>();
        if (!store.EnsureCreated())
            return;

        var engineHolder = scope.ServiceProvider.GetRequiredService<ClipEngineHolder>();
        var service = scope.ServiceProvider.GetRequiredService<Application.Semantic.ISemanticSearchService>();

        var images = await service.ListImageItemsAsync(cancellationToken);
        var indexed = await LoadIndexedIdsAsync(store, cancellationToken);

        var pending = images.Where(i => !indexed.Contains(i.Id.ToString("D"))).ToList();
        if (pending.Count == 0)
            return;

        IsIndexing = true;
        _logger.LogInformation("语义索引开始: 待编码 {Count} 张", pending.Count);
        try
        {
            var (imageEncoder, _) = await engineHolder.GetEnginesAsync(options.ModelDirectory, cancellationToken);
            var done = 0;
            foreach (var image in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(image.PhysicalPath))
                        continue; // 物理缺失（扫描未对齐）跳过，不阻断整轮
                    var embedding = imageEncoder.Encode(image.PhysicalPath);
                    await store.UpsertAsync(image.Id, embedding, cancellationToken);
                    done++;
                    if (done % 50 == 0)
                        _logger.LogInformation("语义索引进度: {Done}/{Total}", done, pending.Count);
                }
                catch (Exception ex)
                {
                    // 单张失败（解码不了/格式特殊）跳过：索引是增强，
                    // 一张坏图不该停下整个建库
                    _logger.LogWarning(ex, "语义索引单张失败（跳过）: {Path}", image.Path);
                }
            }
            _logger.LogInformation("语义索引完成: 本册编码 {Done} 张", done);
        }
        finally
        {
            IsIndexing = false;
        }
    }

    /// <summary>vec0 表已索引的 FileItemId 集合（大写 D 形态，与写入口径一致）</summary>
    private static async Task<HashSet<string>> LoadIndexedIdsAsync(
        SemanticIndexStore store, CancellationToken cancellationToken)
    {
        var ids = await store.ListIndexedIdsAsync(cancellationToken);
        return ids.ToHashSet(StringComparer.Ordinal);
    }
}
