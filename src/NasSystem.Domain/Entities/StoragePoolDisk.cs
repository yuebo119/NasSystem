// =============================================================================
// NasSystem.Domain - StoragePoolDisk.cs
// 存储池磁盘实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 存储池磁盘实体
/// </summary>
public sealed class StoragePoolDisk : Entity
{
    /// <summary>
    /// 所属存储池 ID
    /// </summary>
    public Guid StoragePoolId { get; private set; }

    /// <summary>
    /// 磁盘序列号
    /// </summary>
    public string SerialNumber { get; private set; } = string.Empty;

    /// <summary>
    /// 磁盘型号
    /// </summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>
    /// 设备路径 (如 \\.\PhysicalDrive0)
    /// </summary>
    public string DevicePath { get; private set; } = string.Empty;

    /// <summary>
    /// 磁盘容量 (字节)
    /// </summary>
    public long SizeBytes { get; private set; }

    /// <summary>
    /// 磁盘类型
    /// </summary>
    public DiskType DiskType { get; private set; }

    /// <summary>
    /// 磁盘状态
    /// </summary>
    public DiskStatus Status { get; private set; } = DiskStatus.Healthy;

    /// <summary>
    /// 健康度 (0-100)
    /// </summary>
    public int HealthPercentage { get; private set; } = 100;

    /// <summary>
    /// 温度 (摄氏度)
    /// </summary>
    public int? TemperatureCelsius { get; private set; }

    /// <summary>
    /// 通电时间 (小时)
    /// </summary>
    public long? PowerOnHours { get; private set; }

    /// <summary>
    /// 是否为热备盘
    /// </summary>
    public bool IsHotSpare { get; private set; }

    /// <summary>
    /// 最后检查时间
    /// </summary>
    public DateTime? LastCheckedAt { get; private set; }

    // 导航属性
    public StoragePool? StoragePool { get; private set; }

    // EF Core 需要的无参构造函数
    private StoragePoolDisk() { }

    /// <summary>
    /// 创建存储池磁盘
    /// </summary>
    public static StoragePoolDisk Create(
        Guid storagePoolId,
        string serialNumber,
        string model,
        string devicePath,
        long sizeBytes,
        DiskType diskType)
    {
        return new StoragePoolDisk
        {
            StoragePoolId = storagePoolId,
            SerialNumber = serialNumber,
            Model = model,
            DevicePath = devicePath,
            SizeBytes = sizeBytes,
            DiskType = diskType,
            Status = DiskStatus.Healthy
        };
    }

    /// <summary>
    /// 更新 SMART 信息
    /// </summary>
    public void UpdateSmartInfo(int healthPercentage, int? temperatureCelsius, long? powerOnHours)
    {
        HealthPercentage = healthPercentage;
        TemperatureCelsius = temperatureCelsius;
        PowerOnHours = powerOnHours;
        LastCheckedAt = DateTime.UtcNow;
        
        // 根据健康度更新状态
        Status = healthPercentage switch
        {
            >= 80 => DiskStatus.Healthy,
            >= 50 => DiskStatus.Warning,
            _ => DiskStatus.Critical
        };
        
        MarkAsModified();
    }

    /// <summary>
    /// 更新状态
    /// </summary>
    public void UpdateStatus(DiskStatus status)
    {
        Status = status;
        MarkAsModified();
    }

    /// <summary>
    /// 设置为热备盘
    /// </summary>
    public void SetHotSpare(bool isHotSpare)
    {
        IsHotSpare = isHotSpare;
        MarkAsModified();
    }

    /// <summary>
    /// 是否需要更换 (健康度 < 30%)
    /// </summary>
    public bool NeedsReplacement => HealthPercentage < 30 || Status == DiskStatus.Failed;

    /// <summary>
    /// 格式化后的容量
    /// </summary>
    public string FormattedSize => FormatBytes(SizeBytes);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
