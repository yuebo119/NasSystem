// ═══ 核心字段/构造/共享辅助（ITM-T1 拆分）═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

using Microsoft.Extensions.Configuration;
using NasSystem.Application.Common;
using NasSystem.Application.Files.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Application.Notifications;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 文件服务实现
/// </summary>
public sealed partial class FileService : IFileBrowseService, IFileTransferService, IFileRecycleService, IFileSyncService
{
    private readonly IFileItemRepository _fileRepository;
    private readonly IStoragePoolRepository _storagePoolRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileSystemService _fileSystemService;
    private readonly IHubNotificationService _notifier;

    public FileService(
        IFileItemRepository fileRepository,
        IStoragePoolRepository storagePoolRepository,
        IUnitOfWork unitOfWork,
        IFileSystemService fileSystemService,
        IConfiguration configuration,
        IHubNotificationService notifier)
    {
        _fileRepository = fileRepository;
        _storagePoolRepository = storagePoolRepository;
        _unitOfWork = unitOfWork;
        _fileSystemService = fileSystemService;
        _notifier = notifier;
    }

    /// <summary>操作成功后向父目录订阅组推送变更（SignalR 实时刷新闭环）</summary>
    private Task NotifyChangedAsync(string parentPath, FileChangeType changeType, string path) =>
        _notifier.NotifyFileChangedAsync(parentPath, changeType, new { path });

    /// <summary>
    /// 获取默认存储池，缺失时统一返回 PoolNotFound 失败（各用例域共用的前置检查）
    /// </summary>
    private async Task<Result<StoragePool>> GetDefaultPoolOrFailureAsync(CancellationToken cancellationToken)
    {
        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        return defaultPool is null
            ? Result<StoragePool>.Failure(StorageErrors.PoolNotFound(Guid.Empty))
            : Result<StoragePool>.Success(defaultPool);
    }

    /// <summary>
    /// ITM-N7：保存更改的唯一约束兜底。并发写入撞唯一索引时返回调用方提供的冲突错误，
    /// 收敛各用例重复的 try-catch(UniqueConstraintException) 样板
    /// </summary>
    private async Task<Result> SaveChangesWithConflictGuardAsync(
        Error conflictError,
        CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure(conflictError);
        }
    }


    #region 辅助方法

    // 【单一真源】规范化委托 FilePath.Normalize（解析 .. 与重复斜杠的强防护版），见该处保留论证
    private static string NormalizePath(string path) => Domain.ValueObjects.FilePath.Normalize(path);

    private static string GetFileName(string path) =>
        path == "/" ? "根目录" : path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "根目录";

    private static string GetParentPath(string path)
    {
        if (path == "/") return "/";
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path[..lastSlash];
    }

    private static string CombinePath(string parent, string name) =>
        parent == "/" ? "/" + name : parent + "/" + name;

    private string GetPhysicalPath(string virtualPath, string poolMountPath)
    {
        var relativePath = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(poolMountPath, relativePath);
    }

    private static List<BreadcrumbDto> BuildBreadcrumbs(string path)
    {
        var breadcrumbs = new List<BreadcrumbDto>();
        if (path == "/") return breadcrumbs;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";
        foreach (var part in parts)
        {
            currentPath += "/" + part;
            breadcrumbs.Add(new BreadcrumbDto { Name = part, Path = currentPath });
        }
        return breadcrumbs;
    }

    private static FileItemDto MapToDto(FileItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Path = item.Path,
        ItemType = item.ItemType,
        SizeBytes = item.SizeBytes,
        SizeDisplay = FormatSize(item.SizeBytes),
        MimeType = item.MimeType,
        Extension = item.Extension,
        Hash = item.Hash,
        ParentId = item.ParentId,
        OwnerId = item.OwnerId,
        IsHidden = item.IsHidden,
        IsReadOnly = item.IsReadOnly,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };



    #endregion
}
