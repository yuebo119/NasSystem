// =============================================================================
// NasSystem.Domain - User.cs
// 用户实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;

/// <summary>
/// 用户实体
/// </summary>
public sealed class User : Entity
{
    /// <summary>
    /// 用户名 (唯一)
    /// </summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>
    /// 密码哈希
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// 密码盐值
    /// </summary>
    public string PasswordSalt { get; private set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// 邮箱地址
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>
    /// 用户角色
    /// </summary>
    public UserRole Role { get; private set; } = UserRole.User;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime? LastLoginAt { get; private set; }

    /// <summary>
    /// 用户配额 (字节)，null 表示无限制
    /// </summary>
    public long? QuotaBytes { get; private set; }

    /// <summary>
    /// 已使用空间 (字节)
    /// </summary>
    public long UsedBytes { get; private set; }

    /// <summary>
    /// 用户主目录路径
    /// </summary>
    public string HomeDirectory { get; private set; } = string.Empty;

    // EF Core 需要的无参构造函数
    private User() { }

    /// <summary>
    /// 创建新用户
    /// </summary>
    public static User Create(
        string username,
        string passwordHash,
        string passwordSalt,
        UserRole role = UserRole.User,
        string? displayName = null,
        string? email = null)
    {
        var user = new User
        {
            Username = username.ToLowerInvariant(),
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            Role = role,
            DisplayName = displayName ?? username,
            Email = email,
            HomeDirectory = $"/home/{username.ToLowerInvariant()}"
        };

        user.AddDomainEvent(new UserCreatedEvent(user.Id, user.Username));
        return user;
    }

    /// <summary>
    /// 更新密码
    /// </summary>
    public void UpdatePassword(string newPasswordHash, string newPasswordSalt)
    {
        PasswordHash = newPasswordHash;
        PasswordSalt = newPasswordSalt;
        MarkAsModified();
        AddDomainEvent(new UserPasswordChangedEvent(Id));
    }

    /// <summary>
    /// 更新个人信息
    /// </summary>
    public void UpdateProfile(string? displayName, string? email)
    {
        DisplayName = displayName;
        Email = email;
        MarkAsModified();
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    public void UpdateRole(UserRole newRole)
    {
        var oldRole = Role;
        Role = newRole;
        MarkAsModified();
        AddDomainEvent(new UserRoleChangedEvent(Id, oldRole, newRole));
    }

    /// <summary>
    /// 启用/禁用用户
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        MarkAsModified();
    }

    /// <summary>
    /// 记录登录
    /// </summary>
    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        MarkAsModified();
    }

    /// <summary>
    /// 设置配额
    /// </summary>
    public void SetQuota(long? quotaBytes)
    {
        QuotaBytes = quotaBytes;
        MarkAsModified();
    }

    /// <summary>
    /// 更新已使用空间
    /// </summary>
    public void UpdateUsedSpace(long usedBytes)
    {
        UsedBytes = usedBytes;
        MarkAsModified();
    }

    /// <summary>
    /// 检查是否超出配额
    /// </summary>
    public bool IsOverQuota => QuotaBytes.HasValue && UsedBytes > QuotaBytes.Value;

    /// <summary>
    /// 获取剩余配额
    /// </summary>
    public long? RemainingQuota => QuotaBytes.HasValue ? QuotaBytes.Value - UsedBytes : null;
}
