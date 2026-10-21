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
}
