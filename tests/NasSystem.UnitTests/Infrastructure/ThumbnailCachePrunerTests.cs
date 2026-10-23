// =============================================================================
// NasSystem.UnitTests - Infrastructure/ThumbnailCachePrunerTests.cs
// 缩略图缓存过期清理（F-105）
// =============================================================================

namespace NasSystem.UnitTests.Infrastructure;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NasSystem.Application.Files;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure.Files;
using Xunit;

/// <summary>
/// 锁定 TTL 清理的边界：超过 CacheExpirationHours 的文件删除，
/// 未过期的保留；目录不存在时不抛。
/// </summary>
public sealed class ThumbnailCachePrunerTests : IDisposable
{
    private readonly string _directory;

    public ThumbnailCachePrunerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"thumb-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留给系统清理
        }
    }

    [Fact]
    public void PruneOnce_RemovesExpiredKeepsFresh()
    {
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var expired = Path.Combine(_directory, "expired.jpg");
        var fresh = Path.Combine(_directory, "fresh.jpg");
        File.WriteAllBytes(expired, [1]);
        File.WriteAllBytes(fresh, [2]);
        File.SetLastWriteTimeUtc(expired, now.AddHours(-2));
        File.SetLastWriteTimeUtc(fresh, now.AddMinutes(-10));

        var pruner = CreatePruner(now, expirationHours: 1);

        var removed = pruner.PruneOnce();

        removed.Should().Be(1);
        File.Exists(expired).Should().BeFalse("超过 TTL 的缓存必须删除");
        File.Exists(fresh).Should().BeTrue("未过期的缓存必须保留");
    }

    [Fact]
    public void PruneOnce_MissingDirectory_ReturnsZero()
    {
        var pruner = CreatePruner(
            new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc),
            expirationHours: 1,
            directory: Path.Combine(_directory, "missing"));

        pruner.PruneOnce().Should().Be(0);
    }

    private ThumbnailCachePruner CreatePruner(
        DateTime now,
        int expirationHours,
        string? directory = null) =>
        new(
            Options.Create(new ThumbnailOptions
            {
                CacheDirectory = directory ?? _directory,
                CacheExpirationHours = expirationHours,
            }),
            new FixedTime(now),
            NullLogger<ThumbnailCachePruner>.Instance);

    private sealed class FixedTime(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}