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
        // 每张表的期望外键数，与 EF 时代 schema 逐一对齐
        var expected = new Dictionary<string, int>
        {
            ["FileItems"] = 3,
            ["FileShares"] = 2,
            ["FileVersions"] = 2,
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
            asFolder.Should().BeEmpty();

            var asFile = await fileItems.SearchAsync("filtered", isFolder: false);
            asFile.Should().HaveCount(1);
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
            hits.Should().HaveCount(1);
            hits[0].Name.Should().Be("rep_ort.txt");
        }
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
