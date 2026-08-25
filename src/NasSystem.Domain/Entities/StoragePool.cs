// =============================================================================
// NasSystem.Domain - StoragePool.cs
// 存储池实体（简化版）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Events;

/// <summary>
/// 存储池实体（简化版）
/// 仅作为存储根目录配置，移除 RAID、磁盘管理等硬件相关功能
/// </summary>
public sealed class StoragePool : Entity
{
    /// <summary>
    /// 存储池名称
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 挂载路径（存储根目录）
    /// </summary>
    public string MountPath { get; private set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 是否为默认存储池
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// 总容量（从文件系统动态读取）
    /// </summary>
    public long TotalBytes { get; private set; }

    /// <summary>
    /// 已使用容量
    /// </summary>
    public long UsedBytes { get; private set; }

    /// <summary>
    /// 最大容量限制（null 表示不限制，使用磁盘实际容量）
    /// </summary>
    public long? MaxCapacityBytes { get; private set; }

    // EF Core 需要的无参构造函数
    private StoragePool() { }

    /// <summary>
    /// 创建存储池
    /// </summary>
    public static StoragePool Create(
        string name,
        string mountPath,
        string? description = null,
        long? maxCapacityBytes = null)
    {
        // ITM-N20：挂载点与名称是存储池寻址根，禁止空白；容量上限不可为负
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCapacityBytes ?? 0);

        var pool = new StoragePool
        {
            Name = name,
            MountPath = mountPath,
            Description = description,
            MaxCapacityBytes = maxCapacityBytes,
            IsDefault = false,
            IsEnabled = true
        };

        pool.AddDomainEvent(new StoragePoolCreatedEvent(pool.Id, pool.Name));
        return pool;
    }

    /// <summary>
    /// 更新存储池信息
    /// </summary>
    public void Update(string name, string? description, long? maxCapacityBytes = null)
    {
        Name = name;
        Description = description;
        if (maxCapacityBytes.HasValue)
            MaxCapacityBytes = maxCapacityBytes.Value > 0 ? maxCapacityBytes : null;
        MarkAsModified();
    }

    /// <summary>
    /// 设置最大容量限制
    /// </summary>
    public void SetMaxCapacity(long? maxCapacityBytes)
    {
        MaxCapacityBytes = maxCapacityBytes;
        MarkAsModified();
    }

    /// <summary>
    /// 更新容量信息（从文件系统读取）
    /// </summary>
    public void UpdateCapacity(long totalBytes, long usedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(usedBytes);
        TotalBytes = totalBytes;
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
    /// 有效总容量（考虑最大容量限制）
    /// </summary>
    public long EffectiveTotalBytes => MaxCapacityBytes.HasValue && MaxCapacityBytes.Value < TotalBytes 
        ? MaxCapacityBytes.Value 
        : TotalBytes;

    /// <summary>
    /// 可用容量（考虑最大容量限制）
    /// </summary>
    public long AvailableBytes => Math.Max(0, EffectiveTotalBytes - UsedBytes);

    /// <summary>
    /// 使用率 (0-100)，基于有效总容量
    /// </summary>
    public double UsagePercentage => EffectiveTotalBytes > 0 ? (double)UsedBytes / EffectiveTotalBytes * 100 : 0;

    /// <summary>
    /// 是否即将满 (>90%)
    /// </summary>
    public bool IsNearlyFull => UsagePercentage > 90;

    /// <summary>
    /// 检查是否有足够空间上传指定大小的文件
    /// </summary>
    public bool HasEnoughSpace(long fileSize) => AvailableBytes >= fileSize;
}
