// =============================================================================
// NasSystem.Domain - FileItem.cs
// 文件项实体
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;

/// <summary>
/// 文件项实体 (文件或文件夹)
/// </summary>
public sealed class FileItem : Entity
{
    /// <summary>
    /// 文件/文件夹名称
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 完整路径 (虚拟路径)
    /// </summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// 物理存储路径
    /// </summary>
    public string PhysicalPath { get; private set; } = string.Empty;

    /// <summary>
    /// 项目类型
    /// </summary>
    public FileItemType ItemType { get; private set; }

    /// <summary>
    /// 文件大小 (字节)，文件夹为 0
    /// </summary>
    public long SizeBytes { get; private set; }

    /// <summary>
    /// MIME 类型
    /// </summary>
    public string? MimeType { get; private set; }

    /// <summary>
    /// 文件扩展名
    /// </summary>
    public string? Extension { get; private set; }

    /// <summary>
    /// 文件哈希 (SHA256)
    /// </summary>
    public string? Hash { get; private set; }

    /// <summary>
    /// 所属存储池 ID
    /// </summary>
    public Guid StoragePoolId { get; private set; }

    /// <summary>
    /// 所有者用户 ID
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// 父文件夹 ID，根目录为 null
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// 是否已删除 (软删除)
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// 是否为隐藏文件
    /// </summary>
    public bool IsHidden { get; private set; }

    /// <summary>
    /// 是否为系统文件
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// 是否只读
    /// </summary>
    public bool IsReadOnly { get; private set; }

    // 导航属性
    public User? Owner { get; private set; }
    public StoragePool? StoragePool { get; private set; }
    public FileItem? Parent { get; private set; }
    public ICollection<FileItem> Children { get; private set; } = [];

    /// <summary>
    /// 创建文件夹
    /// </summary>
    public static FileItem CreateFolder(
        string name,
        string path,
        string physicalPath,
        Guid storagePoolId,
        Guid ownerId,
        Guid? parentId = null)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);

        var folder = new FileItem
        {
            Name = name,
            Path = NormalizePath(path),
            PhysicalPath = physicalPath,
            ItemType = FileItemType.Folder,
            StoragePoolId = storagePoolId,
            OwnerId = ownerId,
            ParentId = parentId,
            SizeBytes = 0
        };

        folder.AddDomainEvent(new FolderCreatedEvent(folder.Id, folder.Path));
        return folder;
    }

    /// <summary>
    /// 创建文件
    /// </summary>
    public static FileItem CreateFile(
        string name,
        string path,
        string physicalPath,
        long sizeBytes,
        string? mimeType,
        Guid storagePoolId,
        Guid ownerId,
        Guid? parentId = null,
        string? hash = null)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        var extension = System.IO.Path.GetExtension(name);
        
        var file = new FileItem
        {
            Name = name,
            Path = NormalizePath(path),
            PhysicalPath = physicalPath,
            ItemType = FileItemType.File,
            SizeBytes = sizeBytes,
            MimeType = mimeType,
            Extension = extension,
            Hash = hash,
            StoragePoolId = storagePoolId,
            OwnerId = ownerId,
            ParentId = parentId
        };

        file.AddDomainEvent(new FileCreatedEvent(file.Id, file.Path, file.SizeBytes));
        return file;
    }

    /// <summary>
    /// 重命名
    /// </summary>
    public void Rename(string newName)
    {
        ValidateName(newName);
        var oldName = Name;
        Name = newName;
        
        // 更新路径
        var parentPath = System.IO.Path.GetDirectoryName(Path) ?? "/";
        Path = NormalizePath(System.IO.Path.Combine(parentPath, newName));
        
        if (ItemType == FileItemType.File)
        {
            Extension = System.IO.Path.GetExtension(newName);
        }
        
        MarkAsModified();
        AddDomainEvent(new FileRenamedEvent(Id, oldName, newName));
    }

    /// <summary>
    /// 移动到新位置
    /// </summary>
    public void MoveTo(string newPath, string newPhysicalPath, Guid? newParentId)
    {
        var oldPath = Path;
        Path = NormalizePath(newPath);
        PhysicalPath = newPhysicalPath;
        ParentId = newParentId;
        MarkAsModified();
        AddDomainEvent(new FileMovedEvent(Id, oldPath, Path));
    }

    /// <summary>
    /// 更新文件大小
    /// </summary>
    public void UpdateSize(long newSizeBytes)
    {
        SizeBytes = newSizeBytes;
        MarkAsModified();
    }

    /// <summary>
    /// 更新哈希值
    /// </summary>
    public void UpdateHash(string hash)
    {
        Hash = hash;
        MarkAsModified();
    }

    /// <summary>
    /// 软删除
    /// </summary>
    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        MarkAsModified();
        AddDomainEvent(new FileDeletedEvent(Id, Path));
    }

    /// <summary>
    /// 恢复删除
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeletedAt = null;
        MarkAsModified();
    }

    /// <summary>
    /// 从持久化状态恢复实体（供基础设施层影子模型映射使用）。
    /// </summary>
    /// <remarks>
    /// 领域实体没有公开的"从数据库重建"入口，而替换 ORM 后不能再依赖提供方的变更追踪
    /// 悄悄填属性，故显式提供该工厂。它不做任何业务校验：数据既然已在库里即为合法状态，
    /// 校验职责属于 CreateFolder/CreateFile 这类新建入口。
    /// internal 而非 public：不扩大领域的公开面。
    /// </remarks>
    internal static FileItem Restore(
        Guid id,
        string name,
        string path,
        string physicalPath,
        FileItemType itemType,
        long sizeBytes,
        string? mimeType,
        string? extension,
        string? hash,
        Guid storagePoolId,
        Guid ownerId,
        Guid? parentId,
        bool isDeleted,
        DateTime? deletedAt,
        bool isHidden,
        bool isSystem,
        bool isReadOnly,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        return new FileItem
        {
            Id = id,
            Name = name,
            Path = path,
            PhysicalPath = physicalPath,
            ItemType = itemType,
            SizeBytes = sizeBytes,
            MimeType = mimeType,
            Extension = extension,
            Hash = hash,
            StoragePoolId = storagePoolId,
            OwnerId = ownerId,
            ParentId = parentId,
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            IsHidden = isHidden,
            IsSystem = isSystem,
            IsReadOnly = isReadOnly,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// 名称不变量：非空白、不含路径分隔符、不得为 ".." 或 "." 段——
    /// 防止经实体工厂或重命名构造出层级错乱的虚拟路径进而物理写入逃逸（ITM-N20）。
    /// 公开静态：应用层 Rename 等变更入口必须先行调用（实体变更方法无法回溯校验父路径拼接结果）。
    /// </summary>
    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('/') || name.Contains('\\') || name == ".." || name == ".")
            throw new ArgumentException($"名称不能包含路径分隔符或相对段: {name}", nameof(name));
    }

    /// <summary>
    /// 标准化路径（单一真源委托，见 FilePath.Normalize 的保留论证）
    /// </summary>
    private static string NormalizePath(string path) => ValueObjects.FilePath.Normalize(path);

    /// <summary>
    /// 是否为文件夹
    /// </summary>
    public bool IsFolder => ItemType == FileItemType.Folder;

    /// <summary>
    /// 是否为文件
    /// </summary>
    public bool IsFile => ItemType == FileItemType.File;
}
