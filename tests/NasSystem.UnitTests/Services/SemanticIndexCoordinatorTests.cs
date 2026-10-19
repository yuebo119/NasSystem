// =============================================================================
// NasSystem.UnitTests - Services/SemanticIndexCoordinatorTests.cs
// 语义索引手动触发协调器（唤醒语义，无 IO）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using NasSystem.Infrastructure.Semantic;
using Xunit;

/// <summary>
/// 锁协调器的四条语义：请求能唤醒等待方、消费后重新武装（下一次
/// 请求还能唤醒）、无待办不谎报、多次请求合并为一轮。这是
/// POST /semantic/index/trigger → 后台建库服务的唯一通道，
/// 唤醒丢失的症状是"点了立即建索引没有任何反应"
/// </summary>
public class SemanticIndexCoordinatorTests
{
    [Fact]
    public async Task RequestRun_WakesWaitingConsumer()
    {
        var coordinator = new SemanticIndexCoordinator();
        var wait = coordinator.WaitAsync(CancellationToken.None);

        coordinator.RequestRun();

        // 不挂起：请求必须唤醒已开始的等待
        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.TryConsumeRun().Should().BeTrue("被唤醒后应取到手动轮待办");
    }

    [Fact]
    public async Task RequestRun_AfterConsume_ReArmsSignal()
    {
        var coordinator = new SemanticIndexCoordinator();
        coordinator.RequestRun();
        await coordinator.WaitAsync(CancellationToken.None);
        coordinator.TryConsumeRun().Should().BeTrue();

        // 重新武装：第二轮请求要能再次唤醒（信号只放一次 = 第二次点击失效）
        var secondWait = coordinator.WaitAsync(CancellationToken.None);
        coordinator.RequestRun();
        await secondWait.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.TryConsumeRun().Should().BeTrue();
    }

    [Fact]
    public void TryConsumeRun_WithoutRequest_ReturnsFalse()
    {
        var coordinator = new SemanticIndexCoordinator();

        coordinator.TryConsumeRun().Should().BeFalse("无待办时不能谎报手动轮");
    }

    [Fact]
    public async Task MultipleRequests_MergeIntoOnePass()
    {
        var coordinator = new SemanticIndexCoordinator();
        coordinator.RequestRun();
        coordinator.RequestRun();

        await coordinator.WaitAsync(CancellationToken.None);
        coordinator.TryConsumeRun().Should().BeTrue("触发期间的多轮请求合并为一轮待办");
        coordinator.TryConsumeRun().Should().BeFalse("消费后待办清零，不重复跑");
    }

    [Fact]
    public async Task WaitAsync_Cancelled_ThrowsOperationCanceled()
    {
        var coordinator = new SemanticIndexCoordinator();
        using var cts = new CancellationTokenSource();
        var wait = coordinator.WaitAsync(cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
    }
}
