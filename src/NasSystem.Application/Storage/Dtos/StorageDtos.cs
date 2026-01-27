// =============================================================================
// NasSystem.Application - StorageDtos.cs
// 存储相关 DTO
// =============================================================================

namespace NasSystem.Application.Storage.Dtos;

using NasSystem.Domain.Enums;

/// <summary>
/// 存储池 DTO
/// </summary>
public sealed record StoragePoolDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string MountPath { get; init; }
    public required StoragePoolStatus Status { get; init; }
    public required RaidLevel RaidLevel { get; init; }
    public required long TotalBytes { get; init; }
    public required long UsedBytes { get; init; }
    public required long AvailableBytes { get; init; }
    public required double UsagePercentage { get; init; }
    public string? TotalDisplay { get; init; }
    public string? UsedDisplay { get; init; }
    public string? AvailableDisplay { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool EnableDeduplication { get; init; }
    public required bool EnableCompression { get; init; }
    public required DateTime CreatedAt { get; init; }
    public IReadOnlyList<StoragePoolDiskDto>? Disks { get; init; }
}

/// <summary>
/// 存储池磁盘 DTO
/// </summary>
public sealed record StoragePoolDiskDto
{
    public required Guid Id { get; init; }
    public required string SerialNumber { get; init; }
    public required string Model { get; init; }
    public required string DevicePath { get; init; }
    public required long SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public required DiskType DiskType { get; init; }
    public required DiskStatus Status { get; init; }
    public required int HealthPercentage { get; init; }
    public int? TemperatureCelsius { get; init; }
    public long? PowerOnHours { get; init; }
    public required bool IsHotSpare { get; init; }
    public DateTime? LastCheckedAt { get; init; }
}

/// <summary>
/// 系统磁盘信息 DTO (未加入存储池的磁盘)
/// </summary>
public sealed record SystemDiskDto
{
    public required string DevicePath { get; init; }
    public required string SerialNumber { get; init; }
    public required string Model { get; init; }
    public required long SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public required DiskType DiskType { get; init; }
    public required DiskStatus Status { get; init; }
    public required int HealthPercentage { get; init; }
    public int? TemperatureCelsius { get; init; }
    public required bool IsSystemDisk { get; init; }
    public required bool IsInUse { get; init; }
}

/// <summary>
/// 存储使用情况概览 DTO
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
    public required int DiskCount { get; init; }
    public required int HealthyDiskCount { get; init; }
    public required int WarningDiskCount { get; init; }
    public required int CriticalDiskCount { get; init; }
}

/// <summary>
/// 创建存储池请求
/// </summary>
public sealed record CreateStoragePoolRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string MountPath { get; init; }
    public RaidLevel RaidLevel { get; init; } = RaidLevel.None;
    public required IReadOnlyList<string> DiskDevicePaths { get; init; }
    public bool EnableDeduplication { get; init; }
    public bool EnableCompression { get; init; }
}

/// <summary>
/// 更新存储池请求
/// </summary>
public sealed record UpdateStoragePoolRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? EnableDeduplication { get; init; }
    public bool? EnableCompression { get; init; }
}
