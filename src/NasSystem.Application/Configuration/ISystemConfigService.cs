// =============================================================================
// NasSystem.Application - Configuration/ISystemConfigService.cs
// 系统动态配置服务（descriptor 驱动）
// =============================================================================

namespace NasSystem.Application.Configuration;

using NasSystem.Shared;

/// <summary>
/// 系统动态配置。appsettings 管启动期定死的基础设施（连接串/JWT 密钥），
/// 本服务管运行时行为参数：存 SQLite 键值表，改完即生效不重启。
/// </summary>
/// <remarks>
/// descriptor 是前后端的单一事实源：后端按它校验写入，前端按它渲染表单
/// （labelKey/descriptionKey 是 i18n 键）。新增一个可配置项 = 后端加一条
/// descriptor + 在消费方读一次，前端零改动。
/// </remarks>
public interface ISystemConfigService
{
    /// <summary>取全量快照（descriptor + 当前生效值），供配置页渲染</summary>
    Task<SystemConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量更新。校验失败整单拒绝（返回哪些键错、为什么），
    /// 不做部分应用：配置是给系统用的，半组生效比全不生效更难排查
    /// </summary>
    Task<Result> UpdateAsync(
        IReadOnlyDictionary<string, string> values,
        Guid operatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>取整型配置（无记录或解析失败回落 descriptor 默认值）</summary>
    Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>取布尔配置（无记录或解析失败回落 descriptor 默认值）</summary>
    Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>取字符串配置（无记录回落 descriptor 默认值）</summary>
    Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>配置值类型。决定前端渲染什么控件、后端怎么校验</summary>
public enum ConfigValueType
{
    /// <summary>整数（带 min/max 的 el-input-number）</summary>
    Int,

    /// <summary>布尔（el-switch）</summary>
    Bool,

    /// <summary>短字符串（el-input，如 cron 表达式）</summary>
    String
}

/// <summary>配置项描述符</summary>
public sealed record ConfigDescriptor(
    string Key,
    string Group,
    string LabelKey,
    ConfigValueType Type,
    string DefaultValue,
    int? Min = null,
    int? Max = null,
    string? DescriptionKey = null);

/// <summary>配置分组（一个分组 = 配置页的一张卡）</summary>
public sealed record ConfigGroupDto(
    string Group,
    string TitleKey,
    IReadOnlyList<ConfigItemDto> Items);

/// <summary>配置项（descriptor + 当前生效值）</summary>
public sealed record ConfigItemDto(
    string Key,
    string LabelKey,
    ConfigValueType Type,
    string Value,
    string DefaultValue,
    int? Min,
    int? Max,
    string? DescriptionKey);

/// <summary>配置快照</summary>
public sealed record SystemConfigSnapshot(
    IReadOnlyList<ConfigGroupDto> Groups,
    DateTime? LastUpdatedAt);
