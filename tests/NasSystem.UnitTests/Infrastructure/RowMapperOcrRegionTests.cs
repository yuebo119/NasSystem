// =============================================================================
// NasSystem.UnitTests - Infrastructure/RowMapperOcrRegionTests.cs
// 区域 JSON 列的解析边界（Domain 不再感知存储格式）
// =============================================================================

namespace NasSystem.UnitTests.Infrastructure;

using FluentAssertions;
using NasSystem.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// 锁定 OCR 区域列解析的降级规则：null/空/损坏/坐标数不为 8 一律空列表或过滤，
/// 位置信息是增强，任何存储侧异常都不能影响文本主流程。
/// </summary>
public sealed class RowMapperOcrRegionTests
{
    [Fact]
    public void ParseOcrRegions_ValidJson_ReturnsRegions()
    {
        const string json =
            "[{\"Text\":\"第一行\",\"Points\":[0.1,0.2,0.3,0.4,0.5,0.6,0.7,0.8]}]";

        var regions = RowMapper.ParseOcrRegions(json);

        regions.Should().HaveCount(1);
        regions[0].Text.Should().Be("第一行");
        regions[0].Points.Should().Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{不是 JSON")]
    [InlineData("[]")]
    public void ParseOcrRegions_NullEmptyOrMalformed_ReturnsEmpty(string? json)
    {
        RowMapper.ParseOcrRegions(json).Should().BeEmpty();
    }

    [Fact]
    public void ParseOcrRegions_InvalidPointCount_FiltersOutEntry()
    {
        const string json = "[{\"Text\":\"坐标不足\",\"Points\":[1,2,3]}]";

        RowMapper.ParseOcrRegions(json).Should().BeEmpty("坐标不是 8 值的行必须过滤");
    }
}