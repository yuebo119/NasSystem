// ═══ 扫描同步域 ═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

using NasSystem.Application.Common;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.FileVersions;
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
    public async Task<Result<ScanResultDto>> ScanAndSyncAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var counters = new ScanCounters();

        try
        {
            // 获取默认存储池
            var defaultPool = await GetDefaultPoolOrFailureAsync(cancellationToken);
            if (defaultPool.IsFailure)
                return Result<ScanResultDto>.Failure(defaultPool.Error);

            var storagePath = defaultPool.Value.MountPath;

            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }

            // S2：轻量对比表。原实现载全池活跃实体 + 软删实体两份再 concat 建字典，
            // 十万文件的池就是十万个 FileItem 同时驻留；投影只要六个字段
            // （Id/Path/ItemType/SizeBytes/ParentId/IsDeleted）。含软删记录：
            // 其物理文件仍存在于挂载点，缺席会被当作新发现重建（软删复活），
            // 且恢复时撞 Path 唯一索引（ITM-N26）
            var scanEntries = await _fileRepository.GetScanEntriesAsync(defaultPool.Value.Id, cancellationToken);
            // 同一 Path 允许多条记录：Path 唯一索引是部分索引（只管未删），
            // 软删历史与新记录可以同路径共存（删了再传同名文件即是）。
            // ToDictionary 遇重复键直接抛异常，整个扫描 500（E2E 走查实证：
            // 回收站里积了多条 davprobe.txt 后点扫描同步必炸）。
            // 代表记录按"未删优先"取：物理文件同一个，比对大小/决定新建
            // 看未删那条；纯删历史只在下面的删除集逻辑里按 scanEntries 全量处理
            var dbPathMap = new Dictionary<string, FileItemScanEntry>(StringComparer.Ordinal);
            foreach (var entry in scanEntries)
            {
                if (dbPathMap.TryGetValue(entry.Path, out var existing))
                {
                    if (existing.IsDeleted && !entry.IsDeleted)
                        dbPathMap[entry.Path] = entry;
                }
                else
                {
                    dbPathMap[entry.Path] = entry;
                }
            }

            // 扫描物理文件系统。三类写操作收集到列表末尾一次批量提交：
            // 原实现逐条 AddAsync/UpdateAsync（每文件一条独立 SQL），
            // 首次扫描十万文件就是十万条 INSERT
            var physicalPaths = new HashSet<string>();
            var newItems = new List<FileItem>();
            var updatedItems = new List<FileItem>();
            var changedDirs = new HashSet<string>(StringComparer.Ordinal);

            await ScanDirectoryAsync(
                storagePath,
                "/",
                defaultPool.Value.Id,
                userId,
                dbPathMap,
                physicalPaths,
                counters,
                newItems,
                updatedItems,
                changedDirs,
                null,  // 根目录的 parentId 为 null
                cancellationToken);

            // 删除集：库里有、物理上没了的活跃记录。软删记录不进（ITM-N26 语义），
            // 但被删文件夹下的软删后代要一并删：ParentId 外键是 ON DELETE RESTRICT，
            // 父行被子行引用时删父必失败，软删后代同样引用父行
            var activeRemoved = scanEntries
                .Where(e => !e.IsDeleted && !physicalPaths.Contains(e.Path))
                .ToList();

            var toDelete = activeRemoved.ToList();
            var removedFolderPrefixes = activeRemoved
                .Where(e => e.ItemType == FileItemType.Folder)
                .Select(e => e.Path + "/")
                .ToList();
            if (removedFolderPrefixes.Count > 0)
            {
                toDelete.AddRange(scanEntries.Where(e =>
                    e.IsDeleted &&
                    removedFolderPrefixes.Any(p => e.Path.StartsWith(p, StringComparison.Ordinal))));
            }

            foreach (var e in activeRemoved)
            {
                if (e.ItemType == FileItemType.Folder)
                    counters.RemovedFolders++;
                else
                    counters.RemovedFiles++;
                // 删除也改变父目录的条目集合
                changedDirs.Add(GetParentPath(e.Path));
            }

            if (newItems.Count > 0)
                await _fileRepository.BulkInsertAsync(newItems, cancellationToken);
            if (updatedItems.Count > 0)
                await _fileRepository.BulkUpdateAsync(updatedItems, cancellationToken);

            // 子先父：同父下子路径以 父+/ 开头，字典序子大于父。但要注意
            // BulkDeleteAsync 把 Id 合成一条 DELETE ... IN (...) 语句，语句内部的
            // 行序由 SQLite 决定而不受传入顺序控制，父行先删就撞 ParentId 外键
            // RESTRICT（实测：两个 Id 一条语句照样失败）。安全的分批维度是路径
            // 深度：父的深度恒小于子，同深度的行互不为父子，可一批删掉。
            // 原实现按路径升序逐条删（父先于子），删整目录树时第一个父行删除
            // 就撞外键，整个扫描事务回滚报"扫描失败"
            foreach (var depthGroup in toDelete
                .GroupBy(e => e.Path.Count(c => c == '/'))
                .OrderByDescending(g => g.Key))
            {
                await _fileRepository.BulkDeleteAsync(
                    depthGroup.Select(e => e.Id).ToList(), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 版本号：批量写不跟踪实体（UoW 的 BumpDirectoryVersions 无实体可 bump），
            // 显式递增有变更的目录（新增/删除/大小变化/父链接修复的所在目录）
            foreach (var dir in changedDirs)
                DirectoryVersion.Bump(dir);

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
                Message = $"扫描完成：新增 {counters.AddedFiles} 个文件、{counters.AddedFolders} 个文件夹，"
                    + $"删除 {counters.RemovedFiles} 个文件、{counters.RemovedFolders} 个文件夹，"
                    + $"更新 {counters.UpdatedFiles} 个文件"
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
        Dictionary<string, FileItemScanEntry> dbPathMap,
        HashSet<string> physicalPaths,
        ScanCounters counters,
        List<FileItem> newItems,
        List<FileItem> updatedItems,
        HashSet<string> changedDirs,
        Guid? currentDirId,  // 当前目录的 ID，作为子项的 parentId
        CancellationToken cancellationToken)
    {
        // 扫描子目录。用 Enumerate 而非 Get：Get* 每次调用都物化整个目录的
        // 完整字符串数组，深目录树上万目录时是成比例的白分配；Enumerate 边遍历边产出
        foreach (var dir in Directory.EnumerateDirectories(physicalDir))
        {
            var dirName = Path.GetFileName(dir);

            // 版本库目录（ROADMAP Phase 1）：.versions 存文件历史版本，
            // 与 .uploading- 半成品同理，物理上在池内但不对用户呈现，
            // 扫描入库会把版本文件当正常文件导进来
            if (dirName == FileVersionStore.DirectoryName)
                continue;

            var dirVirtualPath = CombinePath(virtualPath, dirName);
            physicalPaths.Add(dirVirtualPath);

            Guid dirId;
            if (!dbPathMap.TryGetValue(dirVirtualPath, out var existingFolder))
            {
                // 新文件夹：只建实体进批量插入列表，不碰数据库（父先于子的顺序由
                // 遍历顺序天然保证，BulkInsertAsync 按列表顺序执行）
                var folder = FileItem.CreateFolder(
                    name: dirName,
                    path: dirVirtualPath,
                    physicalPath: dir,
                    storagePoolId: storagePoolId,
                    ownerId: userId,
                    parentId: currentDirId);

                newItems.Add(folder);
                dirId = folder.Id;
                counters.AddedFolders++;
                changedDirs.Add(virtualPath);
            }
            else
            {
                dirId = existingFolder.Id;
                // 【保留论证】软删命中物理存在时为何"什么都不做"而不是恢复或重建：
                // 软删语义=用户明确不要了但暂存回收站；扫描只负责"物理↔数据库对齐"，
                // 无权替用户改变删除决定（NPD-4 同族：扫描不是所有权/状态改写工具）。
                // 若外部把文件放回原位，正确动作仍是保持回收站态由用户手动恢复。
                if (existingFolder.IsDeleted)
                    continue;

                // 修复 parentId 如果不正确。按需加载实体：修复是少见路径
                // （树链接漂移），不值得为它把全池实体物化出来
                if (existingFolder.ParentId != currentDirId)
                {
                    var entity = await _fileRepository.GetByPathAsync(dirVirtualPath, cancellationToken);
                    if (entity is not null)
                    {
                        entity.MoveTo(entity.Path, entity.PhysicalPath, currentDirId);
                        updatedItems.Add(entity);
                        counters.UpdatedFiles++;
                        changedDirs.Add(virtualPath);
                    }
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
                newItems,
                updatedItems,
                changedDirs,
                dirId,
                cancellationToken);
        }

        // 扫描文件。同用 Enumerate：GetFiles 会物化整目录文件名字符串数组
        foreach (var file in Directory.EnumerateFiles(physicalDir))
        {
            var fileName = Path.GetFileName(file);

            // S5：上传半成品临时文件（<目标名>.uploading-<guid>）不入库。
            // 上传中断（客户端断开/进程退出）会留下它，库里没有对应记录，
            // 不跳过的话下次扫描会把半成品当用户文件导入
            if (fileName.Contains(".uploading-", StringComparison.Ordinal))
                continue;

            var fileVirtualPath = CombinePath(virtualPath, fileName);
            physicalPaths.Add(fileVirtualPath);

            var fileInfo = new FileInfo(file);
            counters.TotalSize += fileInfo.Length;

            if (!dbPathMap.TryGetValue(fileVirtualPath, out var existingFile))
            {
                // 新文件，进批量插入列表
                var newFile = FileItem.CreateFile(
                    name: fileName,
                    path: fileVirtualPath,
                    physicalPath: file,
                    sizeBytes: fileInfo.Length,
                    mimeType: _fileSystemService.GetMimeType(fileName),
                    storagePoolId: storagePoolId,
                    ownerId: userId,
                    parentId: currentDirId);

                newItems.Add(newFile);
                counters.AddedFiles++;
                changedDirs.Add(virtualPath);
            }
            else
            {
                // 【保留论证】同上——扫描无权解除用户的软删决定（ITM-N26 交互语义）
                if (existingFile.IsDeleted)
                    continue;

                // 修复 parentId 或大小变化。两者都按需加载实体（少见路径）
                var needsParentFix = existingFile.ParentId != currentDirId;
                var needsSizeUpdate = existingFile.SizeBytes != fileInfo.Length;
                if (needsParentFix || needsSizeUpdate)
                {
                    var entity = await _fileRepository.GetByPathAsync(fileVirtualPath, cancellationToken);
                    if (entity is not null)
                    {
                        if (needsParentFix)
                            entity.MoveTo(entity.Path, entity.PhysicalPath, currentDirId);
                        if (needsSizeUpdate)
                            entity.UpdateSize(fileInfo.Length);
                        updatedItems.Add(entity);
                        counters.UpdatedFiles++;
                        changedDirs.Add(virtualPath);
                    }
                }
            }
        }
    }
}
