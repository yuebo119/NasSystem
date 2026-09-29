// ═══ 核心字段/构造/共享辅助（ITM-T1 拆分）═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NasSystem.Application.Common;
using NasSystem.Application.Files.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Application.Notifications;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 文件服务实现
/// </summary>
public sealed partial class FileService : IFileBrowseService, IFileTransferService, IFileRecycleService, IFileSyncService
{
    /// <summary>默认池只读投影缓存键（缓存 Id+挂载点，不缓存可变实体实例）</summary>
    private static readonly string DefaultPoolCacheKey = "fileservice:default-pool";

    /// <summary>默认池缓存有效期：管理端改池后最长 30s 传播到写用例</summary>
    private static readonly TimeSpan DefaultPoolCacheDuration = TimeSpan.FromSeconds(30);

    private readonly IFileItemRepository _fileRepository;
    private readonly IStoragePoolRepository _storagePoolRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileSystemService _fileSystemService;
    private readonly IHubNotificationService _notifier;
    private readonly IMemoryCache _cache;

    public FileService(
        IFileItemRepository fileRepository,
        IStoragePoolRepository storagePoolRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IFileSystemService fileSystemService,
        IConfiguration configuration,
        IHubNotificationService notifier,
        IMemoryCache cache)
    {
        _fileRepository = fileRepository;
        _storagePoolRepository = storagePoolRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _fileSystemService = fileSystemService;
        _notifier = notifier;
        _cache = cache;
    }

    /// <summary>操作成功后向父目录订阅组推送变更（SignalR 实时刷新闭环）</summary>
    private Task NotifyChangedAsync(string parentPath, FileChangeType changeType, string path) =>
        _notifier.NotifyFileChangedAsync(parentPath, changeType, new { path });

    /// <summary>
    /// 获取默认存储池的 Id 与挂载点，缺失时统一返回 PoolNotFound 失败。
    /// 写用例每个请求都查一次默认池，而池极少变化——按只读投影缓存 30s，
    /// 不缓存实体实例本身（调用方不得基于缓存值触发 Update/SaveChanges）
    /// </summary>
    private async Task<Result<(Guid Id, string MountPath)>> GetDefaultPoolOrFailureAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(DefaultPoolCacheKey, out (Guid Id, string MountPath) cached))
            return Result<(Guid, string)>.Success(cached);

        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        if (defaultPool is null)
            return Result<(Guid, string)>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

        var projection = (defaultPool.Id, defaultPool.MountPath);
        _cache.Set(DefaultPoolCacheKey, projection, DefaultPoolCacheDuration);
        return Result<(Guid, string)>.Success(projection);
    }

    /// <summary>
    /// 上传预检：用户配额 + 存储池容量。
    /// </summary>
    /// <remarks>
    /// 用户用量取实时聚合查询——User.UsedBytes 字段没有生产维护者（前端展示口径滞后），
    /// 用它判定会永远放行；池容量用库内 UsedBytes（容量刷新有 60s 节流窗口，
    /// 此处为 best-effort 前置拦截，物理写失败由 IO 层兜底）。
    /// 声明大小来自客户端 metadata.FileSize；超大谎报由物理写失败兜底，
    /// 上传后不对账声明值（record 记账用实际写入字节）。
    /// </remarks>
    private async Task<Result> PreflightUploadAsync(
        long declaredSize,
        Guid userId,
        Guid poolId,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.QuotaBytes.HasValue == true)
        {
            var usedBytes = await _fileRepository.CalculateUserUsedSpaceAsync(userId, cancellationToken);
            if (usedBytes + declaredSize > user.QuotaBytes.Value)
                return Result.Failure(UserErrors.QuotaExceeded);
        }

        if (declaredSize > 0)
        {
            var pool = await _storagePoolRepository.GetByIdAsync(poolId, cancellationToken);
            if (pool is not null && !pool.HasEnoughSpace(declaredSize))
                return Result.Failure(StorageErrors.InsufficientSpace);
        }

        return Result.Success();
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
