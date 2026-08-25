// ═══ 传输与结构变更域 ═══

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
    public async Task<Result<FileItemDto>> CreateFolderAsync(
        CreateFolderRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var parentPath = NormalizePath(request.ParentPath);
        var newPath = CombinePath(parentPath, request.Name);

        // 检查是否已存在
        if (await _fileRepository.PathExistsAsync(newPath, cancellationToken))
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(newPath));

        // 获取父目录
        Guid? parentId = null;
        if (parentPath != "/")
        {
            var parent = await _fileRepository.GetByPathAsync(parentPath, cancellationToken);
            if (parent is null || parent.ItemType != FileItemType.Folder)
                return Result<FileItemDto>.Failure(FileErrors.NotFound(parentPath));
            parentId = parent.Id;
        }

        // 获取默认存储池
        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        if (defaultPool is null)
            return Result<FileItemDto>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

        // 创建物理目录
        var physicalPath = GetPhysicalPath(newPath, defaultPool.MountPath);
        _fileSystemService.CreateDirectory(physicalPath);

        // 创建文件项记录
        var folder = FileItem.CreateFolder(
            name: request.Name,
            path: newPath,
            physicalPath: physicalPath,
            ownerId: userId,
            storagePoolId: defaultPool.Id,
            parentId: parentId);

        await _fileRepository.AddAsync(folder, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // ITM-N7：并发创建同名目录由 Path 唯一部分索引兜底
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(newPath));
        }

        await NotifyChangedAsync(parentPath, FileChangeType.Created, folder.Path);
        return Result<FileItemDto>.Success(MapToDto(folder));
    }

    public async Task<Result<FileItemDto>> UploadFileAsync(
        UploadFileMetadata metadata,
        Stream fileStream,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var destPath = NormalizePath(metadata.DestinationPath);
        var filePath = CombinePath(destPath, metadata.FileName);

        // 检查是否已存在
        var existingItem = await _fileRepository.GetByPathAsync(filePath, cancellationToken);
        if (existingItem != null && !metadata.Overwrite)
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(filePath));

        // 获取父目录 ID
        Guid? parentId = null;
        if (destPath != "/")
        {
            var parent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            parentId = parent?.Id;
        }

        // 获取默认存储池
        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        if (defaultPool is null)
            return Result<FileItemDto>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

        // 写入物理文件并同步计算哈希——单遍 IO 流水线。
        // 【性能精炼记录】原实现"写盘 → 重开读盘 → 整流哈希"，大文件场景读放大 ×2；
        // 精炼为复制缓冲循环内增量喂入 SHA256（IncrementalHash），IO 量减半且语义不变。
        // 缓冲区与 FileHasher 保持同规格（80KB），ArrayPool 复用避免大文件反复分配。
        var physicalPath = GetPhysicalPath(filePath, defaultPool.MountPath);
        var physicalDir = Path.GetDirectoryName(physicalPath)!;
        _fileSystemService.CreateDirectory(physicalDir);

        long totalBytes = 0;
        string hash;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(80 * 1024);
        try
        {
            await using var fs = _fileSystemService.OpenWrite(physicalPath);
            using var incrementalHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            int bytesRead;
            while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                incrementalHash.AppendData(buffer, 0, bytesRead);
                totalBytes += bytesRead;
            }

            hash = Convert.ToBase64String(incrementalHash.GetHashAndReset());
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        // 创建或更新记录
        if (existingItem != null && metadata.Overwrite)
        {
            existingItem.UpdateSize(totalBytes);
            existingItem.UpdateHash(hash);
            await _fileRepository.UpdateAsync(existingItem, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // ITM-N25：覆盖上传是 Modified 变更，与新建同等推送（否则订阅端不刷新）
            await NotifyChangedAsync(destPath, FileChangeType.Modified, existingItem.Path);
            return Result<FileItemDto>.Success(MapToDto(existingItem));
        }

        var file = FileItem.CreateFile(
            name: metadata.FileName,
            path: filePath,
            physicalPath: physicalPath,
            sizeBytes: totalBytes,
            mimeType: metadata.ContentType ?? _fileSystemService.GetMimeType(metadata.FileName),
            ownerId: userId,
            storagePoolId: defaultPool.Id,
            parentId: parentId,
            hash: hash);

        await _fileRepository.AddAsync(file, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(filePath));
        }

        await NotifyChangedAsync(destPath, FileChangeType.Created, file.Path);
        return Result<FileItemDto>.Success(MapToDto(file));
    }

    public async Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadFileAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);
        var item = await _fileRepository.GetByPathAsync(path, cancellationToken);

        if (item is null)
            return Result<(Stream, string, string)>.Failure(FileErrors.NotFound(path));

        if (item.ItemType != FileItemType.File)
            return Result<(Stream, string, string)>.Failure(FileErrors.IsNotFile(path));

        if (!_fileSystemService.FileExists(item.PhysicalPath))
            return Result<(Stream, string, string)>.Failure(FileErrors.NotFound(path));

        var stream = _fileSystemService.OpenRead(item.PhysicalPath);
        return Result<(Stream, string, string)>.Success((stream, item.Name, item.MimeType ?? "application/octet-stream"));
    }

    public async Task<Result<FileItemDto>> RenameAsync(
        RenameItemRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var path = NormalizePath(request.Path);
        var item = await _fileRepository.GetByPathAsync(path, cancellationToken);

        if (item is null)
            return Result<FileItemDto>.Failure(FileErrors.NotFound(path));

        // ITM-N20：新名先过名称不变量，防止 "../x" 类输入经路径拼接逃逸挂载根
        try
        {
            FileItem.ValidateName(request.NewName);
        }
        catch (ArgumentException ex)
        {
            return Result<FileItemDto>.Failure(new Error("File.InvalidName", ex.Message));
        }

        var parentPath = GetParentPath(path);
        var newPath = CombinePath(parentPath, request.NewName);
        
        if (await _fileRepository.PathExistsAsync(newPath, cancellationToken))
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(newPath));

        var newPhysicalPath = Path.Combine(Path.GetDirectoryName(item.PhysicalPath)!, request.NewName);
        _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);

        item.Rename(request.NewName);
        item.MoveTo(newPath, newPhysicalPath, item.ParentId);
        await _fileRepository.UpdateAsync(item, cancellationToken);

        // ITM-N3：文件夹重命名必须同步重写后代路径，否则子树 Path/PhysicalPath 指向旧位置
        await RewriteDescendantPathsAsync(item.Id, GetParentPath(path), newPath, newPhysicalPath, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await NotifyChangedAsync(GetParentPath(newPath), FileChangeType.Renamed, item.Path);

        return Result<FileItemDto>.Success(MapToDto(item));
    }

    public async Task<Result<IReadOnlyList<FileItemDto>>> MoveAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileItemDto>();
        var destPath = NormalizePath(request.DestinationPath);

        // 获取默认存储池
        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        if (defaultPool is null)
            return Result<IReadOnlyList<FileItemDto>>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

        // 获取目标父目录 ID（根目录时为 null）
        Guid? destParentId = null;
        if (destPath != "/")
        {
            var destParent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            // 目标目录不存在且不是根目录，返回错误
            if (destParent is null)
                return Result<IReadOnlyList<FileItemDto>>.Failure(FileErrors.NotFound(destPath));
            destParentId = destParent.Id;
        }

        foreach (var sourcePath in request.SourcePaths)
        {
            var normalized = NormalizePath(sourcePath);
            var item = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            if (item is null) continue;

            // 检查是否移动到自身或子目录
            if (destPath.StartsWith(normalized + "/", StringComparison.Ordinal) || destPath == normalized)
                continue;

            var newPath = CombinePath(destPath, item.Name);
            var newPhysicalPath = GetPhysicalPath(newPath, defaultPool.MountPath);

            try
            {
                // 确保目标目录存在
                var targetDir = Path.GetDirectoryName(newPhysicalPath)!;
                _fileSystemService.CreateDirectory(targetDir);

                // 移动物理文件/目录
                _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);
                
                // 更新数据库记录
                item.MoveTo(newPath, newPhysicalPath, destParentId);
                await _fileRepository.UpdateAsync(item, cancellationToken);

                // ITM-N3：移动文件夹时同步重写后代路径（ParentId 树不变，仅路径串前缀变化）
                if (item.IsFolder)
                    await RewriteDescendantPathsAsync(item.Id, normalized, newPath, newPhysicalPath, cancellationToken);

                results.Add(MapToDto(item));
            }
            catch (Exception)
            {
                // 移动失败，跳过此项
                continue;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (results.Count > 0)
            await NotifyChangedAsync(destPath, FileChangeType.Moved, destPath);
        return Result<IReadOnlyList<FileItemDto>>.Success(results);
    }

    public async Task<Result<IReadOnlyList<FileItemDto>>> CopyAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileItemDto>();
        var destPath = NormalizePath(request.DestinationPath);

        // 获取默认存储池
        var defaultPool = await _storagePoolRepository.GetDefaultAsync(cancellationToken);
        if (defaultPool is null)
            return Result<IReadOnlyList<FileItemDto>>.Failure(StorageErrors.PoolNotFound(Guid.Empty));

        // 获取目标父目录
        Guid? destParentId = null;
        if (destPath != "/")
        {
            var destParent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            destParentId = destParent?.Id;
        }

        foreach (var sourcePath in request.SourcePaths)
        {
            var normalized = NormalizePath(sourcePath);
            var sourceItem = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            if (sourceItem is null) continue;

            // 生成不重复的目标名称
            var targetName = await GenerateUniqueCopyNameAsync(destPath, sourceItem.Name, sourceItem.ItemType == FileItemType.Folder, cancellationToken);
            var targetPath = CombinePath(destPath, targetName);
            var targetPhysicalPath = GetPhysicalPath(targetPath, defaultPool.MountPath);

            if (sourceItem.ItemType == FileItemType.Folder)
            {
                // 复制文件夹（递归）
                var copiedFolder = await CopyFolderRecursiveAsync(
                    sourceItem, targetPath, targetPhysicalPath, destParentId, 
                    userId, defaultPool, cancellationToken);
                if (copiedFolder != null)
                    results.Add(MapToDto(copiedFolder));
            }
            else
            {
                // 复制文件
                var copiedFile = await CopyFileAsync(
                    sourceItem, targetName, targetPath, targetPhysicalPath, 
                    destParentId, userId, defaultPool.Id, cancellationToken);
                if (copiedFile != null)
                    results.Add(MapToDto(copiedFile));
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (results.Count > 0)
            await NotifyChangedAsync(destPath, FileChangeType.Created, destPath);
        return Result<IReadOnlyList<FileItemDto>>.Success(results);
    }

    /// <summary>
    /// 生成不重复的副本名称
    /// </summary>
    private async Task<string> GenerateUniqueCopyNameAsync(
        string destPath, 
        string originalName, 
        bool isFolder,
        CancellationToken cancellationToken)
    {
        var baseName = originalName;
        var extension = "";
        
        // 对于文件，分离扩展名
        if (!isFolder)
        {
            var lastDot = originalName.LastIndexOf('.');
            if (lastDot > 0)
            {
                baseName = originalName[..lastDot];
                extension = originalName[lastDot..];
            }
        }

        // 检查原名称是否可用
        var targetPath = CombinePath(destPath, originalName);
        if (!await _fileRepository.PathExistsAsync(targetPath, cancellationToken))
            return originalName;

        // 尝试 "名称 - 副本"
        var copyName = $"{baseName} - 副本{extension}";
        targetPath = CombinePath(destPath, copyName);
        if (!await _fileRepository.PathExistsAsync(targetPath, cancellationToken))
            return copyName;

        // 尝试 "名称 - 副本 (n)"
        var counter = 2;
        while (counter < 1000) // 防止无限循环
        {
            copyName = $"{baseName} - 副本 ({counter}){extension}";
            targetPath = CombinePath(destPath, copyName);
            if (!await _fileRepository.PathExistsAsync(targetPath, cancellationToken))
                return copyName;
            counter++;
        }

        // 极端情况：使用时间戳
        return $"{baseName} - 副本 ({DateTime.Now:yyyyMMddHHmmss}){extension}";
    }

    /// <summary>
    /// 复制单个文件
    /// </summary>
    private async Task<FileItem?> CopyFileAsync(
        FileItem source,
        string newName,
        string newPath,
        string newPhysicalPath,
        Guid? parentId,
        Guid userId,
        Guid storagePoolId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 确保目标目录存在
            var targetDir = Path.GetDirectoryName(newPhysicalPath)!;
            _fileSystemService.CreateDirectory(targetDir);

            // 复制物理文件
            if (_fileSystemService.FileExists(source.PhysicalPath))
            {
                File.Copy(source.PhysicalPath, newPhysicalPath, overwrite: false);
            }
            else
            {
                return null; // 源文件不存在
            }

            // 创建数据库记录
            var newFile = FileItem.CreateFile(
                name: newName,
                path: newPath,
                physicalPath: newPhysicalPath,
                sizeBytes: source.SizeBytes,
                mimeType: source.MimeType,
                ownerId: userId,
                storagePoolId: storagePoolId,
                parentId: parentId,
                hash: source.Hash);

            await _fileRepository.AddAsync(newFile, cancellationToken);
            return newFile;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 递归复制文件夹
    /// </summary>
    private async Task<FileItem?> CopyFolderRecursiveAsync(
        FileItem sourceFolder,
        string newPath,
        string newPhysicalPath,
        Guid? parentId,
        Guid userId,
        StoragePool storagePool,
        CancellationToken cancellationToken)
    {
        try
        {
            // 创建物理目录
            _fileSystemService.CreateDirectory(newPhysicalPath);

            // 获取新文件夹名称
            var newName = GetFileName(newPath);

            // 创建文件夹记录
            var newFolder = FileItem.CreateFolder(
                name: newName,
                path: newPath,
                physicalPath: newPhysicalPath,
                ownerId: userId,
                storagePoolId: storagePool.Id,
                parentId: parentId);

            await _fileRepository.AddAsync(newFolder, cancellationToken);

            // 获取源文件夹的所有子项
            var children = await _fileRepository.GetChildrenAsync(sourceFolder.Id, includeDeleted: false, cancellationToken);

            foreach (var child in children)
            {
                var childNewName = child.Name;
                var childNewPath = CombinePath(newPath, childNewName);
                var childNewPhysicalPath = Path.Combine(newPhysicalPath, childNewName);

                if (child.ItemType == FileItemType.Folder)
                {
                    // 递归复制子文件夹
                    await CopyFolderRecursiveAsync(
                        child, childNewPath, childNewPhysicalPath, 
                        newFolder.Id, userId, storagePool, cancellationToken);
                }
                else
                {
                    // 复制子文件
                    await CopyFileAsync(
                        child, childNewName, childNewPath, childNewPhysicalPath,
                        newFolder.Id, userId, storagePool.Id, cancellationToken);
                }
            }

            return newFolder;
        }
        catch
        {
            return null;
        }
    }


    private async Task RewriteDescendantPathsAsync(
        Guid folderId,
        string oldBasePath,
        string newVirtualBase,
        string newPhysicalBase,
        CancellationToken ct)
    {
        var descendants = await _fileRepository.GetDescendantsAsync(folderId, ct);
        foreach (var d in descendants)
        {
            if (!d.Path.StartsWith(oldBasePath + "/", StringComparison.Ordinal))
                continue;

            var relative = d.Path[oldBasePath.Length..]; // 以 / 开头
            var newDescendantPath = newVirtualBase + relative;
            var newDescendantPhysical = Path.Combine(
                newPhysicalBase,
                relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            d.MoveTo(newDescendantPath, newDescendantPhysical, d.ParentId);
            await _fileRepository.UpdateAsync(d, ct);
        }
    }


}
