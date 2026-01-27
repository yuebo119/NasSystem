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

        var (statusCode, error) = exception switch
        {
            ArgumentException argEx => (
                HttpStatusCode.BadRequest,
                new ApiError("Error.Argument", argEx.Message)),

            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                new ApiError("Error.Unauthorized", "未授权访问")),

            FileNotFoundException fileEx => (
                HttpStatusCode.NotFound,
                new ApiError("Error.NotFound", $"文件未找到: {fileEx.FileName}")),

            DirectoryNotFoundException => (
                HttpStatusCode.NotFound,
                new ApiError("Error.NotFound", "目录未找到")),

            InvalidOperationException invalidEx => (
                HttpStatusCode.BadRequest,
                new ApiError("Error.InvalidOperation", invalidEx.Message)),

            OperationCanceledException => (
                HttpStatusCode.BadRequest,
                new ApiError("Error.Cancelled", "操作已取消")),

            _ => (
                HttpStatusCode.InternalServerError,
                new ApiError(
                    "Error.Internal",
                    environment.IsDevelopment() ? exception.Message : "发生内部错误，请稍后重试"))
        };

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
