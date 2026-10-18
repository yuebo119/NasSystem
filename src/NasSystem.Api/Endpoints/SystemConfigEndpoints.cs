// =============================================================================
// NasSystem.Api - Endpoints/SystemConfigEndpoints.cs
// 系统动态配置端点（配置页数据源）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Application.Configuration;
using NasSystem.Shared;

/// <summary>
/// 系统配置端点。GET 返回 descriptor + 当前生效值（前端按 descriptor
/// 渲染表单，新增配置项前端零改动），PUT 批量更新（全量校验，
/// 任一非法整单拒绝）。均限管理员
/// </summary>
public static class SystemConfigEndpoints
{
    public static RouteGroupBuilder MapSystemConfigEndpoints(this RouteGroupBuilder group)
    {
        var config = group.MapGroup("/system/config").WithTags("系统配置");

        config.MapGet("/", GetConfig)
              .WithName("GetSystemConfig")
              .WithSummary("获取系统配置")
              .WithDescription("获取全部可配置项的 descriptor 与当前生效值，前端按此渲染配置表单")
              .RequireAuthorization("RequireAdmin");

        config.MapPut("/", UpdateConfig)
              .WithName("UpdateSystemConfig")
              .WithSummary("更新系统配置")
              .WithDescription("批量更新配置。全量校验，任一键非法则整单拒绝；保存后即生效不重启")
              .RequireAuthorization("RequireAdmin");

        return group;
    }

    private static async Task<IResult> GetConfig(
        ISystemConfigService configService,
        CancellationToken cancellationToken)
    {
        var snapshot = await configService.GetSnapshotAsync(cancellationToken);
        return Results.Ok(ApiResponse<object>.Ok(snapshot));
    }

    private static async Task<IResult> UpdateConfig(
        UpdateConfigRequest request,
        ISystemConfigService configService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await configService.UpdateAsync(request.Values, userId, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>更新请求。Values 的键必须是 descriptor 注册表中的键</summary>
    private sealed record UpdateConfigRequest(IReadOnlyDictionary<string, string> Values);
}
