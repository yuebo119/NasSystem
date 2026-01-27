// =============================================================================
// NasSystem.Infrastructure - ShareRepository.cs
// 共享仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 共享仓储实现
/// </summary>
public sealed class ShareRepository(NasDbContext context) : RepositoryBase<Share>(context), IShareRepository
{
    public async Task<Share?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<Share?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(s => s.Path == path, cancellationToken);
    }

    public async Task<IReadOnlyList<Share>> GetEnabledSharesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Share>> GetPublicSharesAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(s => s.IsEnabled && s.IsPublic)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Share>> GetAccessibleByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(s => s.IsEnabled &&
                (s.IsPublic ||
                 s.CreatedByUserId == userId ||
                 s.Permissions.Any(p => p.UserId == userId && !p.IsDeny)))
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Share>> GetByStoragePoolAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(s => s.StoragePoolId == storagePoolId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Share?> GetWithPermissionsAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        return await Query()
            .Include(s => s.Permissions)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(s => s.Name == name, cancellationToken);
    }
}
