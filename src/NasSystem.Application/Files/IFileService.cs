// =============================================================================
// NasSystem.Application - IFileService.cs
// 文件服务接口
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件服务接口
/// </summary>
public interface IFileService
{
    /// <summary>
    /// 获取目录内容
    /// </summary>
    Task<Result<DirectoryContentDto>> GetDirectoryContentsAsync(
        string path,
        Guid userId,
        bool includeHidden = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件信息
    /// </summary>
    Task<Result<FileItemDto>> GetFileInfoAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建文件夹
    /// </summary>
    Task<Result<FileItemDto>> CreateFolderAsync(
        CreateFolderRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 上传文件
    /// </summary>
    Task<Result<FileItemDto>> UploadFileAsync(
        UploadFileMetadata metadata,
        Stream fileStream,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载文件
    /// </summary>
    Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadFileAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重命名文件/文件夹
    /// </summary>
    Task<Result<FileItemDto>> RenameAsync(
        RenameItemRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移动文件/文件夹
    /// </summary>
    Task<Result<IReadOnlyList<FileItemDto>>> MoveAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 复制文件/文件夹
    /// </summary>
    Task<Result<IReadOnlyList<FileItemDto>>> CopyAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文件/文件夹
    /// </summary>
    Task<Result> DeleteAsync(
        DeleteItemsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取回收站内容
    /// </summary>
    Task<Result<IReadOnlyList<FileItemDto>>> GetRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复已删除项
    /// </summary>
    Task<Result<FileItemDto>> RestoreAsync(
        Guid fileId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空回收站
    /// </summary>
    Task<Result> EmptyRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索文件
    /// </summary>
    Task<Result<PagedResult<FileItemDto>>> SearchAsync(
        SearchFilesRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件缩略图
    /// </summary>
    Task<Result<Stream>> GetThumbnailAsync(
        string path,
        int size,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件统计信息
    /// </summary>
    Task<Result<FileStatisticsDto>> GetStatisticsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描存储目录并同步到数据库
    /// </summary>
    Task<Result<ScanResultDto>> ScanAndSyncAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
