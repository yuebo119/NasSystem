// =============================================================================
// NasSystem.Domain - Enums.cs
// 领域枚举定义
// =============================================================================

namespace NasSystem.Domain.Enums;

/// <summary>
/// 用户角色
/// </summary>
public enum UserRole
{
    /// <summary>
    /// 普通用户
    /// </summary>
    User = 0,

    /// <summary>
    /// 高级用户
    /// </summary>
    PowerUser = 1,

    /// <summary>
    /// 管理员
    /// </summary>
    Admin = 2,

    /// <summary>
    /// 超级管理员
    /// </summary>
    SuperAdmin = 3
}

/// <summary>
/// 文件项类型
/// </summary>
public enum FileItemType
{
    /// <summary>
    /// 文件
    /// </summary>
    File = 0,

    /// <summary>
    /// 文件夹
    /// </summary>
    Folder = 1,

    /// <summary>
    /// 符号链接
    /// </summary>
    SymbolicLink = 2
}

/// <summary>
/// 共享类型（已废弃，保留兼容）
/// </summary>
[Obsolete("使用 ShareAccessType 替代")]
public enum ShareType
{
    Smb = 0,
    Nfs = 1,
    WebDav = 2,
    Ftp = 3
}

/// <summary>
/// 分享访问类型
/// </summary>
public enum ShareAccessType
{
    /// <summary>
    /// 公开访问（任何人可访问）
    /// </summary>
    Public = 0,

    /// <summary>
    /// 密码保护
    /// </summary>
    Password = 1,

    /// <summary>
    /// 仅内部用户（需登录）
    /// </summary>
    Internal = 2
}

/// <summary>
/// 权限级别
/// </summary>
public enum PermissionLevel
{
    /// <summary>
    /// 无权限
    /// </summary>
    None = 0,

    /// <summary>
    /// 只读
    /// </summary>
    Read = 1,

    /// <summary>
    /// 读写
    /// </summary>
    ReadWrite = 2,

    /// <summary>
    /// 完全控制
    /// </summary>
    FullControl = 3
}

/// <summary>
/// 存储池状态
/// </summary>
public enum StoragePoolStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing = 1,

    /// <summary>
    /// 降级 (部分磁盘故障)
    /// </summary>
    Degraded = 2,

    /// <summary>
    /// 重建中
    /// </summary>
    Rebuilding = 3,

    /// <summary>
    /// 已损坏
    /// </summary>
    Failed = 4,

    /// <summary>
    /// 离线
    /// </summary>
    Offline = 5
}

/// <summary>
/// RAID 级别
/// </summary>
public enum RaidLevel
{
    /// <summary>
    /// 无 RAID (JBOD)
    /// </summary>
    None = 0,

    /// <summary>
    /// RAID 0 (条带化)
    /// </summary>
    Raid0 = 1,

    /// <summary>
    /// RAID 1 (镜像)
    /// </summary>
    Raid1 = 2,

    /// <summary>
    /// RAID 5 (分布式奇偶校验)
    /// </summary>
    Raid5 = 5,

    /// <summary>
    /// RAID 6 (双重分布式奇偶校验)
    /// </summary>
    Raid6 = 6,

    /// <summary>
    /// RAID 10 (镜像条带化)
    /// </summary>
    Raid10 = 10
}

/// <summary>
/// 磁盘类型
/// </summary>
public enum DiskType
{
    /// <summary>
    /// 机械硬盘
    /// </summary>
    Hdd = 0,

    /// <summary>
    /// 固态硬盘
    /// </summary>
    Ssd = 1,

    /// <summary>
    /// NVMe 固态硬盘
    /// </summary>
    Nvme = 2,

    /// <summary>
    /// USB 外置硬盘
    /// </summary>
    Usb = 3
}

/// <summary>
/// 磁盘状态
/// </summary>
public enum DiskStatus
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 警告
    /// </summary>
    Warning = 1,

    /// <summary>
    /// 严重
    /// </summary>
    Critical = 2,

    /// <summary>
    /// 已失效
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown = 4
}

/// <summary>
/// 任务状态
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// 等待中
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 运行中
    /// </summary>
    Running = 1,

    /// <summary>
    /// 已完成
    /// </summary>
    Completed = 2,

    /// <summary>
    /// 已失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// 已暂停
    /// </summary>
    Paused = 5
}
