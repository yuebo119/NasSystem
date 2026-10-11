// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/StoragePoolRepository.cs
// 存储池仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>存储池仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class StoragePoolRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IStoragePoolRepository
{
    public async Task<StoragePool?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        // EF 时代这里走跟踪查询（Query() 而非 QueryNoTracking()），因为调用方
        // SetAsDefault 后要保存；PalORM 无跟踪概念，写入走意图队列，读一律无跟踪
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<StoragePoolRow>()
            .Where($"IsDefault = 1 AND IsEnabled = 1")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<StoragePoolRow>($"Name = {name}", cancellationToken);
        return count > 0;
    }

    public async Task<bool> MountPathExistsAsync(string mountPath, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<StoragePoolRow>($"MountPath = {mountPath}", cancellationToken);
        return count > 0;
    }

    public async ValueTask<StoragePool?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<StoragePoolRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<StoragePool>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<StoragePoolRow>().ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public Task<StoragePool> AddAsync(StoragePool entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueInsert(entity, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(StoragePool entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueUpdate(entity, async (db, ct) => await db.UpdateAsync(row, ct));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(StoragePool entity, CancellationToken cancellationToken = default)
    {
        var id = entity.Id;
        unitOfWork.EnqueueDelete(entity, async (db, ct) => await db.DeleteAsync<StoragePoolRow>(id, ct));
        return Task.CompletedTask;
    }
}
