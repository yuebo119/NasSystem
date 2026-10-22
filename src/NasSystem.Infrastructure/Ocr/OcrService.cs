// =============================================================================
// NasSystem.Infrastructure - Ocr/OcrService.cs
// OCR 服务实现（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Ocr;

using Microsoft.Extensions.Logging;
using NasSystem.Application.Common;
using NasSystem.Application.Ocr;
using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Files;
using NasSystem.Shared;
using Sdcb.SimdPaddleOCR;
using SkiaSharp;

/// <summary>
/// OCR 服务实现。链路：SkiaSharp 解码（复用缩略图管线已有的解码器，
/// 不引 ImageSharp）→ BGRA8888 交错像素 → SimdPaddleOCR 推理 → 落库
/// </summary>
public sealed class OcrService(
    PaddleOcrEngineHolder engineHolder,
    IFileItemRepository fileItemRepository,
    IFileOcrTextRepository ocrTextRepository,
    IUnitOfWork unitOfWork,
    ILogger<OcrService> logger,
    NasSystem.Domain.Services.IDateTimeProvider dateTime) : IOcrService
{
    /// <summary>可识别的图片扩展名（与缩略图管线的图片集一致，不含视频帧）</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"
    };

    /// <summary>JPEG 等可缩放格式的解码像素预算（24MP 原图会按比例缩小）</summary>
    private const long MaxDecodePixels = 16_000_000;

    /// <summary>不可缩放格式（PNG 实测）的全解上限，超过直接失败而非按原尺寸占内存</summary>
    private const long MaxFallbackPixels = 64_000_000;

    public async Task<Result<OcrTextDto>> RecognizeAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(path);
        var item = await fileItemRepository.GetByPathAsync(normalized, cancellationToken);
        if (item is null || item.OwnerId != userId)
        {
            return Result<OcrTextDto>.Failure(FileErrors.NotFound(normalized));
        }
        if (item.ItemType != FileItemType.File)
        {
            return Result<OcrTextDto>.Failure(FileErrors.IsNotFile(normalized));
        }
        if (!ImageExtensions.Contains(item.Extension ?? string.Empty))
        {
            // Error.Validation 精确命中 400 分支；手捏 "Ocr.NotAnImage"
            // 不含映射表关键词会落默认 500（客户端错误被报成服务端故障）
            return Result<OcrTextDto>.Failure(Error.Validation("仅支持图片文件"));
        }

        var recognized = await RunAndStoreAsync(item, userId, cancellationToken);
        return recognized;
    }

    public async Task<Result<OcrBatchResultDto>> RecognizeDirectoryAsync(
        string path,
        Guid userId,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(path);
        var root = await fileItemRepository.GetByPathAsync(normalized, cancellationToken);
        if (root is null || root.OwnerId != userId)
        {
            return Result<OcrBatchResultDto>.Failure(FileErrors.NotFound(normalized));
        }
        if (root.ItemType != FileItemType.Folder)
        {
            return Result<OcrBatchResultDto>.Failure(FileErrors.IsNotFolder(normalized));
        }

        // 子树条目一次查回（轻量投影，同软删除链路），目录本身排除
        var subtree = await fileItemRepository.GetSubtreeEntriesAsync(normalized, cancellationToken);
        var imagePaths = subtree
            .Where(e => e.ItemType == FileItemType.File
                && ImageExtensions.Contains(System.IO.Path.GetExtension(e.Path)))
            .Select(e => e.Path)
            .ToList();

        var result = new OcrBatchResultDto(imagePaths.Count, 0, 0, 0);
        if (imagePaths.Count == 0)
        {
            return Result<OcrBatchResultDto>.Success(result);
        }

        // force 时全部重识别；否则按已有文本跳过。已有文本按 FileItemId 查，
        // 路径→Id 的映射从子树装载时顺带拿到（GetSubtreeEntries 无 Id，
        // 这里按路径逐条 GetByPathAsync 取实体：目录下图片数是百级，
        // 百次点查询在本地 SQLite 上可忽略，且避免为一次批量引入新投影）
        var toRecognize = new List<NasSystem.Domain.Entities.FileItem>();
        foreach (var imagePath in imagePaths)
        {
            var item = await fileItemRepository.GetByPathAsync(imagePath, cancellationToken);
            if (item is null || item.OwnerId != userId)
            {
                result = result with { Failed = result.Failed + 1 };
                continue;
            }
            if (!force)
            {
                var existing = await ocrTextRepository.GetByFileItemIdAsync(item.Id, cancellationToken);
                if (existing is not null)
                {
                    result = result with { Skipped = result.Skipped + 1 };
                    continue;
                }
            }
            toRecognize.Add(item);
        }

        foreach (var item in toRecognize)
        {
            var outcome = await RunAndStoreAsync(item, userId, cancellationToken);
            result = outcome.IsSuccess
                ? result with { Recognized = result.Recognized + 1 }
                : result with { Failed = result.Failed + 1 };
        }

        return Result<OcrBatchResultDto>.Success(result);
    }

    public async Task<Result<OcrTextDto?>> GetTextAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(path);
        var item = await fileItemRepository.GetByPathAsync(normalized, cancellationToken);
        if (item is null || item.OwnerId != userId)
        {
            return Result<OcrTextDto?>.Failure(FileErrors.NotFound(normalized));
        }

        var ocrText = await ocrTextRepository.GetByFileItemIdAsync(item.Id, cancellationToken);
        if (ocrText is null)
        {
            return Result<OcrTextDto?>.Success(null);
        }

        return Result<OcrTextDto?>.Success(ToDto(item, ocrText.Text, ocrText.Regions,
            ocrText.Model, ocrText.ElapsedMs, ocrText.CreatedAt));
    }

    /// <summary>
    /// 识别并落库（upsert）。失败不抛：调用方（单条/批量）各自决定
    /// 报错还是计 failed 继续
    /// </summary>
    private async Task<Result<OcrTextDto>> RunAndStoreAsync(
        NasSystem.Domain.Entities.FileItem item,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var engine = await engineHolder.GetEngineAsync(cancellationToken);
            var started = System.Diagnostics.Stopwatch.StartNew();

            string text;
            List<NasSystem.Domain.Entities.OcrTextRegion> regions = [];

            // 解码必须在识别闸内：Run 闸只压 2 路推理，原先解码在闸外，
            // N 个并发请求会各自先解一张全图（24MP 约 94MB 原生内存）再排队。
            // 代价是解码与推理串行，吞吐略降；相对 OOM 风险是正确取舍。
            using (var runScope = await engineHolder.EnterRunAsync(cancellationToken))
            using (var bitmap = ImageDecoder.Decode(
                item.PhysicalPath, MaxDecodePixels, MaxFallbackPixels, SKColorType.Bgra8888))
            {
                if (bitmap is null)
                {
                    // 解码失败或超出兜底像素上限，都是输入文件问题，按客户端错误报
                    return Result<OcrTextDto>.Failure(Error.Validation("图片解码失败或尺寸过大"));
                }

                // 直接按 BGRA 解码，省掉"原生格式 + 转换副本"的第二份位图。
                // GetPixelSpan 托管直取像素（span 生命周期与位图绑定），
                // stride 传 RowBytes：位图可能有行对齐填充，不能按 width*4 密排假设
                ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
                var ocrResult = engine.Run(
                    pixels, bitmap.Width, bitmap.Height, bitmap.RowBytes,
                    Sdcb.SimdPaddleOCR.ImagePixelFormat.Bgra32);
                text = ocrResult.Text;
                // 行区域（2026-10-21 OCR 叠加定位）：引擎每行带四角像素坐标，
                // 归一化到 0..1 后随文本落库——前端按显示尺寸比例换算叠回原位
                regions = BuildRegions(ocrResult.Lines, bitmap.Width, bitmap.Height);
            }

            started.Stop();

            // 原子 upsert：并发首次识别同一张图时，先读后写的两方都会走 INSERT，
            // FileItemId 唯一索引让后者 500；ON CONFLICT 把判定交给数据库，
            // 冲突方变成覆盖更新，两个请求都成功
            var ocrText = NasSystem.Domain.Entities.FileOcrText.Create(
                item.Id, text, PaddleOcrEngineHolder.ModelName, (int)started.ElapsedMilliseconds, regions);
            await ocrTextRepository.UpsertAsync(ocrText, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "OCR 完成: FileItemId={FileItemId}, 字符数={Chars}, 行区域={Regions}, 耗时={Ms}ms",
                item.Id, text.Length, regions.Count, started.ElapsedMilliseconds);

            return Result<OcrTextDto>.Success(ToDto(item, text, regions,
                PaddleOcrEngineHolder.ModelName, (int)started.ElapsedMilliseconds, dateTime.UtcNow));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OCR 失败: FileItemId={FileItemId}, Path={Path}", item.Id, item.PhysicalPath);
            return Result<OcrTextDto>.Failure(new Error("Ocr.Failed", $"识别失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 引擎行结果 → 归一化区域。坐标除以图片宽高转 0..1：显示尺寸随布局变化，
    /// 前端按比例换算即用，与图片存储分辨率解耦。越界坐标钳到 0..1
    ///（引擎偶发给出贴边外扩值，不钳会让前端文字层画出容器）
    /// </summary>
    private static List<NasSystem.Domain.Entities.OcrTextRegion> BuildRegions(
        Sdcb.SimdPaddleOCR.PaddleOcrLine[] lines, int width, int height)
    {
        if (lines is null || width <= 0 || height <= 0)
            return [];

        var regions = new List<NasSystem.Domain.Entities.OcrTextRegion>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line.Text))
                continue;
            var box = line.Box;
            var points = new float[8];
            var xs = new[] { box.X1, box.X2, box.X3, box.X4 };
            var ys = new[] { box.Y1, box.Y2, box.Y3, box.Y4 };
            for (var i = 0; i < 4; i++)
            {
                points[i * 2] = Math.Clamp(xs[i] / width, 0f, 1f);
                points[i * 2 + 1] = Math.Clamp(ys[i] / height, 0f, 1f);
            }
            regions.Add(new NasSystem.Domain.Entities.OcrTextRegion(line.Text, points));
        }
        return regions;
    }

    private static OcrTextDto ToDto(
        NasSystem.Domain.Entities.FileItem item,
        string text,
        IReadOnlyList<NasSystem.Domain.Entities.OcrTextRegion> regions,
        string model,
        int elapsedMs,
        DateTime createdAt) =>
        new(item.Id, item.Path, text, text.Length, model, elapsedMs, createdAt,
            regions.Select(r => new OcrTextRegionDto(r.Text, r.Points)).ToList());

    private static string NormalizePath(string path) =>
        NasSystem.Domain.ValueObjects.FilePath.Normalize(path);
}
