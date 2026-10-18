// =============================================================================
// NasSystem.IntegrationTests - CronScheduleTests.cs
// 最小 cron 解析器测试（ROADMAP Phase 2 定时快照）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using NasSystem.Infrastructure.Services;
using Xunit;

/// <summary>
/// cron 解析与匹配。定时快照的触发正确性全押在这个解析器上，
/// 而它的消费方（BackgroundService）没有可观察出口，故在此逐形态锁定。
/// </summary>
public class CronScheduleTests
{
    // 2026-10-18 是星期日。用固定时刻断言匹配，避免时间相关漂移
    private static DateTime At(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("0 3 * * *")]           // 每日 03:00
    [InlineData("*/15 * * * *")]        // 每 15 分钟
    [InlineData("30 2 1 * *")]          // 每月 1 日 02:30
    [InlineData("0 0 * * 1")]           // 每周一 00:00
    [InlineData("0 9-17 * * *")]        // 9 到 17 点整
    [InlineData("0 0,12 * * *")]        // 0 点与 12 点
    [InlineData("0 0 * * 7")]           // 周日（7 归一为 0）
    public void TryParse_AcceptsValidExpressions(string expression)
    {
        CronSchedule.TryParse(expression, out var schedule).Should().BeTrue(expression);
        schedule.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 3 * *")]             // 少一段
    [InlineData("0 3 * * * *")]         // 多一段
    [InlineData("60 3 * * *")]          // 分钟越界
    [InlineData("0 25 * * *")]          // 小时越界
    [InlineData("0 3 0 * *")]           // 日越界（1-31）
    [InlineData("0 3 * 13 *")]          // 月越界
    [InlineData("*/0 * * * *")]         // 步长为 0
    [InlineData("a 3 * * *")]           // 非数字
    [InlineData("5-2 * * * *")]         // 范围倒置
    public void TryParse_RejectsInvalidExpressions(string expression)
    {
        CronSchedule.TryParse(expression, out _).Should().BeFalse(expression);
    }

    [Fact]
    public void Matches_DailyAtThree()
    {
        CronSchedule.TryParse("0 3 * * *", out var schedule).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 3, 0)).Should().BeTrue("03:00 命中");
        schedule.Matches(At(2026, 10, 18, 4, 0)).Should().BeFalse("04:00 不命中");
        schedule.Matches(At(2026, 10, 19, 3, 0)).Should().BeTrue("另一天同时刻命中（每日）");
    }

    [Fact]
    public void Matches_EveryFifteenMinutes()
    {
        CronSchedule.TryParse("*/15 * * * *", out var schedule).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 8, 0)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 8, 15)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 8, 30)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 8, 45)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 8, 7)).Should().BeFalse();
    }

    [Fact]
    public void Matches_WeekdayMonday()
    {
        CronSchedule.TryParse("0 0 * * 1", out var schedule).Should().BeTrue();
        // 2026-10-19 是星期一，10-18 是星期日
        schedule.Matches(At(2026, 10, 19, 0, 0)).Should().BeTrue("周一命中");
        schedule.Matches(At(2026, 10, 18, 0, 0)).Should().BeFalse("周日不命中");
    }

    [Fact]
    public void Matches_WeekSeven_NormalizesToSunday()
    {
        CronSchedule.TryParse("0 0 * * 7", out var schedule).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 0, 0)).Should().BeTrue("周 7 = 周日");
    }

    [Fact]
    public void Matches_HourRangeAndList()
    {
        CronSchedule.TryParse("0 9-17 * * *", out var range).Should().BeTrue();
        range.Matches(At(2026, 10, 18, 12, 0)).Should().BeTrue();
        range.Matches(At(2026, 10, 18, 18, 0)).Should().BeFalse("17 点后不命中");

        CronSchedule.TryParse("0 0,12 * * *", out var list).Should().BeTrue();
        list.Matches(At(2026, 10, 18, 0, 0)).Should().BeTrue();
        list.Matches(At(2026, 10, 18, 12, 0)).Should().BeTrue();
        list.Matches(At(2026, 10, 18, 6, 0)).Should().BeFalse();
    }

    [Fact]
    public void Matches_DayOfMonth()
    {
        CronSchedule.TryParse("30 2 1 * *", out var schedule).Should().BeTrue();
        schedule.Matches(At(2026, 11, 1, 2, 30)).Should().BeTrue("每月 1 日");
        schedule.Matches(At(2026, 11, 2, 2, 30)).Should().BeFalse();
    }

    [Fact]
    public void Matches_StepWithStart()
    {
        // "5/20" 分钟段：5、25、45
        CronSchedule.TryParse("5/20 * * * *", out var schedule).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 0, 5)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 0, 25)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 0, 45)).Should().BeTrue();
        schedule.Matches(At(2026, 10, 18, 0, 10)).Should().BeFalse();
    }
}
