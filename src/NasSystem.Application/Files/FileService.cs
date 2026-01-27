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
    private readonly string _storagePath;

    public FileService(
        IFileItemRepository fileRepository,
        IStoragePoolRepository storagePoolRepository,
        IUnitOfWork unitOfWork,
        IFileSystemService fileSystemService,
        IFileHasher fileHasher,
        IConfiguration configuration)
    {
        _fileRepository = fileRepository;
        _storagePoolRepository = storagePoolRepository;
        _unitOfWork = unitOfWork;
        _fileSystemService = fileSystemService;
        _fileHasher = fileHasher;
        _storagePath = configuration["NasSystem:StoragePath"] ?? "storage";
    }

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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        var parentPath = GetParentPath(path);
        var newPath = CombinePath(parentPath, request.NewName);
        
        if (await _fileRepository.PathExistsAsync(newPath, cancellationToken))
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(newPath));

        var newPhysicalPath = Path.Combine(Path.GetDirectoryName(item.PhysicalPath)!, request.NewName);
        _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);

        item.Rename(request.NewName);
        item.MoveTo(newPath, newPhysicalPath, item.ParentId);
        await _fileRepository.UpdateAsync(item, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FileItemDto>.Success(MapToDto(item));
    }

    public async Task<Result<IReadOnlyList<FileItemDto>>> MoveAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileItemDto>();
        var destPath = NormalizePath(request.DestinationPath);

        foreach (var sourcePath in request.SourcePaths)
        {
            var normalized = NormalizePath(sourcePath);
            var item = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            if (item is null) continue;

            var newPath = CombinePath(destPath, item.Name);
            var destParent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            var newPhysicalPath = GetPhysicalPath(newPath, Path.GetDirectoryName(item.PhysicalPath)!.Split(Path.DirectorySeparatorChar)[0]);

            _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);
            item.MoveTo(newPath, newPhysicalPath, destParent?.Id);

            await _fileRepository.UpdateAsync(item, cancellationToken);
            results.Add(MapToDto(item));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
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

    public async Task<Result> DeleteAsync(
        DeleteItemsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        foreach (var path in request.Paths)
        {
            var normalized = NormalizePath(path);
            var item = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            if (item is null) continue;

            // 始终物理删除文件/目录
            try
            {
                if (item.ItemType == FileItemType.Folder)
                {
                    if (_fileSystemService.DirectoryExists(item.PhysicalPath))
                        _fileSystemService.DeleteDirectory(item.PhysicalPath, true);
                }
                else
                {
                    if (_fileSystemService.FileExists(item.PhysicalPath))
                        _fileSystemService.DeleteFile(item.PhysicalPath);
                }
            }
            catch (Exception)
            {
                // 物理文件删除失败时仍然删除数据库记录
            }

            // 始终从数据库删除记录
            await _fileRepository.DeleteAsync(item, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<FileItemDto>.Success(MapToDto(item));
    }

    public async Task<Result> EmptyRecycleBinAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var deletedItems = await _fileRepository.GetDeletedItemsAsync(userId, cancellationToken);

        foreach (var item in deletedItems)
        {
            if (_fileSystemService.FileExists(item.PhysicalPath))
                _fileSystemService.DeleteFile(item.PhysicalPath);
            else if (_fileSystemService.DirectoryExists(item.PhysicalPath))
                _fileSystemService.DeleteDirectory(item.PhysicalPath, true);

            await _fileRepository.DeleteAsync(item, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PagedResult<FileItemDto>>> SearchAsync(
        SearchFilesRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var items = await _fileRepository.SearchAsync(
            request.Query,
            ownerId: userId,
            storagePoolId: null,
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

    public Task<Result<Stream>> GetThumbnailAsync(
        string path,
        int size,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现缩略图生成
        return Task.FromResult(Result<Stream>.Failure(new Error("NotImplemented", "缩略图功能暂未实现")));
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