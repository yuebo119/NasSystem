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
/// 锁定"池即文件夹"的容量口径：池大小 = 挂载点文件夹的实测大小（递归扫描），
/// 已用 = NAS 管理的文件（数据库记账），可用 = 磁盘物理剩余（无配额时）。
/// </summary>
/// <remarks>
/// 2026-10-15 用户明确该模型后从磁盘口径改过来。改之前总容量取磁盘总大小、
/// 可用取盘总减 NAS 记账，首页显示可用 953 GB 而盘上实际只剩 142 GB。
/// 两个数字来源不同是该模型的核心：文件夹里可以有非 NAS 的文件，
/// 扫目录与查数据库必须分开，混用一个数就丢了"池大小"与"NAS 占用"的差别
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
    public async Task GetOverviewAsync_PoolSizeEqualsFolderMeasuredSize()
    {
        // 已知内容的文件夹：根 2 个文件加子目录 1 个文件，合计 4122 字节
        await File.WriteAllBytesAsync(Path.Combine(_poolRoot, "a.bin"), new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(_poolRoot, "b.bin"), new byte[2345]);
        var sub = Path.Combine(_poolRoot, "sub");
        Directory.CreateDirectory(sub);
        await File.WriteAllBytesAsync(Path.Combine(sub, "c.bin"), new byte[777]);
        const long expectedFolderSize = 1000 + 2345 + 777;

        var (scope, _, pools, _, uow) = CreateDataScope();
        using (scope)
        {
            await pools.AddAsync(StoragePool.Create("FolderPool", _poolRoot));
            await uow.SaveChangesAsync();
        }

        var sut = CreateService();
        // 走强制刷新入口：节流窗口按 CreatedAt 判定，刚建出的池不 force 会被窗口挡掉
        await sut.RefreshCapacityAsync();

        var result = await sut.GetOverviewAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.FolderSizeBytes.Should().Be(expectedFolderSize,
            "池大小应等于文件夹实测大小（含子目录），不是所在磁盘的容量");
        result.Value.PoolCount.Should().Be(1);
        // 无容量上限：使用率没有意义，不展示
        result.Value.HasCapacityLimit.Should().BeFalse();
        result.Value.UsagePercentage.Should().Be(0);
        // 可用 = 文件夹的增长空间，即所在磁盘的物理剩余
        result.Value.AvailableBytes.Should().BeGreaterThan(0);
        result.Value.HealthStatus.Should().Be("Healthy");
    }

    [Fact]
    public async Task GetPoolsAsync_FolderSizeFromScan_UsedFromDatabase()
    {
        // 文件夹里有 1000 字节，数据库只记了 400 字节的 NAS 文件：
        // 池大小按文件夹实测（1000），已用按数据库记账（400），两个来源不混。
        // 文件夹里多出来的是非 NAS 内容，恰恰是"池大小"与"NAS 占用"的差别所在
        await File.WriteAllBytesAsync(Path.Combine(_poolRoot, "x.bin"), new byte[1000]);

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
                400, "text/plain", pool.Id, user.Id));
            await uow.SaveChangesAsync();
        }

        var sut = CreateService();
        await sut.RefreshCapacityAsync();
        var result = await sut.GetPoolsAsync();

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.FolderSizeBytes.Should().Be(1000, "池大小来自扫目录，包含非 NAS 内容");
        dto.UsedBytes.Should().Be(400, "已用来自数据库记账，只算 NAS 管理的文件");
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
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>());
    }
}
