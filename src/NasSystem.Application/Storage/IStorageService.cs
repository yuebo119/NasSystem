// =============================================================================
// NasSystem.Application - IStorageService.cs
// 存储服务接口
// =============================================================================

namespace NasSystem.Application.Storage;

using NasSystem.Application.Storage.Dtos;
using NasSystem.Shared;

/// <summary>
/// 存储服务接口
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
    /// 获取系统磁盘列表
    /// </summary>
    Task<Result<IReadOnlyList<SystemDiskDto>>> GetSystemDisksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取可用磁盘列表 (未加入存储池)
    /// </summary>
    Task<Result<IReadOnlyList<SystemDiskDto>>> GetAvailableDisksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 向存储池添加磁盘
    /// </summary>
    Task<Result<StoragePoolDiskDto>> AddDiskToPoolAsync(
        Guid poolId,
        string devicePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 从存储池移除磁盘
    /// </summary>
    Task<Result> RemoveDiskFromPoolAsync(
        Guid poolId,
        Guid diskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描磁盘 SMART 信息
    /// </summary>
    Task<Result> ScanDiskHealthAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新存储使用情况
    /// </summary>
    Task<Result> RefreshUsageAsync(
        CancellationToken cancellationToken = default);
}
