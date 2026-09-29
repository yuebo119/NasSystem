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
        // NoTracking：公开分享信息/密码校验/目录浏览都是只读路径；
        // 下载计数走显式 UpdateAsync（DbSet.Update 可作用于分离实体）
        return await QueryNoTracking()
            .Include(s => s.FileItem)
            .Include(s => s.CreatedByUser)
            .FirstOrDefaultAsync(s => s.ShareCode == shareCode, cancellationToken);
    }

    public async Task<IReadOnlyList<FileShare>> GetByCreatorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // 同时 Include 文件项与创建者：调用方映射 DTO 需要两者，
        // 原实现只 Include FileItem，创建者显示名按 share 逐个查库（1+2N）
        return await DbSet
            .Include(s => s.FileItem)
            .Include(s => s.CreatedByUser)
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
