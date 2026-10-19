// =============================================================================
// NasSystem.Api - Endpoints/ExportEndpoints.cs
// 3-2-1 导出 API 端点（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Api.Extensions;

using System.Security.Claims;
using NasSystem.Api.Endpoints;
using NasSystem.Domain.Repositories;

/// <summary>
/// 3-2-1 导出端点：把目录子树打包下载，可选 AES-256-CBC 加密
/// （openssl enc 兼容格式，解密命令见 ExportResult 的 remarks）。
/// </summary>
/// <remarks>
/// 单请求流式完成（不落临时文件、不起后台任务）：校验通过后直接返回
/// 流式 IResult，客户端边收边写盘。密码一次性使用，服务端不存储。
/// 管理员操作（导出范围是全库路径，不做 per-owner 过滤——与快照同口径）。
/// </remarks>
public static class ExportEndpoints
{
    public static RouteGroupBuilder MapExportEndpoints(this RouteGroupBuilder group)
    {
        var exports = group.MapGroup("/exports").WithTags("3-2-1 导出");

        exports.MapPost("/", CreateExport)
               .WithName("CreateExport")
               .WithSummary("导出目录（可选加密）")
               .WithDescription("把目录子树边扫边压直接写入响应体。提供密码时产出 AES-256-CBC 加密包（.zip.enc，openssl enc -d -aes-256-cbc -pbkdf2 可解密），否则明文 zip")
               .RequireAuthorization("RequireAdmin");

        return exports;
    }

    private static async Task<IResult> CreateExport(
        CreateExportRequest request,
        IFileItemRepository fileRepository,
        IStoragePoolRepository poolRepository,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RootPath))
            return Results.BadRequest(new { message = "导出根路径不能为空" });

        var normalized = request.RootPath.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        // 根 / 没有库记录（根本身不是实体），但它是导出页的默认值：
        // 解析为默认存储池的挂载点，导出该池全库（3-2-1 的主场景）。
        // 多池部署下只含默认池——UI 提示文案对此明示，不假装是全库
        if (normalized == "/")
        {
            var defaultPool = await poolRepository.GetDefaultAsync(cancellationToken);
            if (defaultPool is null)
                return Results.NotFound(new { message = "默认存储池不存在，无法导出根目录" });
            var rootPrefix = string.IsNullOrWhiteSpace(defaultPool.Name) ? "nas" : defaultPool.Name;
            return new ExportResult(
                defaultPool.MountPath,
                rootPrefix,
                $"{rootPrefix}-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
                request.Password);
        }

        // 校验在流式响应开始之前：路径不存在/不是目录时还能返回结构化错误，
        // 一旦开始写响应体就只剩半截流
        var item = await fileRepository.GetByPathAsync(normalized, cancellationToken);
        if (item is null)
            return Results.NotFound(new { message = $"路径不存在: {normalized}" });
        if (!item.IsFolder)
            return Results.BadRequest(new { message = "只能导出目录（单文件请直接下载）" });

        var fileName = $"{item.Name}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        return new ExportResult(item.PhysicalPath, item.Name, fileName, request.Password);
    }

    private sealed record CreateExportRequest(string RootPath, string? Password);

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
