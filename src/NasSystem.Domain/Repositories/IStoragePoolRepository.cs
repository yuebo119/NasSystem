// =============================================================================
// NasSystem.Domain - IStoragePoolRepository.cs
// 存储池仓储接口（简化版）
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// 存储池仓储接口（简化版）
/// </summary>
public interface IStoragePoolRepository : IRepository<StoragePool>
{
    /// <summary>
    /// 获取默认存储池
    /// </summary>
    Task<StoragePool?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查名称是否已存在
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查挂载路径是否已存在
    /// </summary>
    Task<bool> MountPathExistsAsync(string mountPath, CancellationToken cancellationToken = default);
}
