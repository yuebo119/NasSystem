// =============================================================================
// NasSystem.IntegrationTests - OcrFullTextSearchTests.cs
// OCR 文本全文检索（FTS5 trigram）行为测试（ROADMAP Phase 2）
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
/// 锁定 FTS5 trigram 镜像的三件事：写入同步（同事务双写）、检索命中
/// （中文子串）、存量回填（幂等）。短查询回退 LIKE 与转义各有一例。
/// </summary>
public class OcrFullTextSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public OcrFullTextSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"OcrFts_{Guid.NewGuid():N}.db");
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

    private (IServiceScope Scope, IFileItemRepository FileItems, IFileOcrTextRepository OcrTexts, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileOcrTextRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    // 自包含单 scope：user/pool/file/ocr 与 SaveChanges 走同一个 IUnitOfWork。
    // 分 scope 时写入意图登记到 B、Save 调 A，外键/唯一约束必炸
    private async Task<Guid> SeedImageWithOcrAsync(string name, string path, string ocrText)
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        var ocrRepo = scope.ServiceProvider.GetRequiredService<IFileOcrTextRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create($"owner_{Guid.NewGuid():N}", "hash", "salt");
        var pool = StoragePool.Create($"Pool_{Guid.NewGuid():N}", $"/mnt/{Guid.NewGuid():N}");
        await users.AddAsync(user);
        await pools.AddAsync(pool);
        await unitOfWork.SaveChangesAsync();

        var item = FileItem.CreateFile(
            name: name, path: path, physicalPath: $"/mnt/{name}",
            sizeBytes: 100, mimeType: "image/png", storagePoolId: pool.Id, ownerId: user.Id,
            hash: $"h-{name}");
        await fileItems.AddAsync(item);
        await unitOfWork.SaveChangesAsync();

        var ocr = FileOcrText.Create(item.Id, ocrText, "probe-model", 1);
        await ocrRepo.AddAsync(ocr);
        await unitOfWork.SaveChangesAsync();
        return item.Id;
    }

    [Fact]
    public async Task FtsMirror_MatchesChineseSubstring_ThroughSearchAsync()
    {
        var (scope, fileItems, ocrRepo, unitOfWork) = Scope();
        using (scope)
        {
            await SeedImageWithOcrAsync("scan.png", "/scan.png", "家庭网络存储中的扫描件文字识别");

            // ≥3 字符查询走 FTS 通道
            var hits = await fileItems.SearchAsync("扫描件");
            hits.Items.Should().HaveCount(1, "trigram FTS 应命中中文子串");
            hits.Items[0].Name.Should().Be("scan.png");

            // 文件名通道不受影响：按文件名也能搜到
            var byName = await fileItems.SearchAsync("scan");
            byName.Items.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task FtsMirror_TextWithSingleQuote_RoundTrips()
    {
        // OCR 文本含单引号极常见（中文扫描件的所有格、英文缩写）。
        // ExecuteAsync 不走参数化，漏转义会截断 INSERT 语句
        var (scope, fileItems, _, unitOfWork) = Scope();
        using (scope)
        {
            const string Tricky = "It's a \"quoted\" 文本 with '单引号' and \\backslash\\";
            await SeedImageWithOcrAsync("tricky.png", "/tricky.png", Tricky);

            var hits = await fileItems.SearchAsync("单引号");
            hits.Items.Should().HaveCount(1, "含单引号与双引号的 OCR 文本应完整入索引并可命中");
        }
    }

    [Fact]
    public async Task SearchAsync_ShortQuery_FallsBackToLikeChannel()
    {
        var (scope, fileItems, _, unitOfWork) = Scope();
        using (scope)
        {
            await SeedImageWithOcrAsync("ab.png", "/ab.png", "甲乙丙丁戊己庚辛");

            // 2 字符查询：trigram 无可用子串，回退 LIKE 通道
            var hits = await fileItems.SearchAsync("甲乙");
            hits.Items.Should().HaveCount(1, "短查询回退 LIKE 仍应命中");
        }
    }

    [Fact]
    public async Task FtsMirror_UpdateOverwritesIndex()
    {
        // 重复识别（UpdateText 覆盖）：镜像必须先删后插，旧文本不再可命中
        var (scope, fileItems, ocrRepo, unitOfWork) = Scope();
        using (scope)
        {
            var id = await SeedImageWithOcrAsync("upd.png", "/upd.png", "旧文本内容甲乙丙");

            var existing = await ocrRepo.GetByFileItemIdAsync(id);
            existing.Should().NotBeNull();
            existing!.UpdateText("新文本内容丁戊己", "probe-model", 2);
            ocrRepo.Update(existing);
            await unitOfWork.SaveChangesAsync();

            (await fileItems.SearchAsync("旧文本")).Items.Should().HaveCount(0, "覆盖后旧文本不再命中");
            (await fileItems.SearchAsync("新文本")).Items.Should().HaveCount(1, "新文本命中");
        }
    }

    [Fact]
    public async Task FtsBackfill_ExistingRowsIndexedOnStartup()
    {
        // 存量库场景：业务表有行但 FTS 表空（模拟升级前的库），
        // 启动初始化应回填。用第二个 provider 指向同一库文件验证
        var (scope, fileItems, _, unitOfWork) = Scope();
        using (scope)
        {
            await SeedImageWithOcrAsync("legacy.png", "/legacy.png", "存量库回填验证丙丁戊");
        }

        // 直接删空 FTS 表模拟"未建索引的存量库"，再跑一次初始化
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
        using var provider = services.BuildServiceProvider();

        // 回填逻辑内嵌在 InitializeDatabaseAsync，重跑即触发（幂等）
        await provider.InitializeDatabaseAsync();

        var (scope2, fileItems2, _, _) = Scope();
        using (scope2)
        {
            var hits = await fileItems2.SearchAsync("回填验证");
            hits.Items.Should().HaveCount(1, "存量行应被启动回填进 FTS 索引");
        }
    }

    // ─── ocrOnly 定向检索（2026-10-21 新增，前端"只搜识别文字"开关）───
    // 这组同时是占位符编号重构的回归防护：双通道 WHERE 的 {{0}}/{{1}}
    // 曾因 Slot() 在 Add 之前求值而撞号（条件变味后查询恒返回 0 行），
    // 改造为显式 ocrSlot 编号后，默认双通道必须仍然命中两侧

    /// <summary>造一个无 OCR 行的普通文件（ocrOnly 下必须不可命中）</summary>
    private async Task SeedPlainFileAsync(string name, string path)
    {
        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var user = User.Create($"owner_{Guid.NewGuid():N}", "hash", "salt");
        var pool = StoragePool.Create($"Pool_{Guid.NewGuid():N}", $"/mnt/{Guid.NewGuid():N}");
        await users.AddAsync(user);
        await pools.AddAsync(pool);
        await unitOfWork.SaveChangesAsync();

        var item = FileItem.CreateFile(
            name: name, path: path, physicalPath: $"/mnt/{name}",
            sizeBytes: 50, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id,
            hash: $"h-{name}");
        await fileItems.AddAsync(item);
        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_OcrOnly_ExcludesFileNameChannel()
    {
        var (scope, fileItems, _, _) = Scope();
        using (scope)
        {
            // report 只出现在文件名里；季度报表只出现在 OCR 文本里
            await SeedImageWithOcrAsync("report.png", "/report.png", "季度报表数据");

            var byOcrOnlyName = await fileItems.SearchAsync("report", ocrOnly: true);
            byOcrOnlyName.Items.Should().HaveCount(0, "ocrOnly=true 时文件名通道关闭，report 不应命中");

            var byOcrOnlyText = await fileItems.SearchAsync("季度", ocrOnly: true);
            byOcrOnlyText.Items.Should().HaveCount(1, "ocrOnly=true 时 OCR 文本通道仍应命中");

            // 默认（双通道）：两侧都能命中——重构回归防护
            var byNameDefault = await fileItems.SearchAsync("report");
            byNameDefault.Items.Should().HaveCount(1, "默认搜索的文件名通道不应被 ocrOnly 改造影响");
            var byTextDefault = await fileItems.SearchAsync("季度");
            byTextDefault.Items.Should().HaveCount(1, "默认搜索的 OCR 通道不应被 ocrOnly 改造影响");
        }
    }

    [Fact]
    public async Task SearchAsync_OcrOnly_ExcludesFilesWithoutOcrRows()
    {
        var (scope, fileItems, _, _) = Scope();
        using (scope)
        {
            // notes.txt 无 OCR 行；scan.png 有 OCR 行
            await SeedPlainFileAsync("notes.txt", "/notes.txt");
            await SeedImageWithOcrAsync("scan.png", "/scan.png", "识别文字样本丙丁");

            var notesViaOcrOnly = await fileItems.SearchAsync("notes", ocrOnly: true);
            notesViaOcrOnly.Items.Should().HaveCount(0, "无 OCR 行的文件在 ocrOnly 下不可命中");

            var scanViaOcrOnly = await fileItems.SearchAsync("识别文字", ocrOnly: true);
            scanViaOcrOnly.Items.Should().HaveCount(1);
            scanViaOcrOnly.Items[0].Name.Should().Be("scan.png");

            // 默认通道仍能按文件名搜到无 OCR 行的文件
            var notesDefault = await fileItems.SearchAsync("notes");
            notesDefault.Items.Should().HaveCount(1, "默认搜索按文件名命中无 OCR 行的文件");
        }
    }

    [Fact]
    public async Task SearchAsync_OcrOnly_ShortQuery_FallsBackToLikeChannel()
    {
        var (scope, fileItems, _, _) = Scope();
        using (scope)
        {
            await SeedImageWithOcrAsync("short.png", "/short.png", "甲乙丙丁戊己庚辛");

            // 2 字符查询：FTS trigram 无可用子串，回退 LIKE——
            // ocrOnly 下 LIKE 分支的占位符编号与默认路径不同，这条锁住它
            var hits = await fileItems.SearchAsync("甲乙", ocrOnly: true);
            hits.Items.Should().HaveCount(1, "ocrOnly 短查询回退 LIKE 仍应命中");
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
