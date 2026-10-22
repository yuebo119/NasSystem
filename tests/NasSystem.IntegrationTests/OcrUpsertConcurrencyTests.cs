// =============================================================================
// NasSystem.IntegrationTests - OcrUpsertConcurrencyTests.cs
// OCR 文本原子 upsert：并发首次识别同一张图不再撞 FileItemId 唯一索引
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
/// 锁定 upsert 的两条保证：同文件第二次写入是覆盖而不是撞唯一索引，
/// 两个作用域并发写同一文件项都成功且只留一行。
/// </summary>
public class OcrUpsertConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public OcrUpsertConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"OcrUpsert_{Guid.NewGuid():N}.db");
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

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
    }

    [Fact]
    public async Task UpsertAsync_SecondWriteSameFile_OverwritesAndKeepsSingleRow()
    {
        var fileId = await SeedFileItemAsync();

        using (var first = _provider.CreateScope())
        {
            var repository = first.ServiceProvider.GetRequiredService<IFileOcrTextRepository>();
            var unitOfWork = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repository.UpsertAsync(FileOcrText.Create(fileId, "第一次识别", "m1", 1));
            await unitOfWork.SaveChangesAsync();
        }

        using (var second = _provider.CreateScope())
        {
            var repository = second.ServiceProvider.GetRequiredService<IFileOcrTextRepository>();
            var unitOfWork = second.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repository.UpsertAsync(FileOcrText.Create(fileId, "第二次识别", "m2", 2));
            await unitOfWork.SaveChangesAsync();
        }

        using var read = _provider.CreateScope();
        var rows = await read.ServiceProvider
            .GetRequiredService<IFileOcrTextRepository>()
            .GetByFileItemIdAsync(fileId);
        rows.Should().NotBeNull();
        rows!.Text.Should().Be("第二次识别");
        rows.Model.Should().Be("m2");
    }

    [Fact]
    public async Task UpsertAsync_TwoScopesConcurrently_BothSucceedWithSingleRow()
    {
        var fileId = await SeedFileItemAsync();

        using var scopeA = _provider.CreateScope();
        using var scopeB = _provider.CreateScope();
        var repositoryA = scopeA.ServiceProvider.GetRequiredService<IFileOcrTextRepository>();
        var repositoryB = scopeB.ServiceProvider.GetRequiredService<IFileOcrTextRepository>();
        var unitOfWorkA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var unitOfWorkB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repositoryA.UpsertAsync(FileOcrText.Create(fileId, "并发A", "m", 1));
        await repositoryB.UpsertAsync(FileOcrText.Create(fileId, "并发B", "m", 1));

        await Task.WhenAll(unitOfWorkA.SaveChangesAsync(), unitOfWorkB.SaveChangesAsync());

        using var read = _provider.CreateScope();
        var row = await read.ServiceProvider
            .GetRequiredService<IFileOcrTextRepository>()
            .GetByFileItemIdAsync(fileId);
        row.Should().NotBeNull();
        row!.Text.Should().BeOneOf("并发A", "并发B");
    }

    /// <summary>造 user + pool + 一个文件项，返回文件项 Id</summary>
    private async Task<Guid> SeedFileItemAsync()
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create($"upsert_{Guid.NewGuid():N}", "hash", "salt");
        var pool = StoragePool.Create($"UpsertPool_{Guid.NewGuid():N}", $"/mnt/{Guid.NewGuid():N}");
        await users.AddAsync(user);
        await pools.AddAsync(pool);
        await unitOfWork.SaveChangesAsync();

        var item = FileItem.CreateFile(
            name: "concurrent.png", path: "/concurrent.png", physicalPath: "/mnt/concurrent.png",
            sizeBytes: 1, mimeType: "image/png", storagePoolId: pool.Id, ownerId: user.Id,
            hash: "h-concurrent");
        await fileItems.AddAsync(item);
        await unitOfWork.SaveChangesAsync();
        return item.Id;
    }
}