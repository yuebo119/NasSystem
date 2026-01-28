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
    public required long TotalBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public required double UsagePercentage { get; init; }
    public long? MaxCapacityBytes { get; init; }
    public string? MaxCapacityDisplay { get; init; }
    public string? TotalDisplay { get; init; }
    public string? UsedDisplay { get; init; }
    public string? AvailableDisplay { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsNearlyFull { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// 存储使用情况概览 DTO（简化版）
/// </summary>
public sealed record StorageOverviewDto
{
    public required long TotalBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public required double UsagePercentage { get; init; }
    public string? TotalDisplay { get; init; }
    public string? UsedDisplay { get; init; }
    public string? AvailableDisplay { get; init; }
    public required int PoolCount { get; init; }
    public string HealthStatus { get; init; } = "Healthy";
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
