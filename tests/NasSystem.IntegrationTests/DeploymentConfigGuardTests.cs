// =============================================================================
// NasSystem.IntegrationTests - DeploymentConfigGuardTests.cs
// 部署配置的机械守护（本轮审计 Critical：compose 默认 JWT 密钥绕过 fail-fast）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Xunit;

/// <summary>
/// 锁本轮审计发现的 Critical：docker-compose.yml 曾写
/// <c>Jwt__SecretKey=${JWT_SECRET:-your-super-secret-key-at-least-32-characters-long}</c>。
/// 应用层 fail-fast 只拒空值与 <c>&lt;SET-VIA-ENV&gt;</c> 占位符，这个公开字面量
/// 两者都不是——未设 JWT_SECRET 的部署会带着一个写进仓库的密钥启动，
/// 任何读到该文件的人都能伪造任意用户（含 SuperAdmin）的 JWT。
/// ITM-N1「配置缺失时的默认值兜底是反模式，应 fail-fast」在部署层复发。
///
/// 断言形态：compose 的 Jwt__SecretKey 行不得含 <c>:-</c> 默认值回落，
/// 缺失变量必须是空值，由 AuthenticationExtensions 的启动期 fail-fast 拦截。
/// </summary>
public class DeploymentConfigGuardTests
{
    [Fact]
    public void DockerCompose_JwtSecretKey_MustNotCarryDefaultFallback()
    {
        var composePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker-compose.yml");
        File.Exists(composePath).Should().BeTrue("测试应能在仓库根定位 docker-compose.yml");

        var lines = File.ReadAllLines(composePath);
        var secretLines = lines
            .Where(l => l.Contains("Jwt__SecretKey", StringComparison.Ordinal))
            .ToList();

        secretLines.Should().HaveCount(1, "compose 中应只有一处 Jwt__SecretKey 注入");
        var line = secretLines[0];

        // ${VAR:-default} 与 ${VAR-default} 都是默认值回落形态，一律禁止
        line.Should().NotMatch("*:-*",
            "JWT 密钥不得带 compose 层默认值：公开字面量会绕过应用启动期 fail-fast");
        line.Should().Contain("${JWT_SECRET}",
            "缺失变量应求值为空，由应用 fail-fast 明确报错");
    }

    [Fact]
    public void SmbEntrypoint_MustNotShipDefaultPassword()
    {
        // F-096：SMB sidecar 曾在 NAS_SMB_PASSWORD 缺省时兜底为 "nasadmin"，
        // 启用 smb profile 又忘记设置变量会让 445 端口用弱口令暴露整个共享卷
        var entrypointPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docker", "smb", "entrypoint.sh");
        File.Exists(entrypointPath).Should().BeTrue("测试应能在仓库根定位 SMB sidecar entrypoint");

        var script = File.ReadAllText(entrypointPath);

        script.Should().NotContain("NAS_SMB_PASSWORD=\"nasadmin\"",
            "SMB 直通不得带写进仓库的默认密码");
        script.Should().NotContain("NAS_SMB_PASSWORD=nasadmin");
        script.Should().Contain("NAS_SMB_PASSWORD 未设置",
            "缺省时必须 fail-fast 并给出可操作提示");
        script.Should().Contain("exit 1",
            "缺省密码应拒绝启动而不是用弱口令继续");
    }

    [Fact]
    public void NginxConfig_MustAlignWithUpstreamLimitsAndLongTranscode()
    {
        // F-097：nginx 默认值曾与上游语义不匹配——body 上限 10G 对 Kestrel
        // 500MB（白缓冲）、全局 600s 读超时会截断同步转码的长视频、
        // 缺 HSTS/server_tokens、上传被 nginx 全量缓冲
        var nginxPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "nginx.conf");
        File.Exists(nginxPath).Should().BeTrue("测试应能在仓库根定位 nginx.conf");

        var text = File.ReadAllText(nginxPath);
        text.Should().Contain("server_tokens off", "不回显 nginx 版本号");
        text.Should().Contain("Strict-Transport-Security", "HTTPS 部署应带 HSTS");
        text.Should().Contain("client_max_body_size 512m",
            "应与 Kestrel 的 500MB 上限对齐，避免缓冲超限请求");
        text.Should().Contain("proxy_request_buffering off", "上传应流式直通");

        var streamIndex = text.IndexOf("location /api/v1/media/stream/", StringComparison.Ordinal);
        streamIndex.Should().BeGreaterThanOrEqualTo(0, "HLS 同步转码需要独立的放长超时");
        var streamBlock = text[streamIndex..];
        streamBlock = streamBlock[..streamBlock.IndexOf('}')];
        streamBlock.Should().Contain("proxy_read_timeout 3600s",
            "长视频转码会超过全局 600s，超时断流会让转码作废并反复重来");
    }

    [Fact]
    public void ContainerConfig_MustKeepSafeDefaults()
    {
        // F-099：容器默认值的机械守护——浮动镜像标签、root 运行、Development 环境、
        // 缺健康检查或重启策略都是"部署后才发现"的静默风险
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile"));

        // 镜像显式标签，禁 latest 浮动标签
        compose.Should().NotContain(":latest");
        dockerfile.Should().NotContain(":latest");
        var imageLines = compose.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("image:", StringComparison.Ordinal))
            .ToList();
        imageLines.Should().NotBeEmpty("compose 应声明外部镜像");
        imageLines.Should().OnlyContain(l => l.Contains(':'),
            "每个镜像必须带显式标签，裸仓库名等于随时跟随最新版");

        // 运行时不以 root 启动
        var lastUser = dockerfile.LastIndexOf("USER ", StringComparison.Ordinal);
        lastUser.Should().BeGreaterThanOrEqualTo(0, "Dockerfile 必须有 USER 指令");
        dockerfile[lastUser..].Should().StartWith("USER nasuser",
            "最后一条 USER 必须是非 root 的 nasuser");

        // compose 不得用 Development（GlobalExceptionHandler 会回显异常细节）
        compose.Should().NotContain("ASPNETCORE_ENVIRONMENT=Development");
        compose.Should().Contain("ASPNETCORE_ENVIRONMENT=Production");

        // 健康检查与重启策略
        compose.Should().Contain("healthcheck");
        compose.Should().Contain("restart: unless-stopped");
        dockerfile.Should().Contain("HEALTHCHECK");
    }
}
