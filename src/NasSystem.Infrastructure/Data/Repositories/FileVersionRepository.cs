// =============================================================================
// NasSystem.Infrastructure - FileVersionRepository.cs
// 文件版本仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 文件版本仓储实现
/// </summary>
public class FileVersionRepository : IFileVersionRepository
{
    private readonly NasDbContext _context;
    private DbSet<FileVersion> DbSet => _context.FileVersions;

    public FileVersionRepository(NasDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(FileVersion version, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(version, cancellationToken);
    }

    /// <inheritdoc />
    public void Update(FileVersion version)
    {
        DbSet.Update(version);
    }

    /// <inheritdoc />
    public void Delete(FileVersion version)
    {
        DbSet.Remove(version);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileVersion>> GetByFileItemIdAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(v => v.FileItemId == fileItemId)
            .OrderByDescending(v => v.VersionNumber)
            .Include(v => v.CreatedByUser)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FileVersion?> GetCurrentVersionAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(v => v.FileItemId == fileItemId && v.IsCurrent)
            .Include(v => v.CreatedByUser)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FileVersion?> GetByVersionNumberAsync(
        Guid fileItemId,
        int versionNumber,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(v => v.FileItemId == fileItemId && v.VersionNumber == versionNumber)
            .Include(v => v.CreatedByUser)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetLatestVersionNumberAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        var maxVersion = await DbSet
            .Where(v => v.FileItemId == fileItemId)
            .MaxAsync(v => (int?)v.VersionNumber, cancellationToken);

        return maxVersion ?? 0;
    }
}
