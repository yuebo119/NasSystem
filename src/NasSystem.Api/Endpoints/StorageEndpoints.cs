// =============================================================================
// NasSystem.Api - StorageEndpoints.cs
// 存储池管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using NasSystem.Application.Storage;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Shared;

/// <summary>
/// 存储池管理端点扩展
/// </summary>
public static class StorageEndpoints
{
    /// <summary>
    /// 注册存储池管理相关端点
    /// </summary>
    public static RouteGroupBuilder MapStorageEndpoints(this RouteGroupBuilder group)
    {
        var storage = group.MapGroup("/storage").WithTags("存储管理");

        // 获取存储概览
        storage.MapGet("/overview", GetStorageOverview)
               .WithName("GetStorageOverview")
               .WithSummary("获取存储概览")
               .RequireAuthorization();

        // 获取存储池列表
        storage.MapGet("/pools", GetStoragePools)
               .WithName("GetStoragePools")
               .WithSummary("获取存储池列表")
               .RequireAuthorization();

        // 获取单个存储池
        storage.MapGet("/pools/{id:guid}", GetStoragePoolById)
               .WithName("GetStoragePoolById")
               .WithSummary("获取存储池详情")
               .RequireAuthorization();

        // 创建存储池
        storage.MapPost("/pools", CreateStoragePool)
               .WithName("CreateStoragePool")
               .WithSummary("创建存储池")
               .RequireAuthorization("RequireAdmin");

        // 更新存储池
        storage.MapPut("/pools/{id:guid}", UpdateStoragePool)
               .WithName("UpdateStoragePool")
               .WithSummary("更新存储池配置")
               .RequireAuthorization("RequireAdmin");

        // 删除存储池
        storage.MapDelete("/pools/{id:guid}", DeleteStoragePool)
               .WithName("DeleteStoragePool")
               .WithSummary("删除存储池")
               .RequireAuthorization("RequireAdmin");

        // 设置默认存储池
        storage.MapPost("/pools/{id:guid}/default", SetDefaultPool)
               .WithName("SetDefaultPool")
               .WithSummary("设置默认存储池")
               .RequireAuthorization("RequireAdmin");

        // 获取系统磁盘列表
        storage.MapGet("/disks", GetSystemDisks)
               .WithName("GetSystemDisks")
               .WithSummary("获取系统磁盘列表")
               .RequireAuthorization("RequireAdmin");

        // 获取可用磁盘
        storage.MapGet("/disks/available", GetAvailableDisks)
               .WithName("GetAvailableDisks")
               .WithSummary("获取可用磁盘列表")
               .RequireAuthorization("RequireAdmin");

        // 刷新存储使用情况
        storage.MapPost("/refresh", RefreshUsage)
               .WithName("RefreshStorageUsage")
               .WithSummary("刷新存储使用情况")
               .RequireAuthorization("RequireAdmin");

        // 扫描磁盘健康
        storage.MapPost("/scan-health", ScanDiskHealth)
               .WithName("ScanDiskHealth")
               .WithSummary("扫描磁盘健康状态")
               .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static async Task<IResult> GetStorageOverview(IStorageService storageService)
    {
        var result = await storageService.GetOverviewAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetStoragePools(IStorageService storageService)
    {
        var result = await storageService.GetPoolsAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetStoragePoolById(Guid id, IStorageService storageService)
    {
        var result = await storageService.GetPoolByIdAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateStoragePool(
        CreateStoragePoolRequest request,
        IStorageService storageService)
    {
        var result = await storageService.CreatePoolAsync(request);
        return result.IsSuccess
            ? Results.Created($"/api/v1/storage/pools/{result.Value.Id}", ApiResponse<StoragePoolDto>.Ok(result.Value))
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateStoragePool(
        Guid id,
        UpdateStoragePoolRequest request,
        IStorageService storageService)
    {
        var result = await storageService.UpdatePoolAsync(id, request);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteStoragePool(Guid id, IStorageService storageService)
    {
        var result = await storageService.DeletePoolAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetDefaultPool(Guid id, IStorageService storageService)
    {
        var result = await storageService.SetDefaultPoolAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSystemDisks(IStorageService storageService)
    {
        var result = await storageService.GetSystemDisksAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAvailableDisks(IStorageService storageService)
    {
        var result = await storageService.GetAvailableDisksAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> RefreshUsage(IStorageService storageService)
    {
        var result = await storageService.RefreshUsageAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> ScanDiskHealth(IStorageService storageService)
    {
        var result = await storageService.ScanDiskHealthAsync();
        return result.ToHttpResult();
    }
}
