// =============================================================================
// NasSystem.Api - Security/WebDavAuthGuard.cs
// WebDAV Basic 认证暴力破解防护
// =============================================================================

namespace NasSystem.Api.Security;

using System.Collections.Concurrent;

/// <summary>
/// WebDAV Basic 认证失败锁定。WebDAV 端点不走 auth 限流策略（那是全局限流，
/// 不能按客户端锁定），认证失败计数在这里独立维护。
/// 锁定策略（阈值/窗口期）由调用方读配置后传入：Guard 是单例内存态，
/// 不能直接消费 scoped 的 ISystemConfigService（DI 校验不过），
/// 也不自己读库——策略值与 WebDAV 启用开关同源同口径，一次请求读一次
/// </summary>
public interface IWebDavAuthGuard
{
    /// <summary>客户端是否处于锁定状态</summary>
    bool IsLockedOut(string clientKey, int maxFailures, TimeSpan window);

    /// <summary>记录一次认证失败</summary>
    void RecordFailure(string clientKey, TimeSpan window);

    /// <summary>认证成功后清除该客户端的失败计数</summary>
    void Reset(string clientKey);
}

/// <summary>
/// 内存实现：同客户端在窗口期内失败达阈值即锁。
/// 自托管 NAS 单实例部署，来源 IP 集合小且固定，字典规模可控；
/// 过期条目在该客户端下次访问时重置，不单独起清理线程。
/// 多实例水平扩展需换共享存储，当前形态不涉及
/// </summary>
internal sealed class WebDavAuthGuard : IWebDavAuthGuard
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _failures = new();

    // 时间源注入（CONVENTIONS §5.1 / 审计 F-028）：锁窗口的边界判定可测试
    private readonly NasSystem.Domain.Services.IDateTimeProvider _dateTime;

    public WebDavAuthGuard(NasSystem.Domain.Services.IDateTimeProvider dateTime)
    {
        _dateTime = dateTime;
    }

    public bool IsLockedOut(string clientKey, int maxFailures, TimeSpan window)
    {
        if (!_failures.TryGetValue(clientKey, out var failure))
        {
            return false;
        }

        if (failure.Count < maxFailures)
        {
            return false;
        }

        if (_dateTime.UtcNow - failure.WindowStart >= window)
        {
            // 窗口已过：重置而非判锁，避免过期条目把字典撑大
            _failures.TryRemove(clientKey, out _);
            return false;
        }

        return true;
    }

    public void RecordFailure(string clientKey, TimeSpan window)
    {
        _failures.AddOrUpdate(
            clientKey,
            _ => (1, _dateTime.UtcNow),
            (_, current) => _dateTime.UtcNow - current.WindowStart < window
                ? (current.Count + 1, current.WindowStart)
                : (1, _dateTime.UtcNow));
    }

    public void Reset(string clientKey)
    {
        _failures.TryRemove(clientKey, out _);
    }
}
