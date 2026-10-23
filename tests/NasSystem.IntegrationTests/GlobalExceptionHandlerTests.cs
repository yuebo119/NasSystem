// =============================================================================
// NasSystem.IntegrationTests - GlobalExceptionHandlerTests.cs
// 全局异常映射：服务端故障不能按客户端错误回 400
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NasSystem.Api.Middleware;
using Xunit;

/// <summary>
/// 锁定异常类型到 HTTP 状态的边界：InvalidOperationException 来自服务端
/// 实现故障（缩放失败、子进程启动失败），必须 500；客户端状态错误由
/// 服务层 Result 的 Error.Validation/Error.Conflict 表达。
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task InvokeAsync_ServerSideInvalidOperation_ReturnsInternalServerError()
    {
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };
        var middleware = new GlobalExceptionHandler(
            _ => throw new InvalidOperationException("服务端实现故障"),
            NullLogger<GlobalExceptionHandler>.Instance,
            environment);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(
            (int)HttpStatusCode.InternalServerError,
            "InvalidOperationException 是服务端故障，不能按客户端错误回 400");
    }

    [Fact]
    public async Task InvokeAsync_FileNotFound_DoesNotLeakPhysicalPath()
    {
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };
        var middleware = new GlobalExceptionHandler(
            _ => throw new FileNotFoundException("missing", "/mnt/secret-pool/user/file.txt"),
            NullLogger<GlobalExceptionHandler>.Instance,
            environment);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
        body.Position = 0;
        using var reader = new StreamReader(body);
        var text = await reader.ReadToEndAsync();
        text.Should().NotContain("/mnt/secret-pool", "物理路径不能回显给客户端");
    }

    [Fact]
    public async Task InvokeAsync_SqliteReadOnly_ReturnsServiceUnavailable()
    {
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };
        var middleware = new GlobalExceptionHandler(
            _ => throw new Microsoft.Data.Sqlite.SqliteException("readonly", 8),
            NullLogger<GlobalExceptionHandler>.Instance,
            environment);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable,
            "数据库只读是暂时性依赖故障，不是内部 bug");
        body.Position = 0;
        using var reader = new StreamReader(body);
        (await reader.ReadToEndAsync()).Should().Contain("Error.DatabaseUnavailable");
    }

    [Fact]
    public async Task InvokeAsync_SqliteFull_ReturnsInsufficientStorage()
    {
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };
        var middleware = new GlobalExceptionHandler(
            _ => throw new Microsoft.Data.Sqlite.SqliteException("database full", 13),
            NullLogger<GlobalExceptionHandler>.Instance,
            environment);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InsufficientStorage);
    }

    [Fact]
    public async Task InvokeAsync_DiskFull_ReturnsInsufficientStorage()
    {
        var environment = new TestHostEnvironment { EnvironmentName = "Production" };
        var diskFull = new IOException("disk full") { HResult = unchecked((int)0x80070070) };
        var middleware = new GlobalExceptionHandler(
            _ => throw diskFull,
            NullLogger<GlobalExceptionHandler>.Instance,
            environment);
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InsufficientStorage);
        body.Position = 0;
        using var reader = new StreamReader(body);
        (await reader.ReadToEndAsync()).Should().Contain("Storage.InsufficientSpace");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "NasSystem.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}