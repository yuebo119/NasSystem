// =============================================================================
// NasSystem.Infrastructure - DatabaseInitializer.cs
// 数据库初始化器
// =============================================================================

namespace NasSystem.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Services;

/// <summary>
/// 数据库初始化器
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 初始化数据库
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NasDbContext>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        try
        {
            logger.LogInformation("正在初始化数据库...");

            // 确保数据库已创建
            await context.Database.EnsureCreatedAsync();

            // 检查是否需要初始化数据
            if (!await context.Users.AnyAsync())
            {
                logger.LogInformation("创建默认管理员账户...");
                await SeedDefaultAdminAsync(context, passwordHasher);
            }

            if (!await context.StoragePools.AnyAsync())
            {
                logger.LogInformation("创建默认存储池...");
                await SeedDefaultStoragePoolAsync(context);
            }

            logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "数据库初始化失败");
            throw;
        }
    }

    /// <summary>
    /// 创建默认管理员账户
    /// </summary>
    private static async Task SeedDefaultAdminAsync(NasDbContext context, IPasswordHasher passwordHasher)
    {
        var (hash, salt) = passwordHasher.HashPassword("admin123");

        var admin = User.Create(
            username: "admin",
            passwordHash: hash,
            passwordSalt: salt,
            role: UserRole.SuperAdmin,
            displayName: "系统管理员",
            email: "admin@localhost");

        context.Users.Add(admin);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 创建默认存储池
    /// </summary>
    private static async Task SeedDefaultStoragePoolAsync(NasDbContext context)
    {
        // 创建默认存储目录
        var storagePath = Path.Combine(AppContext.BaseDirectory, "storage");
        Directory.CreateDirectory(storagePath);

        var defaultPool = StoragePool.Create(
            name: "默认存储池",
            mountPath: storagePath,
            raidLevel: RaidLevel.None,
            description: "系统默认存储池");

        defaultPool.SetAsDefault(true);
        defaultPool.UpdateStatus(StoragePoolStatus.Healthy);

        // 尝试获取磁盘容量
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(storagePath) ?? storagePath);
            // 通过 UpdateUsage 设置使用量
            defaultPool.UpdateUsage(driveInfo.TotalSize - driveInfo.AvailableFreeSpace);
        }
        catch
        {
            // 忽略无法获取磁盘信息的情况
        }

        context.StoragePools.Add(defaultPool);
        await context.SaveChangesAsync();
    }
}
