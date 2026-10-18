// =============================================================================
// NasSystem.Domain - IBackupDeviceRepository.cs
// 备份设备仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// 备份设备仓储
/// </summary>
public interface IBackupDeviceRepository
{
    Task AddAsync(BackupDevice device, CancellationToken cancellationToken = default);

    void Update(BackupDevice device);

    Task DeleteAsync(BackupDevice device, CancellationToken cancellationToken = default);

    Task<BackupDevice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>取用户的全部设备（含已吊销），按创建时间倒序</summary>
    Task<IReadOnlyList<BackupDevice>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>同名设备是否已存在（同用户内唯一：备份目录同名会互相覆盖）</summary>
    Task<bool> NameExistsAsync(Guid userId, string name, CancellationToken cancellationToken = default);
}
