// =============================================================================
// NasSystem.Domain - ISystemSettingRepository.cs
// 系统配置仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// 系统配置仓储。键值表，无按用户过滤（全局单例配置）
/// </summary>
public interface ISystemSettingRepository
{
    Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    void Update(SystemSetting setting);

    Task DeleteAsync(SystemSetting setting, CancellationToken cancellationToken = default);

    /// <summary>按键取（键是主键路径上的唯一索引，拿不到返回 null）</summary>
    Task<SystemSetting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>全量取（配置项是个位数，一次性载入内存无压力）</summary>
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken = default);
}
