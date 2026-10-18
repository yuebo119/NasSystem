// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/BackupDeviceRepository.cs
// 备份设备仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>备份设备仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class BackupDeviceRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IBackupDeviceRepository
{
    public Task AddAsync(BackupDevice device, CancellationToken cancellationToken = default)
    {
        var row = device.ToRow();
        unitOfWork.EnqueueInsert(device, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Update(BackupDevice device)
    {
        var row = device.ToRow();
        unitOfWork.EnqueueUpdate(device, async (db, ct) => await db.UpdateAsync(row, ct));
    }

    public Task DeleteAsync(BackupDevice device, CancellationToken cancellationToken = default)
    {
        var id = device.Id;
        unitOfWork.EnqueueDelete(device, async (db, ct) => await db.DeleteAsync<BackupDeviceRow>(id, ct));
        return Task.CompletedTask;
    }

    public async Task<BackupDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<BackupDeviceRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<BackupDevice>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<BackupDeviceRow>()
            .Where($"UserId = {userId}")
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<bool> NameExistsAsync(
        Guid userId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<BackupDeviceRow>(
            $"UserId = {userId} AND Name = {name}", cancellationToken);
        return count > 0;
    }
}
