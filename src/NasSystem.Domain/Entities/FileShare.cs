// =============================================================================
// NasSystem.Domain - FileShare.cs
// 文件分享实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using System.Security.Cryptography;
using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 文件分享实体
/// </summary>
public sealed class FileShare : Entity
{
    /// <summary>
    /// 分享码（短链接）
    /// </summary>
    public string ShareCode { get; private set; } = string.Empty;

    /// <summary>
    /// 关联的文件/文件夹 ID
    /// </summary>
    public Guid FileItemId { get; private set; }

    /// <summary>
    /// 访问类型
    /// </summary>
    public ShareAccessType AccessType { get; private set; }

    /// <summary>
    /// 密码哈希（密码保护时使用）
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// 权限级别
    /// </summary>
    public PermissionLevel Permission { get; private set; } = PermissionLevel.Read;

    /// <summary>
    /// 过期时间（null 表示永不过期）
    /// </summary>
    public DateTime? ExpiresAt { get; private set; }

    /// <summary>
    /// 最大下载次数（null 表示无限制）
    /// </summary>
    public int? MaxDownloads { get; private set; }

    /// <summary>
    /// 已下载次数
    /// </summary>
    public int DownloadCount { get; private set; }

    /// <summary>
    /// 创建者用户 ID
    /// </summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    // 导航属性
    public FileItem? FileItem { get; private set; }
    public User? CreatedByUser { get; private set; }

    // EF Core 需要的无参构造函数
    private FileShare() { }

    /// <summary>
    /// 创建文件分享
    /// </summary>
    public static FileShare Create(
        Guid fileItemId,
        Guid createdByUserId,
        ShareAccessType accessType = ShareAccessType.Public,
        string? passwordHash = null,
        PermissionLevel permission = PermissionLevel.Read,
        DateTime? expiresAt = null,
        int? maxDownloads = null)
    {
        // ITM-N20：分享必须锚定具体文件且创建者可追溯
        ArgumentOutOfRangeException.ThrowIfEqual(fileItemId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(createdByUserId, Guid.Empty);
        if (accessType == ShareAccessType.Password && string.IsNullOrEmpty(passwordHash))
            throw new ArgumentException("密码型分享必须提供密码哈希", nameof(passwordHash));

        return new FileShare
        {
            ShareCode = GenerateShareCode(),
            FileItemId = fileItemId,
            CreatedByUserId = createdByUserId,
            AccessType = accessType,
            PasswordHash = passwordHash,
            Permission = permission,
            ExpiresAt = expiresAt,
            MaxDownloads = maxDownloads,
            DownloadCount = 0,
            IsEnabled = true
        };
    }

    /// <summary>
    /// 生成分享码
    /// </summary>
    private static string GenerateShareCode()
    {
        // 使用小写字母和数字，排除易混淆字符 (0/o/l/1)
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        // ITM-N1：分享码可被猜测遍历，必须用加密随机数而非 Random
        return new string(Enumerable.Range(0, 6)
            .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }

    /// <summary>
    /// 更新设置
    /// </summary>
    public void Update(
        ShareAccessType? accessType = null,
        string? passwordHash = null,
        PermissionLevel? permission = null,
        DateTime? expiresAt = null,
        int? maxDownloads = null)
    {
        if (accessType.HasValue) AccessType = accessType.Value;
        if (passwordHash != null) PasswordHash = passwordHash;
        if (permission.HasValue) Permission = permission.Value;
        ExpiresAt = expiresAt;
        MaxDownloads = maxDownloads;
        MarkAsModified();
    }

    /// <summary>
    /// 清除密码
    /// </summary>
    public void ClearPassword()
    {
        PasswordHash = null;
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
    /// 增加下载次数
    /// </summary>
    public void IncrementDownloadCount()
    {
        DownloadCount++;
        MarkAsModified();
    }

    /// <summary>
    /// 重新生成分享码
    /// </summary>
    public void RegenerateShareCode()
    {
        ShareCode = GenerateShareCode();
        MarkAsModified();
    }

    /// <summary>
    /// 是否已过期
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// 是否已达到下载限制
    /// </summary>
    public bool IsDownloadLimitReached => MaxDownloads.HasValue && DownloadCount >= MaxDownloads.Value;

    /// <summary>
    /// 是否可访问（启用、未过期、未达到下载限制）
    /// </summary>
    public bool IsAccessible => IsEnabled && !IsExpired && !IsDownloadLimitReached;

    /// <summary>
    /// 是否需要密码
    /// </summary>
    public bool RequiresPassword => AccessType == ShareAccessType.Password && !string.IsNullOrEmpty(PasswordHash);
}
