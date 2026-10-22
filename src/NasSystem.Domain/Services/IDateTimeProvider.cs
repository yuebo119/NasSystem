// =============================================================================
// NasSystem.Domain - Services/IDateTimeProvider.cs
// 时间源抽象（CONVENTIONS §5.1 的目标态）
// =============================================================================

namespace NasSystem.Domain.Services;

/// <summary>
/// 时间源抽象。库代码经它取 UtcNow，不直接读 <c>DateTime.UtcNow</c>：
/// 过期判定、令牌过期、保留策略截止时间全部因此可测试、可回拨。
/// </summary>
/// <remarks>
/// 2026-10-22 审计 F-028 重新引入。ITM-N33 曾把旧版当死代码清掉（当时确实
/// 无注入方），此后版本/分享/备份等域又新增了大量硬编码 UtcNow，门禁
/// NAS-G15 基线（35 处）被推到 52 处。这次按「先服务层后实体层」的顺序
/// 渐进收口：服务层构造注入可达，实体工厂里的硬编码留待下一批（传时间戳
/// 入工厂），不做半程注入之外的风格混用。
/// </remarks>
public interface IDateTimeProvider
{
    /// <summary>当前 UTC 时间</summary>
    DateTime UtcNow { get; }
}
