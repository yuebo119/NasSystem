// =============================================================================
// NasSystem.Api - StorageEndpoints.cs
// 存储池管理 API 端点（简化版）
// =============================================================================

namespace NasSystem.Api.Extensions;

using NasSystem.Application.Storage;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Shared;

/// <summary>
/// 存储池管理端点扩展（简化版）
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

        // 刷新容量信息
        storage.MapPost("/refresh", RefreshCapacity)
               .WithName("RefreshStorageCapacity")
               .WithSummary("刷新存储容量信息")
               .RequireAuthorization("RequireAdmin");

        // 检查存储空间
        storage.MapPost("/check-space", CheckStorageSpace)
               .WithName("CheckStorageSpace")
               .WithSummary("检查存储空间是否足够")
               .RequireAuthorization();

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

    private static async Task<IResult> RefreshCapacity(IStorageService storageService)
    {
        var result = await storageService.RefreshCapacityAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> CheckStorageSpace(
        CheckStorageSpaceRequest request,
        IStorageService storageService)
    {
        var result = await storageService.CheckStorageSpaceAsync(request);
        return result.ToHttpResult();
    }
}
