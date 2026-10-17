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
    /// 池大小：NAS 管理的文件总量（数据库记账）。
    /// </summary>
    /// <remarks>
    /// 2026-10-17 用户裁决"以库记账为准"后从物理实测改来：原实现递归扫描挂载点
    /// 目录求和，十万文件的池要数秒且阻塞请求线程；改为库内 SUM 后读路径是一条
    /// 聚合 SQL。语义变化：外部直接放到盘上、不经本应用的文件不再计入池大小。
    /// 与 <see cref="UsedBytes"/> 同源同值，两个字段保留是 API 与存量列的兼容
    /// （存储列名 TotalBytes 是历史遗留，未做列迁移）。
    /// </remarks>
    public long FolderSizeBytes { get; private set; }

    /// <summary>
    /// 已使用容量：NAS 管理的文件占用（数据库记账）。与 <see cref="FolderSizeBytes"/>
    /// 同源同值，见其注释
    /// </summary>
    public long UsedBytes { get; private set; }

    /// <summary>
    /// 最大容量限制（null 表示不限制）。池本身没有容量概念，
    /// 上限是用户给文件夹划的账目上限
    /// </summary>
    public long? MaxCapacityBytes { get; private set; }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCapacityBytes ?? 0);
        Name = name;
        Description = description;
        if (maxCapacityBytes.HasValue)
            MaxCapacityBytes = maxCapacityBytes.Value > 0 ? maxCapacityBytes : null;
        MarkAsModified();
    }

    /// <summary>
    /// 更新池大小与已用容量。2026-10-17 起两者同源：都是 NAS 管理的文件
    /// 数据库记账 SUM（用户裁决"以库记账为准"，原 folderSizeBytes 扫目录、
    /// usedBytes 查数据库的两个来源口径废止）。单参数即因为这个同源。
    /// </summary>
    public void UpdateUsage(long managedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(managedBytes);
        FolderSizeBytes = managedBytes;
        UsedBytes = managedBytes;
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
    /// 配额剩余：设置了容量上限时为 上限 − 已用（不为负），未设置时无上限返回 null
    /// </summary>
    public long? QuotaRemainingBytes =>
        MaxCapacityBytes.HasValue ? Math.Max(0, MaxCapacityBytes.Value - UsedBytes) : null;

    /// <summary>
    /// 可用容量：这个文件夹还能往里长多少。取配额剩余与磁盘物理剩余的较小值，
    /// 未设备配额时只受物理剩余约束。<paramref name="physicalFreeBytes"/> 为 null
    /// （挂载点不可达或路径取不到盘根）时退回配额剩余，两者都不可知返回 null
    /// </summary>
    public long? AvailableBytesWithin(long? physicalFreeBytes)
    {
        var quotaCap = QuotaRemainingBytes ?? long.MaxValue;
        if (physicalFreeBytes.HasValue)
            return Math.Min(quotaCap, physicalFreeBytes.Value);
        return QuotaRemainingBytes;
    }

    /// <summary>
    /// 指定大小是否放得下。物理剩余不可知且无配额时（无从判断）返回 false，
    /// 由健康状态的 Warning 暴露挂载点不可达
    /// </summary>
    public bool HasEnoughSpace(long fileSize, long? physicalFreeBytes) =>
        AvailableBytesWithin(physicalFreeBytes) is { } available && fileSize <= available;

    /// <summary>
    /// 使用率 (0-100)，只对设置了容量上限的池有意义：已用占上限的比例。
    /// 未设上限的池没有容量概念，返回 0，展示层据此不渲染百分比
    /// </summary>
    public double UsagePercentage => MaxCapacityBytes > 0
        ? (double)UsedBytes / MaxCapacityBytes.Value * 100
        : 0;

    /// <summary>
    /// 是否即将满 (&gt;90%)，只对设置了容量上限的池有意义
    /// </summary>
    public bool IsNearlyFull => MaxCapacityBytes > 0 && UsagePercentage > 90;

    /// <summary>
    /// 从持久化状态恢复实体（供基础设施层影子模型映射使用）。动因见 <see cref="FileItem.Restore"/>。
    /// </summary>
    internal static StoragePool Restore(
        Guid id,
        string name,
        string mountPath,
        string? description,
        bool isDefault,
        bool isEnabled,
        long folderSizeBytes,
        long usedBytes,
        long? maxCapacityBytes,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        return new StoragePool
        {
            Id = id,
            Name = name,
            MountPath = mountPath,
            Description = description,
            IsDefault = isDefault,
            IsEnabled = isEnabled,
            FolderSizeBytes = folderSizeBytes,
            UsedBytes = usedBytes,
            MaxCapacityBytes = maxCapacityBytes,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }
}
