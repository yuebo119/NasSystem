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
using Microsoft.Extensions.Logging;
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
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NasDbContext>>();

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

        // 执行架构迁移（添加缺失的列与索引）
        await MigrateSchemaAsync(context, logger);

        // ITM-N7：SQLite 并发三件套——WAL 读写分离、busy_timeout 防 SQLITE_BUSY、NORMAL 同步级别
        await EnsureSqlitePragmasAsync(context);

        // 初始化种子数据
        await SeedDataAsync(context);
    }

    /// <summary>
    /// 启用 SQLite 并发配置（CONVENTIONS §3.1）。WAL 模式持久化在库文件中，
    /// busy_timeout/synchronous 为连接级参数，每次打开需重设。
    /// </summary>
    private static async Task EnsureSqlitePragmasAsync(NasDbContext context)
    {
        // 集成测试使用 InMemory provider，PRAGMA 仅对 SQLite 生效
        if (!context.Database.IsSqlite())
            return;

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var pragma in new[] { "PRAGMA journal_mode=WAL;", "PRAGMA busy_timeout=5000;", "PRAGMA synchronous=NORMAL;" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 迁移数据库架构（添加缺失的列）
    /// </summary>
    private static async Task MigrateSchemaAsync(NasDbContext context, Microsoft.Extensions.Logging.ILogger logger)
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
                using var alterCommand = connection.CreateCommand();
                alterCommand.CommandText = "ALTER TABLE StoragePools ADD COLUMN MaxCapacityBytes INTEGER NULL;";
                await alterCommand.ExecuteNonQueryAsync();
            }

            // ITM-N7：EnsureCreated 不更新已有库的索引——旧库用 DDL 补业务唯一性兜底
            // （新库由 EF 配置的 HasFilter 唯一索引覆盖，IF NOT EXISTS 双向幂等）
            var uniqueIndexes = new[]
            {
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_FileItems_Path ON FileItems(Path) WHERE [IsDeleted] = 0;",
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_FileShares_ShareCode ON FileShares(ShareCode);",
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username ON Users(Username);",
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_FileVersions_Item_Version ON FileVersions(FileItemId, VersionNumber);"
            };
            foreach (var ddl in uniqueIndexes)
            {
                try
                {
                    using var indexCommand = connection.CreateCommand();
                    indexCommand.CommandText = ddl;
                    await indexCommand.ExecuteNonQueryAsync();
                }
                catch (Exception indexEx)
                {
                    // 存量数据含重复值时建索引失败——记录并放行，由人工清理后重启重试
                    logger.LogWarning(indexEx, "唯一索引创建失败（存量数据可能存在重复），DDL: {Ddl}", ddl);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "数据库架构迁移时出现警告");
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
