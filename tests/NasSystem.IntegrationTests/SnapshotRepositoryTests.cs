// =============================================================================
// NasSystem.IntegrationTests - SnapshotRepositoryTests.cs
// 清单快照与只读保护的数据层测试（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Common;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 快照仓储与受保护目录仓储的行为测试。diff 的 SQL 对齐逻辑
/// （FULL OUTER JOIN + COALESCE）是纯数据层能力，在这里锁定三态判定，
/// 应用层只做搬运。
/// </summary>
public class SnapshotRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SnapshotRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"SnapshotRepo_{Guid.NewGuid():N}.db");
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

    // 单 scope 返回全部仓储：写入意图与 SaveChanges 必须走同一个
    // IUnitOfWork（分两个 scope 时 Add 登记到 B、Save 调 A，写入静默丢失）
    private (IServiceScope Scope, ISnapshotRepository Snapshots, IProtectedDirectoryRepository Protected,
        IUserRepository Users, IStoragePoolRepository Pools, IFileItemRepository FileItems, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<ISnapshotRepository>(),
            scope.ServiceProvider.GetRequiredService<IProtectedDirectoryRepository>(),
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private static readonly Guid Operator = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Snapshot_CreateEntriesComplete_AndReadBack()
    {
        var (scope, snapshots, _, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var snapshot = DirectorySnapshot.Create("/Docs", Operator, "第一次快照");
            await snapshots.AddAsync(snapshot);

            var entries = new[]
            {
                SnapshotEntry.Create(snapshot.Id, "a.txt", false, 100, "hash-a", DateTime.UtcNow),
                SnapshotEntry.Create(snapshot.Id, "sub", true, 0, null, DateTime.UtcNow),
                SnapshotEntry.Create(snapshot.Id, "sub/b.txt", false, 200, "hash-b", DateTime.UtcNow),
            };
            await snapshots.AddEntriesAsync(entries);
            snapshot.MarkCompleted(fileCount: 2, folderCount: 1, totalSizeBytes: 300);
            snapshots.Update(snapshot);
            await unitOfWork.SaveChangesAsync();

            var readBack = await snapshots.GetByIdAsync(snapshot.Id);
            readBack.Should().NotBeNull();
            readBack!.Status.Should().Be(SnapshotStatus.Completed);
            readBack.FileCount.Should().Be(2);
            readBack.TotalSizeBytes.Should().Be(300);

            var (items, total) = await snapshots.GetEntriesPagedAsync(snapshot.Id, 1, 10);
            total.Should().Be(3);
            items.Should().HaveCount(3);
            // 按 RelativePath 排序：a.txt < sub < sub/b.txt（Ordinal 下 '/' < 字母）
            items[0].RelativePath.Should().Be("a.txt");
        }
    }

    [Fact]
    public async Task Diff_ClassifiesAddedRemovedModifiedAndUnchanged()
    {
        var (scope, snapshots, _, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var baseSnapshot = DirectorySnapshot.Create("/Docs", Operator, "base");
            await snapshots.AddAsync(baseSnapshot);
            await snapshots.AddEntriesAsync(
            [
                SnapshotEntry.Create(baseSnapshot.Id, "keep.txt", false, 10, "h-keep", DateTime.UtcNow),
                SnapshotEntry.Create(baseSnapshot.Id, "del.txt", false, 20, "h-del", DateTime.UtcNow),
                SnapshotEntry.Create(baseSnapshot.Id, "mod.txt", false, 30, "h-mod-old", DateTime.UtcNow),
            ]);
            baseSnapshot.MarkCompleted(3, 0, 60);
            snapshots.Update(baseSnapshot);
            await unitOfWork.SaveChangesAsync();

            var targetSnapshot = DirectorySnapshot.Create("/Docs", Operator, "target");
            await snapshots.AddAsync(targetSnapshot);
            await snapshots.AddEntriesAsync(
            [
                SnapshotEntry.Create(targetSnapshot.Id, "keep.txt", false, 10, "h-keep", DateTime.UtcNow),
                SnapshotEntry.Create(targetSnapshot.Id, "mod.txt", false, 35, "h-mod-new", DateTime.UtcNow),
                SnapshotEntry.Create(targetSnapshot.Id, "new.txt", false, 40, "h-new", DateTime.UtcNow),
            ]);
            targetSnapshot.MarkCompleted(3, 0, 85);
            snapshots.Update(targetSnapshot);
            await unitOfWork.SaveChangesAsync();

            var diff = await snapshots.DiffAsync(baseSnapshot.Id, targetSnapshot.Id, 1, 50);

            diff.TotalCount.Should().Be(4, "keep/del/mod/new 四个相对路径");
            diff.AddedCount.Should().Be(1, "仅新快照有 new.txt");
            diff.RemovedCount.Should().Be(1, "仅旧快照有 del.txt");
            diff.ModifiedCount.Should().Be(1, "mod.txt hash 由 h-mod-old 变 h-mod-new");
            diff.UnchangedCount.Should().Be(1, "keep.txt 两边 hash 与大小都相同");
            (diff.AddedCount + diff.RemovedCount + diff.ModifiedCount + diff.UnchangedCount)
                .Should().Be(diff.TotalCount, "四态计数自洽");

            var mod = diff.Items.Single(i => i.RelativePath == "mod.txt");
            mod.Kind.Should().Be(SnapshotDiffKind.Modified);
            var keep = diff.Items.Single(i => i.RelativePath == "keep.txt");
            keep.Kind.Should().Be(SnapshotDiffKind.Unchanged);
            mod.BaseHash.Should().Be("h-mod-old");
            mod.TargetHash.Should().Be("h-mod-new");

            var del = diff.Items.Single(i => i.RelativePath == "del.txt");
            del.Kind.Should().Be(SnapshotDiffKind.Removed);
        }
    }

    [Fact]
    public async Task Diff_EmptyBothSides_ReturnsZeroCounts()
    {
        var (scope, snapshots, _, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var a = DirectorySnapshot.Create("/A", Operator);
            var b = DirectorySnapshot.Create("/B", Operator);
            await snapshots.AddAsync(a);
            await snapshots.AddAsync(b);
            a.MarkCompleted(0, 0, 0);
            b.MarkCompleted(0, 0, 0);
            snapshots.Update(a);
            snapshots.Update(b);
            await unitOfWork.SaveChangesAsync();

            var diff = await snapshots.DiffAsync(a.Id, b.Id, 1, 50);
            diff.TotalCount.Should().Be(0);
            diff.Items.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesEntriesToo()
    {
        var (scope, snapshots, _, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var snapshot = DirectorySnapshot.Create("/Docs", Operator);
            await snapshots.AddAsync(snapshot);
            await snapshots.AddEntriesAsync(
            [
                SnapshotEntry.Create(snapshot.Id, "x.txt", false, 1, "h", DateTime.UtcNow),
                SnapshotEntry.Create(snapshot.Id, "y.txt", false, 2, "h", DateTime.UtcNow),
            ]);
            await unitOfWork.SaveChangesAsync();
            (await snapshots.CountEntriesAsync(snapshot.Id)).Should().Be(2);

            snapshots.Delete(snapshot);
            await unitOfWork.SaveChangesAsync();

            (await snapshots.GetByIdAsync(snapshot.Id)).Should().BeNull();
            (await snapshots.CountEntriesAsync(snapshot.Id)).Should().Be(0, "删快照头必须连带删条目（无外键级联，仓储显式删）");
        }
    }

    [Fact]
    public async Task GetOldestAsync_ReturnsSnapshotsBeyondKeepCount()
    {
        var (scope, snapshots, _, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            for (var i = 0; i < 4; i++)
            {
                var s = DirectorySnapshot.Create($"/D{i}", Operator);
                await snapshots.AddAsync(s);
                s.MarkCompleted(0, 0, 0);
                snapshots.Update(s);
                // 制造确定的创建序：逐个 SaveChanges，CreatedAt 由实体工厂取 UtcNow
                await unitOfWork.SaveChangesAsync();
                await Task.Delay(15);
            }

            var oldest = await snapshots.GetOldestAsync(keepCount: 2);
            oldest.Should().HaveCount(2, "4 份保留 2 份，最旧的 2 份是淘汰对象");
            oldest[0].RootPath.Should().Be("/D0");
            oldest[1].RootPath.Should().Be("/D1");

            var all = await snapshots.GetOldestAsync(keepCount: 10);
            all.Should().BeEmpty("保留数不小于总数时无淘汰对象");
        }
    }

    [Fact]
    public async Task ProtectedDirectory_MarkAndPrefixJudgment()
    {
        var (scope, _, protectedDirs, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            // 造一个真实目录项（IsPathProtectedAsync 要 JOIN FileItems 取路径）
            var user = User.Create("protector", "hash", "salt");
            var pool = StoragePool.Create("ProtectPool", "/mnt/protect");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            var docs = FileItem.CreateFolder(
                name: "Docs", path: "/Docs", physicalPath: "/mnt/protect/Docs",
                storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(docs);
            await unitOfWork.SaveChangesAsync();

            (await protectedDirs.IsPathProtectedAsync("/Docs")).Should().BeFalse("标记前不受保护");

            await protectedDirs.AddAsync(ProtectedDirectory.Create(docs.Id, user.Id, "核心文档"));
            await unitOfWork.SaveChangesAsync();

            (await protectedDirs.IsPathProtectedAsync("/Docs")).Should().BeTrue("标记目录自身");
            (await protectedDirs.IsPathProtectedAsync("/Docs/a.txt")).Should().BeTrue("子孙文件");
            (await protectedDirs.IsPathProtectedAsync("/Docs/sub/deep/b.txt")).Should().BeTrue("深层子孙");
            (await protectedDirs.IsPathProtectedAsync("/Docs2/x.txt")).Should().BeFalse("兄弟前缀不算（段边界）");
            (await protectedDirs.IsPathProtectedAsync("/Other")).Should().BeFalse("无关路径");

            var existing = await protectedDirs.GetByFileItemIdAsync(docs.Id);
            existing.Should().NotBeNull();
            existing!.Note.Should().Be("核心文档");

            protectedDirs.Delete(existing);
            await unitOfWork.SaveChangesAsync();
            (await protectedDirs.IsPathProtectedAsync("/Docs/a.txt")).Should().BeFalse("取消标记后子孙不再受保护");
        }
    }

    [Fact]
    public async Task ProtectedDirectory_DuplicateMark_IsRejectedByUniqueIndex()
    {
        var (scope, _, protectedDirs, users, pools, fileItems, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("dupmarker", "hash", "salt");
            var pool = StoragePool.Create("DupPool", "/mnt/dup");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            var dir = FileItem.CreateFolder(
                name: "D", path: "/D", physicalPath: "/mnt/dup/D",
                storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(dir);
            await unitOfWork.SaveChangesAsync();

            await protectedDirs.AddAsync(ProtectedDirectory.Create(dir.Id, user.Id));
            await unitOfWork.SaveChangesAsync();

            await protectedDirs.AddAsync(ProtectedDirectory.Create(dir.Id, user.Id));
            var act = async () => await unitOfWork.SaveChangesAsync();
            await act.Should().ThrowAsync<Exception>("FileItemId 唯一索引应拒绝重复标记");
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
