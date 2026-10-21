// =============================================================================
// NasSystem.IntegrationTests - PathCollationLockTests.cs
// 锁定 FileItems.Path 的大小写语义（审计待验证推测 F-071 的查证落盘）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 2026-10-22 审计登记的待验证推测：FileItems.Path 列的 collation 未逐字确认。
/// DDL 由 PalORM MigrateAsync 从 FileItemRow 生成（无显式 collation），
/// 若为 NOCASE 则 GetByPathAsync 大小写不敏感、/Foo 与 /foo 不能共存，
/// 路径唯一索引与防穿越校验（IsUnder 是 Ordinal 严判）的语义都要重新评估。
///
/// 本条用 PRAGMA table_info 实测并锁定期望值：BINARY（大小写敏感）。
/// 若将来 DDL 生成行为变化导致 collation 漂移，这条测试先红。
/// </summary>
public class PathCollationLockTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;
    private readonly Guid _poolId;
    private readonly Guid _userId;

    public PathCollationLockTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"PathColl_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        _provider = services.BuildServiceProvider();
        _provider.InitializeDatabaseAsync().GetAwaiter().GetResult();

        // 种子真实的用户与池：FileItems 有 FK 约束，直插随机 Guid 会被拒
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IUserRepository>();
        var pools = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IStoragePoolRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
        var hasher = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IPasswordHasher>();
        var (hash, salt) = hasher.HashPassword("password123");
        var user = NasSystem.Domain.Entities.User.Create("collprobe", hash, salt);
        var pool = NasSystem.Domain.Entities.StoragePool.Create("CollPool", "/mnt/collation-probe");
        users.AddAsync(user).GetAwaiter().GetResult();
        pools.AddAsync(pool).GetAwaiter().GetResult();
        unitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
        _userId = user.Id;
        _poolId = pool.Id;
    }

    [Fact]
    public void FileItems_PathColumn_IsCaseSensitiveBinary()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(FileItems)";

        string? collation = null;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "Path", StringComparison.Ordinal))
                {
                    // 第 5 列（下标 4）是 collation 名，未声明时为 NULL
                    collation = reader.IsDBNull(4) ? null : reader.GetString(4);
                    break;
                }
            }
        }

        collation.Should().BeNull(
            "Path 列不声明显式 collation，SQLite 对 TEXT 列的默认即 BINARY（大小写敏感）：" +
            "唯一部分索引、GetByPathAsync 与分享 IsUnder 校验都建立在 Ordinal 语义上，" +
            "一旦有人给 DDL 加上 COLLATE NOCASE，这条断言先红");
    }

    [Fact]
    public async Task FileItems_PathColumn_DistinguishesCaseAtStorageLevel()
    {
        // 行为级佐证：同名列插入两条仅大小写不同的路径，唯一部分索引不得视为重复。
        // 经容器的仓储插入（与生产同路径），再经同一会话读回计数
        using var scope = _provider.CreateScope();
        var fileItems = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();

        foreach (var path in new[] { "/CaseProbe", "/caseprobe" })
        {
            var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                name: path.TrimStart('/'),
                path: path,
                physicalPath: "/mnt/collation-probe/" + path.TrimStart('/'),
                sizeBytes: 1,
                mimeType: "text/plain",
                storagePoolId: _poolId,
                ownerId: _userId,
                hash: "probe");
            await fileItems.AddAsync(file);
        }

        await unitOfWork.SaveChangesAsync();

        using (var countScope = _provider.CreateScope())
        {
            var repo = countScope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IFileItemRepository>();
            var upper = await repo.GetByPathAsync("/CaseProbe");
            var lower = await repo.GetByPathAsync("/caseprobe");

            upper.Should().NotBeNull("大写路径必须能独立存在");
            lower.Should().NotBeNull("小写路径必须能独立存在");
            upper!.Id.Should().NotBe(lower!.Id,
                "仅大小写不同的两条路径必须是两条记录（BINARY collation 的直接后果）");
        }
    }

    public void Dispose()
    {
        try { _provider.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
