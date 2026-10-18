// =============================================================================
// NasSystem.Application - ProtectedDirectories/IProtectedDirectoryService.cs
// 关键目录只读保护服务（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Application.ProtectedDirectories;

using NasSystem.Shared;

/// <summary>
/// 受保护目录：管理员标记的目录在应用层禁止覆盖/删除，防勒索软件经
/// WebDAV/网页端篡改。判定语义是"路径自身或任一祖先被标记"。
/// </summary>
/// <remarks>
/// 拦截点在 FileService 的全部写路径（上传覆盖/Move/Delete/Rename）与
/// WebDAV 对应端点——两者都走 FileService，一处接线全覆盖。
/// SMB sidecar 是管理员直通模式，天然不受本保护约束（文档明示）。
/// </remarks>
public interface IProtectedDirectoryService
{
    /// <summary>标记目录为受保护（幂等：已标记返回现有标记）</summary>
    Task<Result<ProtectedDirectoryDto>> MarkAsync(
        Guid fileItemId,
        Guid operatorUserId,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>取消保护（未标记时返回 NotFound）</summary>
    Task<Result> UnmarkAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>全部受保护目录（带路径展示）</summary>
    Task<Result<IReadOnlyList<ProtectedDirectoryDto>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>路径是否受保护（写路径拦截用；空路径按未保护处理）</summary>
    Task<bool> IsProtectedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>文件项是否被直接标记（取消保护按钮的可见性判定）</summary>
    Task<bool> IsMarkedAsync(Guid fileItemId, CancellationToken cancellationToken = default);
}

/// <summary>受保护目录展示</summary>
public sealed record ProtectedDirectoryDto(
    Guid FileItemId,
    string Path,
    string Name,
    string? Note,
    Guid MarkedByUserId,
    DateTime CreatedAt);
