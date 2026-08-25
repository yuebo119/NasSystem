// =============================================================================
// NasSystem.Application - FileVersionService.cs
// 文件版本服务实现
// =============================================================================

namespace NasSystem.Application.FileVersions;

using Microsoft.Extensions.Logging;
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

    public FileVersionService(
        IFileVersionRepository versionRepository,
        IFileItemRepository fileItemRepository,
        IUnitOfWork unitOfWork,
        ILogger<FileVersionService> logger,
        IFileSystemService fileSystemService)
    {
        _versionRepository = versionRepository;
        _fileItemRepository = fileItemRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _fileSystemService = fileSystemService;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FileVersionDto>>> GetVersionsAsync(
        Guid fileItemId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var fileItem = await _fileItemRepository.GetByIdAsync(fileItemId, cancellationToken);
        if (fileItem is null)
        {
            return Result<IReadOnlyList<FileVersionDto>>.Failure(
                new Error("FileVersion.FileNotFound", "文件不存在"));
        }

        var versions = await _versionRepository.GetByFileItemIdAsync(fileItemId, cancellationToken);
        var dtos = versions.Select(MapToDto).ToList();

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

        if (version is null)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        return Result<FileVersionDto>.Success(MapToDto(version));
    }

    /// <inheritdoc />
    public async Task<Result<FileVersionDto>> CreateVersionAsync(
        Guid fileItemId,
        string storagePath,
        long fileSize,
        string fileHash,
        Guid userId,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        var fileItem = await _fileItemRepository.GetByIdAsync(fileItemId, cancellationToken);
        if (fileItem is null)
        {
            return Result<FileVersionDto>.Failure(
                new Error("FileVersion.FileNotFound", "文件不存在"));
        }

        // 获取最新版本号
        var latestVersionNumber = await _versionRepository.GetLatestVersionNumberAsync(
            fileItemId, cancellationToken);

        // 将当前版本标记为旧版本
        var currentVersion = await _versionRepository.GetCurrentVersionAsync(
            fileItemId, cancellationToken);
        if (currentVersion is not null)
        {
            currentVersion.MarkAsOld();
            _versionRepository.Update(currentVersion);
        }

        // 创建新版本
        var newVersion = FileVersion.Create(
            fileItemId,
            latestVersionNumber + 1,
            storagePath,
            fileSize,
            fileHash,
            userId,
            comment);

        await _versionRepository.AddAsync(newVersion, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "创建文件版本: FileItemId={FileItemId}, Version={Version}",
            fileItemId, newVersion.VersionNumber);

        return Result<FileVersionDto>.Success(MapToDto(newVersion));
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

        if (version is null)
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

        // 恢复文件内容
        var fileItem = await _fileItemRepository.GetByIdAsync(fileItemId, cancellationToken);
        if (fileItem is not null)
        {
            // ITM-N18：目标是物理路径而非虚拟路径（旧代码把 /xxx 当磁盘路径写到盘根）；
            // 先拷贝到临时文件再替换，Copy 失败时原文件不受影响，消除"先删后拷"数据丢失窗口
            var targetPath = fileItem.PhysicalPath;
            var tempPath = targetPath + ".restore.tmp";
            File.Copy(version.StoragePath, tempPath, overwrite: true);

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        if (version is null)
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

        if (version is null)
        {
            return Result.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber} 不存在"));
        }

        if (version.IsCurrent)
        {
            return Result.Failure(
                new Error("FileVersion.CannotDeleteCurrent", "无法删除当前版本"));
        }

        // 删除版本文件
        if (File.Exists(version.StoragePath))
        {
            try
            {
                File.Delete(version.StoragePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除版本文件失败: {Path}", version.StoragePath);
            }
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

        if (version1 is null)
        {
            return Result<VersionComparisonDto>.Failure(
                new Error("FileVersion.NotFound", $"版本 {versionNumber1} 不存在"));
        }

        if (version2 is null)
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
