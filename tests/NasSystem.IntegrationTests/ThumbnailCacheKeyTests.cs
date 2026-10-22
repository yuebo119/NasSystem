// =============================================================================
// NasSystem.IntegrationTests - ThumbnailCacheKeyTests.cs
// 缩略图缓存键与清除入口的口径一致性（2026-10-21 全量审计 P2 回归）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NasSystem.Application.Files;
using NasSystem.Application.Media;
using NasSystem.Infrastructure.Files;
using NasSystem.Shared;
using Xunit;

/// <summary>
/// 锁 2026-10-21 审计发现的 P2：缩略图生成侧曾用物理路径建缓存键，而三个
/// 清除入口（删除领域事件、回收站子树清理、端点路由参数）持有的全是虚拟
/// 路径，Directory.GetFiles 的前缀恒匹配空集合——方法真实执行了枚举与
/// 删除逻辑但一条都删不掉，端点仍回 200"已清除"，缓存只进不出。
/// 修复为键统一用虚拟路径（稳定身份），物理路径只用于读源文件。
/// 服务级测试而非 HTTP 级：临时缓存目录让"删没删掉"可精确计数，
/// 不受同工厂其他用例往共享缓存目录里落的文件干扰
/// </summary>
public class ThumbnailCacheKeyTests
{
    [Fact]
    public async Task ClearCache_WithVirtualPath_RemovesCachedThumbnail()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"thumb-cache-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(Path.GetTempPath(), $"thumb-src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(sourceDir);
        try
        {
            var physicalFile = Path.Combine(sourceDir, "probe.png");
            await File.WriteAllBytesAsync(physicalFile, CreateTinyPng());
            var virtualPath = $"/probe-{Guid.NewGuid():N}.png";

            var service = new ThumbnailService(
                Options.Create(new ThumbnailOptions { CacheDirectory = cacheDir }),
                NullLogger<ThumbnailService>.Instance,
                new NoOpTranscodeService(),
                new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

            var generated = await service.GetThumbnailAsync(
                virtualPath, physicalFile, 128, 128, Guid.NewGuid());

            generated.IsSuccess.Should().BeTrue("临时目录里的有效 PNG 应能生成缩略图");
            Directory.GetFiles(cacheDir, "*.jpg").Should().HaveCount(1, "生成后应落一份缓存文件");

            // 清除入口传虚拟路径（与删除事件/回收站/端点同口径）
            await service.ClearCacheAsync(virtualPath);

            Directory.GetFiles(cacheDir, "*.jpg").Should().BeEmpty(
                "按虚拟路径清除必须真的删掉缓存文件；修复前键用物理路径，前缀恒不匹配");
        }
        finally
        {
            TryDelete(cacheDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public async Task ClearCache_OnlyRemovesTargetFile_CacheKeyedByVirtualPath()
    {
        // 两个文件的物理路径同目录不同名、虚拟路径也不同：清一个不能清另一个。
        // 这条同时锁"键确实随文件身份变化"，防修复退化成删全库
        var cacheDir = Path.Combine(Path.GetTempPath(), $"thumb-cache2-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(Path.GetTempPath(), $"thumb-src2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(sourceDir);
        try
        {
            var png = CreateTinyPng();
            var fileA = Path.Combine(sourceDir, "a.png");
            var fileB = Path.Combine(sourceDir, "b.png");
            await File.WriteAllBytesAsync(fileA, png);
            await File.WriteAllBytesAsync(fileB, png);

            var service = new ThumbnailService(
                Options.Create(new ThumbnailOptions { CacheDirectory = cacheDir }),
                NullLogger<ThumbnailService>.Instance,
                new NoOpTranscodeService(),
                new NasSystem.Infrastructure.Services.SystemDateTimeProvider());

            await service.GetThumbnailAsync("/a.png", fileA, 128, 128, Guid.NewGuid());
            await service.GetThumbnailAsync("/b.png", fileB, 128, 128, Guid.NewGuid());
            Directory.GetFiles(cacheDir, "*.jpg").Should().HaveCount(2);

            await service.ClearCacheAsync("/a.png");

            Directory.GetFiles(cacheDir, "*.jpg").Should().HaveCount(1, "只应清掉 a 的缓存");
        }
        finally
        {
            TryDelete(cacheDir);
            TryDelete(sourceDir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* 临时目录，留给系统清理 */ }
    }

    /// <summary>SkiaSharp 生成 4x4 有效 PNG（手写字节数组易在 CRC/zlib 上出错）</summary>
    private static byte[] CreateTinyPng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 4);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>图片路径不触达转码，成员全部诚实不可用即可</summary>
    private sealed class NoOpTranscodeService : ITranscodeService
    {
        public bool IsAvailable => false;

        public Task<Result<HlsPlaylist>> GetOrCreateHlsAsync(
            string physicalPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("图片缩略图不走转码");

        public Task<Result<TranscodeStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("图片缩略图不走转码");

        public Task<long> ClearCacheAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public Task<byte[]?> ExtractVideoFrameAsync(
            string physicalPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }
}
