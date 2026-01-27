// =============================================================================
// NasSystem.UnitTests - SampleTest.cs
// 示例单元测试
// =============================================================================

namespace NasSystem.UnitTests;

using FluentAssertions;
using Xunit;

/// <summary>
/// 示例测试类 - 验证测试框架配置正确
/// </summary>
public class SampleTest
{
    [Fact]
    public void Sample_ShouldPass()
    {
        // Arrange
        var expected = 4;

        // Act
        var result = 2 + 2;

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 3, 5)]
    [InlineData(10, 20, 30)]
    public void Add_ShouldReturnCorrectSum(int a, int b, int expected)
    {
        // Act
        var result = a + b;

        // Assert
        result.Should().Be(expected);
    }
}
