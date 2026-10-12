// =============================================================================
// NasSystem.Domain - FileVersion.cs
// 文件版本实体
// =============================================================================

namespace NasSystem.Domain.Entities;

/// <summary>
/// 文件版本实体 - 用于跟踪文件的历史版本
/// </summary>
public class FileVersion
{
    /// <summary>
    /// 版本 ID
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// 关联的文件项 ID
    /// </summary>
    public Guid FileItemId { get; private set; }

    /// <summary>
    /// 版本号
    /// </summary>
    public int VersionNumber { get; private set; }

    /// <summary>
    /// 版本存储路径（备份文件的物理路径）
    /// </summary>
    public string StoragePath { get; private set; } = string.Empty;

    /// <summary>
    /// 此版本的文件大小
    /// </summary>
    public long FileSize { get; private set; }

    /// <summary>
    /// 文件哈希值
    /// </summary>
    public string FileHash { get; private set; } = string.Empty;

    /// <summary>
    /// 版本说明
    /// </summary>
    public string? Comment { get; private set; }

    /// <summary>
    /// 是否为当前版本
    /// </summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    /// 创建此版本的用户 ID
    /// </summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// 关联的文件项
    /// </summary>
    public FileItem? FileItem { get; private set; }

    /// <summary>
    /// 创建此版本的用户
    /// </summary>
    public User? CreatedByUser { get; private set; }

    /// <summary>
    /// 创建新的文件版本
    /// </summary>
    public static FileVersion Create(
        Guid fileItemId,
        int versionNumber,
        string storagePath,
        long fileSize,
        string fileHash,
        Guid createdByUserId,
        string? comment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileHash);

        return new FileVersion
        {
            Id = Guid.NewGuid(),
            FileItemId = fileItemId,
            VersionNumber = versionNumber,
            StoragePath = storagePath,
            FileSize = fileSize,
            FileHash = fileHash,
            CreatedByUserId = createdByUserId,
            Comment = comment,
            IsCurrent = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 标记为非当前版本
    /// </summary>
    public void MarkAsOld()
    {
        IsCurrent = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 恢复为当前版本
    /// </summary>
    public void MarkAsCurrent()
    {
        IsCurrent = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 从持久化状态恢复实体（供基础设施层影子模型映射使用）。动因见 <see cref="FileItem.Restore"/>。
    /// </summary>
    internal static FileVersion Restore(
        Guid id,
        Guid fileItemId,
        int versionNumber,
        string storagePath,
        long fileSize,
        string fileHash,
        string? comment,
        bool isCurrent,
        Guid createdByUserId,
        DateTime createdAt,
        DateTime? updatedAt,
        FileItem? fileItem = null,
        User? createdByUser = null)
    {
        return new FileVersion
        {
            Id = id,
            FileItemId = fileItemId,
            VersionNumber = versionNumber,
            StoragePath = storagePath,
            FileSize = fileSize,
            FileHash = fileHash,
            Comment = comment,
            IsCurrent = isCurrent,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            FileItem = fileItem,
            CreatedByUser = createdByUser,
        };
    }
}
