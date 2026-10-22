// ═══ 核心字段/构造/共享辅助（ITM-T1 拆分）═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Common;
using NasSystem.Application.Configuration;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.FileVersions;
using NasSystem.Application.Ocr;
using NasSystem.Application.ProtectedDirectories;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Application.Notifications;
using NasSystem.Application.Storage;
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
    private readonly DiskSpace _diskSpace;
    private readonly IHubNotificationService _notifier;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// 缩略图服务：子树软删时按路径清理缓存用。
    /// </summary>
    /// <remarks>
    /// 原实现经 FileDeletedEvent 领域事件逐实体分发到 Infrastructure 的处理器，
    /// 子树软删改单条 SQL 后没有实体可产生事件，副作用（缩略图缓存清理）
    /// 随之改为服务显式调用。接口在 Application 层，依赖方向不变。
    /// </remarks>
    private readonly IThumbnailService _thumbnailService;

    /// <summary>
    /// 版本服务：覆盖上传/永久删除前自动产版本用（ROADMAP Phase 1）
    /// </summary>
    private readonly IFileVersionService _fileVersionService;

    /// <summary>
    /// OCR 服务与动态配置：上传后按开关自动识别图内文字（ROADMAP Phase 2）
    /// </summary>
    private readonly IOcrService _ocrService;
    private readonly ISystemConfigService _configService;

    /// <summary>
    /// 受保护目录服务：写路径的只读保护判定（ROADMAP Phase 2）
    /// </summary>
    private readonly IProtectedDirectoryService _protectedDirectoryService;
    private readonly ILogger<FileService> _logger;

    public FileService(
        IFileItemRepository fileRepository,
        IStoragePoolRepository storagePoolRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IFileSystemService fileSystemService,
        IConfiguration configuration,
        IHubNotificationService notifier,
        IMemoryCache cache,
        IThumbnailService thumbnailService,
        IFileVersionService fileVersionService,
        IOcrService ocrService,
        ISystemConfigService configService,
        IProtectedDirectoryService protectedDirectoryService,
        DiskSpace diskSpace,
        ILogger<FileService> logger)
    {
        _fileRepository = fileRepository;
        _storagePoolRepository = storagePoolRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _fileSystemService = fileSystemService;
        _notifier = notifier;
        _cache = cache;
        _thumbnailService = thumbnailService;
        _fileVersionService = fileVersionService;
        _ocrService = ocrService;
        _configService = configService;
        _protectedDirectoryService = protectedDirectoryService;
        _diskSpace = diskSpace;
        _logger = logger;
    }

    /// <summary>
    /// ROADMAP Phase 2 只读保护判定：路径自身或任一祖先被标记即受保护。
    /// 判定经仓储的"标记表空则短路"路径，未配置保护的部署（默认态）
    /// 写路径零额外开销
    /// </summary>
    private Task<bool> IsPathProtectedAsync(string path, CancellationToken cancellationToken) =>
        _protectedDirectoryService.IsProtectedAsync(path, cancellationToken);

    /// <summary>
    /// 只读保护拒绝错误。错误码含 Forbidden 关键词（ResultExtensions 的
    /// 后缀映射落 403），码名带 Protected 便于日志与监控区分于普通越权
    /// </summary>
    private static Error ProtectedError(string path) =>
        new("File.ProtectedForbidden", $"目录处于只读保护状态，禁止修改: {path}");

    /// <summary>
    /// 上传后的 OCR 自动识别（配置页开关，默认关）。同步等待：
    /// 识别是 CPU 密集的百毫秒级操作，开启即接受这点上传延迟；
    /// 失败只记日志不影响上传结果——识别是增强不是主路径
    /// </summary>
    private async Task AutoRecognizeAsync(FileItem item, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _configService.GetBoolAsync(
                    ConfigDescriptors.Keys.OcrAutoRecognize, cancellationToken))
            {
                return;
            }

            var extension = item.Extension?.ToLowerInvariant();
            var isImage = extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif";
            if (!isImage)
            {
                return;
            }

            var result = await _ocrService.RecognizeAsync(item.Path, item.OwnerId, cancellationToken);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "上传后自动 OCR 失败（不影响上传）: Path={Path}, 原因={Reason}",
                    item.Path, result.Error.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "上传后自动 OCR 异常（不影响上传）: Path={Path}", item.Path);
        }
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
    /// 上传预检：用户配额与存储池容量。
    /// </summary>
    /// <remarks>
    /// 用户用量取实时聚合查询——User.UsedBytes 字段没有生产维护者（前端展示口径滞后），
    /// 用它判定会永远放行；池容量用库内 UsedBytes（容量刷新有 60s 节流窗口，
    /// 此处为 best-effort 前置拦截，物理写失败由 IO 层兜底）。
    /// 声明大小来自客户端 metadata.FileSize；超大谎报由物理写失败兜底，
    /// 上传后不对账声明值（record 记账用实际写入字节）。
    /// <para>
    /// 【S5】流式 multipart 解析拿不到声明大小（FileSize=0），此时预检退化为
    /// 余量判定：有配额/上限时确认还有余量即放行，返回的余量由写入循环边写边检。
    /// 无配额且无上限时对应余量为 null（不设防）。
    /// </para>
    /// </remarks>
    private async Task<Result<(long? QuotaRemainingBytes, long? PoolAvailableBytes)>> PreflightUploadAsync(
        long declaredSize,
        Guid userId,
        Guid poolId,
        CancellationToken cancellationToken)
    {
        // 用户配额余量。无配额时 null（不设防）
        long? quotaRemaining = null;
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user?.QuotaBytes.HasValue == true)
        {
            var usedBytes = await _fileRepository.CalculateUserUsedSpaceAsync(userId, cancellationToken);
            if (usedBytes + declaredSize > user.QuotaBytes.Value)
                return Result<(long?, long?)>.Failure(UserErrors.QuotaExceeded);
            quotaRemaining = user.QuotaBytes.Value - usedBytes;
        }

        // 池容量余量：放得下的判定要连同磁盘物理剩余——池只是盘上的文件夹，
        // 只按配额放行会把盘写爆（写失败发生在流式中途，比预检拒绝更难收拾）。
        // 流式上传声明大小为 0 时这里取余量供边写边检，判定退化为"还有余量即放行"。
        // 余量无从判断（无配额且挂载点不可达）时拒绝：与旧 HasEnoughSpace 的
        // fail-closed 语义一致，真写下去也只会在流式中途失败，更难收拾
        long? poolAvailable = null;
        var pool = await _storagePoolRepository.GetByIdAsync(poolId, cancellationToken);
        if (pool is not null)
        {
            var available = pool.AvailableBytesWithin(_diskSpace.GetPhysicalFreeBytes(pool.MountPath));
            if (available is null || available.Value < declaredSize)
                return Result<(long?, long?)>.Failure(StorageErrors.InsufficientSpace);
            poolAvailable = available;
        }

        return Result<(long?, long?)>.Success((quotaRemaining, poolAvailable));
    }

    /// <summary>清理上传半成品临时文件（存在性竞争与 IO 错误都吞掉：它本来就是可弃的）</summary>
    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (_fileSystemService.FileExists(tempPath))
                _fileSystemService.DeleteFile(tempPath);
        }
        catch
        {
            // 临时文件清理失败不阻断主流程；残留的 .uploading-* 文件由扫描同步忽略
            //（物理存在但库里没有对应记录时会被当新文件入库——见下方说明）
        }
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
        MimeType = item.MimeType,
        Extension = item.Extension,
        ParentId = item.ParentId,
        OwnerId = item.OwnerId,
        IsHidden = item.IsHidden,
        IsReadOnly = item.IsReadOnly,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };



    #endregion
}
