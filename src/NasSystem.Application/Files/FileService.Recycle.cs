// ═══ 回收站生命周期域 ═══

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
public sealed partial class FileService 
{
    public async Task<Result<DeleteResultDto>> DeleteAsync(
        DeleteItemsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = new DeleteResultDto();
        var succeededPaths = new List<string>();

        foreach (var path in request.Paths)
        {
            var normalized = NormalizePath(path);
            var item = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            if (item is null)
            {
                result.FailedItems.Add($"路径不存在: {normalized}");
                continue;
            }

            if (request.Permanent)
            {
                if (await PermanentDeleteAsync(item, result, cancellationToken))
                    succeededPaths.Add(normalized);
            }
            else
            {
                // ITM-N7/N4：默认软删除——自身 + 后代子树标记，回收站读 IsDeleted 记录
                var descendants = await _fileRepository.GetDescendantsAsync(item.Id, cancellationToken);
                item.SoftDelete();
                await _fileRepository.UpdateAsync(item, cancellationToken);

                foreach (var descendant in descendants)
                {
                    descendant.SoftDelete();
                    await _fileRepository.UpdateAsync(descendant, cancellationToken);
                }
                result.DeletedCount++;
                succeededPaths.Add(normalized);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        // ITM-N25：只对成功项推送——失败项记录仍在，推送 Deleted 会误导订阅端
        foreach (var path in succeededPaths)
            await NotifyChangedAsync(GetParentPath(path), FileChangeType.Deleted, path);
        return Result<DeleteResultDto>.Success(result);
    }

    /// <summary>
    /// 永久删除：物理删除成功才删数据库记录（ITM-N14：物理失败仍删库会产生孤儿文件，
    /// 并在下次扫描时被当作新文件重新入库）；文件夹级联清理后代记录。
    /// </summary>
    private async Task<bool> PermanentDeleteAsync(FileItem item, DeleteResultDto result, CancellationToken ct)
    {
        try
        {
            if (item.ItemType == FileItemType.Folder)
            {
                if (_fileSystemService.DirectoryExists(item.PhysicalPath))
                    _fileSystemService.DeleteDirectory(item.PhysicalPath, true);
            }
            else if (_fileSystemService.FileExists(item.PhysicalPath))
            {
                _fileSystemService.DeleteFile(item.PhysicalPath);
            }

            var descendants = await _fileRepository.GetDescendantsAsync(item.Id, ct);
            if (descendants.Count > 0)
                await _fileRepository.DeleteRangeAsync(descendants, ct);
            await _fileRepository.DeleteAsync(item, ct);

            result.DeletedCount++;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.FailedItems.Add($"{item.Name}: 物理删除失败，已保留记录（{ex.Message}）");
            return false;
        }
    }

    public async Task<Result<IReadOnlyList<FileItemDto>>> GetRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await _fileRepository.GetDeletedItemsAsync(userId, cancellationToken);
        return Result<IReadOnlyList<FileItemDto>>.Success(items.Select(MapToDto).ToList());
    }

    public async Task<Result<FileItemDto>> RestoreAsync(
        Guid fileId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var item = await _fileRepository.GetByIdAsync(fileId, cancellationToken);
        if (item is null)
            return Result<FileItemDto>.Failure(FileErrors.NotFoundById(fileId));

        item.Restore();
        await _fileRepository.UpdateAsync(item, cancellationToken);

        // ITM-N7：恢复文件夹时后代一并恢复，否则子项仍处于软删态导致目录内容为空
        var descendants = await _fileRepository.GetDescendantsAsync(item.Id, cancellationToken);
        foreach (var descendant in descendants)
        {
            descendant.Restore();
            await _fileRepository.UpdateAsync(descendant, cancellationToken);
        }

        var saved = await SaveChangesWithConflictGuardAsync(
            new Error("File.AlreadyExists", "同路径已存在记录，无法恢复"),
            cancellationToken);
        if (saved.IsFailure)
            return Result<FileItemDto>.Failure(saved.Error);

        return Result<FileItemDto>.Success(MapToDto(item));
    }

    public async Task<Result<DeleteResultDto>> EmptyRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = new DeleteResultDto();
        var deletedItems = await _fileRepository.GetDeletedItemsAsync(userId, cancellationToken);

        foreach (var item in deletedItems)
        {
            try
            {
                if (_fileSystemService.FileExists(item.PhysicalPath))
                    _fileSystemService.DeleteFile(item.PhysicalPath);
                else if (_fileSystemService.DirectoryExists(item.PhysicalPath))
                    _fileSystemService.DeleteDirectory(item.PhysicalPath, true);

                await _fileRepository.DeleteAsync(item, cancellationToken);
                result.DeletedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单个锁定文件不阻塞整批清理（ITM-N14）
                result.FailedItems.Add($"{item.Name}: 清理失败（{ex.Message}）");
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<DeleteResultDto>.Success(result);
    }

}
