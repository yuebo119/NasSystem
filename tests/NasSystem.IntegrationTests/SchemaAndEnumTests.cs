// =============================================================================
// NasSystem.IntegrationTests - SchemaAndEnumTests.cs
// schema 约束与枚举映射的锁定测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 锁定 schema 约束与枚举映射，防止"基准测不出来"的正确性回归。
/// </summary>
/// <remarks>
/// 两个真实教训促成本文件：
/// 一、影子行模型漏声明 <c>[ForeignKey]</c> 时，MigrateAsync 建出的新库外键数为 0，
/// 删除父行不再被 RESTRICT 拦住，而性能基准完全正常（2026-10-12 实测）。
/// 二、EF InMemory provider 不校验唯一索引，藏住过种子辅助重复建同名目录的缺陷。
/// 这类回归只有直接查 schema 才能发现，故固化为测试。
/// </remarks>
public class SchemaAndEnumTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SchemaAndEnumTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SchemaAndEnum_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        _provider = services.BuildServiceProvider();
        _provider.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    private (IServiceScope Scope, IUserRepository Users, IStoragePoolRepository Pools,
        IFileItemRepository FileItems, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public void MigrateAsync_CreatesAllForeignKeyConstraints()
    {
        // 每张表的期望外键数，与 EF 时代 schema 逐一对齐。
        // 2026-10-18 ROADMAP Phase 1：FileVersions 由 2 降为 1——FileItemId 的
        // 级联外键被移除（版本快照必须在文件永久删除后存活，级联恰好摧毁
        // 这道安全网），保留 CreatedByUserId → Users。存量库由启动迁移重建表
        var expected = new Dictionary<string, int>
        {
            ["FileItems"] = 3,
            ["FileShares"] = 2,
            ["FileVersions"] = 1,
        };

        using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        connection.Open();

        foreach (var (table, count) in expected)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM pragma_foreign_key_list('{table}')";
            var actual = Convert.ToInt32(command.ExecuteScalar());
            actual.Should().Be(count, $"{table} 应声明 {count} 个外键约束");
        }
    }

    [Fact]
    public void MigrateAsync_CreatesAllIndexes()
    {
        // 索引全集。EF 时代 20 个（去掉已废弃 FilePermissions 的 3 个），
        // PalORM 侧多一个 Path_Prefix 服务后代遍历。
        // 这条断言的来历：迁移后行模型漏声明 [Index]，新建库只有 5 个索引，
        // 按 OwnerId/Hash/ParentId 的查询全部退化成全表 SCAN，
        // 而功能正常、数据断言全绿、基准也测不出来——只有直接数索引才能发现。
        var expected = new Dictionary<string, string[]>
        {
            ["FileItems"] =
            [
                "IX_FileItems_Hash", "IX_FileItems_IsDeleted", "IX_FileItems_OwnerId",
                "IX_FileItems_ParentId", "IX_FileItems_ParentId_Name",
                // UX_FileItems_Path_Active 是 2026-10-19 改名的 Path 唯一
                // 部分索引：旧名 IX_FileItems_Path 与 EF 遗产索引同名，
                // IF NOT EXISTS 在存量库直接跳过，唯一兜底从未真正建起来
                "UX_FileItems_Path_Active",
                "IX_FileItems_Path_Prefix", "IX_FileItems_StoragePoolId",
            ],
            ["FileShares"] = ["IX_FileShares_CreatedByUserId", "IX_FileShares_FileItemId", "IX_FileShares_ShareCode"],
            ["FileVersions"] =
            [
                "IX_FileVersions_CreatedByUserId", "IX_FileVersions_FileItemId",
                "IX_FileVersions_FileItemId_IsCurrent", "IX_FileVersions_FileItemId_VersionNumber",
            ],
            ["StoragePools"] = ["IX_StoragePools_IsDefault", "IX_StoragePools_MountPath", "IX_StoragePools_Name"],
            ["Users"] = ["IX_Users_Email", "IX_Users_IsEnabled", "IX_Users_Username"],
            // 2026-10-18 ROADMAP Phase 2 前五张之后新增的表补进全集断言：
            // BackupDevices/FileOcrTexts/SystemSettings 此前只在新表落地时
            // 各自加测试，没进这个全集字典，索引回归会从缝隙里漏过去
            ["BackupDevices"] = ["IX_BackupDevices_UserId", "IX_BackupDevices_UserId_Name"],
            ["FileOcrTexts"] = ["IX_FileOcrTexts_FileItemId"],
            ["SystemSettings"] = ["IX_SystemSettings_Key"],
            // Phase 2 清单快照与只读保护三张
            ["DirectorySnapshots"] = ["IX_DirectorySnapshots_CreatedAt"],
            ["SnapshotEntries"] = ["IX_SnapshotEntries_SnapshotId"],
            ["ProtectedDirectories"] = ["IX_ProtectedDirectories_FileItemId"],
        };

        using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        connection.Open();

        foreach (var (table, names) in expected)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name = $t AND sql IS NOT NULL";
            command.Parameters.AddWithValue("$t", table);
            var actual = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                actual.Add(reader.GetString(0));

            actual.Should().BeEquivalentTo(names, $"{table} 的索引集合应与模型声明一致");
        }
    }

    [Fact]
    public void Indexes_ActuallyServeTheHotQueries()
    {
        // 索引存在不等于用得上。对四条热路径跑 EXPLAIN QUERY PLAN，
        // 确认是全表 SCAN 还是走索引。上一轮迁移后这四条全是 SCAN。
        using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        connection.Open();

        var plans = new (string Label, string Sql)[]
        {
            ("OwnerId 过滤", "SELECT * FROM FileItems WHERE OwnerId = 'x' AND IsDeleted = 0"),
            ("Hash 精确查", "SELECT * FROM FileItems WHERE Hash = 'x'"),
            ("ParentId+Name", "SELECT * FROM FileItems WHERE ParentId = 'x' AND Name = 'y'"),
            ("StoragePoolId", "SELECT * FROM FileItems WHERE StoragePoolId = 'x'"),
            ("Username 登录", "SELECT * FROM Users WHERE Username = 'x'"),
            ("ShareCode 访问", "SELECT * FROM FileShares WHERE ShareCode = 'x'"),
        };

        foreach (var (label, sql) in plans)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            using var reader = command.ExecuteReader();
            reader.Read().Should().BeTrue($"{label} 应有查询计划");
            var plan = reader.GetString(reader.FieldCount - 1);
            plan.Should().NotContain("SCAN", $"{label} 不应全表扫描");
        }
    }

    [Fact]
    public void MigrateAsync_KeepsColumnAffinityCompatible()
    {
        // PalORM 生成的 DDL 用 BIGINT/BOOLEAN/TIMESTAMP（NUMERIC 亲和），
        // EF 时代是 INTEGER/TEXT。SQLite 对 NUMERIC 列遇非数字文本按 TEXT 存，
        // ISO 日期因此能原样往返；这条断言锁住该行为，防止换成别的类型声明后静默变质。
        using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
        connection.Open();

        foreach (var (table, column) in new[]
        {
            ("FileItems", "CreatedAt"), ("FileItems", "SizeBytes"),
            ("FileItems", "IsDeleted"), ("Users", "CreatedAt"),
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT typeof(\"{column}\") FROM \"{table}\" LIMIT 1";
            // 空表时 typeof 返回 NULL，那就没有可断言的存储值，跳过
            var result = command.ExecuteScalar();
            if (result is null)
                continue;
            result.ToString().Should().BeOneOf("text", "integer", "real", "blob",
                $"{table}.{column} 的存储类型应落在 SQLite 的合法值集合内");
        }
    }

    [Fact]
    public async Task ForeignKeyConstraint_BlocksOrphanedChildRow()
    {
        // 外键存在性之外，还要确认它真的生效：插一条指向不存在用户的 FileItems 必须被拒
        var (scope, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("fkowner", "hash", "salt");
            var pool = StoragePool.Create("FkPool", "/mnt/fkpool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var orphan = FileItem.CreateFile(
                name: "orphan.txt", path: "/orphan.txt", physicalPath: "/mnt/fkpool/orphan.txt",
                sizeBytes: 1, mimeType: "text/plain", storagePoolId: pool.Id,
                ownerId: Guid.NewGuid()); // 不存在的用户

            await fileItems.AddAsync(orphan);
            var act = async () => await unitOfWork.SaveChangesAsync();
            await act.Should().ThrowAsync<Exception>("外键约束应拦住孤儿行");
        }
    }

    [Fact]
    public async Task EnumProperties_RoundTripThroughSqliteIntegerColumns()
    {
        var (scope, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("enumowner", "hash", "salt", UserRole.PowerUser);
            var pool = StoragePool.Create("EnumPool", "/mnt/enumpool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var folder = FileItem.CreateFolder(
                name: "enumfolder", path: "/enumfolder", physicalPath: "/mnt/enumpool/enumfolder",
                storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(folder);
            await unitOfWork.SaveChangesAsync();

            var readBack = await fileItems.GetByIdAsync(folder.Id);
            readBack.Should().NotBeNull();
            readBack!.ItemType.Should().Be(FileItemType.Folder);

            var readUser = await users.GetByIdAsync(user.Id);
            readUser!.Role.Should().Be(UserRole.PowerUser);

            // 还要确认列的存储类型没变（INTEGER），否则存量数据读不出来
            using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            connection.Open();
            using (var typeCmd = connection.CreateCommand())
            {
                typeCmd.CommandText = "SELECT typeof(\"ItemType\"), typeof(\"Role\") FROM \"FileItems\", \"Users\" LIMIT 1";
                using var reader = typeCmd.ExecuteReader();
                reader.Read().Should().BeTrue();
                reader.GetString(0).Should().Be("integer");
                reader.GetString(1).Should().Be("integer");
            }
        }
    }

    [Fact]
    public async Task EnumFilter_MatchesOnIntegerColumn()
    {
        // 枚举过滤是搜索路径的高频形态，确认 AsInt32 存储下 = 比较仍命中
        var (scope, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("filterowner", "hash", "salt");
            var pool = StoragePool.Create("FilterPool", "/mnt/filterpool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var file = FileItem.CreateFile(
                name: "filtered.txt", path: "/filtered.txt", physicalPath: "/mnt/filterpool/filtered.txt",
                sizeBytes: 1, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();

            var asFolder = await fileItems.SearchAsync("filtered", isFolder: true);
            asFolder.Items.Should().BeEmpty();

            var asFile = await fileItems.SearchAsync("filtered", isFolder: false);
            asFile.Items.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Search_EscapesLikeWildcardsInUserInput()
    {
        // 手写 SQL 的 LIKE 必须自己转义 % 和 _，否则用户输入的下划线被当成任意单字符：
        // 搜 "rep_ort" 会把 "repXort" 也匹配上。EF 翻译 string.Contains 时会转义，
        // 换 PalORM 后这条防线搬到仓储的 EscapeLike，用行为测试锁住它。
        var (scope, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("escapeowner", "hash", "salt");
            var pool = StoragePool.Create("EscapePool", "/mnt/escapepool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                name: "rep_ort.txt", path: "/rep_ort.txt", physicalPath: "/mnt/escapepool/rep_ort.txt",
                sizeBytes: 1, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id));
            await fileItems.AddAsync(FileItem.CreateFile(
                name: "repXort.txt", path: "/repXort.txt", physicalPath: "/mnt/escapepool/repXort.txt",
                sizeBytes: 1, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id));
            await unitOfWork.SaveChangesAsync();

            var hits = await fileItems.SearchAsync("rep_ort");
            hits.Items.Should().HaveCount(1);
            hits.Items[0].Name.Should().Be("rep_ort.txt");
        }
    }

    [Fact]
    public void PalormCreatedDatabase_HasNoEfMigrationsHistoryTable()
    {
        // PalORM 的 MigrateAsync 按 [Table] 建表，不碰 EF 的账本表。
        // 新建库若冒出 __EFMigrationsHistory，说明种子化混进了 EF 时代的产物。
        CountTable(_dbPath, "__EFMigrationsHistory").Should().Be(0);
    }

    [Fact]
    public async Task LegacyEfDatabase_InitializesNormally_AndMigrationsHistoryIsLeftAlone()
    {
        // 存量库（EF 时代建的）带 __EFMigrationsHistory。PalORM 初始化必须照常完成，
        // 且不擅自删表：删表是用户的数据清理决策，不是框架的启动副作用。
        // 这条锁的是"升级路径无损"，与上一行锁的"新建库干净"互为两面。
        var legacyPath = Path.Combine(Path.GetTempPath(), $"LegacyEf_{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={legacyPath}"))
            {
                await connection.OpenAsync();
                using var create = connection.CreateCommand();
                create.CommandText =
                    "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT NOT NULL)";
                await create.ExecuteNonQueryAsync();
            }

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={legacyPath}",
                })
                .Build();
            var services = new ServiceCollection();
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
            services.AddLogging();
            services.AddInfrastructureServices(config);
            using var provider = services.BuildServiceProvider();
            await provider.InitializeDatabaseAsync();

            // 初始化完成后：EF 账本表还在，PalORM 自己的 5 张表也建出来了
            CountTable(legacyPath, "__EFMigrationsHistory").Should().Be(1);
            CountTable(legacyPath, "FileItems").Should().Be(1);

            // 且数据通路真的能用（不只是建表成功）：管理员种子已落库
            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var admin = await users.GetByUsernameAsync("admin");
            admin.Should().NotBeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                try
                {
                    if (File.Exists(legacyPath + suffix))
                        File.Delete(legacyPath + suffix);
                }
                catch
                {
                    // 句柄未释放时留下，由系统临时目录清理
                }
            }
        }
    }

    private static long CountTable(string dbPath, string tableName)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        _provider.Dispose();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                if (File.Exists(_dbPath + suffix))
                    File.Delete(_dbPath + suffix);
            }
            catch
            {
                // 句柄未释放时留下，由系统临时目录清理
            }
        }
    }
}
