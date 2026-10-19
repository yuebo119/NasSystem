// =============================================================================
// NasSystem.Infrastructure - Semantic/SemanticIndexCoordinator.cs
// 语义索引手动触发协调器
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using NasSystem.Application.Semantic;

/// <summary>
/// 把"立即跑一轮索引"的意图从 HTTP 端点转交给后台建库服务。
/// </summary>
/// <remarks>
/// 为什么端点不自己跑：单轮索引是分钟级容量动作（单张 58ms，10 万张
/// 外推约 97 分钟），HTTP 同步跑会占住连接直到超时，与后台服务的设计
/// 前提（ADR-0003 探针）冲突。端点只负责唤醒，真正编码仍在
/// <see cref="SemanticIndexBackgroundService"/> 的单轮里。
/// 合并语义：触发期间的多轮请求合并为一轮待办——一轮本来就消费全部
/// 缺失向量，重复跑没有新增工作。
/// </remarks>
public sealed class SemanticIndexCoordinator : ISemanticIndexTrigger
{
    private readonly object _gate = new();
    private TaskCompletionSource _wake = NewSignal();
    private int _pending;

    /// <summary>请求立即执行一轮索引（auto-index 关闭时的手动入口）</summary>
    public void RequestRun()
    {
        lock (_gate)
        {
            _pending++;
            _wake.TrySetResult();
        }
    }

    /// <summary>等待下一次手动触发；取消时抛 OperationCanceledException</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Task wake;
        lock (_gate)
        {
            wake = _wake.Task;
        }
        await wake.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 取走待办并重新武装信号。返回 true 表示调用方应立即执行一轮
    /// （manual 轮：不受 semantic.auto-index 约束）。
    /// </summary>
    public bool TryConsumeRun()
    {
        lock (_gate)
        {
            var manual = _pending > 0;
            _pending = 0;
            // 重新武装：本次唤醒已消费，后续 RequestRun 要能再次唤醒等待方
            _wake = NewSignal();
            return manual;
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
