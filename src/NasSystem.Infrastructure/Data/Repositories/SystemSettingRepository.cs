// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/SystemSettingRepository.cs
// 系统配置仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>系统配置仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class SystemSettingRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : ISystemSettingRepository
{
    public Task AddAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        var row = setting.ToRow();
        unitOfWork.EnqueueInsert(setting, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Update(SystemSetting setting)
    {
        var row = setting.ToRow();
        unitOfWork.EnqueueUpdate(setting, async (db, ct) => await db.UpdateAsync(row, ct));
    }

    public Task DeleteAsync(SystemSetting setting, CancellationToken cancellationToken = default)
    {
        var id = setting.Id;
        unitOfWork.EnqueueDelete(setting, async (db, ct) => await db.DeleteAsync<SystemSettingRow>(id, ct));
        return Task.CompletedTask;
    }

    public async Task<SystemSetting?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<SystemSettingRow>()
            .Where($"Key = {key}")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<SystemSettingRow>()
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }
}
