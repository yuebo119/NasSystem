// =============================================================================
// NasSystem.Application - Files/IFileRecycleService.cs
// 回收站生命周期用例域窄接口（ITM-T3：ISP 接口隔离）
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;

/// <summary>
/// 回收站服务——软删/恢复/清空生命周期终态域
/// </summary>
public interface IFileRecycleService
{
    /// <summary>删除文件/文件夹（默认软删，Permanent=true 物理删除）</summary>
    Task<Result<DeleteResultDto>> DeleteAsync(
        DeleteItemsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>获取回收站内容</summary>
    Task<Result<IReadOnlyList<FileItemDto>>> GetRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>恢复已删除项</summary>
    Task<Result<FileItemDto>> RestoreAsync(
        Guid fileId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>清空回收站</summary>
    Task<Result<DeleteResultDto>> EmptyRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
