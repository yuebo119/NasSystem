// =============================================================================
// NasSystem.IntegrationTests - WebApplicationFactoryExtensions.cs
// Web 应用工厂扩展
// =============================================================================

namespace NasSystem.IntegrationTests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NasSystem.Infrastructure.Data;

/// <summary>
/// 自定义 Web 应用工厂 - 每个测试类使用独立的数据库实例
/// </summary>
public class NasSystemWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName;
    private bool _databaseCreated;

    public NasSystemWebApplicationFactory()
    {
        // 每个工厂实例使用唯一的数据库名称
        _dbName = $"NasSystemTestDb_{Guid.NewGuid():N}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        
        builder.ConfigureServices(services =>
        {
            // 移除所有 EF Core 相关服务
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true ||
                d.ServiceType == typeof(DbContextOptions<NasDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(NasDbContext))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // 添加内存数据库用于测试 - 使用唯一名称确保隔离
            services.AddDbContext<NasDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_dbName);
            }, ServiceLifetime.Scoped);
        });
    }

    public void EnsureDatabaseCreated()
    {
        if (_databaseCreated) return;
        
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        db.Database.EnsureCreated();
        _databaseCreated = true;
    }

    /// <summary>
    /// 重置数据库状态 - 用于测试之间的隔离
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        
        // 清空所有表数据
        db.FileShares.RemoveRange(db.FileShares);
        db.FilePermissions.RemoveRange(db.FilePermissions);
        db.FileVersions.RemoveRange(db.FileVersions);
        db.FileItems.RemoveRange(db.FileItems);
        db.StoragePools.RemoveRange(db.StoragePools);
        db.Users.RemoveRange(db.Users);
        
        await db.SaveChangesAsync();
    }
}

