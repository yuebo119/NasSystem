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
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using PalORM;
using PalORM.Sqlite;
using Xunit;

/// <summary>
/// API 测试基类
/// </summary>
/// <remarks>
/// 种子辅助从直连 DbContext 改为走仓储接口 + 工作单元：
/// 仓储实现已换成 PalORM，Domain 接口不变，故这里只换解析目标。
/// </remarks>
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
    /// 建立数据库作用域，供种子辅助使用
    /// </summary>
    protected (IServiceScope Scope, IUserRepository Users, IStoragePoolRepository Pools,
        IFileItemRepository FileItems, IFileShareRepository Shares, IUnitOfWork UnitOfWork) CreateDataScope()
    {
        var scope = Factory.Services.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileShareRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
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
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var (hash, salt) = passwordHasher.HashPassword("password123");
        var user = User.Create(username, hash, salt);
        await users.AddAsync(user);
        await unitOfWork.SaveChangesAsync();

        var loginRequest = new LoginDto { Username = username, Password = "password123" };
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("Failed to get token");
    }

    protected async Task SeedDefaultStoragePoolAsync()
    {
        var (scope, _, pools, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var existing = await pools.GetDefaultAsync();
            if (existing is not null)
                return;

            var pool = StoragePool.Create("DefaultPool", "/mnt/storage");
            pool.SetAsDefault(true);
            pool.UpdateCapacity(1024L * 1024 * 1024 * 100, 0); // 100GB total, 0 used
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
        }
    }

    protected async Task SeedTestFolderAsync(string path, string ownerUsername)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var allUsers = await users.GetAllAsync();
            var user = allUsers.FirstOrDefault(u => u.Username == ownerUsername);
            if (user is null) return;

            var pool = await pools.GetDefaultAsync();
            if (pool is null)
            {
                await SeedDefaultStoragePoolAsync();
                pool = await pools.GetDefaultAsync();
                if (pool is null) return;
            }

            // 幂等：真实库上有 Path 的唯一部分索引（CONVENTIONS §3.2 的 TOCTOU 兜底），
            // 同名目录重复插入会被拒。EF InMemory 不校验唯一索引，这个坑一直被藏着——
            // 别的用例种 /documents/test.txt 时会顺带建父目录 /documents，故必须先查重
            if (await fileItems.GetByPathAsync(path) is not null)
                return;

            var folderName = path.TrimStart('/').Split('/').Last();
            var folder = FileItem.CreateFolder(
                name: folderName,
                path: path,
                physicalPath: $"/mnt/storage{path}",
                storagePoolId: pool.Id,
                ownerId: user.Id);

            await fileItems.AddAsync(folder);
            await unitOfWork.SaveChangesAsync();
        }
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
