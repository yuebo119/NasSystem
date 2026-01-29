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
