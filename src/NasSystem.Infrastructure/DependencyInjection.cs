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
            // busy_timeout/synchronous 为连接级 PRAGMA，池内每个物理连接打开后重设
            options.AddInterceptors(new SqlitePragmaInterceptor());
        });

        // 注册 UnitOfWork
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 注册仓储
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IFileItemRepository, FileItemRepository>();
        services.AddScoped<IFileVersionRepository, FileVersionRepository>();
        services.AddScoped<IStoragePoolRepository, StoragePoolRepository>();
        services.AddScoped<IFileShareRepository, FileShareRepository>();

        // 注册领域服务
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // 领域事件处理器注册（ITM-N29 批次：机制接通首个订阅者）
        services.AddSingleton<NasSystem.Domain.Services.IDomainEventHandler<Domain.Events.FileDeletedEvent>,
            Services.FileDeletedCacheCleanupHandler>();

        // 注册 JWT 服务
        services.AddScoped<IJwtService, JwtService>();

        // 注册基础设施服务
        services.AddSingleton<IFileSystemService, FileSystemService>();

        // 注册缩略图服务
        services.Configure<Application.Files.ThumbnailOptions>(
            configuration.GetSection("Thumbnails"));
        services.AddSingleton<Application.Files.IThumbnailService, Files.ThumbnailService>();

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

        // 集成测试 InMemory provider：仅建模型，不走关系型初始化
        if (!context.Database.IsRelational())
        {
            await context.Database.EnsureCreatedAsync();
            return;
        }

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

        // ITM-N7：SQLite 并发三件套——WAL 读写分离、busy_timeout 防 SQLITE_BUSY、NORMAL 同步级别
        await EnsureSqlitePragmasAsync(context);

        // 迁移调度（ITM-N26 批次）：EF Migrations 取代手写 PRAGMA 列/索引迁移
        await BaselineAndMigrateAsync(context, logger);

        // 初始化种子数据
        await SeedDataAsync(context);
    }

    /// <summary>
    /// 存量库兼容的迁移入口：EnsureCreated 时代的旧库没有 __EFMigrationsHistory 表，
    /// 直接 Migrate 会尝试重建全部表。检测到旧库时先打基线标记（声明现状=InitialBaseline）
    /// 再执行后续迁移（AddBusinessUniqueIndexes 幂等补齐新增索引）。
    /// </summary>
    private static async Task BaselineAndMigrateAsync(NasDbContext context, Microsoft.Extensions.Logging.ILogger logger)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        var historyTableExists = false;
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            historyTableExists = Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;
        }

        if (!historyTableExists)
        {
            var usersTableExists = false;
            using (var check2 = connection.CreateCommand())
            {
                check2.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users';";
                usersTableExists = Convert.ToInt64(await check2.ExecuteScalarAsync()) > 0;
            }

            if (usersTableExists)
            {
                logger.LogInformation("检测到 EnsureCreated 时代的存量库——打基线标记后增量迁移");
                using var createHist = connection.CreateCommand();
                createHist.CommandText = @"CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                    ""MigrationId"" TEXT NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY,
                    ""ProductVersion"" TEXT NOT NULL);";
                await createHist.ExecuteNonQueryAsync();

                using var mark = connection.CreateCommand();
                mark.CommandText = "INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20260825080018_InitialBaseline', '10.0.2');";
                await mark.ExecuteNonQueryAsync();
            }
            // 全新空库：无历史表也无业务表，Migrate 会自动建历史表并应用全部迁移
        }

        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// 启用 SQLite 库级配置。busy_timeout/synchronous 已移交 SqlitePragmaInterceptor
    /// 在每个物理连接打开时重设（连接级参数）；此处仅设持久化在库文件的 journal_mode=WAL。
    /// </summary>
    private static async Task EnsureSqlitePragmasAsync(NasDbContext context)
    {
        // 集成测试使用 InMemory provider，PRAGMA 仅对 SQLite 生效
        if (!context.Database.IsSqlite())
            return;

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        await command.ExecuteNonQueryAsync();
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
