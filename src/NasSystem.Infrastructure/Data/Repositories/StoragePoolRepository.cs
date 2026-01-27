// =============================================================================
// NasSystem.Infrastructure - StoragePoolRepository.cs
// 存储池仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;

/// <summary>
/// 存储池仓储实现
/// </summary>
public sealed class StoragePoolRepository(NasDbContext context) : RepositoryBase<StoragePool>(context), IStoragePoolRepository
{
    public async Task<StoragePool?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(sp => sp.Name == name, cancellationToken);
    }

    public async Task<StoragePool?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(sp => sp.IsDefault && sp.IsEnabled, cancellationToken);
    }

    public async Task<IReadOnlyList<StoragePool>> GetEnabledPoolsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(sp => sp.IsEnabled)
            .OrderBy(sp => sp.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoragePool>> GetHealthyPoolsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(sp => sp.IsEnabled && sp.Status == StoragePoolStatus.Healthy)
            .OrderBy(sp => sp.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<StoragePool?> GetWithDisksAsync(
        Guid poolId,
        CancellationToken cancellationToken = default)
    {
        return await Query()
            .Include(sp => sp.Disks)
            .FirstOrDefaultAsync(sp => sp.Id == poolId, cancellationToken);
    }

    public async Task<IReadOnlyList<StoragePool>> GetAllWithDisksAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Include(sp => sp.Disks)
            .OrderBy(sp => sp.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(sp => sp.Name == name, cancellationToken);
    }

    public async Task<(long TotalBytes, long UsedBytes)> GetTotalUsageAsync(CancellationToken cancellationToken = default)
    {
        var pools = await QueryNoTracking()
            .Where(sp => sp.IsEnabled)
            .ToListAsync(cancellationToken);

        var totalBytes = pools.Sum(sp => sp.TotalBytes);
        var usedBytes = pools.Sum(sp => sp.UsedBytes);

        return (totalBytes, usedBytes);
    }
}
