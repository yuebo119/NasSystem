// =============================================================================
// NasSystem.Infrastructure - DependencyInjection.cs
// 基础设施层服务注册
// =============================================================================

namespace NasSystem.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Common;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure.Data.Repositories;
using NasSystem.Infrastructure.Persistence;
using NasSystem.Infrastructure.Services;
using PalORM;
using PalORM.Sqlite;

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
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=data/nas.db";

        // PalORM 会话按作用域持有，惰性创建（CreateAsync 是异步的，DI 只能同步构造）
        services.AddScoped(_ => new PalormSessionHolder(new DbOptions
        {
            ConnectionString = connectionString,
            // SQLite 的 busy_timeout/synchronous/journal_mode 等 8 项 PRAGMA 由
            // PalORM.Sqlite 在连接打开时自动调优，取代原 SqlitePragmaInterceptor
        }));

        // 注册 UnitOfWork。同时注册具体类型：仓储的写入意图队列要用它，
        // 只注册接口会让仓储构造失败
        services.AddScoped<PalormUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PalormUnitOfWork>());

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
    /// 初始化数据库：建表、补两个部分索引、种子数据。
    /// </summary>
    /// <remarks>
    /// 迁移机制的变化：EF 时代靠迁移链 + __EFMigrationsHistory 判定已应用的迁移，
    /// 还额外做了"EnsureCreated 时代旧库打基线"的兼容逻辑。PalORM 侧由
    /// <c>MigrateAsync</c> 依据 <c>[Table]</c>/<c>[Index]</c> 注解生成 DDL，
    /// 幂等建表；两个带 WHERE 过滤条件的部分索引生成器不产出，沿用裸 SQL 补
    /// （与 AddBusinessUniqueIndexes/AddPathPrefixIndex 两个迁移当初的做法一致）。
    /// 存量库无需 __EFMigrationsHistory：MigrateAsync 的建表是幂等的，
    /// 老库已有的表不会被重建。
    /// </remarks>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var sessions = scope.ServiceProvider.GetRequiredService<PalormSessionHolder>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PalormSessionHolder>>();

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

        var db = await sessions.GetAsync();

        // 建表（幂等）。ITM-N7 的 SQLite 并发三件套由 provider 在连接级自动设置
        await db.MigrateAsync();

        // 带 WHERE 过滤条件的部分索引，生成器不产出，手工补。
        // 普通索引已随行模型的 [Index] 声明交给 MigrateAsync，不在这里重复。
        // 三条分别是：Path 唯一部分索引（TOCTOU 兜底，软删记录不占路径）、
        // Path 前缀 NOCASE 部分索引（服务后代遍历的 LIKE 'prefix%'）、
        // Email 唯一部分索引（NULL 邮箱不参与唯一性，否则第二个空邮箱用户插不进去）。
        // IF NOT EXISTS 保证幂等，存量库重复执行无副作用。
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_FileItems_Path ON FileItems(Path) WHERE [IsDeleted] = 0;
            CREATE INDEX IF NOT EXISTS IX_FileItems_Path_Prefix ON FileItems(Path COLLATE NOCASE) WHERE "IsDeleted" = 0;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email) WHERE [Email] IS NOT NULL;
            """));

        // 建表后校验 schema 与编译期生成的模型是否一致。
        // MigrateAsync 是幂等的，表已存在时不会重建，因而"库里少了列/索引"这类漂移
        // 它不会报，只在查询时才以"no such column"暴露。启动时主动校验能把问题
        // 提前到启动日志，而不是等用户点某个端点才 500。
        await LogSchemaDriftAsync(db, logger);

        await LogEfResidueAsync(db, logger);

        // 初始化种子数据
        await SeedDataAsync(db);
    }

    /// <summary>
    /// 检测 EF 时代的残留表 __EFMigrationsHistory，只告知不自动删。
    /// </summary>
    /// <remarks>
    /// PalORM 不读这张表，MigrateAsync 也不会碰它，存量库里它会一直留着。
    /// 不自动 DROP 的理由：那是用户库里的表，删表是数据清理决策不是启动副作用
    /// （AGENTS.md P0 #5 破坏性操作确认）。新建库本就不该有它，真有说明种子化
    /// 脚本混用了 EF 时代的产物，值得在启动日志里显形。
    /// </remarks>
    private static async Task LogEfResidueAsync(
        DataSession<SqliteProvider> db,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var efHistory = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'"),
            false);

        if (efHistory > 0)
            logger.LogInformation(
                "检测到 __EFMigrationsHistory 表：EF Core 时代的迁移账本，PalORM 不读取也不维护它，" +
                "留着不影响运行；确认无需回退到 EF 后可手工 DROP TABLE __EFMigrationsHistory 清理");
    }

    /// <summary>
    /// 对 5 个行模型逐个跑 schema 校验，把漂移项写进启动日志。
    /// </summary>
    /// <remarks>
    /// 漂移不阻断启动：存量库可能故意多一张表（如已删除的 FilePermissions 遗留），
    /// 那是数据清理问题不是阻塞问题，所以只 WARN。
    /// </remarks>
    private static async Task LogSchemaDriftAsync(
        DataSession<SqliteProvider> db,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var drift = new List<string>();
        drift.AddRange(await db.ValidateSchemaAsync<FileItemRow>());
        drift.AddRange(await db.ValidateSchemaAsync<UserRow>());
        drift.AddRange(await db.ValidateSchemaAsync<StoragePoolRow>());
        drift.AddRange(await db.ValidateSchemaAsync<FileShareRow>());
        drift.AddRange(await db.ValidateSchemaAsync<FileVersionRow>());

        if (drift.Count == 0)
        {
            logger.LogInformation("PalORM schema 校验通过：5 个实体与数据库一致");
            return;
        }

        foreach (var item in drift)
            logger.LogWarning("PalORM schema 漂移: {Drift}", item);
    }

    private static async Task SeedDataAsync(DataSession<SqliteProvider> db)
    {
        // 检查是否已有管理员用户
        var existing = await db.CountAsync<UserRow>($"Username = {"admin"}");
        if (existing > 0)
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

        var defaultPool = Domain.Entities.StoragePool.Create(
            name: "默认存储池",
            mountPath: Path.Combine(AppContext.BaseDirectory, "storage"),
            description: "系统默认存储池");
        defaultPool.SetAsDefault(true);

        await db.InsertAsync(adminUser.ToRow());
        await db.InsertAsync(defaultPool.ToRow());
    }
}
