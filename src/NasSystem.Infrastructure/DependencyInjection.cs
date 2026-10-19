// =============================================================================
// NasSystem.Infrastructure - DependencyInjection.cs
// 基础设施层服务注册
// =============================================================================

namespace NasSystem.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NasSystem.Application.Common;
using NasSystem.Application.Semantic;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure.Data.Repositories;
using NasSystem.Infrastructure.Persistence;
using NasSystem.Infrastructure.Services;
using NasSystem.Infrastructure.Semantic;
using NasSystem.Infrastructure.Media;
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
        services.AddScoped<IBackupDeviceRepository, BackupDeviceRepository>();
        services.AddScoped<IFileOcrTextRepository, FileOcrTextRepository>();
        services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
        services.AddScoped<IStoragePoolRepository, StoragePoolRepository>();
        services.AddScoped<IFileShareRepository, FileShareRepository>();
        services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        services.AddScoped<IProtectedDirectoryRepository, ProtectedDirectoryRepository>();

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

        // 系统信息采集（运行时自省 + 数据库版本 + 网卡统计）。
        // Scoped：内部用 PalormSessionHolder 查 SQLite 版本，与仓储同生命周期
        services.AddScoped<Application.SystemInfo.ISystemInfoService, SystemInfoService>();

        // 系统控制（重启/关机）：单例，内部只依赖 IHostApplicationLifetime
        services.AddSingleton<Application.SystemInfo.ISystemControlService, SystemInfo.SystemControlService>();

        // 注册缩略图服务
        services.Configure<Application.Files.ThumbnailOptions>(
            configuration.GetSection("Thumbnails"));
        services.AddSingleton<Application.Files.IThumbnailService, Files.ThumbnailService>();

        // OCR（ROADMAP Phase 2）。引擎单例：模型从 Assembly 嵌入资源解析，
        // 首次加载一两秒，每请求新建等于每次识别都付这个成本；引擎无状态可重入。
        // 识别服务 Scoped：走仓储与工作单元，与请求同生命周期
        services.AddSingleton<Ocr.PaddleOcrEngineHolder>();
        services.AddScoped<Application.Ocr.IOcrService, Ocr.OcrService>();

        // 媒体库（ROADMAP Phase 3）。无状态提取器 + 走仓储的聚合服务
        services.AddScoped<Application.Media.IMediaService, Media.MediaService>();

        // 视频转码（ROADMAP Phase 3）：FFmpeg HLS 按需转码 + 视频帧抽取。
        // Singleton：内部持进程内并发闸与缓存目录状态，无请求态。
        // ThumbnailService 的视频封面依赖它（ffmpeg 就位时抽真帧）
        var transcodeOptions = new TranscodeOptions();
        configuration.GetSection("Transcode").Bind(transcodeOptions);
        services.AddSingleton(transcodeOptions);
        services.AddSingleton<Application.Media.ITranscodeService, Media.FfmpegTranscodeService>();

        // 清单快照与只读保护（ROADMAP Phase 2）。快照采集是库内查询 +
        // 批量写，保护判定是个位数表查询，都随请求作用域
        services.AddScoped<Application.Snapshots.ISnapshotService, Services.SnapshotService>();
        services.AddScoped<Application.ProtectedDirectories.IProtectedDirectoryService,
            Services.ProtectedDirectoryService>();

        // 定时快照（ROADMAP Phase 2）：原生 BackgroundService，cron 由
        // snapshot.schedule 配置驱动，每分钟 tick 判定。默认关闭
        // （snapshot.enabled=false），管理员在配置页开启
        services.AddHostedService<Services.SnapshotBackgroundService>();

        // 语义搜图（ROADMAP Phase 2，探针结论见 ADR-0003）。引擎单例
        // （模型加载秒级，编码器无状态可重入）；索引存储 Scoped 会每请求
        // 开连接太浪费，注册为 Singleton 自带并发闸；服务 Scoped 走仓储
        var semanticOptions = new SemanticOptions();
        configuration.GetSection("Semantic").Bind(semanticOptions);
        services.AddSingleton(semanticOptions);
        services.AddSingleton<Semantic.ClipEngineHolder>();
        services.AddSingleton(provider => new Semantic.SemanticIndexStore(
            connectionString,
            ResolveVecLibraryPath(configuration),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Semantic.SemanticIndexStore>>()));
        services.AddHttpClient<Semantic.ClipModelDownloader>();
        services.AddScoped<Application.Semantic.ISemanticSearchService, Semantic.SemanticSearchService>();

        // 语义索引后台建库：模型就位且开关打开时增量编码（单张 58ms，
        // 容量口径决定必须后台）。coordinator 是手动触发（trigger 端点）
        // 到后台服务的唤醒通道；接口映射委托到同一单例，端点与后台
        // 服务共享一个实例（两处各注册一份会裂成两个对象，唤醒丢失）
        services.AddSingleton<Semantic.SemanticIndexCoordinator>();
        services.AddSingleton<ISemanticIndexTrigger>(
            provider => provider.GetRequiredService<Semantic.SemanticIndexCoordinator>());
        services.AddHostedService<Semantic.SemanticIndexBackgroundService>();

        // 系统动态配置（配置页）：descriptor 驱动，键值表存储，改完即生效。
        // Scoped：与仓储同生命周期；消费方（版本保留/OCR 开关/WebDAV 闸）
        // 都是 scoped 或 per-request，直接注入
        services.AddScoped<Application.Configuration.ISystemConfigService, Configuration.SystemConfigService>();

        // 内存缓存
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// 定位 sqlite-vec 的原生件（vec0.dll / vec0.so）。NuGet 移植包的
    /// runtimes 布局把原生件放在输出目录的 runtimes/{rid}/native/ 下；
    /// 允许配置覆盖（自备扩展或非标准布局）
    /// </summary>
    private static string ResolveVecLibraryPath(IConfiguration configuration)
    {
        var configured = configuration["Semantic:VecLibraryPath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var isWindows = OperatingSystem.IsWindows();
        var name = isWindows ? "vec0.dll" : "vec0.so";
        var rid = isWindows ? "win-x64" : "linux-x64";
        var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name);
        if (File.Exists(candidate))
            return candidate;

        // 回退：与程序集同目录（某些发布形态会摊平 runtimes）
        var flat = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(flat) ? flat : candidate;
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

        // OCR 全文检索（ROADMAP Phase 2 全文检索升级）：FTS5 trigram 镜像表。
        // 与 PalORM 行模型体系的关系：FTS5 虚拟表不是行模型（源生成器不产出），
        // 与两个部分索引同理走幂等裸 SQL。选 trigram tokenizer：中文场景下
        // unicode61 按 Unicode 类别分词，双字词查询必然落空（探针实测），
        // trigram 按三字子串切分是 FTS5 对中文开箱可用的形态。
        // FileItemId 列 UNINDEXED：它只作关联投影不参与检索，不建索引
        // 省一半写入成本。同步写入在 FileOcrTextRepository（同事务双写），
        // 存量回填在下面——新库两张表皆空，回填是 no-op
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS FileOcrTextsFts USING fts5(
                FileItemId UNINDEXED,
                Text,
                tokenize='trigram'
            );
            INSERT INTO FileOcrTextsFts(FileItemId, Text)
            SELECT FileItemId, Text FROM FileOcrTexts
            WHERE FileItemId NOT IN (SELECT FileItemId FROM FileOcrTextsFts);
            """));

        // FileVersions.FileItemId 的级联外键移除（ROADMAP Phase 1）。
        // SQLite 不支持 DROP CONSTRAINT，外键变更只能重建表。检测到旧外键
        // （pragma_foreign_key_list 里有指向 FileItems 的行）才执行，新库
        // 行模型已无该外键、本就不会命中，天然幂等
        await MigrateLegacyFileVersionForeignKeyAsync(db, logger);

        // 建表后校验 schema 与编译期生成的模型是否一致。
        // MigrateAsync 是幂等的，表已存在时不会重建，因而"库里少了列/索引"这类漂移
        // 它不会报，只在查询时才以"no such column"暴露。启动时主动校验能把问题
        // 提前到启动日志，而不是等用户点某个端点才 500。
        await LogSchemaDriftAsync(db, logger);

        await LogEfResidueAsync(db, logger);

        // 存储根目录：优先配置 NasSystem:StoragePath（Docker 部署经
        // NasSystem__StoragePath 环境变量注入，与 /app/storage 卷映射对应），
        // 未配置退回 AppContext.BaseDirectory/storage。
        // 曾经硬编码 BaseDirectory：应用从 net10.0 迁到 net11.0 后池路径与
        // 全部 PhysicalPath 落在旧构建目录，dotnet clean 即数据全失
        // （2026-10-15 实测并迁移修复）
        var storagePath = configuration["NasSystem:StoragePath"];
        if (string.IsNullOrWhiteSpace(storagePath))
            storagePath = Path.Combine(AppContext.BaseDirectory, "storage");

        // 初始化种子数据
        await SeedDataAsync(db, storagePath);
    }

    /// <summary>
    /// 移除 FileVersions.FileItemId 的级联外键（ROADMAP Phase 1）。
    /// SQLite 不支持 DROP CONSTRAINT，外键变更只能重建表。检测到旧外键
    /// （pragma_foreign_key_list 里有指向 FileItems 的行）才执行，新库
    /// 行模型已无该外键、本就不会命中，天然幂等。
    /// </summary>
    /// <remarks>
    /// 语句顺序：先建新表灌数据（失败则旧表完好），再删旧表改名，
    /// 把不可逆操作压在最后；残留风险是删旧表后改名前崩溃，下次启动
    /// 检测不到旧外键不会重跑，需人工把 FileVersions_migrated 改名救回。
    /// 提为 public 供迁移测试直接驱动（否则该一次性脚本无任何执行证据）
    /// </remarks>
    public static async Task MigrateLegacyFileVersionForeignKeyAsync(
        DataSession<SqliteProvider> db,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        var legacyVersionFk = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                """SELECT COUNT(*) FROM pragma_foreign_key_list('FileVersions') WHERE "table" = 'FileItems'"""),
            false);
        if (legacyVersionFk == 0)
            return;

        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
            """
            CREATE TABLE "FileVersions_migrated" (
                "Id" TEXT PRIMARY KEY NOT NULL,
                "FileItemId" TEXT NOT NULL,
                "VersionNumber" INTEGER NOT NULL,
                "StoragePath" TEXT NOT NULL,
                "FileSize" BIGINT NOT NULL,
                "FileHash" TEXT NOT NULL,
                "Comment" TEXT,
                "IsCurrent" BOOLEAN NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "CreatedAt" TIMESTAMP NOT NULL,
                "UpdatedAt" TIMESTAMP,
                FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            INSERT INTO "FileVersions_migrated"
                ("Id", "FileItemId", "VersionNumber", "StoragePath", "FileSize", "FileHash", "Comment", "IsCurrent", "CreatedByUserId", "CreatedAt", "UpdatedAt")
                SELECT "Id", "FileItemId", "VersionNumber", "StoragePath", "FileSize", "FileHash", "Comment", "IsCurrent", "CreatedByUserId", "CreatedAt", "UpdatedAt" FROM "FileVersions";
            DROP TABLE "FileVersions";
            ALTER TABLE "FileVersions_migrated" RENAME TO "FileVersions";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FileVersions_FileItemId_VersionNumber" ON "FileVersions"("FileItemId", "VersionNumber");
            CREATE INDEX IF NOT EXISTS "IX_FileVersions_FileItemId" ON "FileVersions"("FileItemId");
            CREATE INDEX IF NOT EXISTS "IX_FileVersions_FileItemId_IsCurrent" ON "FileVersions"("FileItemId", "IsCurrent");
            CREATE INDEX IF NOT EXISTS "IX_FileVersions_CreatedByUserId" ON "FileVersions"("CreatedByUserId");
            """));
        logger.LogInformation("已迁移 FileVersions：移除 FileItemId 级联外键（版本快照在文件永久删除后保留）");
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
        drift.AddRange(await db.ValidateSchemaAsync<BackupDeviceRow>());
        drift.AddRange(await db.ValidateSchemaAsync<FileOcrTextRow>());
        drift.AddRange(await db.ValidateSchemaAsync<SystemSettingRow>());
        drift.AddRange(await db.ValidateSchemaAsync<DirectorySnapshotRow>());
        drift.AddRange(await db.ValidateSchemaAsync<SnapshotEntryRow>());
        drift.AddRange(await db.ValidateSchemaAsync<ProtectedDirectoryRow>());

        if (drift.Count == 0)
        {
            logger.LogInformation("PalORM schema 校验通过：11 个行模型与数据库一致");
            return;
        }

        foreach (var item in drift)
            logger.LogWarning("PalORM schema 漂移: {Drift}", item);
    }

    private static async Task SeedDataAsync(DataSession<SqliteProvider> db, string storagePath)
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

        // 挂载点先落成实体再建目录：容量刷新要求目录存在才取磁盘容量，
        // 新装实例不预建的话首页总容量会显示 0
        var defaultPool = Domain.Entities.StoragePool.Create(
            name: "默认存储池",
            mountPath: storagePath,
            description: "系统默认存储池");
        defaultPool.SetAsDefault(true);
        Directory.CreateDirectory(storagePath);

        await db.InsertAsync(adminUser.ToRow());
        await db.InsertAsync(defaultPool.ToRow());
    }
}
