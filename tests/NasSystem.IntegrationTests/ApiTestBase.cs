// =============================================================================
// NasSystem.IntegrationTests - ApiTestBase.cs
// API 测试基类
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure.Data;
using Xunit;

/// <summary>
/// API 测试基类
/// </summary>
public abstract class ApiTestBase : IClassFixture<NasSystemWebApplicationFactory>, IDisposable
{
    protected readonly HttpClient Client;
    protected readonly NasSystemWebApplicationFactory Factory;
    protected readonly JsonSerializerOptions JsonOptions;

    protected ApiTestBase(NasSystemWebApplicationFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// 获取数据库上下文
    /// </summary>
    protected NasDbContext GetDbContext()
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<NasDbContext>();
    }

    /// <summary>
    /// 设置认证令牌
    /// </summary>
    protected void SetAuthToken(string token)
    {
        Client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// 清除认证令牌
    /// </summary>
    protected void ClearAuthToken()
    {
        Client.DefaultRequestHeaders.Authorization = null;
    }

    public virtual void Dispose()
    {
        Client?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// API 响应包装类
/// </summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public ApiError? Error { get; init; }
}

/// <summary>
/// API 错误信息
/// </summary>
public sealed record ApiError
{
    public string? Code { get; init; }
    public string? Message { get; init; }
}
