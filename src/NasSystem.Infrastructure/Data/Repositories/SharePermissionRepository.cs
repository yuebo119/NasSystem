// =============================================================================
// NasSystem.Infrastructure - SharePermissionRepository.cs
// 共享权限仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;

/// <summary>
/// 共享权限仓储实现
/// </summary>
public sealed class SharePermissionRepository(NasDbContext context) 
    : RepositoryBase<SharePermission>(context), ISharePermissionRepository
{
    public async Task<IReadOnlyList<SharePermission>> GetByShareAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Include(p => p.User)
            .Where(p => p.ShareId == shareId)
            .OrderBy(p => p.UserId.HasValue ? 0 : 1) // 用户权限优先
            .ThenBy(p => p.User != null ? p.User.Username : p.GroupName)
            .ToListAsync(cancellationToken);
    }

    public async Task<SharePermission?> GetUserPermissionAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(p => p.ShareId == shareId && p.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<SharePermission>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Include(p => p.Share)
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<PermissionLevel> GetEffectivePermissionAsync(
        Guid shareId,
        Guid userId,
        IEnumerable<string>? userGroups = null,
        CancellationToken cancellationToken = default)
    {
        var permissions = await QueryNoTracking()
            .Where(p => p.ShareId == shareId &&
                (p.UserId == userId ||
                 (userGroups != null && p.GroupName != null && userGroups.Contains(p.GroupName))))
            .ToListAsync(cancellationToken);

        if (permissions.Count == 0)
            return PermissionLevel.None;

        // 检查是否有显式拒绝
        if (permissions.Any(p => p.IsDeny))
            return PermissionLevel.None;

        // 返回最高权限级别
        return permissions.Max(p => p.Permission);
    }

    public async Task DeleteByShareAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var permissions = await Query()
            .Where(p => p.ShareId == shareId)
            .ToListAsync(cancellationToken);

        DbSet.RemoveRange(permissions);
    }

    public async Task DeleteByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var permissions = await Query()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        DbSet.RemoveRange(permissions);
    }
}
