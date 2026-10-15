// =============================================================================
// NasSystem.Api - RequestLoggingMiddleware.cs
// 请求日志中间件
// =============================================================================

namespace NasSystem.Api.Middleware;

using System.Diagnostics;

/// <summary>
/// 请求日志中间件
/// </summary>
public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            var elapsedMs = stopwatch.ElapsedMilliseconds;

            // 根据状态码选择日志级别
            if (statusCode >= 500)
            {
                logger.LogError(
                    "[{RequestId}] {Method} {Path} 完成 - {StatusCode} ({ElapsedMs}ms)",
                    requestId,
                    context.Request.Method,
                    context.Request.Path,
                    statusCode,
                    elapsedMs);
            }
            else if (statusCode >= 400)
            {
                logger.LogWarning(
                    "[{RequestId}] {Method} {Path} 完成 - {StatusCode} ({ElapsedMs}ms)",
                    requestId,
                    context.Request.Method,
                    context.Request.Path,
                    statusCode,
                    elapsedMs);
            }
            else
            {
                logger.LogInformation(
                    "[{RequestId}] {Method} {Path} 完成 - {StatusCode} ({ElapsedMs}ms)",
                    requestId,
                    context.Request.Method,
                    context.Request.Path,
                    statusCode,
                    elapsedMs);
            }
        }
    }
}

/// <summary>
/// 中间件扩展
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestLoggingMiddleware>();
    }
}
