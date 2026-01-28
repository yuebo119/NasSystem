// =============================================================================
// NasSystem.Domain - FilePermission.cs
// 文件权限实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 文件权限实体
/// </summary>
public sealed class FilePermission : Entity
{
    /// <summary>
    /// 文件/文件夹 ID
    /// </summary>
    public Guid FileItemId { get; private set; }

    /// <summary>
    /// 被授权用户 ID
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// 权限级别
    /// </summary>
    public PermissionLevel Permission { get; private set; }

    // 导航属性
    public FileItem? FileItem { get; private set; }
    public User? User { get; private set; }

    // EF Core 需要的无参构造函数
    private FilePermission() { }

    /// <summary>
    /// 创建文件权限
    /// </summary>
    public static FilePermission Create(
        Guid fileItemId,
        Guid userId,
        PermissionLevel permission)
    {
        return new FilePermission
        {
            FileItemId = fileItemId,
            UserId = userId,
            Permission = permission
        };
    }

    /// <summary>
    /// 更新权限级别
    /// </summary>
    public void UpdatePermission(PermissionLevel permission)
    {
        Permission = permission;
        MarkAsModified();
    }
}
