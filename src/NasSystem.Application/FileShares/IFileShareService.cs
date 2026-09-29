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
    Task<Result<ShareDownloadTarget>> GetDownloadStreamAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        bool isAuthenticated = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分享文件夹内容
    /// </summary>
    Task<Result<ShareFolderContentDto>> GetFolderContentAsync(
        string shareCode,
        string? accessToken,
        string? subPath,
        bool isAuthenticated = false,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// 分享下载目标：单文件走 Stream，文件夹走 ZipDirectory。
/// </summary>
/// <remarks>
/// 文件夹下载不在服务层产出 ZIP 字节——压缩流由 Api 层 ZipDirectoryResult
/// 直接写入 HTTP 响应体；原实现先整体打进 MemoryStream 再返回，匿名公开端点
/// 被大目录触发时会 OOM。
/// </remarks>
public sealed record ShareDownloadTarget
{
    /// <summary>单文件下载流（文件夹下载为 null）</summary>
    public Stream? Stream { get; init; }

    /// <summary>文件夹 ZIP 的物理目录（单文件下载为 null，由 Api 层边扫边压缩）</summary>
    public string? ZipDirectory { get; init; }

    public required string FileName { get; init; }
    public required string ContentType { get; init; }
}
