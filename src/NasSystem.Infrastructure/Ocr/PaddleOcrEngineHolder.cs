// =============================================================================
// NasSystem.Infrastructure - Ocr/PaddleOcrEngineHolder.cs
// SimdPaddleOCR 引擎持有者（单例）
// =============================================================================

namespace NasSystem.Infrastructure.Ocr;

using Microsoft.Extensions.Logging;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;

/// <summary>
/// PaddleOcrAll 引擎的进程内单例持有者。
/// </summary>
/// <remarks>
/// 为什么是单例：模型从 Assembly 嵌入资源解析，首次加载要一两秒，
/// 每请求新建等于每次识别都付这个成本；引擎本身无状态（可重入），
/// 官方 README 明确多图共享同一实例、由调用端限并发。
/// 为什么懒加载：启动时不阻塞，首次识别才付模型解析成本；
/// 没装模型包或加载失败时启动日志留痕，功能降级为不可用而非进程起不来。
/// 并发闸：Run 内部已按文本行并行，但整图并发仍受 CPU 预算约束，
/// 用信号量把同时在跑的图数压到 2（官方示例同量级）
/// </remarks>
public sealed class PaddleOcrEngineHolder : IDisposable
{
    /// <summary>模型标识。落库在 FileOcrTexts.Model，模型升级后据此判断是否重识别</summary>
    public const string ModelName = "ppocrv6-tiny-zh";

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _runGate = new(2, 2);
    private readonly ILogger<PaddleOcrEngineHolder> _logger;
    private PaddleOcrAll? _engine;
    private bool _disposed;

    public PaddleOcrEngineHolder(ILogger<PaddleOcrEngineHolder> logger)
    {
        _logger = logger;
    }

    /// <summary>引擎是否可用（加载失败过则为 false，不再重复尝试）</summary>
    public bool IsBroken { get; private set; }

    /// <summary>
    /// 取引擎（首次调用时加载）。加载失败抛异常，由调用方决定降级行为
    /// </summary>
    public async Task<PaddleOcrAll> GetEngineAsync(CancellationToken cancellationToken = default)
    {
        if (_engine is not null)
            return _engine;

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_engine is not null)
                return _engine;

            var started = System.Diagnostics.Stopwatch.StartNew();
            // LoadAsync 的第二参数是 PaddleOcrOptions 不是 CancellationToken：
            // 加载过程本身快（嵌入资源解析），不值得为取消Token改同步阻塞，
            // 需要取消的场景由调用方的请求取消在入口拦
            _engine = await PaddleOcrAll.LoadAsync(ChineseV6TinyModels.Default);
            started.Stop();
            _logger.LogInformation(
                "OCR 引擎已加载: 模型={Model}, 耗时={Ms}ms", ModelName, started.ElapsedMilliseconds);
            return _engine;
        }
        catch (Exception ex)
        {
            IsBroken = true;
            _logger.LogError(ex, "OCR 引擎加载失败，识别功能不可用（不影响其他功能）");
            throw;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>进入识别并发闸。出参 mustDispose 为 true 时调用方必须释放返回的 IDisposable</summary>
    public async Task<IDisposable> EnterRunAsync(CancellationToken cancellationToken = default)
    {
        await _runGate.WaitAsync(cancellationToken);
        return new ReleaseHandle(this);
    }

    private void ReleaseRun() => _runGate.Release();

    private sealed class ReleaseHandle(PaddleOcrEngineHolder owner) : IDisposable
    {
        public void Dispose() => owner.ReleaseRun();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine?.Dispose();
        _initGate.Dispose();
        _runGate.Dispose();
    }
}
