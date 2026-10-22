// =============================================================================
// NasSystem.Application - Files/IFileAccessResolver.cs
// 按虚拟路径解析当前用户的文件访问（属主判定单一入口）
// =============================================================================

namespace NasSystem.Application.Files;

/// <summary>文件访问解析状态。Forbidden 与 NotFound 分开，由端点决定是否对外区分</summary>
public enum FileAccessStatus
{
    Ok = 0,
    NotFound = 1,
    Forbidden = 2,
}

/// <summary>解析成功后的文件访问信息（只含端点实际需要的字段）</summary>
public sealed record FileAccessInfo(
    string Path,
    string Name,
    string PhysicalPath,
    string? MimeType,
    string? Extension,
    bool IsFolder);

/// <summary>解析结果。非 Ok 时 File 为 null</summary>
public sealed record FileAccessResult(FileAccessStatus Status, FileAccessInfo? File);

/// <summary>
/// 按虚拟路径解析当前用户的文件。把"查库 + 软删过滤 + 属主判定"收成
/// Application 层的单一入口，避免每个端点各自注入仓储、各自写属主比较。
/// </summary>
public interface IFileAccessResolver
{
    /// <summary>
    /// 解析虚拟路径。<see cref="FileItemRepository.GetByPathAsync"/> 已过滤软删记录，
    /// 因此 NotFound 同时覆盖"不存在"与"已删除"。
    /// </summary>
    Task<FileAccessResult> ResolveAsync(
        string virtualPath,
        Guid userId,
        CancellationToken cancellationToken = default);
}