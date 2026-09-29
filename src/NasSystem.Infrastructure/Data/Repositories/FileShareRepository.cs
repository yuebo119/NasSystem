// =============================================================================
// NasSystem.Infrastructure - FileShareRepository.cs
// 文件分享仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 文件分享仓储实现
/// </summary>
public sealed class FileShareRepository(NasDbContext context) : RepositoryBase<FileShare>(context), IFileShareRepository
{
    public async Task<FileShare?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.FileItem)
            .Include(s => s.CreatedByUser)
            .FirstOrDefaultAsync(s => s.ShareCode == shareCode, cancellationToken);
    }

    public async Task<IReadOnlyList<FileShare>> GetByCreatorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.FileItem)
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(s => s.ShareCode == shareCode, cancellationToken);
    }
}
