// =============================================================================
// NasSystem.UnitTests - Infrastructure/ReparsePointPolicyTests.cs
// 重解析点单一策略（F-106）
// =============================================================================

namespace NasSystem.UnitTests.Infrastructure;

using FluentAssertions;
using NasSystem.Shared.IO;
using Xunit;

/// <summary>
/// 锁定策略默认值：普通文件/目录不跳过，路径不可判断时按 true（跳过）。
/// 扫描、ZIP 分享与导出都依赖这一条。
/// </summary>
public sealed class ReparsePointPolicyTests : IDisposable
{
    private readonly string _directory;

    public ReparsePointPolicyTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"reparse-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留给系统清理
        }
    }

    [Fact]
    public void IsReparsePoint_MissingPath_ReturnsTrue()
    {
        var missing = Path.Combine(_directory, "missing");

        ReparsePointPolicy.IsReparsePoint(missing).Should().BeTrue(
            "不可判断的路径按不安全对待，调用方应跳过");
    }

    [Fact]
    public void IsReparsePoint_RegularFileAndDirectory_ReturnsFalse()
    {
        var file = Path.Combine(_directory, "normal.txt");
        File.WriteAllText(file, "x");

        ReparsePointPolicy.IsReparsePoint(file).Should().BeFalse();
        ReparsePointPolicy.IsReparsePoint(_directory).Should().BeFalse();
    }
}