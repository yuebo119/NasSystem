// =============================================================================
// NasSystem.Application - Files/IFileTransferService.cs
// 传输与结构变更用例域窄接口（ITM-T3：ISP 接口隔离）
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件传输与结构变更服务——创建/上传/下载/重命名/移动/复制
/// </summary>
public interface IFileTransferService
{
    /// <summary>创建文件夹</summary>
    Task<Result<FileItemDto>> CreateFolderAsync(
        CreateFolderRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>上传文件</summary>
    Task<Result<FileItemDto>> UploadFileAsync(
        UploadFileMetadata metadata,
        Stream fileStream,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>下载文件</summary>
    Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadFileAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>重命名文件/文件夹</summary>
    Task<Result<FileItemDto>> RenameAsync(
        RenameItemRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>移动文件/文件夹</summary>
    Task<Result<IReadOnlyList<FileItemDto>>> MoveAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>复制文件/文件夹</summary>
    Task<Result<IReadOnlyList<FileItemDto>>> CopyAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);
}
