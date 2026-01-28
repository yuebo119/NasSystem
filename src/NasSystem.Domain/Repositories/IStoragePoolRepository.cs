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
    /// 根据名称获取存储池
    /// </summary>
    Task<StoragePool?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取默认存储池
    /// </summary>
    Task<StoragePool?> GetDefaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取启用的存储池列表
    /// </summary>
    Task<IReadOnlyList<StoragePool>> GetEnabledPoolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查名称是否已存在
    /// </summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查挂载路径是否已存在
    /// </summary>
    Task<bool> MountPathExistsAsync(string mountPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取存储池总使用情况
    /// </summary>
    Task<(long TotalBytes, long UsedBytes)> GetTotalUsageAsync(CancellationToken cancellationToken = default);
}
