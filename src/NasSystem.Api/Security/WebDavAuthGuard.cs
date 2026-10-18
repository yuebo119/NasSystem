// =============================================================================
// NasSystem.Api - Security/WebDavAuthGuard.cs
// WebDAV Basic 认证暴力破解防护
// =============================================================================

namespace NasSystem.Api.Security;

using System.Collections.Concurrent;

/// <summary>
/// WebDAV Basic 认证失败锁定。WebDAV 端点不走 auth 限流策略（那是全局限流，
/// 不能按客户端锁定），认证失败计数在这里独立维护
/// </summary>
public interface IWebDavAuthGuard
{
    /// <summary>客户端是否处于锁定状态</summary>
    bool IsLockedOut(string clientKey);

    /// <summary>记录一次认证失败</summary>
    void RecordFailure(string clientKey);

    /// <summary>认证成功后清除该客户端的失败计数</summary>
    void Reset(string clientKey);
}

/// <summary>
/// 内存实现：同客户端 5 分钟内 10 次失败即锁。
/// 自托管 NAS 单实例部署，来源 IP 集合小且固定，字典规模可控；
/// 过期条目在该客户端下次访问时重置，不单独起清理线程。
/// 多实例水平扩展需换共享存储，当前形态不涉及
/// </summary>
internal sealed class WebDavAuthGuard : IWebDavAuthGuard
{
    private const int MaxFailures = 10;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _failures = new();

    public bool IsLockedOut(string clientKey)
    {
        if (!_failures.TryGetValue(clientKey, out var failure))
        {
            return false;
        }

        if (failure.Count < MaxFailures)
        {
            return false;
        }

        if (DateTime.UtcNow - failure.WindowStart >= FailureWindow)
        {
            // 窗口已过：重置而非判锁，避免过期条目把字典撑大
            _failures.TryRemove(clientKey, out _);
            return false;
        }

        return true;
    }

    public void RecordFailure(string clientKey)
    {
        _failures.AddOrUpdate(
            clientKey,
            _ => (1, DateTime.UtcNow),
            (_, current) => DateTime.UtcNow - current.WindowStart < FailureWindow
                ? (current.Count + 1, current.WindowStart)
                : (1, DateTime.UtcNow));
    }

    public void Reset(string clientKey)
    {
        _failures.TryRemove(clientKey, out _);
    }
}
