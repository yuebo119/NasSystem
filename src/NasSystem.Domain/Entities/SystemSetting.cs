// =============================================================================
// NasSystem.Domain - SystemSetting.cs
// 系统动态配置项（键值存储）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;

/// <summary>
/// 系统配置项。appsettings 管基础设施（连接串、JWT 密钥等启动期定死的），
/// 本表管运行时行为参数（版本保留策略、OCR 开关等需要不重启调整的）。
/// 值统一存字符串，类型由读取方的 descriptor 解释
/// </summary>
public sealed class SystemSetting : Entity
{
    /// <summary>配置键（如 fileversion.max-count）。主键即键，无自增 Id 概念</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>配置值（字符串形态；bool 存 "true"/"false"，数字存十进制文本）</summary>
    public string Value { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }

    public static SystemSetting Create(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new SystemSetting
        {
            Id = Guid.NewGuid(),
            Key = key,
            Value = value ?? string.Empty,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateValue(string value)
    {
        Value = value ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
        MarkAsModified();
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static SystemSetting Restore(Guid id, string key, string value, DateTime updatedAt)
    {
        return new SystemSetting
        {
            Id = id,
            Key = key,
            Value = value,
            UpdatedAt = updatedAt
        };
    }
}
