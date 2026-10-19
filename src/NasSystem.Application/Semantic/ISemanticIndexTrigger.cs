// =============================================================================
// NasSystem.Application - Semantic/ISemanticIndexTrigger.cs
// 语义索引手动触发抽象
// =============================================================================

namespace NasSystem.Application.Semantic;

/// <summary>
/// 请求立即执行一轮语义索引。实现是 Infrastructure 的后台服务协调器：
/// 表现层只依赖抽象发意图，不感知 BackgroundService 的唤醒机制
/// （与 IHubNotificationService 同款"接口在内、实现在外"布局）。
/// </summary>
public interface ISemanticIndexTrigger
{
    /// <summary>请求一轮立即执行的索引（可重复请求，合并为一轮待办）</summary>
    void RequestRun();
}
