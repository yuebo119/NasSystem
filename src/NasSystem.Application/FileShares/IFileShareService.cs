// =============================================================================
// NasSystem.Application - IFileShareService.cs
// 文件分享服务接口
// =============================================================================

namespace NasSystem.Application.FileShares;

using NasSystem.Application.FileShares.Dtos;
using NasSystem.Shared;

/// <summary>
/// 文件分享服务接口
/// </summary>
public interface IFileShareService
{
    #region 管理 API（需要登录）

    /// <summary>
    /// 创建文件分享
    /// </summary>
    Task<Result<FileShareDto>> CreateAsync(
        CreateFileShareRequest request,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的分享列表
    /// </summary>
    Task<Result<IReadOnlyList<FileShareDto>>> GetMySharesAsync(
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享详情
    /// </summary>
    Task<Result<FileShareDto>> GetByIdAsync(
        Guid shareId,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新分享设置
    /// </summary>
    Task<Result<FileShareDto>> UpdateAsync(
        Guid shareId,
        UpdateFileShareRequest request,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除分享
    /// </summary>
    Task<Result> DeleteAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重新生成分享码
    /// </summary>
    Task<Result<FileShareDto>> RegenerateShareCodeAsync(
        Guid shareId,
        Guid userId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    #endregion

    #region 公开访问 API（无需登录）

    /// <summary>
    /// 获取公开分享信息
    /// </summary>
    Task<Result<PublicShareInfoDto>> GetPublicShareInfoAsync(
        string shareCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证分享密码
    /// </summary>
    Task<Result<string>> VerifyPasswordAsync(
        string shareCode,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享文件下载流
    /// </summary>
    Task<Result<(Stream Stream, string FileName, string ContentType)>> GetDownloadStreamAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享文件夹内容
    /// </summary>
    Task<Result<ShareFolderContentDto>> GetFolderContentAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        CancellationToken cancellationToken = default);

    #endregion
}
