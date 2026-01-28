// =============================================================================
// NasSystem.Domain - IFileShareRepository.cs
// 文件分享仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// 文件分享仓储接口
/// </summary>
public interface IFileShareRepository : IRepository<FileShare>
{
    /// <summary>
    /// 根据分享码获取分享
    /// </summary>
    Task<FileShare?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据文件 ID 获取分享列表
    /// </summary>
    Task<IReadOnlyList<FileShare>> GetByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据创建者获取分享列表
    /// </summary>
    Task<IReadOnlyList<FileShare>> GetByCreatorAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查分享码是否存在
    /// </summary>
    Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有分享
    /// </summary>
    Task<IReadOnlyList<FileShare>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件相关的所有分享
    /// </summary>
    Task DeleteByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);
}
