// =============================================================================
// NasSystem.UnitTests - Infrastructure/PaddleOcrEngineHolderTests.cs
// OCR 识别并发闸的边界（解码进入闸内之后，峰值解码位图数由此闸决定）
// =============================================================================

namespace NasSystem.UnitTests.Infrastructure;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NasSystem.Infrastructure.Ocr;
using Xunit;

/// <summary>
/// 锁定 OCR 引擎的 2 路并发约束。解码已移入闸内，闸的并发上限
/// 就是解码位图峰值的上限，不能被改成无界。
/// </summary>
public sealed class PaddleOcrEngineHolderTests
{
    [Fact]
    public async Task EnterRunAsync_SixCallers_AllowsAtMostTwoConcurrent()
    {
        using var holder = new PaddleOcrEngineHolder(
            NullLogger<PaddleOcrEngineHolder>.Instance);
        var current = 0;
        var peak = 0;
        var sync = new object();

        var tasks = Enumerable.Range(0, 6).Select(async _ =>
        {
            using var scope = await holder.EnterRunAsync();
            lock (sync)
            {
                current++;
                peak = Math.Max(peak, current);
            }

            await Task.Delay(50);

            lock (sync)
            {
                current--;
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        peak.Should().Be(2);
    }
}
