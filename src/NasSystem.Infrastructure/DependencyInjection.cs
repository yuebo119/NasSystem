// =============================================================================
// NasSystem.Infrastructure - DependencyInjection.cs
// 基础设施层服务注册
// =============================================================================

namespace NasSystem.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Common;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure.Data;
using NasSystem.Infrastructure.Data.Repositories;
using NasSystem.Infrastructure.Services;

/// <summary>
/// 基础设施层依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册基础设施层服务
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 注册 DbContext
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=data/nas.db";
        
        services.AddDbContext<NasDbContext>(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptions.MigrationsAssembly(typeof(NasDbContext).Assembly.FullName);
            });
        });

        // 注册 UnitOfWork
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 注册仓储
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFileItemRepository, FileItemRepository>();
        services.AddScoped<IFileVersionRepository, FileVersionRepository>();
        services.AddScoped<IStoragePoolRepository, StoragePoolRepository>();
        services.AddScoped<IFileShareRepository, FileShareRepository>();
        services.AddScoped<IFilePermissionRepository, FilePermissionRepository>();

        // 注册领域服务
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // 注册 JWT 服务
        services.AddScoped<IJwtService, JwtService>();

        // 注册基础设施服务
        services.AddSingleton<IFileSystemService, FileSystemService>();

        // 注册缩略图服务
        services.Configure<Application.Thumbnails.ThumbnailOptions>(
            configuration.GetSection("Thumbnails"));
        services.AddSingleton<Application.Thumbnails.IThumbnailService, Files.ThumbnailService>();

        // 内存缓存
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // 确保数据库目录存在
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/nas.db";
        var match = System.Text.RegularExpressions.Regex.Match(
            connectionString, @"Data Source=(.+\.db)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var dbPath = match.Groups[1].Value;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        // 应用迁移或创建数据库
        await context.Database.EnsureCreatedAsync();

        // 执行架构迁移（添加缺失的列）
        await MigrateSchemaAsync(context);

        // 初始化种子数据
        await SeedDataAsync(context);
    }

    /// <summary>
    /// 迁移数据库架构（添加缺失的列）
    /// </summary>
    private static async Task MigrateSchemaAsync(NasDbContext context)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            // 检查 StoragePools 表是否有 MaxCapacityBytes 列
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
                Console.WriteLine("添加 MaxCapacityBytes 列到 StoragePools 表...");
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE StoragePools ADD COLUMN MaxCapacityBytes INTEGER NULL;";
                await alterCommand.ExecuteNonQueryAsync();
                Console.WriteLine("MaxCapacityBytes 列添加成功");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"数据库架构迁移时出现警告: {ex.Message}");
        }
    }

    /// <summary>
    /// 种子数据初始化
    /// </summary>
    private static async Task SeedDataAsync(NasDbContext context)
    {
        // 检查是否已有管理员用户
        if (await context.Users.AnyAsync())
            return;

        // 创建默认管理员
        var passwordHasher = new PasswordHasher();
        var (hash, salt) = passwordHasher.HashPassword("admin123");

        var adminUser = Domain.Entities.User.Create(
            username: "admin",
            passwordHash: hash,
            passwordSalt: salt,
            role: Domain.Enums.UserRole.SuperAdmin,
            displayName: "系统管理员",
            email: "admin@localhost");

        context.Users.Add(adminUser);

        // 创建默认存储池
        var defaultPool = Domain.Entities.StoragePool.Create(
            name: "默认存储池",
            mountPath: Path.Combine(AppContext.BaseDirectory, "storage"),
            description: "系统默认存储池");
        defaultPool.SetAsDefault(true);

        context.StoragePools.Add(defaultPool);

        await context.SaveChangesAsync();
    }
}
