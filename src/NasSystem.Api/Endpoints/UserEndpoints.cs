// =============================================================================
// NasSystem.Api - UserEndpoints.cs
// 用户管理 API 端点
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Users;
using NasSystem.Application.Users.Dtos;
using NasSystem.Shared;

/// <summary>
/// 用户管理端点扩展
/// </summary>
public static class UserEndpoints
{
    /// <summary>
    /// 注册用户管理相关端点
    /// </summary>
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users").WithTags("用户管理");

        // 用户登录 (匿名访问)
        users.MapPost("/login", Login)
             .WithName("Login")
             .WithSummary("用户登录")
             .WithDescription("使用用户名和密码登录，成功后返回 JWT Token")
             .RequireRateLimiting("auth")
             .AllowAnonymous();

        // 用户登出
        users.MapPost("/logout", Logout)
             .WithName("Logout")
             .WithSummary("用户登出")
             .WithDescription("登出当前用户（客户端需删除本地存储的 Token）")
             .RequireAuthorization();

        // 获取当前用户信息
        users.MapGet("/me", GetCurrentUser)
             .WithName("GetCurrentUser")
             .WithSummary("获取当前登录用户信息")
             .WithDescription("获取当前已认证用户的详细信息")
             .RequireAuthorization();

        // 获取用户列表 (管理员)
        users.MapGet("/", GetUsers)
             .WithName("GetUsers")
             .WithSummary("获取用户列表")
             .WithDescription("分页获取系统中的用户列表，支持搜索。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        // 获取用户全站统计 (管理员)。管理页统计卡专用：分页响应只含当前页，
        // 统计卡要全站口径，两者解耦（2026-10-21 审计前前端对当前页求和冒充总量）
        users.MapGet("/stats", GetUserStats)
             .WithName("GetUserStats")
             .WithSummary("获取用户全站统计")
             .WithDescription("总数/管理员数/启用数/总使用空间，全站口径。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        // 获取单个用户 (管理员)
        users.MapGet("/{id:guid}", GetUserById)
             .WithName("GetUserById")
             .WithSummary("获取用户详情")
             .WithDescription("根据用户 ID 获取用户详细信息。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        // 创建用户 (管理员)
        users.MapPost("/", CreateUser)
             .WithName("CreateUser")
             .WithSummary("创建用户")
             .WithDescription("创建新用户账户。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        // 更新用户 (管理员)
        users.MapPut("/{id:guid}", UpdateUser)
             .WithName("UpdateUser")
             .WithSummary("更新用户信息")
             .WithDescription("更新指定用户的信息。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        // 删除用户 (管理员)
        users.MapDelete("/{id:guid}", DeleteUser)
             .WithName("DeleteUser")
             .WithSummary("删除用户")
             .WithDescription("删除指定用户。需要管理员权限。不能删除超级管理员。")
             .RequireAuthorization("RequireAdmin");

        // 修改密码
        users.MapPost("/{id:guid}/password", ChangePassword)
             .WithName("ChangePassword")
             .WithSummary("修改密码")
             .WithDescription("修改用户密码，需要提供当前密码验证")
             .RequireAuthorization();

        // 重置密码 (管理员)
        users.MapPost("/{id:guid}/reset-password", ResetPassword)
             .WithName("ResetPassword")
             .WithSummary("重置用户密码")
             .WithDescription("管理员重置用户密码，无需验证原密码。需要管理员权限。")
             .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static async Task<IResult> Login(
        LoginDto request,
        IUserService userService)
    {
        var result = await userService.LoginAsync(request);
        return result.ToHttpResult();
    }

    private static IResult Logout()
    {
        // JWT 是无状态的，客户端删除令牌即可
        return Results.Ok(ApiResponse.Ok());
    }

    private static async Task<IResult> GetCurrentUser(
        ClaimsPrincipal user,
        IUserService userService)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Results.Unauthorized();

        var result = await userService.GetByIdAsync(userId);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetUsers(
        IUserService userService,
        int page = 1,
        int pageSize = 20,
        string? searchTerm = null)
    {
        var result = await userService.GetPagedAsync(page, pageSize, searchTerm);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetUserStats(
        IUserService userService,
        CancellationToken cancellationToken)
    {
        // 全站统计。与 /{id:guid} 的路由不冲突：stats 不是 guid 形态，
        // 约束直接排歧，无需依赖注册顺序
        var result = await userService.GetStatsAsync(cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetUserById(
        Guid id,
        IUserService userService)
    {
        var result = await userService.GetByIdAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CreateUser(
        CreateUserDto request,
        IUserService userService)
    {
        var result = await userService.CreateAsync(request);
        return result.IsSuccess
            ? Results.Created($"/api/v1/users/{result.Value.Id}", ApiResponse<UserDto>.Ok(result.Value))
            : result.ToHttpResult();
    }

    private static async Task<IResult> UpdateUser(
        Guid id,
        UpdateUserDto request,
        IUserService userService)
    {
        var result = await userService.UpdateAsync(id, request);
        return result.ToHttpResult();
    }

    private static async Task<IResult> DeleteUser(
        Guid id,
        IUserService userService)
    {
        var result = await userService.DeleteAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ChangePassword(
        Guid id,
        ChangePasswordDto request,
        ClaimsPrincipal user,
        IUserService userService)
    {
        // 只能修改自己的密码或管理员修改任何人
        var currentUserIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = user.IsInRole("SuperAdmin") || user.IsInRole("Admin");

        if (!isAdmin && currentUserIdClaim != id.ToString())
            return Results.Forbid();

        var result = await userService.ChangePasswordAsync(id, request.CurrentPassword, request.NewPassword);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ResetPassword(
        Guid id,
        ResetPasswordDto request,
        IUserService userService)
    {
        var result = await userService.ResetPasswordAsync(id, request.NewPassword);
        return result.ToHttpResult();
    }
}

/// <summary>
/// 修改密码请求
/// </summary>
public sealed record ChangePasswordDto(string CurrentPassword, string NewPassword);

/// <summary>
/// 重置密码请求
/// </summary>
public sealed record ResetPasswordDto(string NewPassword);
