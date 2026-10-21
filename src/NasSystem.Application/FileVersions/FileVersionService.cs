// =============================================================================
// NasSystem.Application - FileVersionService.cs
// 文件版本服务实现
// =============================================================================

namespace NasSystem.Application.FileVersions;

using Microsoft.Extensions.Logging;
using NasSystem.Application.Configuration;
using NasSystem.Application.Files;
using NasSystem.Application.Notifications;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;

/// <summary>
/// 文件版本服务实现
/// </summary>
public class FileVersionService : IFileVersionService
{
    private readonly IFileVersionRepository _versionRepository;
    private readonly IFileItemRepository _fileItemRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FileVersionService> _logger;
    private readonly IFileSystemService _fileSystemService;
    private readonly ISystemConfigService _configService;
    private readonly IHubNotificationService _notifier;

    public FileVersionService(
        IFileVersionRepository versionRepository,
        IFileItemRepository fileItemRepository,
        IUnitOfWork unitOfWork,
        ILogger<FileVersionService> logger,
        IFileSystemService fileSystemService,
        ISystemConfigService configService,
        IHubNotificationService notifier)
    {
        _versionRepository = versionRepository;
        _fileItemRepository = fileItemRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _fileSystemService = fileSystemService;
        _configService = configService;
        _notifier = notifier;
    }

    /// <inheritdoc />
    public async Task CreateAutoVersionAsync(
        FileItem item,
        string poolMountPath,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // 快照落盘先于 DB 插入。两者之间失败（典型：并发覆盖同一文件，
        // 版本号先读后写撞 IX_FileVersions_FileItemId_VersionNumber 唯一
        // 索引）会留下无记录引用的孤儿文件——保留策略只按库记录删文件，
        // 清不掉它。落库成功即置空，之后的保留策略失败不动它
        string? orphanSnapshot = null;
        try
        {
            if (!File.Exists(item.PhysicalPath))
            {
                // 库有记录但物理文件缺失（历史遗留或外部删除）：无内容可快照
                _logger.LogWarning(
                    "跳过自动版本：物理文件不存在 FileItemId={FileItemId}, Path={Path}",
                    item.Id, item.PhysicalPath);
                return;
            }

            var versionPath = FileVersionStore.GetVersionPath(poolMountPath, item.Id, item.Name);
            FileVersionStore.CreateSnapshot(item.PhysicalPath, versionPath);
            orphanSnapshot = versionPath;

            // 版本号先读后写，并发覆盖同一文件时两个请求都读到同一个 max：
            // 唯一索引兜底的后果是整件事被外层 catch 吞掉（只留一行日志），
            // 这一次的旧内容没有版本可回。重试一次：重新读 max + 换快照名，
            // 仍败才降级
            const int MaxAttempts = 2;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await InsertVersionRecordAsync(item, versionPath, userId, cancellationToken);
                    break;
                }
                catch (UniqueConstraintException) when (attempt < MaxAttempts)
                {
                    _logger.LogWarning(
                        "自动版本号冲突（并发覆盖？），重试: FileItemId={FileItemId}, Attempt={Attempt}",
                        item.Id, attempt);
                    // 换一个新快照名：旧路径已被上一次尝试占用（硬链接/复制皆唯一）
                    versionPath = FileVersionStore.GetVersionPath(poolMountPath, item.Id, item.Name);
                    FileVersionStore.CreateSnapshot(item.PhysicalPath, versionPath);
                    orphanSnapshot = versionPath;
                }
            }

            orphanSnapshot = null; // 已落库，快照有记录引用，不再是孤儿

            _logger.LogInformation(
                "自动版本已创建: FileItemId={FileItemId}, Snapshot={Path}",
                item.Id, versionPath);

            await ApplyRetentionAsync(item.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 落库失败时补删孤儿快照（硬链接只删链接不动源文件）。
            // 删失败只记日志：主流程已放行，清理是尽力而为
            if (orphanSnapshot is not null)
            {
                try
                {
                    File.Delete(orphanSnapshot);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx,
                        "孤儿版本快照清理失败: {Path}", orphanSnapshot);
                }
            }

