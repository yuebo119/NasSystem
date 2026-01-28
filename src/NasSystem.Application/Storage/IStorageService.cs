// =============================================================================
// NasSystem.Application - IStorageService.cs
// 存储服务接口（简化版）
// =============================================================================

namespace NasSystem.Application.Storage;

using NasSystem.Application.Storage.Dtos;
using NasSystem.Shared;

/// <summary>
/// 存储服务接口（简化版）
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// 获取存储概览
    /// </summary>
    Task<Result<StorageOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取存储池列表
    /// </summary>
    Task<Result<IReadOnlyList<StoragePoolDto>>> GetPoolsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取存储池
    /// </summary>
    Task<Result<StoragePoolDto>> GetPoolByIdAsync(
        Guid poolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建存储池
    /// </summary>
    Task<Result<StoragePoolDto>> CreatePoolAsync(
        CreateStoragePoolRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新存储池
    /// </summary>
    Task<Result<StoragePoolDto>> UpdatePoolAsync(
        Guid poolId,
        UpdateStoragePoolRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除存储池
    /// </summary>
    Task<Result> DeletePoolAsync(
        Guid poolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置默认存储池
    /// </summary>
    Task<Result> SetDefaultPoolAsync(
        Guid poolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新容量信息
    /// </summary>
    Task<Result> RefreshCapacityAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查存储空间是否足够
    /// </summary>
    Task<Result<CheckStorageSpaceResponse>> CheckStorageSpaceAsync(
        CheckStorageSpaceRequest request,
        CancellationToken cancellationToken = default);
}
