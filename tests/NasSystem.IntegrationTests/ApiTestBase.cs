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
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Entities;
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

    // ─── 通用测试辅助（自 FileEndpointsIntegrationTests 提升，供多测试类复用）───

    protected async Task<string> GetAuthTokenAsync(string username)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IPasswordHasher>();

        var (hash, salt) = passwordHasher.HashPassword("password123");
        var user = User.Create(username, hash, salt);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var loginRequest = new LoginDto { Username = username, Password = "password123" };
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("Failed to get token");
    }

    protected async Task SeedDefaultStoragePoolAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();

        if (!context.StoragePools.Any(p => p.IsDefault))
        {
            var pool = StoragePool.Create("DefaultPool", "/mnt/storage");
            pool.SetAsDefault(true);
            pool.UpdateCapacity(1024L * 1024 * 1024 * 100, 0); // 100GB total, 0 used
            context.StoragePools.Add(pool);
            await context.SaveChangesAsync();
        }
    }

    protected async Task SeedTestFolderAsync(string path, string ownerUsername)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();

        var user = context.Users.FirstOrDefault(u => u.Username == ownerUsername);
        if (user is null) return;

        var pool = context.StoragePools.FirstOrDefault(p => p.IsDefault);
        if (pool is null)
        {
            await SeedDefaultStoragePoolAsync();
            pool = context.StoragePools.First(p => p.IsDefault);
        }

        var folderName = path.TrimStart('/').Split('/').Last();
        var folder = FileItem.CreateFolder(
            name: folderName,
            path: path,
            physicalPath: $"/mnt/storage{path}",
            storagePoolId: pool.Id,
            ownerId: user.Id);

        context.FileItems.Add(folder);
        await context.SaveChangesAsync();
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
