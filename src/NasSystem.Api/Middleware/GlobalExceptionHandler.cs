// =============================================================================
// NasSystem.Api - GlobalExceptionHandler.cs
// 全局异常处理中间件
// =============================================================================

namespace NasSystem.Api.Middleware;

using System.Net;
using System.Text.Json;
using NasSystem.Shared;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public sealed class GlobalExceptionHandler(
    RequestDelegate next,
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        logger.LogError(exception, "处理请求时发生未处理的异常: {Path}", context.Request.Path);

        HttpStatusCode statusCode;
        ApiError? error;
        (statusCode, error) = exception switch
        {
            ArgumentException => (
                HttpStatusCode.BadRequest,
                new ApiError("Error.Argument", "请求参数无效")),

            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                new ApiError("Error.Unauthorized", "未授权访问")),

            // 不回显异常里的文件路径：内部 IO 异常常带物理路径（池挂载点、用户名、
            // 目录布局），客户端只需要"不存在"这一事实
            FileNotFoundException => (
                HttpStatusCode.NotFound,
                new ApiError("Error.NotFound", "请求的文件或目录不存在")),

            DirectoryNotFoundException => (
                HttpStatusCode.NotFound,
                new ApiError("Error.NotFound", "目录未找到")),

            // 不再把 InvalidOperationException 一律当 400：仓库内该类异常
            // 主要来自服务端故障（缩略图缩放失败、ffmpeg 启动失败、模型下载为空），
            // 映射成客户端错误会让调用方误判并掩盖真实故障。客户端状态类错误
            // 由服务层返回 Error.Validation/Error.Conflict，不靠异常类型表达。

            // 参数绑定失败（必填参数缺失、JSON 反序列化失败）： minimal API 在
            // 绑定层抛 BadHttpRequestException，落在默认分支会变 500。这是
            // 客户端错误，500 会让调用方把"我少传了参数"当成"服务端挂了"重试。
            // StatusCode 默认 400，绑定层也可能给 413/415 等，原样透传
            BadHttpRequestException bindEx => (
                (HttpStatusCode)bindEx.StatusCode,
                new ApiError("Error.BadRequest", bindEx.Message)),
            // ITM-N10：客户端断开导致的取消——连接已终止，不再尝试写响应（与 Pal.DDD ITM-030 同型）
            OperationCanceledException => (HttpStatusCode.RequestTimeout, null),

            // 唯一约束冲突是客户端错误（重名/重复码），不是服务端故障：
            // 落默认分支会变 500，调用方把"我提交了重复数据"当成"服务端挂了"
            // 重试。服务层守卫覆盖主路径，这里是漏网之物的统一出口
            // （2026-10-21 审计：并发建池/配置 upsert 竞态曾穿透成 500）
            NasSystem.Domain.Common.UniqueConstraintException => (
                HttpStatusCode.Conflict,
                new ApiError("Error.Conflict", "相同记录已被并发创建，请修改后重试")),

            _ => (
                HttpStatusCode.InternalServerError,
                new ApiError(
                    "Error.Internal",
                    environment.IsDevelopment() ? exception.Message : "发生内部错误，请稍后重试"))
        };

        // 取消场景：客户端已断开，写响应会抛异常——直接结束管道
        if (error is null)
        {
            context.Response.StatusCode = (int)statusCode;
            return;
        }

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new ApiResponse
        {
            Success = false,
            Error = error
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

/// <summary>
/// 中间件扩展
/// </summary>
public static class GlobalExceptionHandlerExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandler>();
    }
}
