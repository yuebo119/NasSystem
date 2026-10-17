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
        // 健康检查（Docker HEALTHCHECK 每 30s 一次）与静态资源（内容哈希文件名，
        // nginx 已代理缓存并记 access log）不产生排查价值，跳过整条日志路径：
        // 省掉每请求一次 Stopwatch 加五个属性的结构化日志开销
        var path = context.Request.Path;
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

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
