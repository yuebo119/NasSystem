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
using NasSystem.Domain.Common;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 文件服务实现
public sealed partial class FileService : IFileService
{
    public async Task<Result<DirectoryContentDto>> GetDirectoryContentsAsync(
        string path,
        Guid userId,
        bool includeHidden = false,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);

        // 获取目录项和子项
        var parent = await _fileRepository.GetByPathAsync(path, cancellationToken);
        var children = await _fileRepository.GetChildrenAsync(parent?.Id, includeDeleted: false, cancellationToken);
        
        if (!includeHidden)
            children = children.Where(c => !c.IsHidden).ToList();

        var breadcrumbs = BuildBreadcrumbs(path);
        var folders = children.Where(c => c.ItemType == FileItemType.Folder).ToList();
        var files = children.Where(c => c.ItemType == FileItemType.File).ToList();

        return Result<DirectoryContentDto>.Success(new DirectoryContentDto
        {
            Path = path,
            Name = GetFileName(path),
            ParentId = parent?.ParentId,
            ParentPath = GetParentPath(path),
            Items = children.Select(MapToDto).ToList(),
            FolderCount = folders.Count,
            FileCount = files.Count,
            TotalSizeBytes = files.Sum(f => f.SizeBytes),
            TotalSizeDisplay = FormatSize(files.Sum(f => f.SizeBytes)),
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

        var items = await _fileRepository.SearchAsync(
            request.Query,
            ownerId: userId,
            storagePoolId: null,
            basePath: request.Path,
            extension: request.Extension,
            isFolder: isFolder,
            cancellationToken: cancellationToken);

        // 客户端分页 (简化实现)
        var paged = items
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var dtos = paged.Select(MapToDto).ToList();
        return Result<PagedResult<FileItemDto>>.Success(
            PagedResult<FileItemDto>.Create(dtos, request.Page, request.PageSize, items.Count));
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
