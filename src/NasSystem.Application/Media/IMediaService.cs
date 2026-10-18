// =============================================================================
// NasSystem.Application - Media/IMediaService.cs
// 媒体库服务（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Application.Media;

using NasSystem.Shared;

/// <summary>
/// 媒体库：按类型聚合图片/视频/音频，时间线浏览 + 按需元数据。
/// 列表只走数据库（不碰文件），元数据按页批量提取（解析盒/帧头，
/// 大文件也是毫秒级），两者分离避免列表页被解码拖死
/// </summary>
public interface IMediaService
{
    /// <summary>媒体列表：按类型过滤，按日期倒序，可选月份过滤</summary>
    Task<Result<PagedResult<MediaItemDto>>> GetMediaAsync(
        MediaQuery query,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量取元数据。paths 里非本人或不存在的一静默跳过（返回集合只含有效项），
    /// 单个文件解析失败按无元数据返回（不阻断同批其他文件）
    /// </summary>
    Task<Result<IReadOnlyList<MediaMetadataDto>>> GetMetadataAsync(
        IReadOnlyList<string> paths,
        Guid userId,
        CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>媒体查询</summary>
/// <param name="Type">image / video / audio</param>
/// <param name="Month">yyyy-MM 可选，时间线跳月用</param>
public sealed record MediaQuery(
    string Type,
    string? Month = null,
    int Page = 1,
    int PageSize = 60);

/// <summary>
/// 媒体列表项。复用 FileItemDto 的字段集（前端网格直接用），
/// 外加媒体类型（筛选态回显）与日期分组键
/// </summary>
public sealed record MediaItemDto(
    Guid Id,
    string Name,
    string Path,
    string Extension,
    long SizeBytes,
    string MediaType,
    string DateGroup,
    DateTime CreatedAt);

/// <summary>媒体元数据</summary>
public sealed record MediaMetadataDto(
    string Path,
    int? Width,
    int? Height,
    double? DurationSeconds);
