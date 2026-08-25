// ═══ 扫描同步域 ═══

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
    public async Task<Result<ScanResultDto>> ScanAndSyncAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var counters = new ScanCounters();

        try
        {
            // 获取默认存储池
            var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
            if (defaultPool is null)
                return Result<ScanResultDto>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

            var storagePath = defaultPool.MountPath;
            
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }

            // 获取数据库中文件记录——ITM-N13：只取默认池的记录。
            // 物理扫描仅覆盖默认池挂载点，若对比全库记录，其他池的记录会被误判
            // "物理不存在"而批量删除（多存储池数据销毁）。
            var allDbItems = await _fileRepository.GetAllAsync(cancellationToken);
            var dbItems = allDbItems.Where(x => x.StoragePoolId == defaultPool.Id).ToList();

            // ITM-N26：软删记录也必须进入对比表——其物理文件仍存在于挂载点，
            // 若缺席则会被扫描当作"新发现"重建（软删文件复活），且恢复时撞 Path 唯一索引
            var softDeletedItems = await _fileRepository.GetDeletedItemsAsync(
                ownerId: null, cancellationToken);
            var softDeletedInPool = softDeletedItems.Where(x => x.StoragePoolId == defaultPool.Id);

            var dbPathMap = dbItems.Concat(softDeletedInPool)
                .ToDictionary(x => x.Path, x => x);

            // 扫描物理文件系统
            var physicalPaths = new HashSet<string>();
            await ScanDirectoryAsync(
                storagePath, 
                "/", 
                defaultPool.Id, 
                userId, 
                dbPathMap, 
                physicalPaths,
                counters,
                null,  // 根目录的 parentId 为 null
                cancellationToken);

            // 删除数据库中存在但物理文件不存在的记录
            var itemsToRemove = dbItems.Where(item => !physicalPaths.Contains(item.Path)).ToList();
            foreach (var item in itemsToRemove)
            {
                if (item.IsFolder)
                    counters.RemovedFolders++;
                else
                    counters.RemovedFiles++;
            }

            if (itemsToRemove.Count > 0)
            {
                await _fileRepository.DeleteRangeAsync(itemsToRemove, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            return Result<ScanResultDto>.Success(new ScanResultDto
            {
                AddedFiles = counters.AddedFiles,
                AddedFolders = counters.AddedFolders,
                RemovedFiles = counters.RemovedFiles,
                RemovedFolders = counters.RemovedFolders,
                UpdatedFiles = counters.UpdatedFiles,
                TotalSizeBytes = counters.TotalSize,
                TotalSizeDisplay = FormatSize(counters.TotalSize),
                Duration = duration,
                Message = $"扫描完成：新增 {counters.AddedFiles} 个文件、{counters.AddedFolders} 个文件夹，删除 {counters.RemovedFiles} 个文件、{counters.RemovedFolders} 个文件夹，更新 {counters.UpdatedFiles} 个文件"
            });
        }
        catch (Exception ex)
        {
            return Result<ScanResultDto>.Failure(new Error("Scan.Failed", $"扫描失败: {ex.Message}"));
        }
    }

    private sealed class ScanCounters
    {
        public int AddedFiles { get; set; }
        public int AddedFolders { get; set; }
        public int RemovedFiles { get; set; }
        public int RemovedFolders { get; set; }
        public int UpdatedFiles { get; set; }
        public long TotalSize { get; set; }
    }

    private async Task ScanDirectoryAsync(
        string physicalDir,
        string virtualPath,
        Guid storagePoolId,
        Guid userId,
        Dictionary<string, FileItem> dbPathMap,
        HashSet<string> physicalPaths,
        ScanCounters counters,
        Guid? currentDirId,  // 当前目录的 ID，作为子项的 parentId
        CancellationToken cancellationToken)
    {
        // 扫描子目录
        foreach (var dir in Directory.GetDirectories(physicalDir))
        {
            var dirName = Path.GetFileName(dir);
            var dirVirtualPath = CombinePath(virtualPath, dirName);
            physicalPaths.Add(dirVirtualPath);

            FileItem folder;
            if (!dbPathMap.TryGetValue(dirVirtualPath, out var existingFolder))
            {
                // 新文件夹，添加到数据库
                folder = FileItem.CreateFolder(
                    name: dirName,
                    path: dirVirtualPath,
                    physicalPath: dir,
                    storagePoolId: storagePoolId,
                    ownerId: userId,
                    parentId: currentDirId);

                await _fileRepository.AddAsync(folder, cancellationToken);
                dbPathMap[dirVirtualPath] = folder;
                counters.AddedFolders++;
            }
            else
            {
                folder = existingFolder;
                // 【保留论证】软删命中物理存在时为何"什么都不做"而不是恢复或重建：
                // 软删语义=用户明确不要了但暂存回收站；扫描只负责"物理↔数据库对齐"，
                // 无权替用户改变删除决定（NPD-4 同族：扫描不是所有权/状态改写工具）。
                // 若外部把文件放回原位，正确动作仍是保持回收站态由用户手动恢复。
                if (existingFolder.IsDeleted)
                    continue;

                // 修复 parentId 如果不正确
                if (existingFolder.ParentId != currentDirId)
                {
                    existingFolder.MoveTo(existingFolder.Path, existingFolder.PhysicalPath, currentDirId);
                    await _fileRepository.UpdateAsync(existingFolder, cancellationToken);
                }
            }

            // 递归扫描子目录，传递文件夹的 ID 作为子项的 parentId
            await ScanDirectoryAsync(
                dir, 
                dirVirtualPath, 
                storagePoolId, 
                userId, 
                dbPathMap, 
                physicalPaths,
                counters,
                folder.Id,
                cancellationToken);
        }

        // 扫描文件
        foreach (var file in Directory.GetFiles(physicalDir))
        {
            var fileName = Path.GetFileName(file);
            var fileVirtualPath = CombinePath(virtualPath, fileName);
            physicalPaths.Add(fileVirtualPath);

            var fileInfo = new FileInfo(file);
            counters.TotalSize += fileInfo.Length;

            if (!dbPathMap.TryGetValue(fileVirtualPath, out var existingFile))
            {
                // 新文件，添加到数据库
                var newFile = FileItem.CreateFile(
                    name: fileName,
                    path: fileVirtualPath,
                    physicalPath: file,
                    sizeBytes: fileInfo.Length,
                    mimeType: _fileSystemService.GetMimeType(fileName),
                    storagePoolId: storagePoolId,
                    ownerId: userId,
                    parentId: currentDirId);

                await _fileRepository.AddAsync(newFile, cancellationToken);
                dbPathMap[fileVirtualPath] = newFile;
                counters.AddedFiles++;
            }
            else
            {
                // 【保留论证】同上——扫描无权解除用户的软删决定（ITM-N26 交互语义）
                if (existingFile.IsDeleted)
                    continue;

                // 修复 parentId 如果不正确
                if (existingFile.ParentId != currentDirId)
                {
                    existingFile.MoveTo(existingFile.Path, existingFile.PhysicalPath, currentDirId);
                    await _fileRepository.UpdateAsync(existingFile, cancellationToken);
                    counters.UpdatedFiles++;
                }
                else if (existingFile.SizeBytes != fileInfo.Length)
                {
                    // 文件大小变化，更新
                    existingFile.UpdateSize(fileInfo.Length);
                    counters.UpdatedFiles++;
                }
            }
        }
    }


    /// <summary>
    /// 重写后代记录的路径前缀（ITM-N3）。ParentId 树保持不变——被移动的只有祖先链顶层。
    /// </summary>
    /// <param name="folderId">被重命名/移动的文件夹 Id</param>
    /// <param name="oldBasePath">后代的旧虚拟路径前缀（Rename=所在父目录；Move=源目录）</param>
    /// <param name="newVirtualBase">后代的虚拟路径新基底</param>
    /// <param name="newPhysicalBase">后代的物理路径新基底</param>
}
