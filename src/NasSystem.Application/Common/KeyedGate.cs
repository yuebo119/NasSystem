// =============================================================================
// NasSystem.Application - Common/KeyedGate.cs
// 固定分片的按 key 串行闸
// =============================================================================

namespace NasSystem.Application.Common;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// 按 key 串行化的固定分片闸：落在同一分片的 key 互斥，不同分片并行。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不用 ConcurrentDictionary&lt;key, SemaphoreSlim&gt; 加"无等待者即移除"：
/// 释放与移除之间存在窗口。请求 A 释放后、移除前，请求 B 的 GetOrAdd 取到同一
/// 实例并开始等待（Count 归 0）；A 随后仍执行移除；请求 C 再取就拿到新实例，
/// B 与 C 会同时对同一 key 进入临界区。窗口只有两条语句，但它是真实竞态，
/// 对"限流判定 + 计数自增"这类不变量是漏口。
/// </para>
/// <para>
/// 分片表长度固定，条目不随 key 数量增长，也没有移除动作，因此不存在上述窗口。
/// 分片碰撞只让两个不同 key 多一次串行等待，不影响正确性。
/// </para>
/// <para>
/// 释放不释放：分片闸随服务静态字段存活到进程退出，由 OS 回收；不做 Dispose
/// 是有意为之（与 <c>ThumbnailService._generationGate</c> 同现状）。
/// </para>
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "进程生命周期对象，无释放点；随进程退出回收")]
internal sealed class KeyedGate
{
    private readonly SemaphoreSlim[] _gates;

    public KeyedGate(int stripes = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stripes);
        _gates = new SemaphoreSlim[stripes];
        for (var i = 0; i < stripes; i++)
            _gates[i] = new SemaphoreSlim(1, 1);
    }

    /// <summary>按 key 取分片并等待进入。返回的句柄释放即退出临界区。</summary>
    public async Task<IDisposable> AcquireAsync(Guid key, CancellationToken cancellationToken = default)
    {
        var gate = _gates[(uint)key.GetHashCode() % (uint)_gates.Length];
        await gate.WaitAsync(cancellationToken);
        return new ReleaseHandle(gate);
    }

    private sealed class ReleaseHandle(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}