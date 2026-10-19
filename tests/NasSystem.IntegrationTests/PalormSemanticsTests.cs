// =============================================================================
// NasSystem.IntegrationTests - PalormSemanticsTests.cs
// 锁定 PalORM 迁移后与 EF 时代等价的行为语义
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 锁定 EF 时代有、PalORM 迁移后容易丢的行为。四条都对应已实证的缺陷：
/// 根目录浏览、版本号聚合、时间边界、外键异常转译。
/// </summary>
/// <remarks>
/// 这些缺陷有一个共同点：数据断言与 schema 断言都抓不到。
/// 根目录浏览返回空列表时 HTTP 200，测试只断言"OK 或 404"就静默通过；
/// MaxAsync 用错时返回 1 而不是 3，功能尚未接线所以没有调用方报错；
/// 时间分隔符只在边界日那天露出；外键异常要真的触发一次才看得到类型。
/// 所以每条都直接打在仓储接口上，用真库跑完整读写闭环。
/// </remarks>
public class PalormSemanticsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public PalormSemanticsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"PalormSem_{Guid.NewGuid():N}.db");
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
        IFileItemRepository FileItems, IFileVersionRepository Versions, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileVersionRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public async Task GetChildren_WithNullParent_ReturnsRootLevelItems()
    {
        // 根目录的 parentId 是 null。PalORM 的 FormattableString 格式化与值无关，
        // null 实参只会变成 @pN 占位，而 SQLite 里 `x = NULL` 不匹配任何行。
        // EF 时代 LINQ 由提供方翻译成 IS NULL，无此问题。
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("root", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            // 根级两项：一个文件加一个文件夹
            await fileItems.AddAsync(FileItem.CreateFile(
                "root.txt", "/root.txt", "/mnt/p/root.txt", 1, "text/plain", pool.Id, user.Id, parentId: null));
            await fileItems.AddAsync(FileItem.CreateFolder(
                "sub", "/sub", "/mnt/p/sub", pool.Id, user.Id, parentId: null));
            await uow.SaveChangesAsync();

            var rootChildren = await fileItems.GetChildrenAsync(null, includeDeleted: false);
            rootChildren.Should().HaveCount(2, "根目录应列出 ParentId 为 NULL 的项");
        }
    }

    [Fact]
    public async Task GetChildrenSummaries_WithNullParent_ReturnsRootLevelItems()
    {
        // 浏览投影查询的根目录分支：parentId 为 null 必须生成 ParentId IS NULL。
        // 这是 B 层投影改造后新写的一条 SQL，与 GetChildrenAsync 走的是不同代码路径，
        // 原先的根目录守护测试覆盖不到它，故单独锁一次
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("sumroot", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "root.txt", "/root.txt", "/mnt/p/root.txt", 1, "text/plain", pool.Id, user.Id, parentId: null));
            await fileItems.AddAsync(FileItem.CreateFolder(
                "sub", "/sub", "/mnt/p/sub", pool.Id, user.Id, parentId: null));
            await uow.SaveChangesAsync();

            var rootChildren = await fileItems.GetChildrenSummariesAsync(null, user.Id, includeHidden: false);
            rootChildren.Should().HaveCount(2, "根目录应列出 ParentId 为 NULL 的项");
        }
    }

    [Fact]
    public async Task GetChildrenSummaries_ExcludesHiddenItemsUnlessRequested()
    {
        // 隐藏过滤已从服务层内存 Where 下推到仓储 SQL（GetChildrenSummariesAsync 的
        // IsHidden = 0 条件）。服务层的单测只能验证参数传递，真正的过滤行为在这里
        // 用真库锁住：includeHidden=false 时隐藏项不出现，true 时出现
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("hiddenowner", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "visible.txt", "/visible.txt", "/mnt/p/visible.txt", 1, "text/plain", pool.Id, user.Id));
            await fileItems.AddAsync(FileItem.CreateFile(
                "hidden.txt", "/hidden.txt", "/mnt/p/hidden.txt", 1, "text/plain", pool.Id, user.Id));
            await uow.SaveChangesAsync();

            // 直接改库置隐藏位：FileItem 的创建工厂没有 IsHidden 入口（历史契约），
            // 而这条测试要的正是"库里 IsHidden = 1 的行不被浏览返回"
            var sessions = scope.ServiceProvider.GetRequiredService<NasSystem.Infrastructure.Persistence.PalormSessionHolder>();
            var db = await sessions.GetAsync();
            await db.ExecuteAsync(System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "UPDATE FileItems SET IsHidden = 1 WHERE Path = '/hidden.txt'"));

            var visibleOnly = await fileItems.GetChildrenSummariesAsync(null, user.Id, includeHidden: false);
            visibleOnly.Should().HaveCount(1);
            visibleOnly[0].Name.Should().Be("visible.txt");

            var withHidden = await fileItems.GetChildrenSummariesAsync(null, user.Id, includeHidden: true);
            withHidden.Should().HaveCount(2, "includeHidden=true 时隐藏项应返回");
        }
    }

    [Fact]
    public async Task GetLatestVersionNumber_ReturnsMaxNotBooleanComparison()
    {
        // MaxAsync 的 FormattableString 参数是聚合表达式本身，没有 WHERE 通道。
        // 把 `FileItemId = {id}` 传进去会算出 MAX(FileItemId = @p0)，返回 1 或 0。
        var (scope, users, pools, fileItems, versions, uow) = Scope();
        using (scope)
        {
            var user = User.Create("v", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            var item = FileItem.CreateFile(
                "f.txt", "/f.txt", "/mnt/p/f.txt", 1, "text/plain", pool.Id, user.Id);
            await fileItems.AddAsync(item);
            await uow.SaveChangesAsync();

            for (var v = 1; v <= 3; v++)
                await versions.AddAsync(FileVersion.Create(item.Id, v, $"/mnt/p/v{v}", 100, $"h{v}", user.Id));
            await uow.SaveChangesAsync();

            var latest = await versions.GetLatestVersionNumberAsync(item.Id);
            latest.Should().Be(3, "应返回最大版本号 3，而不是布尔比较的结果");
        }
    }

    [Fact]
    public async Task GetStatistics_WithOwnerId_CountsThatOwnersFiles()
    {
        // ownerId 过滤走 Guid 参数绑定：Microsoft.Data.Sqlite 绑 Guid 参数时输出
        // 大写 D 格式，与写入侧一致。曾把小写 Guid（Guid.ToString() 默认形态）拼进
        // SQL 字面量，SQLite TEXT 大小写敏感，带 owner 的统计恒返回 0
        // （2026-10-15 实测：小写字面量 0 命中，参数化 34 命中）。
        // 首页"文件统计"卡因此全零，与同页"存储概览"卡的已用数字互相矛盾。
        // 上方边界测试只调了无参重载，走的全局分支，所以抓不到这条
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var owner = User.Create("statowner", "h", "s");
            var other = User.Create("statother", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(owner);
            await users.AddAsync(other);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "a.png", "/a.png", "/mnt/p/a.png", 100, "image/png", pool.Id, owner.Id));
            await fileItems.AddAsync(FileItem.CreateFile(
                "b.txt", "/b.txt", "/mnt/p/b.txt", 200, "text/plain", pool.Id, owner.Id));
            await fileItems.AddAsync(FileItem.CreateFolder(
                "d", "/d", "/mnt/p/d", pool.Id, owner.Id));
            // 别人的文件：计数与容量都不该算进来
            await fileItems.AddAsync(FileItem.CreateFile(
                "c.png", "/c.png", "/mnt/p/c.png", 999, "image/png", pool.Id, other.Id));
            await uow.SaveChangesAsync();

            var stats = await fileItems.GetStatisticsAsync(owner.Id);

            stats.TotalFiles.Should().Be(2, "owner 的文件计入，别人的不计");
            stats.TotalFolders.Should().Be(1);
            stats.TotalSizeBytes.Should().Be(300, "只累加 owner 的文件大小");
            stats.ImageCount.Should().Be(1);
            stats.DocumentCount.Should().Be(1);
            stats.RecentFilesCount.Should().Be(2, "刚建的记录都落在最近 7 天窗口内");
        }
    }

    [Fact]
    public async Task GetStatistics_CountsFilesOnBoundaryDay()
    {
        // 时间边界字面量必须用空格分隔：MDS 绑定 DateTime 用的是
        // "yyyy-MM-dd HH:mm:ss.FFFFFFF"，TEXT 二进制比较下 ' ' < 'T'，
        // 写成 T 分隔符会把边界日当天全部记录排除。
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("b", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            // 造一条 CreatedAt 在 7 天前那天、且晚于精确边界时刻的记录。
            // 注意不能直接用 UtcNow.Date.AddDays(-7).AddHours(12)：那是边界日的 12:00，
            // 而 UtcNow.AddDays(-7) 是边界日的当前时刻（含时分秒），12:00 早于它，
            // 记录本就在窗口外，断言会假红。取边界日的 23:00 才稳定落在窗口内
            var boundarySameDay = DateTime.UtcNow.Date.AddDays(-7).AddHours(23);
            var f = FileItem.CreateFile(
                "boundary.txt", "/boundary.txt", "/mnt/p/boundary.txt", 1, "text/plain", pool.Id, user.Id);
            typeof(FileItem).GetProperty("CreatedAt")!.SetValue(f, boundarySameDay);
            await fileItems.AddAsync(f);
            await uow.SaveChangesAsync();

            var stats = await fileItems.GetStatisticsAsync();
            stats.RecentFilesCount.Should().Be(1, "边界日当天中午建的记录应计入最近 7 天");
        }
    }

    [Fact]
    public async Task SaveChanges_TranslatesForeignKeyViolationToDomainException()
    {
        // 外键违反在 PalORM 侧抛裸 SqliteException（IsUniqueViolation 只认 1555/2067）。
        // 不转译会穿透基础设施层，落到 GlobalExceptionHandler 兜底分支变 500；
        // EF 时代主码都是 19，一律转译为领域异常。
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("fk", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            // OwnerId 指向不存在的用户
            await fileItems.AddAsync(FileItem.CreateFile(
                "orphan.txt", "/orphan.txt", "/mnt/p/orphan.txt", 1, "text/plain", pool.Id, Guid.NewGuid()));

            var act = async () => await uow.SaveChangesAsync();
            (await act.Should().ThrowAsync<UniqueConstraintException>())
                .Which.Message.Should().Contain("完整性");
        }
    }

    [Fact]
    public async Task GetChildren_WithSpecificParent_FiltersCorrectly()
    {
        // 对照用例：非 null 的 parentId 仍走等值比较，别被 IS NULL 的改法带坏
        var (scope, users, pools, fileItems, _, uow) = Scope();
        using (scope)
        {
            var user = User.Create("c", "h", "s");
            var pool = StoragePool.Create("P", "/mnt/p");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            var sub = FileItem.CreateFolder("sub", "/sub", "/mnt/p/sub", pool.Id, user.Id, parentId: null);
            await fileItems.AddAsync(sub);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "in.txt", "/sub/in.txt", "/mnt/p/sub/in.txt", 1, "text/plain", pool.Id, user.Id, parentId: sub.Id));
            await uow.SaveChangesAsync();

            var children = await fileItems.GetChildrenAsync(sub.Id, includeDeleted: false);
            children.Should().HaveCount(1);
            children[0].Name.Should().Be("in.txt");
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
