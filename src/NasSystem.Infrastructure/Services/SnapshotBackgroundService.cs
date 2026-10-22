// =============================================================================
// NasSystem.Infrastructure - Services/SnapshotBackgroundService.cs
// 定时清单快照后台服务（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Configuration;
using NasSystem.Application.Snapshots;
using NasSystem.Domain.Repositories;

/// <summary>
/// 按 cron 表达式定时创建清单快照。项目此前没有任何定时框架
/// （Hangfire 两包零使用已清退），本服务用 ASP.NET Core 原生
/// BackgroundService，零新依赖。
/// </summary>
/// <remarks>
/// 每分钟 tick 一次判定 cron 匹配（个人 NAS 场景分钟粒度足够，
/// 不需要秒级调度器）。触发口径：
/// 一、单次失败降级不杀宿主——记 error 日志等下一个匹配分钟，
/// 快照失败不该让整个 NAS 停摆（对照 GlobalExceptionHandler 的
/// 同类取舍：可降级的后台动作不传播异常）。
/// 二、同分钟不重触发（lastFiredMinute 去重），防 tick 与 cron
/// 边界重叠。
/// 三、operator 取首个超级管理员：快照是系统级审计动作，
/// 不属于任何请求用户。
/// 四、创建后立即执行保留策略（keep-count），与手动创建同口径。
/// </remarks>
public sealed class SnapshotBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<SnapshotBackgroundService> logger,
    NasSystem.Domain.Services.IDateTimeProvider dateTime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("定时快照服务已启动（cron 由配置 snapshot.schedule 驱动）");

        // 启动即判定一次：重启进程后若当前分钟恰好匹配，不必再等满一分钟
        await TryFireAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
                await TryFireAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("定时快照服务已停止");
    }

    private async Task TryFireAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<ISystemConfigService>();

            if (!await config.GetBoolAsync(ConfigDescriptors.Keys.SnapshotEnabled, cancellationToken))
                return;

            var expression = await config.GetStringAsync(ConfigDescriptors.Keys.SnapshotSchedule, cancellationToken);
            if (!CronSchedule.TryParse(expression, out var schedule))
            {
                // 表达式非法只记日志不抛：用户改错配置不该反复刷栈，
                // 改对后下一分钟自然恢复
                logger.LogWarning("snapshot.schedule 不是合法的 cron 表达式: {Expression}", expression);
                return;
            }

            var now = dateTime.UtcNow;
            if (!schedule.Matches(now))
                return;

            // 同分钟去重（分钟级 tick 值用 long ticks 记，Volatile 泛型重载
            // 对非内置 struct 的兼容性更稳）：PeriodicTimer 抖动或重启落在
            // 同一分钟时不重拍
            var minuteTicks = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).Ticks;
            if (Interlocked.Read(ref _lastFiredMinuteTicks) == minuteTicks)
                return;

            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var operatorId = await users.GetFirstSuperAdminIdAsync(cancellationToken);
            if (operatorId is null)
            {
                logger.LogWarning("定时快照跳过：系统不存在超级管理员（无法归属操作人）");
                return;
            }

            var snapshots = scope.ServiceProvider.GetRequiredService<ISnapshotService>();
            var result = await snapshots.CreateAsync("/", "定时快照", operatorId.Value, cancellationToken);
            if (result.IsSuccess)
            {
                Interlocked.Exchange(ref _lastFiredMinuteTicks, minuteTicks);
                var keepCount = await config.GetIntAsync(ConfigDescriptors.Keys.SnapshotKeepCount, cancellationToken);
                var removed = await snapshots.ApplyRetentionAsync(keepCount, cancellationToken);
                logger.LogInformation(
                    "定时快照完成: 文件 {Files} 个 / 文件夹 {Folders} 个, 保留策略淘汰 {Removed} 份",
                    result.Value.FileCount, result.Value.FolderCount, removed);
            }
            else
            {
                logger.LogError("定时快照失败: {Error}", result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            // 降级：单次失败不影响后续 tick。宿主级别的取消不在此捕获范围内
            // （OperationCanceledException 由 ExecuteAsync 的循环处理）
            logger.LogError(ex, "定时快照执行异常（已降级，等待下一个匹配分钟）");
        }
    }

    private long _lastFiredMinuteTicks;
}

/// <summary>
/// 最小五段 cron 解析（分 时 日 月 周）。自写不引 Cronos：
/// 个人 NAS 只需要"每日几点"这一档频率，通用 cron 库 30KB 级依赖
/// 与收益不对等。支持：*、数字、a-b 范围、*/n 步长、逗号列表。
/// 周字段 0 与 7 都表示周日。非法表达式 TryParse 返回 false。
/// </summary>
internal sealed class CronSchedule
{
    private readonly HashSet<int> _minutes;
    private readonly HashSet<int> _hours;
    private readonly HashSet<int> _days;
    private readonly HashSet<int> _months;
    private readonly HashSet<int> _weekDays;

    private CronSchedule(HashSet<int> minutes, HashSet<int> hours, HashSet<int> days, HashSet<int> months, HashSet<int> weekDays)
    {
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekDays = weekDays;
    }

    public static bool TryParse(string expression, out CronSchedule schedule)
    {
        schedule = null!;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        if (!TryParseField(parts[0], 0, 59, out var minutes)) return false;
        if (!TryParseField(parts[1], 0, 23, out var hours)) return false;
        if (!TryParseField(parts[2], 1, 31, out var days)) return false;
        if (!TryParseField(parts[3], 1, 12, out var months)) return false;
        if (!TryParseField(parts[4], 0, 7, out var weekDaysRaw)) return false;

        // 周 7 归一为 0（都表示周日）
        var weekDays = new HashSet<int>(weekDaysRaw);
        if (weekDays.Remove(7))
            weekDays.Add(0);

        schedule = new CronSchedule(minutes, hours, days, months, weekDays);
        return true;
    }

    /// <summary>当前时间是否匹配（UTC 口径，与实体时间戳同源）</summary>
    public bool Matches(DateTime utcNow) =>
        _minutes.Contains(utcNow.Minute)
        && _hours.Contains(utcNow.Hour)
        && _days.Contains(utcNow.Day)
        && _months.Contains(utcNow.Month)
        && _weekDays.Contains((int)utcNow.DayOfWeek);

    private static bool TryParseField(string field, int min, int max, out HashSet<int> values)
    {
        values = [];
        foreach (var token in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var step = 1;
            var range = token;

            var slash = token.IndexOf('/');
            if (slash >= 0)
            {
                range = token[..slash];
                var stepText = token[(slash + 1)..];
                if (!int.TryParse(stepText, out step) || step < 1)
                    return false;
            }

            int from, to;
            if (range == "*")
            {
                from = min;
                to = max;
            }
            else
            {
                var dash = range.IndexOf('-');
                if (dash >= 0)
                {
                    if (!int.TryParse(range[..dash], out from) || !int.TryParse(range[(dash + 1)..], out to))
                        return false;
                }
                else
                {
                    if (!int.TryParse(range, out from))
                        return false;
                    to = step > 1 ? max : from; // "5/2" 形态：从 5 起每 2 到界
                }
            }

            if (from < min || to > max || from > to)
                return false;

            for (var v = from; v <= to; v += step)
                values.Add(v);
        }
        return values.Count > 0;
    }
}
