// =============================================================================
// NasSystem.IntegrationTests - FileVersionFkMigrationTests.cs
// FileVersions 级联外键移除迁移（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;
using Xunit;

/// <summary>
/// 锁住一次性迁移脚本的三个事实：
/// 一、旧形态（FileItemId 带 ON DELETE CASCADE）被识别并重建，数据不丢；
/// 二、重建后指向 FileItems 的外键没了，指向 Users 的外键保留；
/// 三、无旧外键的库上是 no-op（幂等，重复执行不炸）。
/// </summary>
/// <remarks>
/// 没有这组测试，迁移脚本的执行证据只存在于"用户下次启动时不报错"，
/// 而数据丢失（INSERT...SELECT 漏列、索引没重建）恰恰是不报错的那类。
/// 全程单 scope 单连接：仓储写入与迁移 SQL 必须在同一连接上，
/// 跨 scope 的另一条连接看不到未提交的父行，外键检查会先于迁移炸掉。
/// 裸 SQL 里插值的 Guid 必须大写：PalORM 的 Guid 落库格式是大写 TEXT，
/// SQLite 的 = 比较大小写敏感，小写插值会让外键检查找不到父行（19 号错误）
/// </remarks>
public class FileVersionFkMigrationTests : ApiTestBase
{
    public FileVersionFkMigrationTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task LegacyCascadeForeignKey_Removed_WithDataAndIndexesPreserved()
    {
        using var scope = Factory.Services.CreateScope();
        var db = await scope.ServiceProvider
            .GetRequiredService<PalormSessionHolder>().GetAsync();
        var logger = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileVersionFkMigrationTests>>();
        var users = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IUserRepository>();
        var pools = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IStoragePoolRepository>();
        var fileItems = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Repositories.IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();

        // Arrange：造一个真实 FileItem 作为版本行的父行（迁移里的 DROP TABLE
        // 会对旧表自身外键做隐式删除检查，引用不存在的父行直接 19 号错误；
        // 真实库里级联外键保证父行存在，合成数据必须自己维持这个不变量）
        var allUsers = await users.GetAllAsync();
        var admin = allUsers.First(u => u.Username == "admin");
        var pool = await pools.GetDefaultAsync();
        pool.Should().NotBeNull("种子数据应含默认存储池");

        var file = NasSystem.Domain.Entities.FileItem.CreateFile(
            name: "legacy.txt",
            path: "/legacy.txt",
            physicalPath: Path.Combine(pool!.MountPath, "legacy.txt"),
            sizeBytes: 4096,
            mimeType: "text/plain",
            ownerId: admin.Id,
            storagePoolId: pool.Id,
            parentId: null,
            hash: "legacyhash");
        await fileItems.AddAsync(file);
        await unitOfWork.SaveChangesAsync();

        await RebuildToLegacyShapeAsync(
            db, file.Id.ToString().ToUpperInvariant(), admin.Id.ToString().ToUpperInvariant());

        (await CountFileItemForeignKeysAsync(db))
            .Should().Be(1, "前置条件：表必须是级联外键的旧形态");

        // Act
        await Infrastructure.DependencyInjection.MigrateLegacyFileVersionForeignKeyAsync(db, logger);

        // Assert①：指向 FileItems 的外键已移除，指向 Users 的保留
        (await CountFileItemForeignKeysAsync(db))
            .Should().Be(0, "迁移后不得再有 FileItemId 级联外键");
        var userFkAfter = await db.ScalarAsync<long>(
            FormattableStringFactory.Create(
                """SELECT COUNT(*) FROM pragma_foreign_key_list('FileVersions') WHERE "table" = 'Users'"""),
            false);
        userFkAfter.Should().Be(1, "CreatedByUserId 的 Users 外键必须保留");

        // Assert②：数据原样保留（StoragePath 与 VersionNumber 是迁移正确性的最小证据）
        var row = await db.From<FileVersionRow>()
            .Where($"Id = '11111111-1111-1111-1111-111111111111'")
            .FirstOrDefaultAsync();
        row.Should().NotBeNull("迁移不得丢数据");
        row!.StoragePath.Should().Be("/legacy/snapshot.bin");
        row.VersionNumber.Should().Be(7);

        // Assert③：四个索引随表重建恢复（DROP TABLE 会带走索引，
        // 漏建不会立刻报错，只在唯一约束失效后才暴露）
        var indexCount = await db.ScalarAsync<long>(
            FormattableStringFactory.Create(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND tbl_name = 'FileVersions'"),
            false);
        indexCount.Should().BeGreaterThanOrEqualTo(4, "四个索引必须随重建恢复");

        // Assert④：幂等，再跑一次不炸也不改数据
        await Infrastructure.DependencyInjection.MigrateLegacyFileVersionForeignKeyAsync(db, logger);
        (await CountFileItemForeignKeysAsync(db)).Should().Be(0, "二次执行应为 no-op");
        var rowsAfter = await db.From<FileVersionRow>().ToListAsync();
        rowsAfter.Should().HaveCount(1, "二次执行不得动数据");
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private static async Task<long> CountFileItemForeignKeysAsync(DataSession<SqliteProvider> db) =>
        await db.ScalarAsync<long>(
            FormattableStringFactory.Create(
                """SELECT COUNT(*) FROM pragma_foreign_key_list('FileVersions') WHERE "table" = 'FileItems'"""),
            false);

    /// <summary>
    /// 把 FileVersions 重建为迁移前形态：带 FileItemId 级联外键，
    /// 并保留一行数据（模拟存量库升级现场）。父行由调用方保证真实存在，
    /// 传入的 Id 必须是大写形式（库中存储格式）
    /// </summary>
    private static async Task RebuildToLegacyShapeAsync(
        DataSession<SqliteProvider> db, string fileItemId, string adminId)
    {
        await db.ExecuteAsync(FormattableStringFactory.Create(
            $"""
            DROP TABLE IF EXISTS FileVersions;
            CREATE TABLE FileVersions (
                Id TEXT PRIMARY KEY NOT NULL,
                FileItemId TEXT NOT NULL,
                VersionNumber INTEGER NOT NULL,
                StoragePath TEXT NOT NULL,
                FileSize BIGINT NOT NULL,
                FileHash TEXT NOT NULL,
                Comment TEXT,
                IsCurrent BOOLEAN NOT NULL,
                CreatedByUserId TEXT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL,
                UpdatedAt TIMESTAMP,
                FOREIGN KEY (FileItemId) REFERENCES FileItems (Id) ON DELETE CASCADE,
                FOREIGN KEY (CreatedByUserId) REFERENCES Users (Id) ON DELETE RESTRICT
            );
            """));
        await db.ExecuteAsync(FormattableStringFactory.Create(
            $"""
            INSERT INTO FileVersions
                (Id, FileItemId, VersionNumber, StoragePath, FileSize, FileHash, Comment, IsCurrent, CreatedByUserId, CreatedAt, UpdatedAt)
            VALUES
                ('11111111-1111-1111-1111-111111111111', '{fileItemId}', 7, '/legacy/snapshot.bin', 4096, 'legacyhash', NULL, 0, '{adminId}', '2026-10-01 00:00:00', NULL);
            """));
    }
}
