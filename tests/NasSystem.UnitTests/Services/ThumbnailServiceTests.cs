// =============================================================================
// NasSystem.UnitTests - Services/ThumbnailServiceTests.cs
// 缩略图源文件不可解码时的错误语义（客户端输入问题映射 400）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NasSystem.Application.Files;
using NasSystem.Application.Media;
using NasSystem.Infrastructure.Files;
using NasSystem.Infrastructure.Services;
using Xunit;

/// <summary>
/// 锁定"源文件损坏/超出像素上限"与"服务端生成异常"的错误码分叉：
/// 前者是客户端输入问题，必须回 DecodeFailed（400），不能落 GenerationFailed（500）。
/// </summary>
public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ThumbnailServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"thumb-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_UndecodableSource_ReturnsDecodeFailed()
    {
        var options = new ThumbnailOptions { CacheDirectory = Path.Combine(_tempDir, "cache") };
        var service = new ThumbnailService(
            Options.Create(options),
            NullLogger<ThumbnailService>.Instance,
            new Mock<ITranscodeService>().Object,
            new SystemDateTimeProvider());

        var source = Path.Combine(_tempDir, "broken.jpg");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5]);

        var result = await service.GetThumbnailAsync("/broken.jpg", source, 128, 128, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Thumbnail.DecodeFailed",
            "源文件不可解码属客户端输入问题，端点应映射 400 而不是 500");
    }
}