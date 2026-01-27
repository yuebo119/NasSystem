// =============================================================================
// NasSystem.Domain - IDateTimeProvider.cs
// 日期时间提供者接口
// =============================================================================

namespace NasSystem.Domain.Services;

/// <summary>
/// 日期时间提供者接口 - 便于测试时模拟时间
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>
    /// 获取当前 UTC 时间
    /// </summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// 获取当前本地时间
    /// </summary>
    DateTime Now { get; }

    /// <summary>
    /// 获取今天的日期
    /// </summary>
    DateOnly Today { get; }

    /// <summary>
    /// 获取当前时间戳
    /// </summary>
    long Timestamp { get; }
}

/// <summary>
/// 系统日期时间提供者实现
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    public long Timestamp => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
