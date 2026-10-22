// =============================================================================
// NasSystem.Application - IFileVersionService.cs
// 文件版本服务接口
// =============================================================================

namespace NasSystem.Application.FileVersions;

using NasSystem.Domain.Entities;
using NasSystem.Shared;

/// <summary>
/// 文件版本服务接口
/// </summary>
public interface IFileVersionService
{
    /// <summary>
    /// 自动产版本：把文件当前内容快照进版本库（ROADMAP Phase 1）。
    /// 覆盖上传与永久删除前由 FileService 调用。快照失败不阻断主流程
    /// （记录告警日志后主操作继续），版本能力是保险而非主路径依赖
    /// </summary>
    /// <param name="item">被快照的文件项（取当前物理内容）</param>
    /// <param name="poolMountPath">该文件所属存储池的挂载点</param>
    /// <param name="userId">触发操作的用户</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task CreateAutoVersionAsync(
        FileItem item,
        string poolMountPath,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件的所有版本
    /// </summary>
    Task<Result<IReadOnlyList<FileVersionDto>>> GetVersionsAsync(
        Guid fileItemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取该文件最新版本快照的物理路径（服务内部补偿用：覆盖上传库提交失败时
    /// 把旧内容拷回活动路径）。不属于对外契约，不走 DTO——DTO 有意不暴露
    /// StoragePath，避免把服务器目录布局泄露给客户端
    /// </summary>
    Task<string?> GetLatestSnapshotPathAsync(
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
