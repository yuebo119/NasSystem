// =============================================================================
// NasSystem.Application - Backups/BackupService.cs
// 备份服务实现（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Application.Backups;

using Microsoft.Extensions.Logging;
using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 备份服务实现。设计边界：服务端只做设备注册与计划 diff，
/// 文件传输完全走标准上传端点——备份文件就是 /Backups/{设备名}/ 下的
/// 普通文件，配额检查、软删除、SignalR 推送、扫描同步全部现成复用，
/// 不为备份造第二条传输管道
/// </summary>
public sealed class BackupService(
    IBackupDeviceRepository deviceRepository,
    IFileItemRepository fileItemRepository,
    IUnitOfWork unitOfWork,
    ILogger<BackupService> logger) : IBackupService
{
    public async Task<Result<BackupDeviceDto>> RegisterDeviceAsync(
        Guid userId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        // 设备名就是备份目录名：先过文件系统名称不变量，
        // 挡住 "../etc" 类输入经路径拼接逃逸挂载根（同 RenameAsync 的防护）
        try
        {
            NasSystem.Domain.Entities.FileItem.ValidateName(request.Name);
        }
        catch (ArgumentException ex)
        {
            // Error.Validation 精确命中 ResultExtensions 的 400 分支；
            // 手捏 "Backup.InvalidName" 会落默认 500
            return Result<BackupDeviceDto>.Failure(Error.Validation(ex.Message));
        }

        if (await deviceRepository.NameExistsAsync(userId, request.Name, cancellationToken))
        {
            return Result<BackupDeviceDto>.Failure(
                Error.Conflict($"设备名 '{request.Name}' 已被使用"));
        }

        var device = NasSystem.Domain.Entities.BackupDevice.Create(
            userId, request.Name, request.DeviceModel);

        await deviceRepository.AddAsync(device, cancellationToken);
        var saved = await SaveChangesWithConflictGuardAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result<BackupDeviceDto>.Failure(saved.Error);
        }

        logger.LogInformation(
            "备份设备已注册: UserId={UserId}, Device={Name}, Path={Path}",
            userId, device.Name, device.BackupPath);

        return Result<BackupDeviceDto>.Success(ToDto(device));
    }

    public async Task<Result<IReadOnlyList<BackupDeviceDto>>> GetDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var devices = await deviceRepository.GetByUserIdAsync(userId, cancellationToken);
        return Result<IReadOnlyList<BackupDeviceDto>>.Success(
            devices.Select(ToDto).ToList());
    }

    public async Task<Result> RevokeDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var device = await deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device is null || device.UserId != userId)
        {
            return Result.Failure(BackupErrors.DeviceNotFound(deviceId));
        }

        device.Revoke();
        deviceRepository.Update(device);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("备份设备已吊销: DeviceId={DeviceId}", deviceId);
        return Result.Success();
    }

    public async Task<Result<BackupSyncPlanDto>> ComputeSyncPlanAsync(
        Guid userId,
        Guid deviceId,
        BackupManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var device = await deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device is null || device.UserId != userId)
        {
            return Result<BackupSyncPlanDto>.Failure(BackupErrors.DeviceNotFound(deviceId));
        }
        if (device.IsRevoked)
        {
            // Error.Forbidden 精确命中 403 分支；手捏 "Backup.DeviceRevoked"
            // 不含任何映射表关键词，会落默认 500
            return Result<BackupSyncPlanDto>.Failure(
                Error.Forbidden("设备已吊销，请重新注册"));
        }

        // 服务端现状：备份目录（根自身 + 全部后代）一次查回。
        // 相对路径 = 条目路径去掉备份根前缀；只收文件，文件夹由上传时按需创建
        var backupRoot = device.BackupPath;
        // 归属收敛：BackupPath 已含 UserId 前缀，这里再按 OwnerId 兜一层——
        // 扫描同步可能把前缀内的无主物理文件记给触发扫描的人（F-011 同源）
        var subtree = await fileItemRepository.GetSubtreeEntriesAsync(backupRoot, userId, cancellationToken);
        var serverFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in subtree)
        {
            if (entry.ItemType != FileItemType.File)
                continue;
            if (entry.Path.Length <= backupRoot.Length + 1)
                continue; // 备份根本身不是文件
            var relative = entry.Path[(backupRoot.Length + 1)..];
            serverFiles[relative] = entry.SizeBytes;
        }

        // diff：缺路径或体积不同 → 待上传；体积相同 → 视为未变。
        // 同体积不同内容（客户端原地改文件且体积恰好一致）漏检：
        // 客户端可对单条传 forceReupload 语义（体积报 -1）强制重传
        var toUpload = new List<BackupUploadEntry>();
        var upToDate = 0;
        foreach (var entry in request.Entries)
        {
            var relative = NormalizeRelativePath(entry.RelativePath);
            if (relative is null)
                continue; // 跳过空段与 .. 逃逸项

            if (!serverFiles.TryGetValue(relative, out var serverSize) || serverSize != entry.SizeBytes)
            {
                toUpload.Add(new BackupUploadEntry(relative, entry.SizeBytes));
            }
            else
            {
                upToDate++;
            }
        }

        // 服务端有、客户端清单没有的条目：只上报不删，
        // 删除是数据清理决策，不放在同步计划的隐式副作用里
        var clientPaths = new HashSet<string>(
            request.Entries
                .Select(e => NormalizeRelativePath(e.RelativePath))
                .Where(p => p is not null)
                .Cast<string>(),
            StringComparer.OrdinalIgnoreCase);
        var orphaned = serverFiles.Keys
            .Where(p => !clientPaths.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return Result<BackupSyncPlanDto>.Success(new BackupSyncPlanDto(
            device.Id,
            backupRoot,
            toUpload,
            upToDate,
            orphaned));
    }

    public async Task<Result> RecordSyncCompleteAsync(
        Guid userId,
        Guid deviceId,
        int uploadedFileCount,
        CancellationToken cancellationToken = default)
    {
        var device = await deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device is null || device.UserId != userId)
        {
            return Result.Failure(BackupErrors.DeviceNotFound(deviceId));
        }

        if (device.IsRevoked)
        {
            // 吊销语义覆盖同批次两个入口：plan 已拦，sync-complete 漏拦会让
            // 被吊销设备继续写 LastSyncAt/统计（ITM-N35 同族：同类问题修覆盖不均匀）
            return Result.Failure(Error.Forbidden("设备已吊销，请重新注册"));
        }

        device.RecordSync(uploadedFileCount);
        deviceRepository.Update(device);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private static BackupDeviceDto ToDto(NasSystem.Domain.Entities.BackupDevice device) =>
        new(
            device.Id,
            device.Name,
            device.DeviceModel,
            device.BackupPath,
            device.LastSyncAt,
            device.LastSyncFileCount,
            device.IsRevoked,
            device.CreatedAt);

    /// <summary>
    /// 相对路径规范化：统一正斜杠、去首尾斜杠与 ./ 段。
    /// 返回 null 表示该项应跳过（空路径、纯 .. 逃逸、含盘符等）
    /// </summary>
    private static string? NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var unified = relativePath.Replace('\\', '/').Trim('/');
        if (unified.Length == 0)
            return null;

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (stack.Count == 0)
                    return null; // 逃逸备份根，跳过
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(segment);
        }

        return stack.Count == 0 ? null : string.Join('/', stack);
    }

    private async Task<Result> SaveChangesWithConflictGuardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (NasSystem.Domain.Common.UniqueConstraintException)
        {
            // 并发注册同名设备：唯一索引兜底，与 UserService 的 UsernameExists 同形态
            return Result.Failure(Error.Conflict("设备名已被使用"));
        }
    }
}

/// <summary>备份域错误（与 ApplicationErrors 同风格的本域副本）</summary>
internal static class BackupErrors
{
    /// <summary>
    /// Error.NotFound 工厂产出的 "Error.NotFound" 码精确命中 404 分支；
    /// 手捏 "Backup.DeviceNotFound" 不以 ".NotFound" 结尾，会落 500
    /// </summary>
    public static Error DeviceNotFound(Guid id) =>
        Error.NotFound("BackupDevice", id);
}
