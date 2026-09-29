// =============================================================================
// NasSystem.Application - IFileVersionService.cs
// 文件版本服务接口
// =============================================================================

namespace NasSystem.Application.FileVersions;

using NasSystem.Shared;

/// <summary>
/// 文件版本服务接口
/// </summary>
public interface IFileVersionService
{
    /// <summary>
    /// 获取文件的所有版本
    /// </summary>
    Task<Result<IReadOnlyList<FileVersionDto>>> GetVersionsAsync(
        Guid fileItemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定版本的详情
    /// </summary>
    Task<Result<FileVersionDto>> GetVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复到指定版本
    /// </summary>
    Task<Result<FileVersionDto>> RestoreVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载指定版本
    /// </summary>
    Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定版本
    /// </summary>
    Task<Result> DeleteVersionAsync(
        Guid fileItemId,
        int versionNumber,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 比较两个版本
    /// </summary>
    Task<Result<VersionComparisonDto>> CompareVersionsAsync(
        Guid fileItemId,
        int versionNumber1,
        int versionNumber2,
        Guid userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件版本 DTO
/// </summary>
public record FileVersionDto(
    Guid Id,
    Guid FileItemId,
    int VersionNumber,
    long FileSize,
    string FileHash,
    string? Comment,
    bool IsCurrent,
    Guid CreatedByUserId,
    string? CreatedByUsername,
    DateTime CreatedAt);

/// <summary>
/// 版本比较 DTO
/// </summary>
public record VersionComparisonDto(
    FileVersionDto Version1,
    FileVersionDto Version2,
    long SizeDifference,
    bool HashDiffers,
    TimeSpan TimeDifference);
