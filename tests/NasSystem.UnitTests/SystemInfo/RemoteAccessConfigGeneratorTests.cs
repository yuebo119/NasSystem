// =============================================================================
// NasSystem.UnitTests - SystemInfo/RemoteAccessConfigGeneratorTests.cs
// 外网访问配置生成（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.UnitTests.SystemInfo;

using FluentAssertions;
using NasSystem.Application.SystemInfo;
using Xunit;

/// <summary>
/// 锁住三个方案的配置内容要点：hostname 与端口必须进配置
/// （漏了的表现是用户照着配完访问不通），未知方案必须抛ArgumentException
/// （端点靠它回 400）
/// </summary>
public class RemoteAccessConfigGeneratorTests
{
    [Theory]
    [InlineData("cloudflare")]
    [InlineData("frp")]
    [InlineData("tailscale")]
    public void Generate_KnownProvider_ReturnsConfigAndSteps(string provider)
    {
        var config = RemoteAccessConfigGenerator.Generate(provider, "nas.example.com", 6050);

        config.Provider.Should().Be(provider);
        config.FileName.Should().NotBeNullOrWhiteSpace("每个方案都要有建议文件名");
        config.Config.Should().NotBeNullOrWhiteSpace();
        config.Steps.Should().NotBeEmpty("配置必须配操作步骤，纯配置文件用户不知道从哪开始");
    }

    [Fact]
    public void Cloudflare_ConfigContainsHostnameAndPort()
    {
        var config = RemoteAccessConfigGenerator.Generate("cloudflare", "nas.mydomain.com", 6050);

        config.Config.Should().Contain("nas.mydomain.com");
        config.Config.Should().Contain("localhost:6050", "ingress 规则必须指向本地服务端口");
        config.Config.Should().Contain("http_status:404", "兜底规则防止隧道把请求漏到内网其他服务");
        config.Steps.Should().Contain(s => s.Contains("cloudflared tunnel create"));
    }

    [Fact]
    public void Frp_ConfigContainsCustomDomainsAndLocalPort()
    {
        var config = RemoteAccessConfigGenerator.Generate("frp", "nas.mydomain.com", 16050);

        config.Config.Should().Contain("customDomains = \"nas.mydomain.com\"");
        config.Config.Should().Contain("localPort = 16050");
        config.Steps.Should().Contain(s => s.Contains("frps"));
    }

    [Fact]
    public void Tailscale_ConfigContainsInstallCommands()
    {
        var config = RemoteAccessConfigGenerator.Generate("tailscale", null, 6050);

        // tailscale 没有配置文件可生成：内容是安装命令脚本
        config.Config.Should().Contain("tailscale up");
        config.Config.Should().Contain("tailscale ip -4");
    }

    [Fact]
    public void NullHostname_UsesPlaceholder()
    {
        var config = RemoteAccessConfigGenerator.Generate("cloudflare", null, 6050);

        config.Config.Should().Contain("nas.example.com", "缺 hostname 时用占位符，用户拿到底稿自己改");
    }

    [Fact]
    public void UnknownProvider_ThrowsArgumentException()
    {
        var act = () => RemoteAccessConfigGenerator.Generate("ngrok", "nas.example.com", 6050);

        act.Should().Throw<ArgumentException>("端点按此回 400，不支持 silent 回落");
    }
}
