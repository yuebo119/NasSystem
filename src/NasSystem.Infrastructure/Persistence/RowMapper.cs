// =============================================================================
// NasSystem.Infrastructure - Persistence/RowMapper.cs
// 行模型与领域实体的双向映射
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using NasSystem.Domain.Entities;

/// <summary>
/// 影子行模型与领域实体的双向转换。
/// </summary>
/// <remarks>
/// 枚举在库里是 INTEGER，此处集中做 int 与枚举的换算，仓储不重复处理。
/// 时间一律原样传递：SQLite 存 TEXT，EF 与 PalORM 读出来都是 Kind=Unspecified
/// （已实测对照），不做时区假设。
/// </remarks>
public static class RowMapper
{
    public static FileItem ToDomain(this FileItemRow row) =>
        FileItem.Restore(
            row.Id, row.Name, row.Path, row.PhysicalPath, row.ItemType,
            row.SizeBytes, row.MimeType, row.Extension, row.Hash,
            row.StoragePoolId, row.OwnerId, row.ParentId,
            row.IsDeleted, row.DeletedAt, row.IsHidden, row.IsSystem, row.IsReadOnly,
            row.CreatedAt, row.UpdatedAt);

    public static FileItemRow ToRow(this FileItem entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Path = entity.Path,
            PhysicalPath = entity.PhysicalPath,
            ItemType = entity.ItemType,
            SizeBytes = entity.SizeBytes,
            MimeType = entity.MimeType,
            Extension = entity.Extension,
            Hash = entity.Hash,
            StoragePoolId = entity.StoragePoolId,
            OwnerId = entity.OwnerId,
            ParentId = entity.ParentId,
            IsDeleted = entity.IsDeleted,
            DeletedAt = entity.DeletedAt,
            IsHidden = entity.IsHidden,
            IsSystem = entity.IsSystem,
            IsReadOnly = entity.IsReadOnly,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };

    public static User ToDomain(this UserRow row) =>
        User.Restore(
            row.Id, row.Username, row.PasswordHash, row.PasswordSalt,
            row.DisplayName, row.Email, row.Role,
            row.IsEnabled, row.LastLoginAt, row.QuotaBytes, row.UsedBytes,
            row.HomeDirectory, row.CreatedAt, row.UpdatedAt);

    public static UserRow ToRow(this User entity) =>
        new()
        {
            Id = entity.Id,
            Username = entity.Username,
            PasswordHash = entity.PasswordHash,
            PasswordSalt = entity.PasswordSalt,
            DisplayName = entity.DisplayName,
            Email = entity.Email,
            Role = entity.Role,
            IsEnabled = entity.IsEnabled,
            LastLoginAt = entity.LastLoginAt,
            QuotaBytes = entity.QuotaBytes,
            UsedBytes = entity.UsedBytes,
            HomeDirectory = entity.HomeDirectory,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };

    public static StoragePool ToDomain(this StoragePoolRow row) =>
        StoragePool.Restore(
            row.Id, row.Name, row.MountPath, row.Description,
            row.IsDefault, row.IsEnabled, row.TotalBytes, row.UsedBytes,
            row.MaxCapacityBytes, row.CreatedAt, row.UpdatedAt);

    public static StoragePoolRow ToRow(this StoragePool entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            MountPath = entity.MountPath,
            Description = entity.Description,
            IsDefault = entity.IsDefault,
            IsEnabled = entity.IsEnabled,
            TotalBytes = entity.TotalBytes,
            UsedBytes = entity.UsedBytes,
            MaxCapacityBytes = entity.MaxCapacityBytes,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };

    public static FileShare ToDomain(this FileShareRow row, FileItem? fileItem = null, User? createdByUser = null) =>
        FileShare.Restore(
            row.Id, row.ShareCode, row.FileItemId, row.AccessType,
            row.PasswordHash, row.Permission,
            row.ExpiresAt, row.MaxDownloads, row.DownloadCount,
            row.CreatedByUserId, row.IsEnabled, row.CreatedAt, row.UpdatedAt,
            fileItem, createdByUser);

    public static FileShareRow ToRow(this FileShare entity) =>
        new()
        {
            Id = entity.Id,
            ShareCode = entity.ShareCode,
            FileItemId = entity.FileItemId,
            AccessType = entity.AccessType,
            PasswordHash = entity.PasswordHash,
            Permission = entity.Permission,
            ExpiresAt = entity.ExpiresAt,
            MaxDownloads = entity.MaxDownloads,
            DownloadCount = entity.DownloadCount,
            CreatedByUserId = entity.CreatedByUserId,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };

    public static FileVersion ToDomain(this FileVersionRow row, FileItem? fileItem = null, User? createdByUser = null) =>
        FileVersion.Restore(
            row.Id, row.FileItemId, row.VersionNumber, row.StoragePath,
            row.FileSize, row.FileHash, row.Comment,
            row.IsCurrent, row.CreatedByUserId, row.CreatedAt, row.UpdatedAt,
            fileItem, createdByUser);

    public static FileVersionRow ToRow(this FileVersion entity) =>
        new()
        {
            Id = entity.Id,
            FileItemId = entity.FileItemId,
            VersionNumber = entity.VersionNumber,
            StoragePath = entity.StoragePath,
            FileSize = entity.FileSize,
            FileHash = entity.FileHash,
            Comment = entity.Comment,
            IsCurrent = entity.IsCurrent,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
}
