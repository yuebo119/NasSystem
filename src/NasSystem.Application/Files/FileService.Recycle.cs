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

        // S3：子树软删改单条 SQL 后的两类副作用收集。原实现由逐实体
        // UpdateAsync 经 UoW 跟踪自动完成（目录版本号递增、FileDeletedEvent
        // 分发缩略图缓存清理），批量 SQL 不跟踪实体，改为服务显式处理
        var subtreeFilePaths = new List<string>();
        var subtreeDirPaths = new List<string>();

        foreach (var path in request.Paths)
        {
            var normalized = NormalizePath(path);

            // ROADMAP Phase 2 只读保护：软删与永久删都拦——软删同样让内容
            // 从可见目录消失，回收站不是受保护内容的合法出口。走 FailedItems
            // 部分失败机制（与"路径不存在"同款），其余项照常删除：
            // Delete 的既有语义就是 per-item 结果汇总，不为策略拒绝改整批语义
            if (await IsPathProtectedAsync(normalized, cancellationToken))
            {
                result.FailedItems.Add($"目录受只读保护，禁止删除: {normalized}");
                continue;
            }

            // 永久删的查找含软删记录：回收站里的条目 IsDeleted=1，按可见口径
            // （GetByPathAsync 排已删）找不到，回收站"单条永久删除"会全数落入
            // "路径不存在"——2026-10-19 E2E 走查实证，该按钮此前从未成功过
            var item = request.Permanent
                ? await _fileRepository.GetByPathIncludingDeletedAsync(normalized, cancellationToken)
                : await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            // 多用户隔离：不存在与非本人的条目都按不存在处理（删/永久删都是写路径）
            if (item is null || item.OwnerId != userId)
            {
                result.FailedItems.Add($"路径不存在: {normalized}");
                continue;
            }

            if (request.Permanent)
            {
                if (await PermanentDeleteAsync(item, result, userId, cancellationToken))
                    succeededPaths.Add(normalized);
            }
            else
            {
                // ITM-N7/N4：默认软删除——自身 + 后代子树标记，回收站读 IsDeleted 记录。
                // 单条 SQL 替代逐条实体 UPDATE（含根自身），先取轻量条目清单供副作用处理
                var subtree = await _fileRepository.GetSubtreeEntriesAsync(item.Path, cancellationToken);
                await _fileRepository.SoftDeleteSubtreeAsync(item.Path, _dateTime.UtcNow, cancellationToken);

                foreach (var entry in subtree)
                {
                    if (entry.ItemType == FileItemType.Folder)
                        subtreeDirPaths.Add(entry.Path);
                    else
                        subtreeFilePaths.Add(entry.Path);
                }

                result.DeletedCount++;
                succeededPaths.Add(normalized);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // S3 副作用（提交成功后执行，与 UoW 的 BumpDirectoryVersions 同时序）：
        // 缩略图缓存清理等价于原 FileDeletedEvent 逐实体分发；目录版本号递增
        // 覆盖子树内所有目录（内容全空）加各删除项的父目录（少一项）
        foreach (var filePath in subtreeFilePaths)
            await _thumbnailService.ClearCacheAsync(filePath);
        foreach (var dir in subtreeDirPaths)
            DirectoryVersion.Bump(dir);
        foreach (var path in succeededPaths)
        {
            DirectoryVersion.Bump(GetParentPath(path));
            // ITM-N25：只对成功项推送——失败项记录仍在，推送 Deleted 会误导订阅端
            await NotifyChangedAsync(GetParentPath(path), FileChangeType.Deleted, path);
        }
        return Result<DeleteResultDto>.Success(result);
    }

    /// <summary>
    /// 永久删除：物理删除成功才删数据库记录（ITM-N14：物理失败仍删库会产生孤儿文件，
    /// 并在下次扫描时被当作新文件重新入库）；文件夹级联清理后代记录。
    /// </summary>
    /// <remarks>
    /// 单文件永久删除前自动产版本（ROADMAP Phase 1）：永久删除不可逆，
    /// 版本快照是误操作的最后防线，保留策略（数量+天数）兜底体积。
    /// 文件夹级联删除不逐文件产版本：一次操作可能产生成千上万条版本记录，
    /// 快照成本从"元数据级"膨胀为"记录级"，超出保留策略的设计口径。
    /// 文件夹场景的防误删由默认软删除（回收站）承担。
    /// </remarks>
    private async Task<bool> PermanentDeleteAsync(FileItem item, DeleteResultDto result, Guid userId, CancellationToken ct)
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
                // 先快照后删除：顺序反了就成了给已删除的文件产版本（必然失败）
                var pool = await _storagePoolRepository.GetByIdAsync(item.StoragePoolId, ct);
                if (pool is not null)
                {
                    await _fileVersionService.CreateAutoVersionAsync(item, pool.MountPath, userId, ct);
                }

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
        var items = await _fileRepository.GetDeletedItemsAsync(ownerId: userId, cancellationToken: cancellationToken);
        return Result<IReadOnlyList<FileItemDto>>.Success(items.Select(MapToDto).ToList());
    }

    public async Task<Result<FileItemDto>> RestoreAsync(
        Guid fileId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var item = await _fileRepository.GetByIdAsync(fileId, cancellationToken);
        // 多用户隔离：非本人的回收站记录按不存在处理（恢复是写路径）
        if (item is null || item.OwnerId != userId)
            return Result<FileItemDto>.Failure(FileErrors.NotFoundById(fileId));

        // 内存实体同步恢复态：持久化走下面的批量 SQL（不经过实体），
        // 但响应 DTO 由实体映射，不同步则 IsDeleted/UpdatedAt 与库内不一致
        item.Restore();

        // S3：整棵子树单条 SQL 恢复（根 + 全部后代不论删除态）。
        // 原实现按 Id 取后代再逐条 Restore，而 GetDescendantsAsync 带
        // IsDeleted = 0 过滤：软删文件夹的后代全是 1，取回空列表，
        // 实际只恢复了根、后代留在回收站（与 ITM-N7 的注释意图相悖）。
        // 先取轻量清单供目录版本号递增
        var subtree = await _fileRepository.GetSubtreeEntriesAsync(item.Path, cancellationToken);
        await _fileRepository.RestoreSubtreeAsync(item.Path, cancellationToken);

        var saved = await SaveChangesWithConflictGuardAsync(
            new Error("File.AlreadyExists", "同路径已存在记录，无法恢复"),
            cancellationToken);
        if (saved.IsFailure)
            return Result<FileItemDto>.Failure(saved.Error);

        // 子树内所有目录的内容都变了（后代回来），父目录少了一项（根回来）
        foreach (var entry in subtree)
        {
            if (entry.ItemType == FileItemType.Folder)
                DirectoryVersion.Bump(entry.Path);
        }
        DirectoryVersion.Bump(GetParentPath(item.Path));

        return Result<FileItemDto>.Success(MapToDto(item));
    }

    public async Task<Result<DeleteResultDto>> EmptyRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var result = new DeleteResultDto();
        var deletedItems = await _fileRepository.GetDeletedItemsAsync(ownerId: userId, cancellationToken: cancellationToken);

        // 按路径倒序处理：父路径是子路径的前缀，倒序保证子先于父删。
        // 软删一个文件夹时整棵子树打同一 DeletedAt（SoftDeleteSubtreeAsync），
        // 按 DeletedAt 排序父子次序不定，父先删即撞子的 ParentId 外键
        // RESTRICT，该条进 FailedItems、子树留孤儿（2026-10-19 审计实证）
        foreach (var item in deletedItems.OrderByDescending(i => i.Path, StringComparer.Ordinal))
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
