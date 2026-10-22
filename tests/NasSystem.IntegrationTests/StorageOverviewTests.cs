// =============================================================================
// NasSystem.IntegrationTests - StorageOverviewTests.cs
// 存储池容量口径的锁定测试（池即文件夹）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Storage;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Services;
using Xunit;

/// <summary>
/// 锁定"以库记账为准"的容量口径（2026-10-17 用户裁决）：池大小 = NAS 管理的
/// 文件数据库记账 SUM（与已用同源同值），可用 = 磁盘物理剩余（无配额时）。
/// </summary>
/// <remarks>
/// 口径变更史：2026-10-15 用户明确"池即文件夹"后从磁盘口径改为文件夹实测
/// （池大小 = 挂载点递归扫描，已用 = 数据库记账，两个来源）；2026-10-17 用户
/// 裁决"以库记账为准"，实测口径废止。物理遍历是唯一随文件数线性增长且同步
/// 阻塞请求线程的操作（十万文件的池要数秒），改为库内 SUM 一条聚合 SQL。
/// 语义变化：外部直接放到盘上、不经本应用的文件不再计入池大小（这样的文件
/// 本来也不在库内，扫描同步会把它们收进来）。可用与健康状态仍取磁盘物理
/// 信息（每次调用一次 syscall，不遍历）。
/// </remarks>
public class StorageOverviewTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _poolRoot;
    private readonly ServiceProvider _provider;

    public StorageOverviewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"StorageOv_{Guid.NewGuid():N}.db");
        _poolRoot = Path.Combine(Path.GetTempPath(), $"StoragePool_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_poolRoot);
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

        // 初始化在无 admin 时会顺带种子化一个默认池。本文件只关心自建池的口径，
        // 种子池的无配额属性会污染聚合断言（HasCapacityLimit 要求全部池都有上限），
        // 且它的挂载点指向测试程序目录，先删掉
        var seedScope = _provider.CreateScope();
        var seedPools = seedScope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var seedUow = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        foreach (var seeded in seedPools.GetAllAsync().GetAwaiter().GetResult())
            seedPools.DeleteAsync(seeded).GetAwaiter().GetResult();
        seedUow.SaveChangesAsync().GetAwaiter().GetResult();
        seedScope.Dispose();
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_poolRoot, recursive: true); }
        catch { /* 临时目录清理失败不影响测试结论 */ }
        try { File.Delete(_dbPath); }
        catch { /* 临时库清理失败不影响测试结论 */ }
    }

    [Fact]
    public async Task GetOverviewAsync_PoolSizeEqualsDatabaseAccounting()
    {
        // 库里记了 3 个文件合计 4122 字节，盘上另有 1 个非 NAS 文件：
        // 池大小按库记账（4122），盘上那个外部文件不计入（2026-10-17 口径）
        await File.WriteAllBytesAsync(Path.Combine(_poolRoot, "external.bin"), new byte[9999]);

        var (scope, users, pools, fileItems, uow) = CreateDataScope();
        using (scope)
        {
            var user = User.Create("sizeowner", "h", "s");
            await users.AddAsync(user);
            var pool = StoragePool.Create("AccountingPool", _poolRoot);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "a.bin", "/a.bin", Path.Combine(_poolRoot, "a.bin"),
                1000, "application/octet-stream", pool.Id, user.Id));
            await fileItems.AddAsync(FileItem.CreateFile(
                "b.bin", "/b.bin", Path.Combine(_poolRoot, "b.bin"),
                2345, "application/octet-stream", pool.Id, user.Id));
            await fileItems.AddAsync(FileItem.CreateFile(
                "c.bin", "/sub/c.bin", Path.Combine(_poolRoot, "sub", "c.bin"),
                777, "application/octet-stream", pool.Id, user.Id));
            await uow.SaveChangesAsync();
        }

        var sut = CreateService();
        // 走强制刷新入口：节流窗口按 CreatedAt 判定，刚建出的池不 force 会被窗口挡掉
        await sut.RefreshCapacityAsync();

        var result = await sut.GetOverviewAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.FolderSizeBytes.Should().Be(4122,
            "池大小应等于库内记账之和，不含盘上非 NAS 文件");
        result.Value.UsedBytes.Should().Be(4122, "已用同源同值");
        result.Value.PoolCount.Should().Be(1);
        // 无容量上限：使用率没有意义，不展示
        result.Value.HasCapacityLimit.Should().BeFalse();
        result.Value.UsagePercentage.Should().Be(0);
        // 可用 = 文件夹的增长空间，即所在磁盘的物理剩余
        result.Value.AvailableBytes.Should().BeGreaterThan(0);
        result.Value.HealthStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetPoolsAsync_FolderSizeAndUsedFromSameAccounting()
    {
        // 文件夹里有 1000 字节的 NAS 记账文件加 5000 字节的非 NAS 文件：
        // 池大小与已用都按库记账（1000），盘上多出来的 5000 两个都不含
        // （2026-10-17 前这里是池大小 6000 扫目录、已用 1000 查库的双来源口径）
        await File.WriteAllBytesAsync(Path.Combine(_poolRoot, "external.bin"), new byte[5000]);

        var (scope, users, pools, fileItems, uow) = CreateDataScope();
        using (scope)
        {
            var user = User.Create("poolowner", "h", "s");
            await users.AddAsync(user);
            var pool = StoragePool.Create("MixedPool", _poolRoot);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "managed.txt", "/managed.txt", Path.Combine(_poolRoot, "managed.txt"),
                1000, "text/plain", pool.Id, user.Id));
            await uow.SaveChangesAsync();
        }

        var sut = CreateService();
        await sut.RefreshCapacityAsync();
        var result = await sut.GetPoolsAsync();

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.FolderSizeBytes.Should().Be(1000, "池大小来自库记账，不含非 NAS 内容");
        dto.UsedBytes.Should().Be(1000, "已用同源同值");
        dto.UsagePercentage.Should().Be(0, "无容量上限的池不计算使用率");
    }

    [Fact]
    public async Task GetOverviewAsync_WithQuota_ComputesUsageAgainstQuota()
    {
        // 设了容量上限的池：使用率 = 已用 / 上限，与文件夹实测大小无关
        var (scope, users, pools, fileItems, uow) = CreateDataScope();
        using (scope)
        {
            var user = User.Create("quotaowner", "h", "s");
            await users.AddAsync(user);
            var pool = StoragePool.Create(
                "QuotaPool", _poolRoot, maxCapacityBytes: 1024L * 1024 * 1024 * 100);
            await pools.AddAsync(pool);
            await uow.SaveChangesAsync();

            await fileItems.AddAsync(FileItem.CreateFile(
                "half.bin", "/half.bin", Path.Combine(_poolRoot, "half.bin"),
                1024L * 1024 * 1024 * 50, "application/octet-stream", pool.Id, user.Id));
            await uow.SaveChangesAsync();
        }

        var sut = CreateService();
        await sut.RefreshCapacityAsync();
        var result = await sut.GetOverviewAsync();

        result.Value.HasCapacityLimit.Should().BeTrue();
        result.Value.UsagePercentage.Should().Be(50);
    }

    private (IServiceScope Scope, IUserRepository Users, IStoragePoolRepository Pools,
        IFileItemRepository FileItems, IUnitOfWork UnitOfWork) CreateDataScope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private StorageService CreateService()
    {
        var scope = _provider.CreateScope();
        return new StorageService(
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IFileSystemService>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            // 磁盘物理剩余探测（F-027 收口后为注入依赖）。本测试的容器只装了
            // Infrastructure（无 AddApplicationServices），直接构造而非取服务
            new NasSystem.Application.Storage.DiskSpace(
                scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IFileSystemService>()),
            // 时间源（CONVENTIONS §5.1 / 审计 F-028）
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());
    }
}
