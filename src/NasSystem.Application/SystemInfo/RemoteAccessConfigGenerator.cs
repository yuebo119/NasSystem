// =============================================================================
// NasSystem.Application - SystemInfo/RemoteAccessConfigGenerator.cs
// 外网访问配置生成（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Application.SystemInfo;

/// <summary>
/// 外网访问配置生成器。不自建中继云（成本与运维超出个人项目边界），
/// 而是按用户选择的成熟方案生成配置文件与操作步骤，用户用自己的
/// 基础设施（Cloudflare 账号 / 自建 frps / Tailscale 账号）完成最后一公里
/// </summary>
public static class RemoteAccessConfigGenerator
{
    /// <summary>支持的方案</summary>
    public static readonly IReadOnlyList<string> Providers = ["cloudflare", "frp", "tailscale"];

    /// <summary>
    /// 生成配置。hostname 仅 cloudflare 与 frp 需要（tailscale 用
    /// 分配的 MagicDNS 名）；缺 hostname 时这两个方案返回带占位符的模板
    /// </summary>
    public static RemoteAccessConfig Generate(string provider, string? hostname, int localPort)
    {
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var effectiveHost = string.IsNullOrWhiteSpace(hostname) ? "nas.example.com" : hostname.Trim();

        return normalizedProvider switch
        {
            "cloudflare" => Cloudflare(effectiveHost, localPort),
            "frp" => Frp(effectiveHost, localPort),
            "tailscale" => Tailscale(localPort),
            _ => throw new ArgumentException($"不支持的方案: {provider}", nameof(provider))
        };
    }

    /// <summary>
    /// Cloudflare Tunnel：免公网 IP、免开端口，HTTPS 由 Cloudflare 边缘终止。
    /// 免费额度覆盖个人场景，是默认推荐
    /// </summary>
    private static RemoteAccessConfig Cloudflare(string hostname, int localPort) => new(
        Provider: "cloudflare",
        FileName: "config.yml",
        Config: $"""
            # cloudflared 配置（放在 /etc/cloudflared/ 或 ~/.cloudflared/）
            # <TUNNEL_ID> 替换为 `cloudflared tunnel create nas` 输出的隧道 ID
            tunnel: <TUNNEL_ID>
            credentials-file: /etc/cloudflared/<TUNNEL_ID>.json
            ingress:
              - hostname: {hostname}
                service: http://localhost:{localPort}
              # 兜底规则：不匹配上面 hostname 的请求一律 404，
                # 防止隧道把请求漏到内网其他服务
              - service: http_status:404
            """,
        Steps:
        [
            "安装 cloudflared：curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o /usr/local/bin/cloudflared && chmod +x /usr/local/bin/cloudflared",
            "登录并创建隧道：cloudflared tunnel login && cloudflared tunnel create nas",
            "把左侧配置写入 /etc/cloudflared/config.yml，<TUNNEL_ID> 换成上一步输出的隧道 ID",
            "把隧道安装为服务：cloudflared --config /etc/cloudflared/config.yml service install",
            "启动服务：systemctl start cloudflared && systemctl enable cloudflared",
            $"绑定域名：cloudflared tunnel route dns nas {hostname}（需域名 NS 已托管在 Cloudflare）",
            $"验证：外网访问 https://{hostname} 应看到登录页"
        ]);

    /// <summary>frp：自建或租用一台有公网 IP 的 frps 服务器，客户端常驻内网</summary>
    private static RemoteAccessConfig Frp(string hostname, int localPort) => new(
        Provider: "frp",
        FileName: "frpc.toml",
        Config: $"""
            # frp 客户端配置（跑在 NAS 上）
            # serverAddr 换成你的 frps 服务器地址，auth.token 与 frps.toml 一致
            serverAddr = "your-frps-server.example.com"
            serverPort = 7000
            auth.token = "change-me-to-a-strong-token"

            [[proxies]]
            name = "nas-web"
            type = "http"
            localPort = {localPort}
            customDomains = "{hostname}"

            # WebDAV 与 SMB 建议按需再开（TCP 类代理）：
            # [[proxies]]
            # name = "nas-webdav"
            # type = "tcp"
            # localPort = {localPort}
            # remotePort = 16050
            """,
        Steps:
        [
            "准备一台有公网 IP 的服务器，部署 frps（frp 项目 releases 页下载，服务端配置见 frps.toml 示例）",
            "NAS 上安装 frpc，把左侧配置保存为 frpc.toml",
            "把 serverAddr 改为 frps 服务器地址，auth.token 改为与服务端一致的强令牌",
            "启动：frpc -c frpc.toml（生产用 systemd 常驻）",
            $"把 {hostname} 的 DNS A 记录指向 frps 服务器 IP",
            $"验证：外网访问 http://{hostname} 应看到登录页"
        ]);

    /// <summary>
    /// Tailscale：组网方案，没有"配置文件"可生成——
    /// 安装入网后设备之间直接用分配的 IP 通信，由用户在各端执行命令
    /// </summary>
    private static RemoteAccessConfig Tailscale(int localPort) => new(
        Provider: "tailscale",
        FileName: "tailscale-up.sh",
        Config: $"""
            # Tailscale 无需服务端配置：安装入网后，NAS 与其他设备
            # 在同一个虚拟内网里，直接用分配的 100.x.x.x 地址互访
            # 以下命令在 NAS 上执行
            curl -fsSL https://tailscale.com/install.sh | sh
            tailscale up
            # 入网后查看本机虚拟地址：
            tailscale ip -4
            # 其他设备（手机/电脑）装 Tailscale 登录同一账号后，
            # 浏览器访问 http://<上一步的 100.x 地址>:{localPort}
            """,
        Steps:
        [
            "NAS 上执行左侧脚本安装并入网（按提示用浏览器完成账号授权）",
            "手机与电脑安装 Tailscale App，登录同一账号",
            $"浏览器访问 http://<NAS 的 100.x 地址>:{localPort}",
            "可选：在 Tailscale 管理后台开启 MagicDNS，用机器名代替 IP 访问",
            "可选：需要公网可访问时在管理后台为 NAS 开启 Funnel（注意这会暴露到公网）"
        ]);
}

/// <summary>外网访问配置生成结果</summary>
/// <param name="FileName">建议保存的文件名</param>
/// <param name="Config">配置内容（tailscale 为安装命令脚本）</param>
/// <param name="Steps">按顺序执行的操作步骤</param>
public sealed record RemoteAccessConfig(
    string Provider,
    string FileName,
    string Config,
    IReadOnlyList<string> Steps);
