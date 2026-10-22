// =============================================================================
// NasSystem.Infrastructure - Services/SystemDateTimeProvider.cs
// 系统时钟实现（IDateTimeProvider 的生产形态）
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using NasSystem.Domain.Services;

/// <summary>
/// 系统时钟。单例：无状态，全进程共享同一个真源。
/// 测试经 FakeDateTimeProvider 替身（见测试项目）替换
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
