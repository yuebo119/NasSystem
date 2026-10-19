// ═══ 传输与结构变更域 ═══

// =============================================================================
// NasSystem.Application - FileService.cs
// 文件服务实现
// =============================================================================

namespace NasSystem.Application.Files;

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
            // 多用户隔离：父目录不存在或非本人一律按不存在处理
            if (parent is null || parent.ItemType != FileItemType.Folder || parent.OwnerId != userId)
                return Result<FileItemDto>.Failure(FileErrors.NotFound(parentPath));
            parentId = parent.Id;
        }

        // 获取默认存储池
        var defaultPool = await GetDefaultPoolOrFailureAsync(cancellationToken);
        if (defaultPool.IsFailure)
            return Result<FileItemDto>.Failure(defaultPool.Error);

        // 创建物理目录
        var physicalPath = GetPhysicalPath(newPath, defaultPool.Value.MountPath);
        _fileSystemService.CreateDirectory(physicalPath);

        // 创建文件项记录
        var folder = FileItem.CreateFolder(
            name: request.Name,
            path: newPath,
            physicalPath: physicalPath,
            ownerId: userId,
            storagePoolId: defaultPool.Value.Id,
            parentId: parentId);

        await _fileRepository.AddAsync(folder, cancellationToken);
        var saved = await SaveChangesWithConflictGuardAsync(FileErrors.AlreadyExists(newPath), cancellationToken);
        if (saved.IsFailure)
            return Result<FileItemDto>.Failure(saved.Error);

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
        // 多用户隔离：同名文件属他人时按不存在处理（不确认也不否认），
        // 否则 AlreadyExists 会成为他人文件存在性的探测器
        if (existingItem is not null && existingItem.OwnerId != userId)
            existingItem = null;
        if (existingItem != null && !metadata.Overwrite)
            return Result<FileItemDto>.Failure(FileErrors.AlreadyExists(filePath));

        // ROADMAP Phase 2 只读保护：覆盖上传即破坏受保护目录内的既有内容，
        // 在任何 IO（临时文件落盘）之前拦截。新建（existingItem 为 null）
        // 不拦：保护语义是防覆盖/防删除，不禁止目录内新增文件
        if (existingItem != null && await IsPathProtectedAsync(existingItem.Path, cancellationToken))
            return Result<FileItemDto>.Failure(ProtectedError(existingItem.Path));

        // 获取父目录 ID
        Guid? parentId = null;
        if (destPath != "/")
        {
            var parent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            // 多用户隔离：目标目录非本人按不存在处理。原实现 parent?.Id 对
            // 不存在的目录静默落到根级（parentId 留 null），记录自相矛盾
            if (parent is null || parent.OwnerId != userId)
                return Result<FileItemDto>.Failure(FileErrors.NotFound(destPath));
            parentId = parent.Id;
        }

        // 获取默认存储池
        var defaultPool = await GetDefaultPoolOrFailureAsync(cancellationToken);
        if (defaultPool.IsFailure)
            return Result<FileItemDto>.Failure(defaultPool.Error);

        // ITM-F：上传预检。流式 multipart 解析下拿不到 part 声明大小
        // （FileSize=0），预检从"声明值判定"退化为"余量判定"：
        // 有配额/容量上限时确认还有余量即放行，超限由写入循环边写边检
        var preflight = await PreflightUploadAsync(
            metadata.FileSize, userId, defaultPool.Value.Id, cancellationToken);
        if (preflight.IsFailure)
            return Result<FileItemDto>.Failure(preflight.Error);
        var limits = preflight.Value;

        // 写入物理文件并同步计算哈希——单遍 IO 流水线。
        // 【性能精炼记录】原实现"写盘 → 重开读盘 → 整流哈希"，大文件场景读放大 ×2；
        // 精炼为复制缓冲循环内增量喂入 SHA256（IncrementalHash），IO 量减半且语义不变。
        // 缓冲区与 FileHasher 保持同规格（80KB），ArrayPool 复用避免大文件反复分配。
        // 【S5】先写 .uploading-<guid> 临时文件，成功后改名落位：边写边检发现
        // 超限时中止删除临时文件，目标路径不被破坏（覆盖上传场景下旧文件
        // 不会像原地截断写那样被半个新文件顶掉）
        var physicalPath = GetPhysicalPath(filePath, defaultPool.Value.MountPath);
        var physicalDir = Path.GetDirectoryName(physicalPath)!;
        _fileSystemService.CreateDirectory(physicalDir);
        var tempPath = $"{physicalPath}.uploading-{Guid.NewGuid():N}";

        long totalBytes = 0;
        string hash = string.Empty;
        Error? limitError = null;
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(80 * 1024);
        try
        {
            // using 块内退出即释放流：超限中止后临时文件不被占用才能删掉
            await using (var fs = _fileSystemService.OpenWrite(tempPath))
            using (var incrementalHash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    // 边写边检：流式上传没有声明大小，超配额/超容量时立即中止，
                    // 删掉半成品临时文件，不在目标路径留下损坏数据
                    if (limits.QuotaRemainingBytes is { } quota && totalBytes + bytesRead > quota)
                    {
                        limitError = UserErrors.QuotaExceeded;
                        break;
                    }
                    if (limits.PoolAvailableBytes is { } available && totalBytes + bytesRead > available)
                    {
                        limitError = StorageErrors.InsufficientSpace;
                        break;
                    }

                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }

                if (limitError is null)
                    hash = Convert.ToBase64String(incrementalHash.GetHashAndReset());
            }

            if (limitError is not null)
            {
                TryDeleteTempFile(tempPath);
                return Result<FileItemDto>.Failure(limitError.Value);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 写入失败：清理半成品临时文件，目标路径保持原状
            TryDeleteTempFile(tempPath);
            throw;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        // 临时文件落位（覆盖语义：同卷 rename，旧文件被替换）
        try
        {
            // 覆盖上传：旧内容先进版本库再替换（ROADMAP Phase 1 自动版本）。
            // 时机必须在 File.Move 之前——替换后旧 inode 只被版本硬链接持有，
            // 内容还在但已无法从当前路径读取。快照失败不阻断上传（服务内兜底）
            if (existingItem is not null && metadata.Overwrite
                && existingItem.ItemType == FileItemType.File)
            {
                await _fileVersionService.CreateAutoVersionAsync(
                    existingItem, defaultPool.Value.MountPath, userId, cancellationToken);
            }

            File.Move(tempPath, physicalPath, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
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
            await AutoRecognizeAsync(existingItem, cancellationToken);
            return Result<FileItemDto>.Success(MapToDto(existingItem));
        }

        var file = FileItem.CreateFile(
            name: metadata.FileName,
            path: filePath,
            physicalPath: physicalPath,
            sizeBytes: totalBytes,
            mimeType: metadata.ContentType ?? _fileSystemService.GetMimeType(metadata.FileName),
            ownerId: userId,
            storagePoolId: defaultPool.Value.Id,
            parentId: parentId,
            hash: hash);

        await _fileRepository.AddAsync(file, cancellationToken);
        var saved = await SaveChangesWithConflictGuardAsync(FileErrors.AlreadyExists(filePath), cancellationToken);
        if (saved.IsFailure)
            return Result<FileItemDto>.Failure(saved.Error);

        await NotifyChangedAsync(destPath, FileChangeType.Created, file.Path);
        await AutoRecognizeAsync(file, cancellationToken);
        return Result<FileItemDto>.Success(MapToDto(file));
    }

    public async Task<Result<(Stream Stream, string FileName, string ContentType)>> DownloadFileAsync(
        string path,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        path = NormalizePath(path);
        var item = await _fileRepository.GetByPathAsync(path, cancellationToken);

        // 多用户隔离：非本人文件按不存在处理（下载是读路径，同样不暴露存在性）
        if (item is null || item.OwnerId != userId)
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

        // 多用户隔离：只能改名自己的条目
        if (item is null || item.OwnerId != userId)
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

        var oldPhysicalPath = item.PhysicalPath;
        var newPhysicalPath = Path.Combine(Path.GetDirectoryName(item.PhysicalPath)!, request.NewName);
        _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);

        item.Rename(request.NewName);
        item.MoveTo(newPath, newPhysicalPath, item.ParentId);
        await _fileRepository.UpdateAsync(item, cancellationToken);

        // ITM-N3：文件夹重命名必须同步重写后代路径，否则子树 Path/PhysicalPath 指向旧位置。
        // 旧基路径传文件夹自身旧路径（path 局部变量，item.Path 已被 Rename 改成新值）：
        // 原实现传 GetParentPath(path)（父目录），后代 Path 以 "/a/" 开头而基是 "/a"，
        // StartsWith("//") 不成立，后代被整体跳过，Path 停留在旧位置——
        // 按路径下载/移动/扫描同步全拿到旧路径（浏览按 ParentId 联动看不出问题）
        await RewriteDescendantPathsAsync(path, newPath, oldPhysicalPath, newPhysicalPath, cancellationToken);

        var saved = await SaveChangesWithConflictGuardAsync(FileErrors.AlreadyExists(newPath), cancellationToken);
        if (saved.IsFailure)
            return Result<FileItemDto>.Failure(saved.Error);

        // 版本号：父目录的条目集合变了（名字变了）。子树内目录的内容没变
        // （还是那些子项），不需要 bump；根实体经 UoW 跟踪已 bump 父目录，
        // 这里显式 bump 旧父目录覆盖移动场景（见 MoveAsync 同款注释）
        BumpParentVersion(path);
        BumpParentVersion(newPath);

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
        var movedFromPaths = new List<string>();

        // ROADMAP Phase 2 只读保护预检：移出受保护目录（内容从可见位置消失，
        // 变相删除）与写入受保护目录（目标被覆盖，变相篡改）都拦截。
        // 放在目录准备与物理移动之前：权限判定先于资源存在性检查
        // （保护拒绝不该因为"目标目录不存在"换一个错误码），且策略拒绝
        // 不做部分执行——部分移动后再报错，用户看到的结果与"整单拒绝"
        // 的预期不符
        if (await IsPathProtectedAsync(destPath, cancellationToken))
            return Result<IReadOnlyList<FileItemDto>>.Failure(ProtectedError(destPath));
        foreach (var sourcePath in request.SourcePaths)
        {
            var normalizedSource = NormalizePath(sourcePath);
            if (await IsPathProtectedAsync(normalizedSource, cancellationToken))
                return Result<IReadOnlyList<FileItemDto>>.Failure(ProtectedError(normalizedSource));
        }

        // 默认池与目标目录准备（Move/Copy 共用的前奏，目标目录缺失统一报错）
        var prepared = await PrepareTransferDestinationAsync(destPath, userId, cancellationToken);
        if (prepared.IsFailure)
            return Result<IReadOnlyList<FileItemDto>>.Failure(prepared.Error);
        var (poolId, poolMountPath, destParentId) = prepared.Value;

        foreach (var sourcePath in request.SourcePaths)
        {
            var normalized = NormalizePath(sourcePath);
            var item = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            // 多用户隔离：不存在与非本人的源都跳过（与既有"不存在即跳过"的
            // 批量语义一致，不阻断其余项的移动）
            if (item is null || item.OwnerId != userId) continue;

            // 检查是否移动到自身或子目录
            if (destPath.StartsWith(normalized + "/", StringComparison.Ordinal) || destPath == normalized)
                continue;

            var newPath = CombinePath(destPath, item.Name);
            var newPhysicalPath = GetPhysicalPath(newPath, poolMountPath);

            try
            {
                // 确保目标目录存在
                var targetDir = Path.GetDirectoryName(newPhysicalPath)!;
                _fileSystemService.CreateDirectory(targetDir);

                // 移动物理文件/目录
                var oldPhysicalPath = item.PhysicalPath;
                _fileSystemService.Move(item.PhysicalPath, newPhysicalPath);
                
                // 更新数据库记录
                item.MoveTo(newPath, newPhysicalPath, destParentId);
                await _fileRepository.UpdateAsync(item, cancellationToken);

                // ITM-N3：移动文件夹时同步重写后代路径（ParentId 树不变，仅路径串前缀变化）。
                // 单条 SQL 批量重写（S3），旧基路径传文件夹自身旧路径
                if (item.IsFolder)
                    await RewriteDescendantPathsAsync(normalized, newPath, oldPhysicalPath, newPhysicalPath, cancellationToken);

                movedFromPaths.Add(GetParentPath(normalized));
                results.Add(MapToDto(item));
            }
            catch (Exception)
            {
                // 移动失败，跳过此项
                continue;
            }
        }

        var saved = await SaveChangesWithConflictGuardAsync(FileErrors.AlreadyExists(destPath), cancellationToken);
        if (saved.IsFailure)
            return Result<IReadOnlyList<FileItemDto>>.Failure(saved.Error);

        if (results.Count > 0)
        {
            // 版本号：目标父目录少了/多了一项由根实体经 UoW 跟踪自动 bump；
            // 来源父目录同样少了一项，原实现漏了它，客户端缓存里来源目录
            // 仍显示已移走的条目（ETag 不变 → 304 → 陈旧）。这里显式补上
            foreach (var fromParent in movedFromPaths)
                DirectoryVersion.Bump(fromParent);
            DirectoryVersion.Bump(destPath);

            await NotifyChangedAsync(destPath, FileChangeType.Moved, destPath);
        }
        return Result<IReadOnlyList<FileItemDto>>.Success(results);
    }

    public async Task<Result<IReadOnlyList<FileItemDto>>> CopyAsync(
        MoveOrCopyRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileItemDto>();
        var destPath = NormalizePath(request.DestinationPath);

        // 默认池与目标目录准备（与 MoveAsync 共用同一前奏与报错语义）
        var prepared = await PrepareTransferDestinationAsync(destPath, userId, cancellationToken);
        if (prepared.IsFailure)
            return Result<IReadOnlyList<FileItemDto>>.Failure(prepared.Error);
        var (poolId, poolMountPath, destParentId) = prepared.Value;

        foreach (var sourcePath in request.SourcePaths)
        {
            var normalized = NormalizePath(sourcePath);
            var sourceItem = await _fileRepository.GetByPathAsync(normalized, cancellationToken);
            // 多用户隔离：不存在与非本人的源都跳过（同移动批次语义）
            if (sourceItem is null || sourceItem.OwnerId != userId) continue;

            // 生成不重复的目标名称
            var targetName = await GenerateUniqueCopyNameAsync(destPath, sourceItem.Name, sourceItem.ItemType == FileItemType.Folder, cancellationToken);
            var targetPath = CombinePath(destPath, targetName);
            var targetPhysicalPath = GetPhysicalPath(targetPath, poolMountPath);

            if (sourceItem.ItemType == FileItemType.Folder)
            {
                // 复制文件夹（递归）
                var copiedFolder = await CopyFolderRecursiveAsync(
                    sourceItem, targetPath, targetPhysicalPath, destParentId,
                    userId, poolId, cancellationToken);
                if (copiedFolder != null)
                    results.Add(MapToDto(copiedFolder));
            }
            else
            {
                // 复制文件
                var copiedFile = await CopyFileAsync(
                    sourceItem, targetName, targetPath, targetPhysicalPath,
                    destParentId, userId, poolId, cancellationToken);
                if (copiedFile != null)
                    results.Add(MapToDto(copiedFile));
            }
        }

        var saved = await SaveChangesWithConflictGuardAsync(FileErrors.AlreadyExists(destPath), cancellationToken);
        if (saved.IsFailure)
            return Result<IReadOnlyList<FileItemDto>>.Failure(saved.Error);

        if (results.Count > 0)
            await NotifyChangedAsync(destPath, FileChangeType.Created, destPath);
        return Result<IReadOnlyList<FileItemDto>>.Success(results);
    }

    /// <summary>
    /// 传输前准备：默认存储池 Id/挂载点 + 目标父目录 ID。
    /// Move/Copy 共用；目标目录显式不存在时报错（两侧语义统一）
    /// </summary>
    private async Task<Result<(Guid PoolId, string MountPath, Guid? ParentId)>> PrepareTransferDestinationAsync(
        string destPath,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var poolResult = await GetDefaultPoolOrFailureAsync(cancellationToken);
        if (poolResult.IsFailure)
            return Result<(Guid, string, Guid?)>.Failure(poolResult.Error);

        Guid? destParentId = null;
        if (destPath != "/")
        {
            var destParent = await _fileRepository.GetByPathAsync(destPath, cancellationToken);
            // 多用户隔离：目标目录非本人按不存在处理（移动与复制共用此前置）
            if (destParent is null || destParent.OwnerId != userId)
                return Result<(Guid, string, Guid?)>.Failure(FileErrors.NotFound(destPath));
            destParentId = destParent.Id;
        }

        return Result<(Guid, string, Guid?)>.Success((poolResult.Value.Id, poolResult.Value.MountPath, destParentId));
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
        Guid storagePoolId,
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
                storagePoolId: storagePoolId,
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
                        newFolder.Id, userId, storagePoolId, cancellationToken);
                }
                else
                {
                    // 复制子文件
                    await CopyFileAsync(
                        child, childNewName, childNewPath, childNewPhysicalPath,
                        newFolder.Id, userId, storagePoolId, cancellationToken);
                }
            }

            return newFolder;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// S3：后代路径重写从逐条实体 UPDATE 改单条 SQL（按旧基路径前缀截断拼接）。
    /// 命令经 UoW 意图队列登记，与根实体的更新在同一事务提交。
    /// 不含 IsDeleted 过滤：软删后代同样要跟随重写，否则其 Path 停在旧位置，
    /// 恢复时可能撞上旧路径上的新记录（Path 唯一部分索引）。
    /// 子树内目录的内容集合没变（还是那些子项），不需要 bump 目录版本号，
    /// 父目录的变更由调用方显式处理（见 Rename/Move 的 BumpParentVersion）。
    /// </summary>
    private Task RewriteDescendantPathsAsync(
        string oldVirtualBase,
        string newVirtualBase,
        string oldPhysicalBase,
        string newPhysicalBase,
        CancellationToken ct) =>
        _fileRepository.RewriteSubtreePathsAsync(
            oldVirtualBase, newVirtualBase, oldPhysicalBase, newPhysicalBase, ct);

    /// <summary>递增路径所在父目录的版本号（批量操作不跟踪实体时的显式 bump）</summary>
    private static void BumpParentVersion(string path) =>
        DirectoryVersion.BumpForFile(path);


}
