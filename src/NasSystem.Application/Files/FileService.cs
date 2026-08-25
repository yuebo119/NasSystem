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

/// <summary>
/// 文件服务实现
/// </summary>
public sealed class FileService : IFileService
{
    private readonly IFileItemRepository _fileRepository;
    private readonly IStoragePoolRepository _storagePoolRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileSystemService _fileSystemService;
    private readonly IFileHasher _fileHasher;
    private readonly IHubNotificationService _notifier;

    public FileService(
        IFileItemRepository fileRepository,
        IStoragePoolRepository storagePoolRepository,
        IUnitOfWork unitOfWork,
        IFileSystemService fileSystemService,
        IFileHasher fileHasher,
        IConfiguration configuration,
        IHubNotificationService notifier)
    {
        _fileRepository = fileRepository;
        _storagePoolRepository = storagePoolRepository;
        _unitOfWork = unitOfWork;
        _fileSystemService = fileSystemService;
        _fileHasher = fileHasher;
        _notifier = notifier;
    }

    /// <summary>操作成功后向父目录订阅组推送变更（SignalR 实时刷新闭环）</summary>
    private Task NotifyChangedAsync(string parentPath, FileChangeType changeType, string path) =>
        _notifier.NotifyFileChangedAsync(parentPath, changeType, new { path });

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

        // 写入物理文件
        var physicalPath = GetPhysicalPath(filePath, defaultPool.MountPath);
        var physicalDir = Path.GetDirectoryName(physicalPath)!;
        _fileSystemService.CreateDirectory(physicalDir);

        await using (var fs = File.Create(physicalPath))
        {
            await fileStream.CopyToAsync(fs, cancellationToken);
        }

        // 计算文件哈希
        await using var hashStream = _fileSystemService.OpenRead(physicalPath);
        var hashValue = await _fileHasher.ComputeHashAsync(hashStream, cancellationToken);
        var hash = hashValue.Value;
        var fileInfo = new FileInfo(physicalPath);

        // 创建或更新记录
        if (existingItem != null && metadata.Overwrite)
        {
            existingItem.UpdateSize(fileInfo.Length);
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
            sizeBytes: fileInfo.Length,
            mimeType: metadata.ContentType ?? GetMimeType(metadata.FileName),
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
            var dbPathMap = dbItems.ToDictionary(x => x.Path, x => x);

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
                    mimeType: GetMimeType(fileName),
                    storagePoolId: storagePoolId,
                    ownerId: userId,
                    parentId: currentDirId);

                await _fileRepository.AddAsync(newFile, cancellationToken);
                dbPathMap[fileVirtualPath] = newFile;
                counters.AddedFiles++;
            }
            else
            {
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

    #region 辅助方法

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        path = path.Replace('\\', '/').Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');
        return path;
    }

    private static string GetFileName(string path) =>
        path == "/" ? "根目录" : path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "根目录";

    private static string GetParentPath(string path)
    {
        if (path == "/") return "/";
        var lastSlash = path.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : path[..lastSlash];
    }

    private static string CombinePath(string parent, string name) =>
        parent == "/" ? "/" + name : parent + "/" + name;

    private string GetPhysicalPath(string virtualPath, string poolMountPath)
    {
        var relativePath = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(poolMountPath, relativePath);
    }

    private static List<BreadcrumbDto> BuildBreadcrumbs(string path)
    {
        var breadcrumbs = new List<BreadcrumbDto>();
        if (path == "/") return breadcrumbs;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";
        foreach (var part in parts)
        {
            currentPath += "/" + part;
            breadcrumbs.Add(new BreadcrumbDto { Name = part, Path = currentPath });
        }
        return breadcrumbs;
    }

    private static FileItemDto MapToDto(FileItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Path = item.Path,
        ItemType = item.ItemType,
        SizeBytes = item.SizeBytes,
        SizeDisplay = FormatSize(item.SizeBytes),
        MimeType = item.MimeType,
        Extension = item.Extension,
        Hash = item.Hash,
        ParentId = item.ParentId,
        OwnerId = item.OwnerId,
        IsHidden = item.IsHidden,
        IsReadOnly = item.IsReadOnly,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    #endregion
}