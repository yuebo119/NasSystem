// =============================================================================
// NasSystem.Application - StorageDtos.cs
// 存储相关 DTO（简化版）
// =============================================================================

namespace NasSystem.Application.Storage.Dtos;

/// <summary>
/// 存储池 DTO（简化版）
/// </summary>
public sealed record StoragePoolDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string MountPath { get; init; }
    /// <summary>
    /// 池文件夹的实测大小。虚拟池即文件夹，池的大小就是文件夹的大小
    /// </summary>
    public required long FolderSizeBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public required double UsagePercentage { get; init; }
    public long? MaxCapacityBytes { get; init; }
    public string? MaxCapacityDisplay { get; init; }
    public string? FolderSizeDisplay { get; init; }
    public string? UsedDisplay { get; init; }
    public string? AvailableDisplay { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
    /// <summary>
    /// 池状态码，与前端既有映射对齐：1=良好（挂载点可达且磁盘就绪），
    /// 5=离线（挂载点不可达，此时容量数字不可信）
    /// </summary>
    public required int Status { get; init; }
    public required bool IsNearlyFull { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// 存储使用情况概览 DTO（简化版）
/// </summary>
public sealed record StorageOverviewDto
{
    /// <summary>
    /// 各池文件夹实测大小之和
    /// </summary>
    public required long FolderSizeBytes { get; init; }
    public required long UsedBytes { get; init; }
    /// <summary>
    /// 可用容量：文件夹的增长空间，按盘根去重后求和（同一盘上的多个池共享该盘剩余）
    /// </summary>
    public required long AvailableBytes { get; init; }
    /// <summary>
    /// 使用率，只对设置了容量上限的池有意义；未设上限时为 0，
    /// 展示层配合 <see cref="HasCapacityLimit"/> 决定是否渲染
    /// </summary>
    public required double UsagePercentage { get; init; }
    /// <summary>
    /// 是否全部启用池都设置了容量上限。只有为 true 时使用率才有意义
    /// </summary>
    public required bool HasCapacityLimit { get; init; }
    public string? FolderSizeDisplay { get; init; }
    public string? UsedDisplay { get; init; }
    public string? AvailableDisplay { get; init; }
    public required int PoolCount { get; init; }
    /// <summary>
    /// 就绪的物理磁盘数（DriveInfo.IsReady）
    /// </summary>
    public required int DiskCount { get; init; }
    /// <summary>
    /// 存储健康状态：Healthy 或 Warning。Warning 的判据是任一启用池
    /// 挂载点不可达、磁盘未就绪，或使用率超过 90%
    /// </summary>
    public required string HealthStatus { get; init; }
}

/// <summary>
/// 创建存储池请求（简化版）
/// </summary>
public sealed record CreateStoragePoolRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string MountPath { get; init; }
    /// <summary>
    /// 最大容量限制（字节），null 或 0 表示不限制
    /// </summary>
    public long? MaxCapacityBytes { get; init; }
}

/// <summary>
/// 更新存储池请求（简化版）
/// </summary>
public sealed record UpdateStoragePoolRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsEnabled { get; init; }
    /// <summary>
    /// 最大容量限制（字节），null 表示不修改，0 表示取消限制
    /// </summary>
    public long? MaxCapacityBytes { get; init; }
}

/// <summary>
/// 检查存储空间请求
/// </summary>
public sealed record CheckStorageSpaceRequest
{
    public required long FileSize { get; init; }
    public Guid? StoragePoolId { get; init; }
}

/// <summary>
/// 检查存储空间响应
/// </summary>
public sealed record CheckStorageSpaceResponse
{
    public required bool HasEnoughSpace { get; init; }
    public required long AvailableBytes { get; init; }
    public required long RequiredBytes { get; init; }
    public string? Message { get; init; }
}
