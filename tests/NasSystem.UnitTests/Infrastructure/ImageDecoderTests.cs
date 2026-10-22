// =============================================================================
// NasSystem.UnitTests - Infrastructure/ImageDecoderTests.cs
// 解码像素预算：大图必须按编解码器能力缩小，不可缩放格式必须受兜底上限约束
// =============================================================================

namespace NasSystem.UnitTests.Infrastructure;

using FluentAssertions;
using NasSystem.Infrastructure.Files;
using SkiaSharp;
using Xunit;

/// <summary>
/// 锁定 <see cref="ImageDecoder"/> 的三条边界：可缩放格式按预算缩小、
/// 小图不放大、不可缩放格式超过兜底上限时拒绝解码。
/// </summary>
public sealed class ImageDecoderTests : IDisposable
{
    private readonly string _tempDir;

    public ImageDecoderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nas-imagedecoder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Decode_LargeJpeg_ScalesWithinPixelBudget()
    {
        var path = CreateImage("large.jpg", 3000, 2000, SKEncodedImageFormat.Jpeg);

        using var bitmap = ImageDecoder.Decode(path, 1_000_000, 64_000_000);

        bitmap.Should().NotBeNull();
        var pixels = (long)bitmap!.Width * bitmap.Height;
        // JPEG 的分级缩放会量化到 1/2，允许到预算的两倍；原图是 6MP，不能原样返回
        pixels.Should().BeLessThanOrEqualTo(2_000_000).And.BeLessThan(6_000_000);
        bitmap.ByteCount.Should().BeLessThan(8_000_000);
    }

    [Fact]
    public void Decode_SmallImage_KeepsOriginalSize()
    {
        var path = CreateImage("small.png", 320, 240, SKEncodedImageFormat.Png);

        using var bitmap = ImageDecoder.Decode(path, 1_000_000, 64_000_000);

        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(320);
        bitmap.Height.Should().Be(240);
    }

    [Fact]
    public void Decode_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "missing.jpg");

        var bitmap = ImageDecoder.Decode(path, 1_000_000, 64_000_000);

        bitmap.Should().BeNull();
    }

    [Fact]
    public void Decode_ForcedColorType_UsesRequestedFormat()
    {
        var path = CreateImage("color.jpg", 640, 480, SKEncodedImageFormat.Jpeg);

        using var bitmap = ImageDecoder.Decode(path, 1_000_000, 64_000_000, SKColorType.Bgra8888);

        bitmap.Should().NotBeNull();
        bitmap!.ColorType.Should().Be(SKColorType.Bgra8888);
    }

    [Fact]
    public void Decode_NonScalableFormatAboveFallbackLimit_ReturnsNull()
    {
        // PNG 编解码器实测不支持按需缩放；原图 1MP，兜底上限设 0.5MP，
        // 必须拒绝而不是按原尺寸解出 1MP（4MB）。
        var path = CreateImage("too-large.png", 1000, 1000, SKEncodedImageFormat.Png);

        var bitmap = ImageDecoder.Decode(path, 100_000, 500_000);

        bitmap.Should().BeNull();
    }

    [Fact]
    public void Decode_NonScalableFormatWithinFallbackLimit_DecodesOriginal()
    {
        var path = CreateImage("fallback.png", 1000, 1000, SKEncodedImageFormat.Png);

        using var bitmap = ImageDecoder.Decode(path, 100_000, 2_000_000);

        bitmap.Should().NotBeNull();
        bitmap!.Width.Should().Be(1000);
        bitmap.Height.Should().Be(1000);
    }

    [Fact]
    public void ReadDimensions_LargeJpeg_ReturnsHeaderSize()
    {
        var path = CreateImage("header.jpg", 3000, 2000, SKEncodedImageFormat.Jpeg);

        var dimensions = ImageDecoder.ReadDimensions(path);

        dimensions.Should().NotBeNull();
        dimensions!.Value.Width.Should().Be(3000);
        dimensions.Value.Height.Should().Be(2000);
    }

    [Fact]
    public void ReadDimensions_MissingFile_ReturnsNull()
    {
        var dimensions = ImageDecoder.ReadDimensions(Path.Combine(_tempDir, "missing.jpg"));

        dimensions.Should().BeNull();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响断言结果
        }
    }

    private string CreateImage(string name, int width, int height, SKEncodedImageFormat format)
    {
        var path = Path.Combine(_tempDir, name);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90)
            ?? throw new InvalidOperationException("测试图片编码失败");
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
