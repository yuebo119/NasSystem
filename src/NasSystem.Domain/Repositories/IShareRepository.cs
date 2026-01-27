// =============================================================================
// NasSystem.Domain - IShareRepository.cs
// 共享仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// 共享仓储接口
/// </summary>
public interface IShareRepository : IRepository<Share>
{
    /// <summary>
    /// 根据名称获取共享
    /// </summary>
    Task<Share?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据路径获取共享
    /// </summary>
    Task<Share?> GetByPathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取启用的共享列表
    /// </summary>
    Task<IReadOnlyList<Share>> GetEnabledSharesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取公开共享列表
    /// </summary>
    Task<IReadOnlyList<Share>> GetPublicSharesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户可访问的共享列表
    /// </summary>
    Task<IReadOnlyList<Share>> GetAccessibleByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取存储池的共享列表
    /// </summary>
    Task<IReadOnlyList<Share>> GetByStoragePoolAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 包含权限的共享查询
    /// </summary>
    Task<Share?> GetWithPermissionsAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查名称是否已存在
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);
}
