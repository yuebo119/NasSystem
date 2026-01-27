// =============================================================================
// NasSystem.Domain - ISharePermissionRepository.cs
// 共享权限仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;

/// <summary>
/// 共享权限仓储接口
/// </summary>
public interface ISharePermissionRepository : IRepository<SharePermission>
{
    /// <summary>
    /// 获取共享的所有权限
    /// </summary>
    Task<IReadOnlyList<SharePermission>> GetByShareAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户在共享上的权限
    /// </summary>
    Task<SharePermission?> GetUserPermissionAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有权限
    /// </summary>
    Task<IReadOnlyList<SharePermission>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户在共享上的有效权限级别
    /// </summary>
    Task<PermissionLevel> GetEffectivePermissionAsync(
        Guid shareId,
        Guid userId,
        IEnumerable<string>? userGroups = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除共享的所有权限
    /// </summary>
    Task DeleteByShareAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除用户的所有权限
    /// </summary>
    Task DeleteByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
