// =============================================================================
// NasSystem.Application - Files/IFileBrowseService.cs
// 浏览与查询用例域窄接口（ITM-T3：ISP 接口隔离）
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件浏览与查询服务——只读用例域
/// </summary>
public interface IFileBrowseService
{
    /// <summary>获取目录内容</summary>
    Task<Result<DirectoryContentDto>> GetDirectoryContentsAsync(
        string path,
        Guid userId,
        bool includeHidden = false,
        CancellationToken cancellationToken = default);

    /// <summary>获取文件信息</summary>
    Task<Result<FileItemDto>> GetFileInfoAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>搜索文件</summary>
    Task<Result<PagedResult<FileItemDto>>> SearchAsync(
        SearchFilesRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>获取文件统计信息</summary>
    Task<Result<FileStatisticsDto>> GetStatisticsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
