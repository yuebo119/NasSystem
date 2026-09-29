// =============================================================================
// NasSystem.Infrastructure - StoragePoolRepository.cs
// 存储池仓储实现（简化版）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 存储池仓储实现（简化版）
/// </summary>
public sealed class StoragePoolRepository(NasDbContext context) : RepositoryBase<StoragePool>(context), IStoragePoolRepository
{
    public async Task<StoragePool?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await Query()
            .FirstOrDefaultAsync(sp => sp.IsDefault && sp.IsEnabled, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(sp => sp.Name == name, cancellationToken);
    }

    public async Task<bool> MountPathExistsAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(sp => sp.MountPath == mountPath, cancellationToken);
    }
}
