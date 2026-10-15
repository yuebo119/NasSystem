// ═══ 浏览与查询域 ═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

using Microsoft.Extensions.Configuration;
using NasSystem.Application.Common;
using NasSystem.Application.Files.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Application.Notifications;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 文件服务实现
public sealed partial class FileService 
{
    public async Task<Result<DirectoryContentDto>> GetDirectoryContentsAsync(
        string path,
        Guid userId,
        bool includeHidden = false,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);

        // 父目录记录只在非根目录时才查：根目录的 parentId 必为 null，而库里不存在
        // Path = "/" 的行，这次查询必然返回 null，纯粹是白跑一趟
        FileItem? parent = null;
        if (path != "/")
            parent = await _fileRepository.GetByPathAsync(path, cancellationToken);

        // 摘要查询：按需取列 + IsHidden 下推 + 不排序（前端 store 会按用户选择重排）
        var children = await _fileRepository.GetChildrenSummariesAsync(
            parent?.Id, includeHidden, cancellationToken);

        var breadcrumbs = BuildBreadcrumbs(path);

        // 单趟遍历同时产出 DTO 列表与计数/求和：原先对 children 做四次完整枚举
        // （隐藏过滤一趟、folders/files 各一趟、MapToDto 一趟）加四个 List 分配
        var items = new List<FileItemDto>(children.Count);
        var folderCount = 0;
        var fileCount = 0;
        long totalSizeBytes = 0;
        foreach (var c in children)
        {
            if (c.ItemType == FileItemType.Folder)
            {
                folderCount++;
            }
            else
            {
                fileCount++;
                totalSizeBytes += c.SizeBytes;
            }

            items.Add(new FileItemDto
            {
                Id = c.Id,
                Name = c.Name,
                Path = c.Path,
                ItemType = c.ItemType,
                SizeBytes = c.SizeBytes,
                SizeDisplay = FormatSize(c.SizeBytes),
                MimeType = c.MimeType,
                Extension = c.Extension,
                Hash = c.Hash,
                ParentId = c.ParentId,
                OwnerId = c.OwnerId,
                IsHidden = c.IsHidden,
                IsReadOnly = c.IsReadOnly,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            });
        }

        return Result<DirectoryContentDto>.Success(new DirectoryContentDto
        {
            Path = path,
            Name = GetFileName(path),
            ParentId = parent?.ParentId,
            ParentPath = GetParentPath(path),
            Items = items,
            FolderCount = folderCount,
            FileCount = fileCount,
            TotalSizeBytes = totalSizeBytes,
            TotalSizeDisplay = FormatSize(totalSizeBytes),
            Breadcrumbs = breadcrumbs
        });
    }

    public async Task<Result<FileItemDto>> GetFileInfoAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);
        var item = await _fileRepository.GetByPathAsync(path, cancellationToken);

        if (item is null)
            return Result<FileItemDto>.Failure(FileErrors.NotFound(path));

        return Result<FileItemDto>.Success(MapToDto(item));
    }


    public async Task<Result<PagedResult<FileItemDto>>> SearchAsync(
        SearchFilesRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // 确定是否只搜索文件夹
        bool? isFolder = request.ItemType switch
        {
            FileItemType.Folder => true,
            FileItemType.File => false,
            _ => null
        };

        // pageSize 上限夹取在服务层：仓储也夹一次，但这里夹住后
        // PagedResult 报出去的 PageSize 与真实取回数才一致
        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 50;
        if (pageSize > 200) pageSize = 200;

        // 分页与计数都在 SQL 侧完成：原实现取满 500 条再内存 Skip/Take，
        // 翻页要把整个 LIKE 扫描重跑，TotalCount 报的还是窗口计数
        var result = await _fileRepository.SearchAsync(
            request.Query,
            ownerId: userId,
            storagePoolId: null,
            basePath: request.Path,
            extension: request.Extension,
            isFolder: isFolder,
            page: page,
            pageSize: pageSize,
            cancellationToken: cancellationToken);

        var dtos = result.Items.Select(MapToDto).ToList();
        return Result<PagedResult<FileItemDto>>.Success(
            PagedResult<FileItemDto>.Create(dtos, page, pageSize, result.TotalCount));
    }

    public async Task<Result<FileStatisticsDto>> GetStatisticsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var stats = await _fileRepository.GetStatisticsAsync(userId, cancellationToken);
        
        return Result<FileStatisticsDto>.Success(new FileStatisticsDto
        {
            TotalFiles = stats.TotalFiles,
            TotalFolders = stats.TotalFolders,
            TotalSizeBytes = stats.TotalSizeBytes,
            TotalSizeDisplay = FormatSize(stats.TotalSizeBytes),
            ImageCount = stats.ImageCount,
            VideoCount = stats.VideoCount,
            AudioCount = stats.AudioCount,
            DocumentCount = stats.DocumentCount,
            OtherCount = stats.OtherCount,
            RecentFilesCount = stats.RecentFilesCount
        });
    }

}
