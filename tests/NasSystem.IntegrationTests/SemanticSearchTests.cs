// =============================================================================
// NasSystem.IntegrationTests - SemanticSearchTests.cs
// 语义搜图的诚实降级与 vec0 KNN 行为测试（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NasSystem.Application.Configuration;
using NasSystem.Application.Semantic;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Semantic;
using NasSystem.Shared;
using Xunit;

/// <summary>
/// 两个层面：一、模型未就位或总开关关闭时全部能力诚实降级（状态可查、
/// 搜索明确失败），不返回空列表假装搜过——这是"不做假 UI"的测试形态；
/// 二、vec0 存储的 KNN 排序（确定性向量，不依赖 606MB 真模型，
/// CLIP 推理质量由部署环境人工验收）。
/// </summary>
public class SemanticSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public SemanticSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"Semantic_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task ModelNotReady_StatusReportsFalseAndSearchFailsLoudly()
    {
        // 指向一个空目录：模型四件套皆无
        var options = new SemanticOptions
        {
            ModelDirectory = Path.Combine(Path.GetTempPath(), $"empty-model-{Guid.NewGuid():N}"),
        };
        var store = CreateStore();
        var service = new SemanticSearchService(
            store,
            new ClipEngineHolder(NullLogger<ClipEngineHolder>.Instance),
            new ClipModelDownloader(
                new HttpClient { BaseAddress = new Uri("http://localhost") },
                NullLogger<ClipModelDownloader>.Instance),
            options,
            _provider.GetRequiredService<NasSystem.Domain.Repositories.IFileItemRepository>(),
            ConfigWithSemanticEnabled(true),
            NullLogger<SemanticSearchService>.Instance);

        var status = await service.GetStatusAsync();
        status.Ready.Should().BeFalse("空目录下模型不可能就位");
        status.Files.Should().HaveCount(4, "四件套逐项报告");

        var search = await service.SearchAsync("猫");
        search.IsFailure.Should().BeTrue("模型未就位必须明确失败");
        search.Error.Code.Should().Be("Semantic.ModelNotReady",
            "错误码让前端区分'未就位'与'搜过了没命中'");
    }

    [Fact]
    public async Task SemanticDisabled_SearchFailsLoudly()
    {
        // 总开关关闭：即使模型就位也不搜（descriptor 的承诺语义）。
        // 用空目录让模型检查同样不通过，但 Disabled 检查在前——
        // 这条锁的正是"先判开关"的顺序
        var options = new SemanticOptions
        {
            ModelDirectory = Path.Combine(Path.GetTempPath(), $"empty-model-{Guid.NewGuid():N}"),
        };
        var service = new SemanticSearchService(
            CreateStore(),
            new ClipEngineHolder(NullLogger<ClipEngineHolder>.Instance),
            new ClipModelDownloader(
                new HttpClient { BaseAddress = new Uri("http://localhost") },
                NullLogger<ClipModelDownloader>.Instance),
            options,
            _provider.GetRequiredService<NasSystem.Domain.Repositories.IFileItemRepository>(),
            ConfigWithSemanticEnabled(false),
            NullLogger<SemanticSearchService>.Instance);

        var search = await service.SearchAsync("猫");
        search.IsFailure.Should().BeTrue("功能关闭时搜索必须明确失败");
        search.Error.Code.Should().Be("Semantic.Disabled",
            "不返回空列表假装搜过；错误码让前端引导去系统配置开启");
    }

    [Fact]
    public async Task Vec0Store_KnnReturnsByDistanceAscending()
    {
        var store = CreateStore();
        store.EnsureCreated().Should().BeTrue("测试环境的 vec0 原生件应可用");

        var idA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var idB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var idC = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

        // 三个确定性 4 维向量（store 的维度常量是 512，这里直接用其 API 的
        // float[]——vec0 表按 512 建，短向量会维度不匹配，故按常量构造）
        float[] Vec(float x, float y, float z, float w)
        {
            var v = new float[SemanticIndexStore.EmbeddingDimensions];
            v[0] = x; v[1] = y; v[2] = z; v[3] = w;
            return v;
        }

        await store.UpsertAsync(idA, Vec(1, 0, 0, 0));
        await store.UpsertAsync(idB, Vec(0, 1, 0, 0));
        await store.UpsertAsync(idC, Vec(0.9f, 0.1f, 0, 0));

        store.CountIndexed().Should().Be(3);

        var hits = await store.KnnSearchAsync(Vec(1, 0, 0, 0), topK: 3);
        hits.Should().HaveCount(3);
        hits[0].FileItemId.Should().Be(idA, "查询 [1,0,0,0] 最近的是它自己");
        hits[1].FileItemId.Should().Be(idC, "0.9/0.1 是第二近");
        hits[2].FileItemId.Should().Be(idB, "正交向量最远");
        // 距离升序
        hits[0].Distance.Should().BeLessThan(hits[1].Distance);
        hits[1].Distance.Should().BeLessThan(hits[2].Distance);
    }

    [Fact]
    public async Task Vec0Store_UpsertOverwritesSameId()
    {
        var store = CreateStore();
        store.EnsureCreated().Should().BeTrue();
        var id = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
        var v = new float[SemanticIndexStore.EmbeddingDimensions];
        v[0] = 1;

        await store.UpsertAsync(id, v);
        v[0] = 0;
        await store.UpsertAsync(id, v);

        store.CountIndexed().Should().Be(1, "同主键 INSERT OR REPLACE 覆盖，不翻倍");
    }

    [Fact]
    public void ModelDownloader_RequiredFilesMatchEngineContract()
    {
        // 下载器与引擎加载的四件套契约一致（改一处忘另一处 = 下载了加载不了）
        ClipModelDownloader.RequiredFiles.Should().BeEquivalentTo(new[]
        {
            "vision_model.onnx", "text_model.onnx", "vocab.json", "merges.txt"
        });
        ClipModelDownloader.IsReady(Path.Combine(Path.GetTempPath(), $"definitely-missing-{Guid.NewGuid():N}"))
            .Should().BeFalse("不存在的目录判定未就位");
    }

    private SemanticIndexStore CreateStore()
    {
        var vecPath = Path.Combine(
            AppContext.BaseDirectory, "runtimes", "win-x64", "native", "vec0.dll");
        return new SemanticIndexStore($"Data Source={_dbPath}", vecPath, NullLogger<SemanticIndexStore>.Instance);
    }

    /// <summary>按总开关取值造配置桩（只影响 semantic.enabled 一个键）</summary>
    private static ISystemConfigService ConfigWithSemanticEnabled(bool enabled) =>
        new StubConfigService(enabled);

    /// <summary>
    /// 最小配置桩：semantic.enabled 返回给定值，其余键走默认。
    /// 集成测试项目不引 Moq，与 NoOpSystemControlService 同款手写
    /// </summary>
    private sealed class StubConfigService : ISystemConfigService
    {
        private readonly bool _semanticEnabled;

        public StubConfigService(bool semanticEnabled) => _semanticEnabled = semanticEnabled;

        public Task<SystemConfigSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemConfigSnapshot([], null));

        public Task<Result> UpdateAsync(
            IReadOnlyDictionary<string, string> values,
            Guid operatorUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfigDescriptors.IntDefault(key));

        public Task<bool> GetBoolAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(key == ConfigDescriptors.Keys.SemanticEnabled
                ? _semanticEnabled
                : ConfigDescriptors.BoolDefault(key));

        public Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfigDescriptors.StringDefault(key));
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
