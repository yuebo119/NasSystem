// =============================================================================
// NasSystem.Infrastructure - Files/ImageDecoder.cs
// 带像素预算的图片解码
// =============================================================================

namespace NasSystem.Infrastructure.Files;

using SkiaSharp;

/// <summary>
/// 图片解码入口：超过像素预算时请求编解码器按比例缩小，避免"先解全图再缩放"
/// 的双份原生内存。
/// </summary>
/// <remarks>
/// <para>
/// 为什么必须限制像素：SkiaSharp 的像素缓冲是原生内存，不受 GC 管理。
/// 2026-10-22 第 1 轮审计实测：6000x4000 JPEG 全解码进程工作集 +94.1MB
/// （BGRA 96MB），按 4MP 预算解码只 +15.3MB。缩略图与 OCR 都是先解码，
/// 调用侧又允许并发，解码前的并发闸挡不住这块峰值。
/// </para>
/// <para>
/// 编解码器能力差异（实测）：JPEG 走 DCT 分级缩放，<see cref="SKCodec.GetScaledDimensions"/>
/// 能给出小尺寸；PNG 返回原尺寸，<see cref="SKBitmap.Decode(SKCodec, SKImageInfo)"/>
/// 也确实按原尺寸解。因此对"不可缩放"格式不能假装有预算，只能用
/// <paramref name="maxFallbackPixels"/> 做拒解码硬上限，超限返回 null 让调用方
/// 明确失败，避免一张异常大图直接吃光进程内存。
/// </para>
/// <para>
/// 缩放请求可能被编解码器量化（实测 4MP 请求得到 3.375MP，16MP 请求得到
/// 18.375MP），所以判定按"实际请求尺寸的像素数"做，并留一倍余量。
/// </para>
/// </remarks>
internal static class ImageDecoder
{
    /// <summary>任何格式都不允许超过的源图像素上限（防御构造超宽高头的恶意文件）</summary>
    private const long AbsoluteSourcePixelLimit = 1_000_000_000;

    /// <summary>
    /// 按像素预算解码文件。<paramref name="maxPixels"/> 是希望的解码结果上限；
    /// <paramref name="maxFallbackPixels"/> 仅用于编解码器不支持缩放时的全解兜底，
    /// 超过它直接返回 null。原图小于预算时保持原尺寸，不放大。
    /// </summary>
    public static SKBitmap? Decode(
        string path,
        long maxPixels,
        long maxFallbackPixels,
        SKColorType? colorType = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFallbackPixels);

        using var codec = SKCodec.Create(path);
        return codec is null ? null : Decode(codec, maxPixels, maxFallbackPixels, colorType);
    }

    /// <summary>
    /// 按像素预算解码内存中的图片字节。视频抽帧走这条：ffmpeg 落的是原始
    /// 分辨率 PNG，不限制同样会按视频分辨率占原生内存。
    /// </summary>
    public static SKBitmap? Decode(
        ReadOnlySpan<byte> data,
        long maxPixels,
        long maxFallbackPixels,
        SKColorType? colorType = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFallbackPixels);
        if (data.IsEmpty) return null;

        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded);
        return codec is null ? null : Decode(codec, maxPixels, maxFallbackPixels, colorType);
    }

    /// <summary>
    /// 只读图片头取尺寸，不解码像素。媒体元数据只需要宽高，
    /// 为两个数字全图解码一张 24MP 图会分配约 94MB 原生内存（实测）。
    /// </summary>
    public static (int Width, int Height)? ReadDimensions(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null) return null;

        var info = codec.Info;
        return info.Width > 0 && info.Height > 0 ? (info.Width, info.Height) : null;
    }

    private static SKBitmap? Decode(
        SKCodec codec,
        long maxPixels,
        long maxFallbackPixels,
        SKColorType? colorType)
    {
        var info = codec.Info;
        var pixels = (long)info.Width * info.Height;
        if (pixels <= 0 || pixels > AbsoluteSourcePixelLimit)
            return null;

        var scale = pixels > maxPixels ? MathF.Sqrt((float)maxPixels / pixels) : 1f;
        var size = scale >= 1f
            ? new SKSizeI(info.Width, info.Height)
            : codec.GetScaledDimensions(scale);

        // 编解码器不支持缩放（PNG 实测返回原尺寸）时，只有原图在兜底上限内
        // 才允许全解；否则拒绝，避免异常大图在解码阶段就把进程内存打满。
        if ((long)size.Width * size.Height > maxPixels * 2)
        {
            if (pixels > maxFallbackPixels)
                return null;
            size = new SKSizeI(info.Width, info.Height);
        }

        var target = new SKImageInfo(
            Math.Max(1, size.Width),
            Math.Max(1, size.Height),
            colorType ?? info.ColorType,
            info.AlphaType);
        return SKBitmap.Decode(codec, target);
    }
}
