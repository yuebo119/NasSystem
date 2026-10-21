// =============================================================================
// NasSystem.Application - Ocr/IOcrService.cs
// 图片 OCR 服务（ROADMAP Phase 2，SimdPaddleOCR PP-OCRv6）
// =============================================================================

namespace NasSystem.Application.Ocr;

using NasSystem.Shared;

/// <summary>
/// 图片文字识别服务。识别结果落库后可被全文搜索命中，
/// 覆盖扫描件、截图、照片内文字三类"文件名搜不到"的场景
/// </summary>
public interface IOcrService
{
    /// <summary>识别单张图片并落库（重复识别覆盖旧结果）</summary>
    Task<Result<OcrTextDto>> RecognizeAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量识别目录下的图片。已有文本的文件默认跳过（force 可重识别），
    /// 供扫描同步后一键补识别
    /// </summary>
    Task<Result<OcrBatchResultDto>> RecognizeDirectoryAsync(
        string path,
        Guid userId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>取已识别的文本（未识别返回 success + null 数据）</summary>
    Task<Result<OcrTextDto?>> GetTextAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>OCR 文本 DTO。Regions 为按行的位置区域（归一化四角），
/// 与 Text 的行同序；存量识别结果为空列表，前端降级纯文本展示</summary>
public sealed record OcrTextDto(
    Guid FileItemId,
    string Path,
    string Text,
    int CharCount,
    string Model,
    int ElapsedMs,
    DateTime CreatedAt,
    IReadOnlyList<OcrTextRegionDto> Regions);

/// <summary>OCR 行区域 DTO：一行文字 + 归一化四角坐标（8 个 float，0..1）</summary>
public sealed record OcrTextRegionDto(string Text, float[] Points);

/// <summary>批量识别结果</summary>
public sealed record OcrBatchResultDto(
    int Total,
    int Recognized,
    int Skipped,
    int Failed);
