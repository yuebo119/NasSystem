// =============================================================================
// NasSystem.Api - ShareEndpoints.cs
// 共享管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Shares;
using NasSystem.Application.Shares.Dtos;
using NasSystem.Shared;

/// <summary>
/// 共享管理端点扩展
/// </summary>
public static class ShareEndpoints
{
    /// <summary>
    /// 注册共享管理相关端点
    /// </summary>
    public static RouteGroupBuilder MapShareEndpoints(this RouteGroupBuilder group)
    {
        var shares = group.MapGroup("/shares").WithTags("共享管理");

        // 获取共享列表
        shares.MapGet("/", GetShares)
              .WithName("GetShares")
              .WithSummary("获取共享列表")
              .RequireAuthorization();

        // 获取用户可访问的共享
        shares.MapGet("/accessible", GetAccessibleShares)
              .WithName("GetAccessibleShares")
              .WithSummary("获取用户可访问的共享")
              .RequireAuthorization();

        // 获取单个共享
        shares.MapGet("/{id:guid}", GetShareById)
              .WithName("GetShareById")
              .WithSummary("获取共享详情")
              .RequireAuthorization();

        // 创建共享
        shares.MapPost("/", CreateShare)
              .WithName("CreateShare")
              .WithSummary("创建共享")
              .RequireAuthorization("RequireAdmin");

        // 更新共享
        shares.MapPut("/{id:guid}", UpdateShare)
              .WithName("UpdateShare")
              .WithSummary("更新共享配置")
              .RequireAuthorization("RequireAdmin");

        // 删除共享
        shares.MapDelete("/{id:guid}", DeleteShare)
              .WithName("DeleteShare")
              .WithSummary("删除共享")
              .RequireAuthorization("RequireAdmin");

        // 启用/禁用共享
        shares.MapPatch("/{id:guid}/enabled", SetShareEnabled)
              .WithName("SetShareEnabled")
              .WithSummary("启用或禁用共享")
              .RequireAuthorization("RequireAdmin");

        // 获取共享权限
        shares.MapGet("/{id:guid}/permissions", GetSharePermissions)
              .WithName("GetSharePermissions")
              .WithSummary("获取共享权限列表")
              .RequireAuthorization("RequireAdmin");

        // 设置共享权限
        shares.MapPut("/{id:guid}/permissions", SetSharePermissions)
              .WithName("SetSharePermissions")
              .WithSummary("设置共享权限")
              .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static async Task<IResult> GetShares(IShareService shareService)
    {
        var result = await shareService.GetSharesAsync();
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAccessibleShares(
        IShareService shareService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await shareService.GetAccessibleSharesAsync(userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetShareById(
        Guid id,
        IShareService shareService)
    {
        var result = await shareService.GetByIdAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateShare(
        CreateShareRequest request,
        IShareService shareService,
        ClaimsPrincipal user)
    {
        var userId = GetUserId(user);
        var result = await shareService.CreateAsync(request, userId);
        return result.IsSuccess
            ? Results.Created($"/api/v1/shares/{result.Value.Id}", ApiResponse<ShareDto>.Ok(result.Value))
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateShare(
        Guid id,
        UpdateShareRequest request,
        IShareService shareService)
    {
        var result = await shareService.UpdateAsync(id, request);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteShare(
        Guid id,
        IShareService shareService)
    {
        var result = await shareService.DeleteAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetShareEnabled(
        Guid id,
        SetEnabledRequest request,
        IShareService shareService)
    {
        var result = await shareService.SetEnabledAsync(id, request.Enabled);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSharePermissions(
        Guid id,
        IShareService shareService)
    {
        var result = await shareService.GetPermissionsAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SetSharePermissions(
        Guid id,
        SetSharePermissionsRequest request,
        IShareService shareService)
    {
        var result = await shareService.SetPermissionsAsync(id, request);
        return result.ToHttpResult();
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

/// <summary>
/// 启用/禁用请求
/// </summary>
public record SetEnabledRequest(bool Enabled);
