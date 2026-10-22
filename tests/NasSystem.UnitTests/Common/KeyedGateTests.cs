// =============================================================================
// NasSystem.UnitTests - Common/KeyedGateTests.cs
// 分片闸的互斥语义
// =============================================================================

namespace NasSystem.UnitTests.Common;

using FluentAssertions;
using NasSystem.Application.Common;
using Xunit;

/// <summary>
/// 锁定 KeyedGate 的核心不变量：同一 key 的持有者绝不重叠。
/// 这是下载计数闸与存储池刷新闸能够替代"读改写"的前提。
/// </summary>
public sealed class KeyedGateTests
{
    [Fact]
    public async Task AcquireAsync_SameKeyUnderConcurrency_NeverOverlapsHolders()
    {
        var gate = new KeyedGate(stripes: 8);
        var key = Guid.NewGuid();
        var inside = 0;
        var peak = 0;
        var sync = new object();

        var tasks = Enumerable.Range(0, 64).Select(async _ =>
        {
            using var handle = await gate.AcquireAsync(key);
            lock (sync)
            {
                inside++;
                peak = Math.Max(peak, inside);
            }

            await Task.Delay(1);

            lock (sync)
            {
                inside--;
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        peak.Should().Be(1, "同一 key 的临界区必须串行，否则读改写会互相覆盖");
    }

    [Fact]
    public async Task AcquireAsync_TwoKeysOnDifferentStripes_CanOverlap()
    {
        // 找一个与基准 key 不同分片的 key（哈希不同则并行，是分片闸的吞吐前提）。
        // 找不到就说明分片过少，跳过断言而不是伪造失败
        var gate = new KeyedGate(stripes: 64);
        var baseKey = Guid.NewGuid();
        Guid otherKey = default;
        var found = false;
        for (var i = 0; i < 200 && !found; i++)
        {
            var candidate = Guid.NewGuid();
            if ((uint)candidate.GetHashCode() % 64 != (uint)baseKey.GetHashCode() % 64)
            {
                otherKey = candidate;
                found = true;
            }
        }

        if (!found)
            return;

        using var baseHandle = await gate.AcquireAsync(baseKey);
        var otherTask = Task.Run(async () =>
        {
            using var handle = await gate.AcquireAsync(otherKey);
            await Task.CompletedTask;
        });

        var completed = await Task.WhenAny(otherTask, Task.Delay(2000));
        completed.Should().Be(otherTask, "不同分片的 key 不应被同一持有者阻塞");
        await otherTask;
    }
}