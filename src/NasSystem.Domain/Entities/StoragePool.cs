// =============================================================================
// NasSystem.Domain - StoragePool.cs
// 存储池实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;

/// <summary>
/// 存储池实体
/// </summary>
public sealed class StoragePool : Entity
{
    /// <summary>
    /// 存储池名称
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 存储池描述
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 挂载路径
    /// </summary>
    public string MountPath { get; private set; } = string.Empty;

    /// <summary>
    /// 存储池状态
    /// </summary>
    public StoragePoolStatus Status { get; private set; } = StoragePoolStatus.Healthy;

    /// <summary>
    /// RAID 级别
    /// </summary>
    public RaidLevel RaidLevel { get; private set; } = RaidLevel.None;

    /// <summary>
    /// 总容量 (字节)
    /// </summary>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// 已使用容量 (字节)
    /// </summary>
    public long UsedBytes { get; private set; }

    /// <summary>
    /// 是否为默认存储池
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// 是否启用去重
    /// </summary>
    public bool EnableDeduplication { get; private set; }

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; private set; }

    // 导航属性
    public ICollection<StoragePoolDisk> Disks { get; private set; } = [];
    public ICollection<Share> Shares { get; private set; } = [];

    // EF Core 需要的无参构造函数
    private StoragePool() { }

    /// <summary>
    /// 创建存储池
    /// </summary>
    public static StoragePool Create(
        string name,
        string mountPath,
        RaidLevel raidLevel = RaidLevel.None,
        string? description = null)
    {
        var pool = new StoragePool
        {
            Name = name,
            Description = description,
            MountPath = mountPath,
            RaidLevel = raidLevel,
            Status = StoragePoolStatus.Initializing
        };

        pool.AddDomainEvent(new StoragePoolCreatedEvent(pool.Id, pool.Name));
        return pool;
    }

    /// <summary>
    /// 添加磁盘
    /// </summary>
    public void AddDisk(StoragePoolDisk disk)
    {
        Disks.Add(disk);
        RecalculateCapacity();
        MarkAsModified();
    }

    /// <summary>
    /// 移除磁盘
    /// </summary>
    public void RemoveDisk(StoragePoolDisk disk)
    {
        Disks.Remove(disk);
        RecalculateCapacity();
        MarkAsModified();
    }

    /// <summary>
    /// 更新状态
    /// </summary>
    public void UpdateStatus(StoragePoolStatus status)
    {
        var oldStatus = Status;
        Status = status;
        MarkAsModified();

        if (oldStatus != status)
        {
            AddDomainEvent(new StoragePoolStatusChangedEvent(Id, oldStatus, status));
        }
    }

    /// <summary>
    /// 更新使用量
    /// </summary>
    public void UpdateUsage(long usedBytes)
    {
        UsedBytes = usedBytes;
        MarkAsModified();
    }

    /// <summary>
    /// 设置为默认存储池
    /// </summary>
    public void SetAsDefault(bool isDefault)
    {
        IsDefault = isDefault;
        MarkAsModified();
    }

    /// <summary>
    /// 设置启用状态
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        MarkAsModified();
    }

    /// <summary>
    /// 设置优化选项
    /// </summary>
    public void SetOptimization(bool enableDeduplication, bool enableCompression)
    {
        EnableDeduplication = enableDeduplication;
        EnableCompression = enableCompression;
        MarkAsModified();
    }

    /// <summary>
    /// 重新计算容量
    /// </summary>
    private void RecalculateCapacity()
    {
        // 根据 RAID 级别计算有效容量
        var totalDiskBytes = Disks.Sum(d => d.SizeBytes);

        TotalBytes = RaidLevel switch
        {
            RaidLevel.None => totalDiskBytes,
            RaidLevel.Raid0 => totalDiskBytes,
            RaidLevel.Raid1 => totalDiskBytes / 2,
            RaidLevel.Raid5 => totalDiskBytes - (Disks.Count > 0 ? Disks.First().SizeBytes : 0),
            RaidLevel.Raid6 => totalDiskBytes - (Disks.Count > 1 ? Disks.Take(2).Sum(d => d.SizeBytes) : 0),
            RaidLevel.Raid10 => totalDiskBytes / 2,
            _ => totalDiskBytes
        };
    }

    /// <summary>
    /// 可用容量 (字节)
    /// </summary>
    public long AvailableBytes => TotalBytes - UsedBytes;

    /// <summary>
    /// 使用率 (0-100)
    /// </summary>
    public double UsagePercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;

    /// <summary>
    /// 是否即将满 (>90%)
    /// </summary>
    public bool IsNearlyFull => UsagePercentage > 90;
}
