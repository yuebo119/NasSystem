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

            // 执行数据库迁移（添加缺失的列）
            await MigrateSchemaAsync(context, logger);

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
    /// 迁移数据库架构（添加缺失的列）
    /// </summary>
    private static async Task MigrateSchemaAsync(NasDbContext context, ILogger logger)
    {
        try
        {
            // 检查 StoragePools 表是否有 MaxCapacityBytes 列
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(StoragePools);";
            
            bool hasMaxCapacityBytes = false;
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString(1);
                    if (columnName == "MaxCapacityBytes")
                    {
                        hasMaxCapacityBytes = true;
                        break;
                    }
                }
            }

            if (!hasMaxCapacityBytes)
            {
                logger.LogInformation("添加 MaxCapacityBytes 列到 StoragePools 表...");
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE StoragePools ADD COLUMN MaxCapacityBytes INTEGER NULL;";
                await alterCommand.ExecuteNonQueryAsync();
                logger.LogInformation("MaxCapacityBytes 列添加成功");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "数据库架构迁移时出现警告");
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
            description: "系统默认存储池");

        defaultPool.SetAsDefault(true);

        // 尝试获取磁盘容量
        try
        {
            var pathRoot = Path.GetPathRoot(storagePath);
            if (!string.IsNullOrEmpty(pathRoot))
            {
                var driveInfo = new DriveInfo(pathRoot);
                if (driveInfo.IsReady)
                {
                    defaultPool.UpdateCapacity(driveInfo.TotalSize, driveInfo.TotalSize - driveInfo.AvailableFreeSpace);
                }
            }
        }
        catch
        {
            // 忽略无法获取磁盘信息的情况
        }

        context.StoragePools.Add(defaultPool);
        await context.SaveChangesAsync();
    }
}
