// =============================================================================
// NasSystem.Application - Backups/IBackupService.cs
// 移动备份服务（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Application.Backups;

using NasSystem.Shared;

/// <summary>
/// 备份服务：设备注册与同步计划。备份文件本身走标准上传管道
/// （/Backups/{设备名}/ 下的普通文件），配额、软删除、实时推送全复用
/// </summary>
public interface IBackupService
{
    /// <summary>注册备份设备。同用户名下设备名唯一（备份目录同名会互相覆盖）</summary>
    Task<Result<BackupDeviceDto>> RegisterDeviceAsync(
        Guid userId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>当前用户的设备列表（含已吊销）</summary>
    Task<Result<IReadOnlyList<BackupDeviceDto>>> GetDevicesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>吊销设备。保留记录与已备份文件（同步历史可查），不再接受新计划</summary>
    Task<Result> RevokeDeviceAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算同步计划：客户端上报清单（相对路径 + 体积），
    /// 服务端对比设备备份目录现状，返回需要上传的条目
    /// </summary>
    Task<Result<BackupSyncPlanDto>> ComputeSyncPlanAsync(
        Guid userId,
        Guid deviceId,
        BackupManifestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>同步完成后登记：刷新设备的最近同步时间与文件数</summary>
    Task<Result> RecordSyncCompleteAsync(
        Guid userId,
        Guid deviceId,
        int uploadedFileCount,
        CancellationToken cancellationToken = default);
}

// -------------------------------------------------------------------------
// DTO
// -------------------------------------------------------------------------

/// <summary>设备注册请求</summary>
public sealed record RegisterDeviceRequest(
    string Name,
    string? DeviceModel = null);

/// <summary>备份设备 DTO</summary>
public sealed record BackupDeviceDto(
    Guid Id,
    string Name,
    string? DeviceModel,
    string BackupPath,
    DateTime? LastSyncAt,
    int LastSyncFileCount,
    bool IsRevoked,
    DateTime CreatedAt);

/// <summary>
/// 客户端清单条目。相对路径统一用正斜杠（手机相册的 DCIM/xxx.jpg 口径），
/// 体积用于服务端的变化检测：同路径体积不同视为已修改需重传
/// </summary>
public sealed record BackupManifestEntry(
    string RelativePath,
    long SizeBytes);

/// <summary>同步计划请求</summary>
public sealed record BackupManifestRequest(
    IReadOnlyList<BackupManifestEntry> Entries);

/// <summary>需要上传的条目</summary>
public sealed record BackupUploadEntry(
    string RelativePath,
    long SizeBytes);

/// <summary>
/// 同步计划。toUpload 由客户端逐条上传（目标路径 = BackupPath + 相对路径），
/// orphanedOnServer 是服务端有、客户端清单里没有的文件（客户端删了原文件），
/// 只上报不自动删：删除决策归客户端
/// </summary>
public sealed record BackupSyncPlanDto(
    Guid DeviceId,
    string BackupPath,
    IReadOnlyList<BackupUploadEntry> ToUpload,
    int UpToDateCount,
    IReadOnlyList<string> OrphanedOnServer);
