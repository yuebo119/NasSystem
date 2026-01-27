// =============================================================================
// NasSystem.Domain - Share.cs
// 共享实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;

/// <summary>
/// 共享实体
/// </summary>
public sealed class Share : Entity
{
    /// <summary>
    /// 共享名称
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 共享描述
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// 共享路径 (虚拟路径)
    /// </summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// 物理路径
    /// </summary>
    public string PhysicalPath { get; private set; } = string.Empty;

    /// <summary>
    /// 共享类型
    /// </summary>
    public ShareType ShareType { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// 是否公开 (匿名访问)
    /// </summary>
    public bool IsPublic { get; private set; }

    /// <summary>
    /// 是否只读
    /// </summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// 创建者用户 ID
    /// </summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>
    /// 所属存储池 ID
    /// </summary>
    public Guid StoragePoolId { get; private set; }

    /// <summary>
    /// 配额 (字节)，null 表示无限制
    /// </summary>
    public long? QuotaBytes { get; private set; }

    /// <summary>
    /// SMB 共享名称 (Windows 共享)
    /// </summary>
    public string? SmbShareName { get; private set; }

    /// <summary>
    /// 是否启用回收站
    /// </summary>
    public bool EnableRecycleBin { get; private set; } = true;

    // 导航属性
    public User? CreatedByUser { get; private set; }
    public StoragePool? StoragePool { get; private set; }
    public ICollection<SharePermission> Permissions { get; private set; } = [];

    // EF Core 需要的无参构造函数
    private Share() { }

    /// <summary>
    /// 创建共享
    /// </summary>
    public static Share Create(
        string name,
        string path,
        string physicalPath,
        ShareType shareType,
        Guid createdByUserId,
        Guid storagePoolId,
        string? description = null,
        bool isPublic = false,
        bool isReadOnly = false)
    {
        var share = new Share
        {
            Name = name,
            Description = description,
            Path = path,
            PhysicalPath = physicalPath,
            ShareType = shareType,
            CreatedByUserId = createdByUserId,
            StoragePoolId = storagePoolId,
            IsPublic = isPublic,
            IsReadOnly = isReadOnly,
            SmbShareName = name.Replace(" ", "_")
        };

        share.AddDomainEvent(new ShareCreatedEvent(share.Id, share.Name));
        return share;
    }

    /// <summary>
    /// 更新基本信息
    /// </summary>
    public void Update(string? name = null, string? description = null)
    {
        if (name is not null)
        {
            Name = name;
            SmbShareName = name.Replace(" ", "_");
        }
        if (description is not null) Description = description;
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
    /// 设置访问模式
    /// </summary>
    public void SetAccessMode(bool isPublic, bool isReadOnly)
    {
        IsPublic = isPublic;
        IsReadOnly = isReadOnly;
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
    /// 设置回收站
    /// </summary>
    public void SetRecycleBin(bool enabled)
    {
        EnableRecycleBin = enabled;
        MarkAsModified();
    }

    /// <summary>
    /// 添加权限
    /// </summary>
    public void AddPermission(SharePermission permission)
    {
        Permissions.Add(permission);
        MarkAsModified();
    }

    /// <summary>
    /// 移除权限
    /// </summary>
    public void RemovePermission(SharePermission permission)
    {
        Permissions.Remove(permission);
        MarkAsModified();
    }
}
