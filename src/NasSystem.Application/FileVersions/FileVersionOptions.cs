// =============================================================================
// NasSystem.Application - FileVersions/FileVersionOptions.cs
// 文件版本保留策略配置（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Application.FileVersions;

/// <summary>
/// 版本保留策略。配置节 NasSystem:FileVersion，未配置时取默认值
/// </summary>
public sealed class FileVersionOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "NasSystem:FileVersion";

    /// <summary>
    /// 单文件最多保留的版本数。每次产新版本后清理最旧的超出部分
    /// </summary>
    public int MaxVersionsPerFile { get; set; } = 10;

    /// <summary>
    /// 版本最长保留天数。超过天数的版本在每次产新版本时一并清理。
    /// 兜底上限：长期不覆盖的文件其历史版本也会过期消失
    /// </summary>
    public int MaxVersionAgeDays { get; set; } = 30;
}
