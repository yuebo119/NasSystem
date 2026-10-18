// =============================================================================
// NasSystem.IntegrationTests - ApiTestBase.cs
// API 测试基类
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

    /// <summary>
    /// 构造 WebDAV 请求。username 存在时挂 Basic 认证（WebDAV 客户端口径），
    /// token 存在时挂 Bearer（网页端接口口径）；两者都无则是匿名请求。
    /// 按请求挂头而非改 Client 默认头：同工厂被多个用例共享，
    /// 默认头会跨用例污染
    /// </summary>
    protected static HttpRequestMessage CreateDavRequest(
        HttpMethod method,
        string url,
        string? username = null,
        string password = "password123",
        string? token = null,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (username is null)
        {
            return request;
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
        return request;
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

            // 挂载点 /mnt/storage 在 Windows 上取不到盘根，物理剩余探测返回 null。
            // 无配额时可用容量无从判断，上传预检会一律拒绝，故给测试池配 100GB 上限
            var pool = StoragePool.Create("DefaultPool", "/mnt/storage", maxCapacityBytes: 1024L * 1024 * 1024 * 100);
            pool.SetAsDefault(true);
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
