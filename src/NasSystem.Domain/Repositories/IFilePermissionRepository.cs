// =============================================================================
// NasSystem.Domain - IFilePermissionRepository.cs
// 文件权限仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;

/// <summary>
/// 文件权限仓储接口
/// </summary>
public interface IFilePermissionRepository : IRepository<FilePermission>
{
    /// <summary>
    /// 根据文件 ID 获取权限列表
    /// </summary>
    Task<IReadOnlyList<FilePermission>> GetByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据用户 ID 获取权限列表
    /// </summary>
    Task<IReadOnlyList<FilePermission>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取特定文件和用户的权限
    /// </summary>
    Task<FilePermission?> GetByFileAndUserAsync(Guid fileItemId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户对文件的有效权限级别
    /// </summary>
    Task<PermissionLevel> GetEffectivePermissionAsync(Guid fileItemId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件相关的所有权限
    /// </summary>
    Task DeleteByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除用户相关的所有权限
    /// </summary>
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
