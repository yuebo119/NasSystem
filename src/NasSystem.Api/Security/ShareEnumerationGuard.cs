// =============================================================================
// NasSystem.Api - Security/ShareEnumerationGuard.cs
// 分享码枚举的全局速率闸（审计 F-073）
// =============================================================================

namespace NasSystem.Api.Security;

using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using NasSystem.Domain.Services;

/// <summary>
/// 公开分享信息端点的全局探测速率闸。
/// </summary>
/// <remarks>
/// 威胁：GET /api/v1/public-shares/{code} 是分享码存在性预言机（404 vs 200），
/// 码空间 31^6 ≈ 8.9×10⁸。按 IP 的限流（"share" 策略）挡得住单来源轰炸，
/// 挡不住分布式枚举——每台机器都在限额内，加起来仍然很快。
///
/// 为什么不用限流策略叠加：ASP.NET Core 的 RequireRateLimiting 写进端点元数据，
/// 同一端点上叠加第二个策略不会都生效（实测只有端点级那个生效，组级被覆盖），
/// 所以全局闸在这里显式实现，与组级的按 IP 窗口互补而非互斥。
///
/// 形态：进程内固定窗口，单例。阈值走配置
/// （NasSystem:RateLimiting:ShareGlobalPerMinute，生产缺省 600/分钟：
/// 合法场景一个链接被几十人同时点开远到不了，枚举全空间则要数年）。
/// 单实例部署（compose 单容器）下进程内计数即全局限流；多实例水平扩展
/// 需换共享存储，与 WebDavAuthGuard 同一边界。
/// </remarks>
public sealed class ShareEnumerationGuard
{
    // 窗口起点（UTC 分钟粒度）与该窗口内的计数。窗口滚动即整体重置，
    // 不逐条清理：单键字典，内存与秒级抖动无关
    private readonly ConcurrentDictionary<long, int> _windowCounts = new();
    private readonly IDateTimeProvider _dateTime;
    private readonly int _perMinute;

    public ShareEnumerationGuard(IConfiguration configuration, IDateTimeProvider dateTime)
    {
        _dateTime = dateTime;
        _perMinute = configuration.GetValue<int?>("NasSystem:RateLimiting:ShareGlobalPerMinute")
            ?? 600;
    }

    /// <summary>
    /// 记一次探测。返回 false 表示已超全局速率，调用方应回 429。
    /// 超限时也计数（让窗口内的持续洪泛始终被拒，不因“这次不算”而放行）
    /// </summary>
    public bool TryAcquire()
    {
        var window = new DateTimeOffset(_dateTime.UtcNow).ToUnixTimeSeconds() / 60;
        var count = _windowCounts.AddOrUpdate(window, 1, (_, c) => c + 1);

        // 顺手清掉上一个窗口的键：正常每秒最多两个键，防御性清理防字典
        // 在“时间回拨/长期空转”形态下累积
        _windowCounts.TryRemove(window - 2, out _);

        return count <= _perMinute;
    }
}
