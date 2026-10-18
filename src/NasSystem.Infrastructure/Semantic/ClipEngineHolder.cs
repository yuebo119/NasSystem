// =============================================================================
// NasSystem.Infrastructure - Semantic/ClipEngineHolder.cs
// CLIP 双塔编码引擎持有者（单例）
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using ElBruno.LocalEmbeddings.ImageEmbeddings;
using Microsoft.Extensions.Logging;

/// <summary>
/// CLIP 编码引擎单例。与 PaddleOcrEngineHolder 同款取舍：模型加载是
/// 秒级操作（视觉图优化 889ms + 文本 473ms，探针实测），每请求新建
/// 等于每次识别都付这个成本；编码器无状态可重入（ElBruno 的
/// ClipImageEncoder/ClipTextEncoder 内部 ONNX Session 线程安全），
/// 并发由调用方（后台索引的并发闸）控制。
/// </summary>
public sealed class ClipEngineHolder : IDisposable
{
    private readonly ILogger<ClipEngineHolder> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ClipImageEncoder? _imageEncoder;
    private ClipTextEncoder? _textEncoder;
    private bool _disposed;

    public ClipEngineHolder(ILogger<ClipEngineHolder> logger)
    {
        _logger = logger;
    }

    /// <summary>模型是否已加载</summary>
    public bool IsLoaded => _imageEncoder is not null && _textEncoder is not null;

    /// <summary>
    /// 惰性加载双塔。模型目录由调用方保证就位（四件套），
    /// 加载失败抛异常由上层转"模型不可用"语义
    /// </summary>
    public async Task<(ClipImageEncoder Image, ClipTextEncoder Text)> GetEnginesAsync(
        string modelDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_imageEncoder is not null && _textEncoder is not null)
            return (_imageEncoder, _textEncoder);

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_imageEncoder is not null && _textEncoder is not null)
                return (_imageEncoder, _textEncoder);

            var visionPath = Path.Combine(modelDirectory, "vision_model.onnx");
            var textPath = Path.Combine(modelDirectory, "text_model.onnx");
            var vocabPath = Path.Combine(modelDirectory, "vocab.json");
            var mergesPath = Path.Combine(modelDirectory, "merges.txt");

            _logger.LogInformation("CLIP 引擎首次加载: {Directory}", modelDirectory);
            _imageEncoder = new ClipImageEncoder(visionPath);
            _textEncoder = new ClipTextEncoder(textPath, vocabPath, mergesPath);
            return (_imageEncoder, _textEncoder);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _imageEncoder?.Dispose();
        _textEncoder?.Dispose();
        _initLock.Dispose();
    }
}
