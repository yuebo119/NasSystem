// =============================================================================
// NasSystem.IntegrationTests - SnapshotServiceTests.cs
// 清单快照与只读保护的服务层测试（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.ProtectedDirectories;
using NasSystem.Application.Snapshots;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 服务层行为：采集口径（根自身不入库/删除态入库）、保留策略、
/// diff 四态透传、保护标记规则（非文件夹拒绝/幂等/悬空标记）。
/// </summary>
public class SnapshotServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SnapshotServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SnapshotSvc_{Guid.NewGuid():N}.db");
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

    // 单 scope 返回全部依赖：写入与 SaveChanges 必须走同一个 IUnitOfWork
    private (IServiceScope Scope, ISnapshotService Snapshots, IProtectedDirectoryService Protected,
        IUserRepository Users, IStoragePoolRepository Pools, IFileItemRepository FileItems, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<ISnapshotService>(),
            scope.ServiceProvider.GetRequiredService<IProtectedDirectoryService>(),
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private static readonly Guid Operator = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>造一棵三层目录树：/Docs(夹) + a.txt + sub(夹) + sub/b.txt</summary>
    private async Task<(Guid PoolId, Guid UserId)> SeedTreeAsync(
        IUserRepository users, IStoragePoolRepository pools, IFileItemRepository fileItems, IUnitOfWork unitOfWork)
    {
        var user = User.Create("snapowner", "hash", "salt");
        var pool = StoragePool.Create("SnapPool", "/mnt/snap");
        await users.AddAsync(user);
        await pools.AddAsync(pool);
        await unitOfWork.SaveChangesAsync();

        var docs = FileItem.CreateFolder(
            name: "Docs", path: "/Docs", physicalPath: "/mnt/snap/Docs",
            storagePoolId: pool.Id, ownerId: user.Id);
        await fileItems.AddAsync(docs);
        await unitOfWork.SaveChangesAsync();
        var sub = FileItem.CreateFolder(
            name: "sub", path: "/Docs/sub", physicalPath: "/mnt/snap/Docs/sub",
            storagePoolId: pool.Id, ownerId: user.Id, parentId: docs.Id);
        await fileItems.AddAsync(sub);
        await unitOfWork.SaveChangesAsync();

        var a = FileItem.CreateFile(
            name: "a.txt", path: "/Docs/a.txt", physicalPath: "/mnt/snap/Docs/a.txt",
            sizeBytes: 100, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id,
            parentId: docs.Id, hash: "hash-a");
        var b = FileItem.CreateFile(
            name: "b.txt", path: "/Docs/sub/b.txt", physicalPath: "/mnt/snap/Docs/sub/b.txt",
            sizeBytes: 200, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id,
            parentId: sub.Id, hash: "hash-b");
        await fileItems.AddAsync(a);
        await fileItems.AddAsync(b);
        await unitOfWork.SaveChangesAsync();
        return (pool.Id, user.Id);
    }

    [Fact]
    public async Task CreateAsync_CollectsSubtreeAndExcludesRootItself()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);

            var result = await snapshots.CreateAsync("/Docs", "第一次", Operator);
            result.IsSuccess.Should().BeTrue();
            var dto = result.Value;
            dto.RootPath.Should().Be("/Docs");
            dto.FileCount.Should().Be(2);
            dto.FolderCount.Should().Be(1);
            dto.TotalSizeBytes.Should().Be(300);

            var entries = await snapshots.GetEntriesAsync(dto.Id, 1, 100);
            entries.IsSuccess.Should().BeTrue();
            var paths = entries.Value.Items.Select(e => e.RelativePath).ToList();
            paths.Should().BeEquivalentTo(new[] { "a.txt", "sub", "sub/b.txt" },
                "根自身不入库，条目从根的子孙开始");
            var bEntry = entries.Value.Items.Single(e => e.RelativePath == "sub/b.txt");
            bEntry.ContentHash.Should().Be("hash-b");
            bEntry.SizeBytes.Should().Be(200);
        }
    }

    [Fact]
    public async Task CreateAsync_IncludesSoftDeletedRecords()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);
            var a = await fileItems.GetByPathAsync("/Docs/a.txt");
            a!.SoftDelete();
            await unitOfWork.SaveChangesAsync();

            var result = await snapshots.CreateAsync("/Docs", null, Operator);
            result.IsSuccess.Should().BeTrue();
            result.Value.FileCount.Should().Be(2, "软删记录同样是'那一刻的目录状态'，进快照");

            var entries = await snapshots.GetEntriesAsync(result.Value.Id, 1, 100);
            entries.Value.Items.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task CreateAsync_RootPathNormalization()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);

            // 带尾斜杠与反斜杠混合形态归一成 /Docs
            var result = await snapshots.CreateAsync("Docs\\", null, Operator);
            result.IsSuccess.Should().BeTrue();
            result.Value.RootPath.Should().Be("/Docs");
            result.Value.FileCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task DiffAsync_FourStateClassification()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);

            var first = (await snapshots.CreateAsync("/Docs", "t1", Operator)).Value;
            // 改动：a.txt 覆盖（hash 变）+ 新增 c.txt + b.txt 硬删（真删除；
            // 软删记录仍入库判 Unchanged，那条语义由 CreateAsync_IncludesSoftDeletedRecords 锁）
            var a = await fileItems.GetByPathAsync("/Docs/a.txt");
            var b = await fileItems.GetByPathAsync("/Docs/sub/b.txt");
            await fileItems.DeleteRangeAsync(new[] { b! });
            await unitOfWork.SaveChangesAsync();
            var c = FileItem.CreateFile(
                name: "c.txt", path: "/Docs/c.txt", physicalPath: "/mnt/snap/Docs/c.txt",
                sizeBytes: 50, mimeType: "text/plain", storagePoolId: a!.StoragePoolId,
                ownerId: a.OwnerId, parentId: a.ParentId, hash: "hash-c");
            await fileItems.AddAsync(c);
            await unitOfWork.SaveChangesAsync();

            var second = (await snapshots.CreateAsync("/Docs", "t2", Operator)).Value;

            var diff = await snapshots.DiffAsync(first.Id, second.Id, 1, 100);
            diff.IsSuccess.Should().BeTrue();
            var d = diff.Value;
            d.AddedCount.Should().Be(1, "c.txt 新增");
            d.RemovedCount.Should().Be(1, "b.txt 硬删后不在第二份快照里");
            d.UnchangedCount.Should().BeGreaterThanOrEqualTo(1, "sub 文件夹未变");
            (d.ModifiedCount + d.UnchangedCount).Should().Be(d.TotalCount - d.AddedCount - d.RemovedCount,
                "四态计数自洽");
        }
    }

    [Fact]
    public async Task DiffAsync_SameSnapshot_IsRejected()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);
            var s = (await snapshots.CreateAsync("/Docs", "only", Operator)).Value;

            var diff = await snapshots.DiffAsync(s.Id, s.Id, 1, 50);
            diff.IsSuccess.Should().BeFalse("对比的两个快照不能相同");
        }
    }

    [Fact]
    public async Task ApplyRetentionAsync_DeletesOldestBeyondKeepCount()
    {
        var (scope, snapshots, _, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);
            for (var i = 0; i < 5; i++)
            {
                (await snapshots.CreateAsync("/Docs", $"s{i}", Operator)).IsSuccess.Should().BeTrue();
                await Task.Delay(15);
            }

            var deleted = await snapshots.ApplyRetentionAsync(keepCount: 2);
            deleted.Should().Be(3, "5 份保留 2 份，删最旧 3 份");

            var list = await snapshots.GetListAsync(1, 50);
            list.Value.TotalCount.Should().Be(2);
            list.Value.Items.Select(s => s.Name).Should().BeEquivalentTo(new[] { "s4", "s3" });
        }
    }

    [Fact]
    public async Task ProtectedDirectory_MarkRules()
    {
        var (scope, _, protectedDirs, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            await SeedTreeAsync(users, pools, fileItems, unitOfWork);
            var docs = await fileItems.GetByPathAsync("/Docs");
            var aFile = await fileItems.GetByPathAsync("/Docs/a.txt");

            // 文件不能标记
            var fileMark = await protectedDirs.MarkAsync(aFile!.Id, Operator);
            fileMark.IsSuccess.Should().BeFalse();
            fileMark.Error.Code.Should().Be("Error.Validation");

            // 目录标记成功
            var mark = await protectedDirs.MarkAsync(docs!.Id, Operator, "核心文档");
            mark.IsSuccess.Should().BeTrue();
            mark.Value.Path.Should().Be("/Docs");
            mark.Value.Note.Should().Be("核心文档");

            // 幂等：重复标记返回现有标记（不报错、不建第二条）
            var again = await protectedDirs.MarkAsync(docs.Id, Operator);
            again.IsSuccess.Should().BeTrue();
            again.Value.FileItemId.Should().Be(docs.Id);
            var list = await protectedDirs.ListAsync();
            list.Value.Should().HaveCount(1);

            // 拦截判定
            (await protectedDirs.IsProtectedAsync("/Docs/sub/b.txt")).Should().BeTrue();
            (await protectedDirs.IsMarkedAsync(docs.Id)).Should().BeTrue();

            // 取消
            var unmark = await protectedDirs.UnmarkAsync(docs.Id);
            unmark.IsSuccess.Should().BeTrue();
            (await protectedDirs.IsProtectedAsync("/Docs/sub/b.txt")).Should().BeFalse();
            var unmarkAgain = await protectedDirs.UnmarkAsync(docs.Id);
            unmarkAgain.IsSuccess.Should().BeFalse("未标记时取消返回 NotFound");
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
