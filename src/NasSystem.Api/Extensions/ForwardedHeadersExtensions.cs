// =============================================================================
// NasSystem.Api - Extensions/ForwardedHeadersExtensions.cs
// 反代真实客户端 IP（审计 F-072）
// =============================================================================

namespace NasSystem.Api.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// 反代后的真实客户端 IP 解析。
/// </summary>
/// <remarks>
/// 背景：部署在 nginx 之后时，Kestrel 看到的对端是反代容器而非真实客户端。
/// WebDAV 失败锁定（WebDavAuthGuard）与分享端点限流都以客户端 IP 为键，
/// 反代后全部请求共享同一个键：一个攻击者的失败轰炸会把同反代下的所有
/// 正常用户一起锁掉（审计 F-072）。
///
/// 为什么默认关闭且必须显式配置：6050 端口在 compose 里是对外发布的，
/// 请求可能来自反代也可能直连。无条件信任 X-Forwarded-For 等于让任何人
/// 伪造客户端键绕过失败锁定，比共享桶更糟。因此只在配置了可信代理
/// （IP 或 CIDR）时才安装中间件，且只在这些对端发来的请求上生效。
///
/// 配置（appsettings / 环境变量）：
///   NasSystem:ForwardedHeaders:KnownProxies = ["10.0.0.5"]      # nginx 容器 IP
///   NasSystem:ForwardedHeaders:KnownNetworks = ["172.16.0.0/12"] # docker 网桥段
/// 两项都为空时不安装中间件，行为与配置前完全一致。
/// docker-compose 部署的推荐做法：给 nginx 容器固定 IP 后填进 KnownProxies。
/// </remarks>
public static class ForwardedHeadersExtensions
{
    public static WebApplication UseConfiguredForwardedHeaders(
        this WebApplication app,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("NasSystem:ForwardedHeaders");
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        if (proxies.Length == 0 && networks.Length == 0)
        {
            app.Logger.LogDebug(
                "未配置 NasSystem:ForwardedHeaders 可信代理，跳过转发头解析（客户端 IP 取直连对端）");
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            // 只认对端声明的第一跳：多跳 X-Forwarded-For 是客户端可伪造的尾巴，
            // 取第一跳之外的段等于把键的控制权交给请求方
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            // 清空默认的本地回环白名单：默认只信任回环，反代场景对端是
            // 容器 IP 不在其中，不显式配置就等于永远不生效
            KnownProxies = { },
            KnownNetworks = { }
        };

        foreach (var proxy in proxies)
        {
            if (System.Net.IPAddress.TryParse(proxy.Trim(), out var ip))
            {
                options.KnownProxies.Add(ip);
            }
            else
            {
                app.Logger.LogWarning("ForwardedHeaders KnownProxies 含无法解析的 IP: {Proxy}", proxy);
            }
        }

        foreach (var network in networks)
        {
            var parts = network.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && System.Net.IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
            }
            else
            {
                app.Logger.LogWarning("ForwardedHeaders KnownNetworks 含无法解析的 CIDR: {Network}", network);
            }
        }

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            app.Logger.LogWarning(
                "ForwardedHeaders 配置了可信代理但无一解析成功，跳过中间件（继续用直连对端作客户端键）");
            return app;
        }

        app.UseForwardedHeaders(options);
        app.Logger.LogInformation(
            "转发头解析已启用：可信代理 {Proxies} 个 / 可信网段 {Networks} 个（反代后按真实客户端 IP 计锁定与限流）",
            options.KnownProxies.Count, options.KnownNetworks.Count);
        return app;
    }
}