            // 快照失败不阻断主流程：覆盖上传/永久删除是用户明确意图，
            // 为版本失败回滚主操作会把"保险"凌驾于主路径之上
            _logger.LogWarning(ex, "自动版本创建失败（主操作继续）: FileItemId={FileItemId}", item.Id);
        }
    }

    /// <summary>
    /// 读当前 max 版本号 + 插一条 IsCurrent=false 的版本记录。
    /// 版本号竞态的唯一点：调用方（CreateAutoVersionAsync）负责重试
    /// </summary>
    private async Task InsertVersionRecordAsync(
        FileItem item,
        string versionPath,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existing = await _versionRepository.GetByFileItemIdAsync(item.Id, cancellationToken);
        var nextNumber = existing.Count > 0 ? existing.Max(v => v.VersionNumber) + 1 : 1;

        // IsCurrent 语义：自动版本记录的是"被覆盖/被删除前的内容"，
        // 覆盖完成后活动内容是新写入的文件，与任何版本快照都不相同，
        // 因此自动版本一律 IsCurrent=false，同时解除上一版本的 current 标记
        // （上一版本若恰是此前恢复出来的，覆盖后也不再与活动内容一致）
        foreach (var old in existing.Where(v => v.IsCurrent))
        {
            old.MarkAsOld();
            _versionRepository.Update(old);
        }

        var version = FileVersion.Create(
            fileItemId: item.Id,
            versionNumber: nextNumber,
            storagePath: versionPath,
            fileSize: item.SizeBytes,
            fileHash: item.Hash ?? string.Empty,
            createdByUserId: userId,
            comment: null);
        version.MarkAsOld();

        await _versionRepository.AddAsync(version, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 保留策略：单文件版本数超限删最旧，超过天数的全清。
    /// 被恢复出来的 current 版本不参与淘汰（删掉它等于删掉与活动内容一致的那份）
    /// </summary>
    private async Task ApplyRetentionAsync(Guid fileItemId, CancellationToken cancellationToken)
    {
        var versions = await _versionRepository.GetByFileItemIdAsync(fileItemId, cancellationToken);
        if (versions.Count == 0)
            return;

        // 保留策略从动态配置读（配置页可调，改完下次产版本即生效）。
        // 每次产版本读一次库：版本创建不是热路径，为它加缓存反而要处理失效
        var maxVersions = await _configService.GetIntAsync(
            ConfigDescriptors.Keys.VersionMaxCount, cancellationToken);
        var maxAgeDays = await _configService.GetIntAsync(
            ConfigDescriptors.Keys.VersionMaxAgeDays, cancellationToken);
        if (maxVersions < 1) maxVersions = 1;
        if (maxAgeDays < 1) maxAgeDays = 1;

        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var evictable = versions
            .Where(v => !v.IsCurrent)
            .OrderBy(v => v.VersionNumber)
            .ToList();

        var expired = evictable.Where(v => v.CreatedAt < cutoff).ToList();
        // 新版本已在清单内（AddAsync + SaveChanges 先于保留策略执行），
        // 超量判定直接用 MaxVersionsPerFile，不再为"即将加入的那个"留位
        var overflow = evictable.Count - expired.Count - maxVersions;
        if (overflow > 0)
        {
            // 未过期部分里最旧的先走：保留策略的牺牲顺序永远是旧版本
            expired.AddRange(evictable
                .Where(v => v.CreatedAt >= cutoff)
                .Take(overflow));
        }

        // 保留策略：逐条"先删物理、成功才删记录"。物理删失败（文件占用）的
        // 条目保留记录跳过——留一个可见的旧版本远好过一个永久孤儿文件
        var removed = 0;
        foreach (var victim in expired)
        {
            if (!FileVersionStore.TryDeleteSnapshot(victim.StoragePath))
            {
                _logger.LogWarning(
                    "保留策略跳过：版本文件删除失败，保留记录: FileItemId={FileItemId}, Path={Path}",
                    fileItemId, victim.StoragePath);
                continue;
            }

            _versionRepository.Delete(victim);
            removed++;
        }

        if (removed > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "版本保留策略清理: FileItemId={FileItemId}, 删除 {Count} 个版本",
                fileItemId, removed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FileVersionDto>>> GetVersionsAsync(
        Guid fileItemId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // 不要求 FileItem 仍存在：永久删除的文件记录已没了，但版本快照的
        // 全部意义就是兜住这种不可逆操作，此时版本必须仍可列出可下载。
        // 版本记录自带 CreatedByUserId，用户名经仓储附带查询，不依赖 FileItem。
        // 多用户隔离：列表按创建者收敛（.ai/lessons.md 记载的"七方法全裸"缺口），
        // 他人文件的版本对本用户不可见
        var versions = await _versionRepository.GetByFileItemIdAsync(fileItemId, cancellationToken);
        var dtos = versions
            .Where(v => v.CreatedByUserId == userId)
            .Select(MapToDto)
            .ToList();

        return Result<IReadOnlyList<FileVersionDto>>.Success(dtos);
    }

    /// <inheritdoc />
    public async Task<Result<FileVersionDto>> GetVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var version = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber, cancellationToken);

        if (version is null || version.CreatedByUserId != userId)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        return Result<FileVersionDto>.Success(MapToDto(version));
    }

    /// <inheritdoc />
    public async Task<Result<FileVersionDto>> RestoreVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var version = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber, cancellationToken);

        if (version is null || version.CreatedByUserId != userId)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        // 检查版本文件是否存在
        if (!File.Exists(version.StoragePath))
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.StorageNotFound", "版本文件已丢失"));
        }

        // 获取当前版本并标记为旧版本
        var currentVersion = await _versionRepository.GetCurrentVersionAsync(
            fileItemId, cancellationToken);
        if (currentVersion is not null)
        {
            currentVersion.MarkAsOld();
            _versionRepository.Update(currentVersion);
        }

        // 将选中的版本标记为当前版本
        version.MarkAsCurrent();
        _versionRepository.Update(version);

        // 恢复文件内容。前置校验：FileItem 已消失（回收站清空/永久删除）时
        // 无处可恢复，必须失败而不是把版本标成 IsCurrent 后返回成功——
        // 假成功比报错更危险（ITM-N9 家族），用户以为恢复完成实际内容没落地
        var fileItem = await _fileItemRepository.GetByIdAsync(fileItemId, cancellationToken);
        if (fileItem is null)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.FileItemNotFound", "文件记录已不存在，无法恢复"));
        }

        if (fileItem.IsDeleted)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.FileItemDeleted", "文件在回收站中，请先恢复文件再恢复版本"));
        }

        {
            // ITM-N18：目标是物理路径而非虚拟路径（旧代码把 /xxx 当磁盘路径写到盘根）。
            // 先拷贝到临时文件再原子替换：Copy 失败时原文件不受影响；Delete+Move
            // 之间仍有崩溃窗口（删了旧的、新的没移入，两边皆丢），File.Replace
            // 是 NTFS 上的原子替换原语，一个调用闭合该窗口
            var targetPath = fileItem.PhysicalPath;
            var tempPath = targetPath + ".restore.tmp";
            try
            {
                File.Copy(version.StoragePath, tempPath, overwrite: true);

                if (File.Exists(targetPath))
                    File.Replace(tempPath, targetPath, null);
                else
                    File.Move(tempPath, targetPath);
            }
            finally
            {
                // 替换失败的残留临时文件必须清掉：它不在扫描排除清单上，
                // 下次扫描会被当用户文件入库（审计 F-017）
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* 尽力而为 */ }
                }
            }

            // 内容换了，库里的体积与哈希必须同步：不同步的话浏览列表显示恢复前的
            // 旧大小，版本比较的 SizeDifference 也整体偏移（快照记录的是各版本
            // 创建时的值，当前值只能从 fileItem 取）。Hash 无条件同步：
            // 版本记录本身没有哈希时清空，留着旧值等于库盘错位
            fileItem.UpdateSize(version.FileSize);
            fileItem.UpdateHash(version.FileHash);
            _fileItemRepository.UpdateAsync(fileItem, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ROADMAP Phase 1 验收"版本操作纳入 SignalR 推送"：恢复换了文件内容，
        // 订阅了所在目录的客户端要实时看到新大小/新内容。ETag 失效由 UoW 的
        // 目录版本号递增自动覆盖（FileItem 被写），这里是实时推送那一半
        {
            var parentPath = DirectoryVersion.GetParentDirectory(fileItem.Path);
            if (parentPath is not null)
                await _notifier.NotifyFileChangedAsync(parentPath, FileChangeType.Modified, new { path = fileItem.Path });
        }

        _logger.LogInformation(
            "恢复文件版本: FileItemId={FileItemId}, Version={Version}",
            fileItemId, versionNumber);

        return Result<FileVersionDto>.Success(MapToDto(version));
    }

    /// <inheritdoc />
    public async Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var version = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber, cancellationToken);

        if (version is null || version.CreatedByUserId != userId)
        {
            return Result<(Stream, string, string)>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        if (!File.Exists(version.StoragePath))
        {
            return Result<(Stream, string, string)>.Failure(
                new Error("FileVersion.StorageNotFound", "版本文件已丢失"));
        }

        var fileItem = await _fileItemRepository.GetByIdAsync(fileItemId, cancellationToken);
        var fileName = fileItem?.Name ?? $"version_{versionNumber}";
        // MIME 统一经 IFileSystemService 真源（原本地映射表为漂移副本，ITM-N2 批次收敛）
        var contentType = _fileSystemService.GetMimeType(fileName);

        var stream = new FileStream(
            version.StoragePath,
            FileMode.Open,
            FileAccess.Read,
            System.IO.FileShare.Read);

        return Result<(Stream, string, string)>.Success((stream, fileName, contentType));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var version = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber, cancellationToken);

        if (version is null || version.CreatedByUserId != userId)
        {
            return Result.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        if (version.IsCurrent)
        {
            // 错误码带 Conflict 后缀：ResultExtensions 的映射表按此命中 409，
            // 不带会落默认 500（客户端错误被报成服务端故障）
            return Result.Failure(
                new Error("FileVersion.CurrentVersionConflict", "无法删除当前版本"));
        }

        // 先删物理、成功才删记录。原顺序（先删文件后删库，或先删库后补删文件）
        // 各有坏结局：前者在库提交失败时留僵尸记录（UI 列得出、恢复报文件丢失），
        // 后者在物理删除失败时留反向孤儿（记录没了文件还在，.versions 被扫描
        // 排除，永远扫不回来）。先删物理则文件占用时记录保留 + 明确报错，
        // 用户知道"文件被占用"而不是看到一个坏掉的版本条目
        var physicalDeleted = FileVersionStore.TryDeleteSnapshot(version.StoragePath);
        if (!physicalDeleted)
        {
            _logger.LogWarning(
                "版本文件删除失败（保留记录）: FileItemId={FileItemId}, Version={Version}, Path={Path}",
                fileItemId, versionNumber, version.StoragePath);
            return Result.Failure(
                new Error("FileVersion.StorageDeleteFailed", "版本文件删除失败（可能被占用），记录已保留"));
        }

        _versionRepository.Delete(version);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "删除文件版本: FileItemId={FileItemId}, Version={Version}",
            fileItemId, versionNumber);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<VersionComparisonDto>> CompareVersionsAsync(
        Guid fileItemId,
        int versionNumber1,
        int versionNumber2,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var version1 = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber1, cancellationToken);
        var version2 = await _versionRepository.GetByVersionNumberAsync(
            fileItemId, versionNumber2, cancellationToken);

        if (version1 is null || version1.CreatedByUserId != userId)
        {
            return Result<VersionComparisonDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber1} 不存在"));
        }

        if (version2 is null || version2.CreatedByUserId != userId)
        {
            return Result<VersionComparisonDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber2} 不存在"));
        }

        var comparison = new VersionComparisonDto(
            Version1: MapToDto(version1),
            Version2: MapToDto(version2),
            SizeDifference: version2.FileSize - version1.FileSize,
            HashDiffers: !string.Equals(version1.FileHash, version2.FileHash, StringComparison.OrdinalIgnoreCase),
            TimeDifference: version2.CreatedAt - version1.CreatedAt);

        return Result<VersionComparisonDto>.Success(comparison);
    }

    private static FileVersionDto MapToDto(FileVersion version)
    {
        return new FileVersionDto(
            Id: version.Id,
            FileItemId: version.FileItemId,
            VersionNumber: version.VersionNumber,
            FileSize: version.FileSize,
            FileHash: version.FileHash,
            Comment: version.Comment,
            IsCurrent: version.IsCurrent,
            CreatedByUserId: version.CreatedByUserId,
            CreatedByUsername: version.CreatedByUser?.Username,
            CreatedAt: version.CreatedAt);
    }

}
