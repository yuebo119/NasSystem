// =============================================================================
// NasSystem.Domain - SharePermission.cs
// 共享权限实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 共享权限实体
/// </summary>
public sealed class SharePermission : Entity
{
    /// <summary>
    /// 所属共享 ID
    /// </summary>
    public Guid ShareId { get; private set; }

    /// <summary>
    /// 用户 ID (如果是用户权限)
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// 用户组名称 (如果是组权限)
    /// </summary>
    public string? GroupName { get; private set; }

    /// <summary>
    /// 权限级别
    /// </summary>
    public PermissionLevel Permission { get; private set; }

    /// <summary>
    /// 是否拒绝 (显式拒绝优先于允许)
    /// </summary>
    public bool IsDeny { get; private set; }

    // 导航属性
    public Share? Share { get; private set; }
    public User? User { get; private set; }

    // EF Core 需要的无参构造函数
    private SharePermission() { }

    /// <summary>
    /// 创建用户权限
    /// </summary>
    public static SharePermission CreateForUser(
        Guid shareId,
        Guid userId,
        PermissionLevel permission,
        bool isDeny = false)
    {
        return new SharePermission
        {
            ShareId = shareId,
            UserId = userId,
            Permission = permission,
            IsDeny = isDeny
        };
    }

    /// <summary>
    /// 创建组权限
    /// </summary>
    public static SharePermission CreateForGroup(
        Guid shareId,
        string groupName,
        PermissionLevel permission,
        bool isDeny = false)
    {
        return new SharePermission
        {
            ShareId = shareId,
            GroupName = groupName,
            Permission = permission,
            IsDeny = isDeny
        };
    }

    /// <summary>
    /// 更新权限级别
    /// </summary>
    public void UpdatePermission(PermissionLevel newPermission, bool? isDeny = null)
    {
        Permission = newPermission;
        if (isDeny.HasValue) IsDeny = isDeny.Value;
        MarkAsModified();
    }

    /// <summary>
    /// 是否为用户权限
    /// </summary>
    public bool IsUserPermission => UserId.HasValue;

    /// <summary>
    /// 是否为组权限
    /// </summary>
    public bool IsGroupPermission => !string.IsNullOrEmpty(GroupName);
}
