// =============================================================================
// NasSystem.Application - Files/FileAccessResolver.cs
// 虚拟路径 -> 属主校验后的文件访问信息
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;

/// <summary>文件访问解析器实现。只读，不改变任何状态。</summary>
public sealed class FileAccessResolver(IFileItemRepository fileRepository) : IFileAccessResolver
{
    public async Task<FileAccessResult> ResolveAsync(
        string virtualPath,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        // 仓储层已过滤 IsDeleted=0：软删文件按不存在处理，与浏览/分享口径一致
        var item = await fileRepository.GetByPathAsync(virtualPath, cancellationToken);
        if (item is null)
            return new FileAccessResult(FileAccessStatus.NotFound, null);

        // 非本人按 Forbidden 返回，由端点决定对外是 403 还是伪装成 404
        if (item.OwnerId != userId)
            return new FileAccessResult(FileAccessStatus.Forbidden, null);

        return new FileAccessResult(
            FileAccessStatus.Ok,
            new FileAccessInfo(
                item.Path,
                item.Name,
                item.PhysicalPath,
                item.MimeType,
                item.Extension,
                item.ItemType == FileItemType.Folder));
    }
}