// =============================================================================
// NasSystem.Infrastructure - Configuration/SystemConfigService.cs
// 系统配置服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Configuration;

using Microsoft.Extensions.Logging;
using NasSystem.Application.Configuration;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 配置服务实现。读路径每次查库（配置项个位数，点查询可忽略，
/// 且配置改完即生效不缓存——缓存反而要处理失效），写路径先全量校验
/// 再整单落库
/// </summary>
public sealed class SystemConfigService(
    ISystemSettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    ILogger<SystemConfigService> logger) : ISystemConfigService
{
    public async Task<SystemConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settingRepository.GetAllAsync(cancellationToken);
        var storedMap = stored.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);

        var groups = ConfigDescriptors.GroupTitles.Select(group => new ConfigGroupDto(
            group.Group,
            group.TitleKey,
            ConfigDescriptors.All
                .Where(d => d.Group == group.Group)
                .Select(d => new ConfigItemDto(
                    d.Key,
                    d.LabelKey,
                    d.Type,
                    // 生效值 = 库中值（解析得了）或 descriptor 默认值
                    ResolveValue(d, storedMap),
                    d.DefaultValue,
                    d.Min,
                    d.Max,
                    d.DescriptionKey))
                .ToList()))
            .ToList();

        var lastUpdated = stored.Count > 0 ? stored.Max(s => s.UpdatedAt) : (DateTime?)null;
        return new SystemConfigSnapshot(groups, lastUpdated);
    }

    public async Task<Result> UpdateAsync(
        IReadOnlyDictionary<string, string> values,
        Guid operatorUserId,
        CancellationToken cancellationToken = default)
    {
        // ① 全量校验：任一键 unknown/越界/类型错，整单拒绝
        var errors = new List<string>();
        foreach (var (key, rawValue) in values)
        {
            var descriptor = ConfigDescriptors.Find(key);
            if (descriptor is null)
            {
                errors.Add($"未知配置项: {key}");
                continue;
            }

            var validationError = Validate(descriptor, rawValue);
            if (validationError is not null)
            {
                errors.Add(validationError);
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(new Error("Config.ValidationFailed", string.Join("；", errors)));
        }

        // ② 逐键 upsert。已存在的改值，不存在的插入
        foreach (var (key, rawValue) in values)
        {
            var normalized = Normalize(key, rawValue);
            var existing = await settingRepository.GetByKeyAsync(key, cancellationToken);
            if (existing is null)
            {
                await settingRepository.AddAsync(
                    Domain.Entities.SystemSetting.Create(key, normalized), cancellationToken);
            }
            else if (!string.Equals(existing.Value, normalized, StringComparison.Ordinal))
            {
                existing.UpdateValue(normalized);
                settingRepository.Update(existing);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "系统配置已更新: 操作人={Operator}, 变更 {Count} 项", operatorUserId, values.Count);
        return Result.Success();
    }

    public async Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default)
    {
        var stored = await settingRepository.GetByKeyAsync(key, cancellationToken);
        if (stored is not null && int.TryParse(stored.Value, out var parsed))
        {
            return parsed;
        }
        return ConfigDescriptors.IntDefault(key);
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default)
    {
        var stored = await settingRepository.GetByKeyAsync(key, cancellationToken);
        if (stored is not null && bool.TryParse(stored.Value, out var parsed))
        {
            return parsed;
        }
        return ConfigDescriptors.BoolDefault(key);
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    /// <summary>库中值可用（解析通过且在界内）才采用，否则回落默认值</summary>
    private static string ResolveValue(ConfigDescriptor descriptor, Dictionary<string, string> storedMap)
    {
        if (storedMap.TryGetValue(descriptor.Key, out var stored) && Validate(descriptor, stored) is null)
        {
            return Normalize(descriptor.Key, stored);
        }
        return descriptor.DefaultValue;
    }

    private static string? Validate(ConfigDescriptor descriptor, string rawValue)
    {
        switch (descriptor.Type)
        {
            case ConfigValueType.Bool:
                if (!bool.TryParse(rawValue, out _))
                    return $"{descriptor.Key} 需要 true/false";
                return null;

            case ConfigValueType.Int:
                if (!int.TryParse(rawValue, out var intValue))
                    return $"{descriptor.Key} 需要整数";
                if (descriptor.Min is { } min && intValue < min)
                    return $"{descriptor.Key} 不能小于 {min}";
                if (descriptor.Max is { } max && intValue > max)
                    return $"{descriptor.Key} 不能大于 {max}";
                return null;

            default:
                return $"{descriptor.Key} 类型不支持";
        }
    }

    /// <summary>归一化存储形态：bool 统一小写 true/false，int 去空白</summary>
    private static string Normalize(string key, string rawValue)
    {
        var descriptor = ConfigDescriptors.Find(key);
        var trimmed = rawValue.Trim();
        return descriptor?.Type == ConfigValueType.Bool
            ? (bool.TryParse(trimmed, out var b) && b ? "true" : "false")
            : trimmed;
    }
}
