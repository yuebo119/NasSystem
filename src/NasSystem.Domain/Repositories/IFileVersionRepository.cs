// =============================================================================
// NasSystem.Domain - IFileVersionRepository.cs
// 文件版本仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// 文件版本仓储接口
/// </summary>
public interface IFileVersionRepository
{
    /// <summary>
    /// 添加版本
    /// </summary>
    Task AddAsync(FileVersion version, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新版本
    /// </summary>
    void Update(FileVersion version);

    /// <summary>
    /// 删除版本
    /// </summary>
    void Delete(FileVersion version);

    /// <summary>
    /// 获取文件的所有版本
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本列表（按版本号降序）</returns>
    Task<IReadOnlyList<FileVersion>> GetByFileItemIdAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件的当前版本
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>当前版本</returns>
    Task<FileVersion?> GetCurrentVersionAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件的指定版本
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="versionNumber">版本号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>指定版本</returns>
    Task<FileVersion?> GetByVersionNumberAsync(
        Guid fileItemId,
        int versionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件的最新版本号
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最新版本号，如果没有版本则返回 0</returns>
    Task<int> GetLatestVersionNumberAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件的版本数量
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>版本数量</returns>
    Task<int> GetVersionCountAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件的所有版本
    /// </summary>
    /// <param name="fileItemId">文件 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DeleteByFileItemIdAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default);
}
