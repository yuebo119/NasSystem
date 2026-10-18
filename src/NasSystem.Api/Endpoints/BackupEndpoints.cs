// =============================================================================
// NasSystem.Api - BackupEndpoints.cs
// 移动备份 API 端点（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Backups;

/// <summary>
/// 备份端点：设备注册/枚举/吊销 + 同步计划 diff。
/// 文件上传不在本组——客户端按计划逐条走标准上传端点
/// （目标路径 = 计划返回的 BackupPath + 相对路径）
/// </summary>
public static class BackupEndpoints
{
    public static RouteGroupBuilder MapBackupEndpoints(this RouteGroupBuilder group)
    {
        var backup = group.MapGroup("/backup").WithTags("移动备份");

        // 注册设备
        backup.MapPost("/devices", RegisterDevice)
              .WithName("RegisterBackupDevice")
              .WithSummary("注册备份设备")
              .WithDescription("注册一个备份设备（手机/电脑）。同用户名下设备名唯一，备份目录为 /Backups/{设备名}")
              .RequireAuthorization();

        // 我的设备列表
        backup.MapGet("/devices", GetDevices)
              .WithName("GetBackupDevices")
              .WithSummary("获取我的备份设备")
              .WithDescription("获取当前用户的全部备份设备（含已吊销）")
              .RequireAuthorization();

        // 吊销设备
        backup.MapDelete("/devices/{id:guid}", RevokeDevice)
              .WithName("RevokeBackupDevice")
              .WithSummary("吊销备份设备")
              .WithDescription("吊销设备：保留记录与已备份文件，不再接受新的同步计划")
              .RequireAuthorization();

        // 同步计划
        backup.MapPost("/devices/{id:guid}/plan", ComputeSyncPlan)
              .WithName("ComputeBackupSyncPlan")
              .WithSummary("计算备份同步计划")
              .WithDescription("客户端上报文件清单，服务端对比设备备份目录现状，返回需要上传的条目")
              .RequireAuthorization();

        // 同步完成登记
        backup.MapPost("/devices/{id:guid}/sync-complete", RecordSyncComplete)
              .WithName("RecordBackupSyncComplete")
              .WithSummary("登记同步完成")
              .WithDescription("同步完成后刷新设备的最近同步时间与本次上传文件数")
              .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> RegisterDevice(
        RegisterDeviceRequest request,
        IBackupService backupService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await backupService.RegisterDeviceAsync(userId, request, cancellationToken);
        // 失败态先分流：Result<T>.Value 在失败时抛异常，
        // 不能直接插值进 Location（ToCreatedResult 只负责把失败转错误响应）
        if (result.IsFailure)
        {
            return result.ToHttpResult();
        }
        return result.ToCreatedResult($"/api/v1/backup/devices/{result.Value.Id}");
    }

    private static async Task<IResult> GetDevices(
        IBackupService backupService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await backupService.GetDevicesAsync(userId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevokeDevice(
        Guid id,
        IBackupService backupService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await backupService.RevokeDeviceAsync(userId, id, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ComputeSyncPlan(
        Guid id,
        BackupManifestRequest request,
        IBackupService backupService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await backupService.ComputeSyncPlanAsync(userId, id, request, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RecordSyncComplete(
        Guid id,
        int uploadedFileCount,
        IBackupService backupService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        var result = await backupService.RecordSyncCompleteAsync(userId, id, uploadedFileCount, cancellationToken);
        return result.ToHttpResult();
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
