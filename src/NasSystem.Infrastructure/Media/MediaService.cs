// =============================================================================
// NasSystem.Infrastructure - Media/MediaService.cs
// 媒体库服务实现（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.Infrastructure.Media;

using NasSystem.Application.Media;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 媒体库服务实现。列表纯走数据库，元数据按需提取：
/// 列表页滚十页不该触发一次图片解码，所以两者分成两个端点
/// </summary>
public sealed class MediaService(
    IFileItemRepository fileItemRepository) : IMediaService
{
    /// <summary>类型 → 扩展名集合。与前端筛选标签同源，改这里要同步 i18n 与路由。
    /// 2026-10-21 扩充：原 audio 只有 .mp3、video 缺 .avi/.flv/.wmv，与缩略图服务
    /// 的 SupportedVideoExtensions 也不一致——媒体库列不出的文件，预览/抽帧/
    /// 转码能力做得再全也用不上。音频取浏览器原生可播集合（预览走 blob 播放）</summary>
    private static readonly Dictionary<string, string[]> TypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".svg"],
        ["video"] = [".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".flv", ".wmv"],
        ["audio"] = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus"],
    };

    public async Task<Result<PagedResult<MediaItemDto>>> GetMediaAsync(
        MediaQuery query,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!TypeExtensions.TryGetValue(query.Type, out var extensions))
        {
            return Result<PagedResult<MediaItemDto>>.Failure(
                Error.Validation($"不支持的媒体类型: {query.Type}"));
        }

        var page = query.Page > 0 ? query.Page : 1;
        var pageSize = query.PageSize > 0 ? query.PageSize : 60;

        var result = await fileItemRepository.GetMediaAsync(
            extensions, userId, query.Month, page, pageSize, cancellationToken);

        var mediaType = query.Type.ToLowerInvariant();
        var dtos = result.Items
            .Select(item => new MediaItemDto(
                item.Id,
                item.Name,
                item.Path,
                item.Extension ?? string.Empty,
                item.SizeBytes,
                mediaType,
                // 日期分组键 yyyy-MM：前端按它分节，与月份过滤同口径
                item.CreatedAt.ToString("yyyy-MM"),
                item.CreatedAt))
            .ToList();

        return Result<PagedResult<MediaItemDto>>.Success(
            PagedResult<MediaItemDto>.Create(dtos, page, pageSize, result.TotalCount));
    }

    public async Task<Result<IReadOnlyList<MediaMetadataDto>>> GetMetadataAsync(
        IReadOnlyList<string> paths,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const int maxBatch = 200;
        var effectivePaths = paths.Count > maxBatch ? paths.Take(maxBatch).ToList() : paths;

        var metadata = new List<MediaMetadataDto>(effectivePaths.Count);
        foreach (var path in effectivePaths)
        {
            var item = await fileItemRepository.GetByPathAsync(path, cancellationToken);
            // 非本人或不存在：静默跳过（返回集合只含有效项，不暴露他人存在性）
            if (item is null || item.OwnerId != userId || item.ItemType != FileItemType.File)
                continue;

            var extracted = MediaMetadataExtractor.Extract(item.PhysicalPath, item.Extension);
            metadata.Add(new MediaMetadataDto(
                item.Path, extracted.Width, extracted.Height, extracted.DurationSeconds));
        }

        return Result<IReadOnlyList<MediaMetadataDto>>.Success(metadata);
    }
}
