// =============================================================================
// NasSystem.IntegrationTests - ScanSyncTests.cs
// 扫描同步的物理↔数据库对齐行为（S2：轻量对比表 + 批量写 + 删除子先父）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using Xunit;

/// <summary>
/// 锁住扫描同步的三类行为：
/// 一、物理新增的文件被扫描入库；
/// 二、物理删除的整棵目录树被扫描清除（父行先删撞 ParentId 外键 RESTRICT 的场景，
///     旧实现按路径升序删必报"扫描失败"，2026-10-17 S2 改为子先父）；
/// 三、扫描提交后受影响目录的 ETag 必须变（批量写不跟踪实体，版本号改由服务显式递增）。
/// </summary>
/// <remarks>
/// 本类的默认池挂载点是每类一个的真实临时目录（扫描要遍历真实文件系统）。
/// 种子池（InitializeDatabaseAsync 按构建目录建的那个）在类内首个用例前删除，
/// 避免两个默认池并存；池 Id 与挂载点在类内恒定，FileService 的 30s 池投影
/// 缓存不会串味。各用例虚拟路径带每进程唯一前缀，互不干扰。
/// </remarks>
public class ScanSyncTests : ApiTestBase, IDisposable
{
    /// <summary>本类共享的真实存储根</summary>
    private static readonly string ClassScanRoot =
        Path.Combine(Path.GetTempPath(), $"scansync-{Guid.NewGuid():N}");

    /// <summary>每进程唯一的路径前缀（物理目录落在池挂载点下，跨运行需避名）</summary>
    private static readonly string PathPrefix = "sc" + Guid.NewGuid().ToString("N")[..6];

    private static bool _poolReady;

    static ScanSyncTests()
    {
        Directory.CreateDirectory(ClassScanRoot);
    }

    public ScanSyncTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    public new void Dispose()
    {
        try
        {
            Directory.Delete(ClassScanRoot, recursive: true);
        }
        catch
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Scan_AddsPhysicallyCreatedFiles()
    {
        // Arrange：登录 + 池就绪 + 物理上放一个目录和文件（不进库）
        var token = await GetAuthTokenAsync("scanadd" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var folderRel = $"{PathPrefix}-newdir";
        var physicalDir = Path.Combine(ClassScanRoot, folderRel);
        Directory.CreateDirectory(physicalDir);
        await File.WriteAllTextAsync(Path.Combine(physicalDir, "dropped.txt"), "hello");

        // Act：扫描
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        var scanBody = await scan.Content.ReadAsStringAsync();
        scan.StatusCode.Should().Be(HttpStatusCode.OK, $"扫描应成功，实际响应：{scanBody}");

        // Assert：库里有记录且可浏览
        var browse = await Client.GetAsync($"/api/v1/files/browse/{folderRel}");
        browse.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await browse.Content.ReadAsStringAsync();
        raw.Should().Contain("dropped.txt", "物理新增的文件必须被扫描入库");
    }

    [Fact]
    public async Task Scan_RemovesPhysicallyDeletedFolderTree()
    {
        // Arrange：经 API 建目录（库 + 物理双真实）并放一个子文件
        var token = await GetAuthTokenAsync("scandel" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var folderName = $"{PathPrefix}-tree";
        var create = await Client.PostAsJsonAsync("/api/v1/files/folder",
            new { parentPath = "/", name = folderName });
        create.StatusCode.Should().Be(HttpStatusCode.OK);

        var physicalDir = Path.Combine(ClassScanRoot, folderName);
        await File.WriteAllTextAsync(Path.Combine(physicalDir, "inside.txt"), "x");
        // 让扫描发现这个子文件（库里有记录、物理存在）
        var scanBefore = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scanBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act：物理上整棵删掉（模拟在资源管理器/SMB 里删目录），再扫描
        Directory.Delete(physicalDir, recursive: true);
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert①：库记录必须被清掉。旧实现按路径升序删（父先于子），
        // 第一个父行删除就撞 ParentId 外键 RESTRICT，整个扫描事务回滚报
        // "扫描失败: FOREIGN KEY constraint failed"，目录树永远留在库里
        (await PathExistsAsync($"/{folderName}")).Should().BeFalse("物理已删的目录记录必须被清除");
        (await PathExistsAsync($"/{folderName}/inside.txt")).Should().BeFalse("后代记录必须被清除");

        // Assert②：根目录不再显示它
        var browseRoot = await Client.GetStringAsync("/api/v1/files/browse/");
        browseRoot.Should().NotContain(folderName);
    }

    [Fact]
    public async Task Scan_WithDuplicatePathRecords_Succeeds()
    {
        // 2026-10-19 E2E 走查实证的回归：Path 唯一索引是部分索引（只管未删），
        // 软删历史与新记录同路径共存（删了再传同名文件即是）。扫描建对比表的
        // ToDictionary 撞重复键，整个扫描 500（"An item with the same key
        // has already been added"）——回收站积几条同名记录后点"扫描同步"必炸
        var token = await GetAuthTokenAsync("scandup" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var name = $"{PathPrefix}-dup";
        // ① 建目录 ② 软删（回收站留一条 IsDeleted=1 的同路径记录）
        var create1 = await Client.PostAsJsonAsync("/api/v1/files/folder", new { parentPath = "/", name });
        create1.StatusCode.Should().Be(HttpStatusCode.OK);
        var softDelete = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { $"/{name}" }, permanent = false });
        softDelete.StatusCode.Should().Be(HttpStatusCode.OK);

        // ③ 同名再建：库里有两条同 Path 记录（一删一活）
        var create2 = await Client.PostAsJsonAsync("/api/v1/files/folder", new { parentPath = "/", name });
        create2.StatusCode.Should().Be(HttpStatusCode.OK);

        // ④ 扫描必须成功（修复前 500 Scan.Failed 重复键）
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        var scanBody = await scan.Content.ReadAsStringAsync();
        scan.StatusCode.Should().Be(HttpStatusCode.OK, $"同路径多记录时扫描应成功，实际响应：{scanBody}");
    }

    [Fact]
    public async Task Scan_BumpsETagOfScannedDirectory()
    {
        // Arrange
        var token = await GetAuthTokenAsync("scanetag" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var before = await Client.GetAsync("/api/v1/files/browse/");
        var etagBefore = before.Headers.ETag?.Tag;
        etagBefore.Should().NotBeNull();

        // Act：物理上丢一个文件到根，扫描入库
        await File.WriteAllTextAsync(Path.Combine(ClassScanRoot, $"{PathPrefix}-drop.bin"), "y");
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert：根目录 ETag 必须变（批量写不跟踪实体，版本号改由服务显式递增）
        var after = await Client.GetAsync("/api/v1/files/browse/");
        after.Headers.ETag?.Tag.Should().NotBe(etagBefore, "扫描提交后根目录版本号必须递增");
    }

    // ─── 辅助 ───

    /// <summary>
    /// 建本类的默认池（挂载点为真实临时目录）。建库时种下的默认池挂在构建目录
    /// （bin/.../storage，跨测试类残留几百个目录），不能用作扫描根，先删再建。
    /// 两个默认池并存时 GetDefaultAsync 取到哪个不确定，也必须删。
    /// </summary>
    private async Task EnsureClassPoolAsync()
    {
        // xUnit 每测试一个类实例，上个实例的 Dispose 已删掉共享根，先补建
        // （CreateDirectory 幂等）
        Directory.CreateDirectory(ClassScanRoot);

        if (_poolReady) return;

        var (scope, _, pools, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var existing = await pools.GetDefaultAsync();
            if (existing is not null && existing.MountPath == ClassScanRoot)
            {
                _poolReady = true;
                return;
            }

            // 全新库上没有任何 FileItems 引用种子池，删除不会撞外键
            var all = await pools.GetAllAsync();
            foreach (var stale in all)
            {
                await pools.DeleteAsync(stale, cancellationToken: default);
            }
            await unitOfWork.SaveChangesAsync();

            var pool = StoragePool.Create("ScanPool", ClassScanRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool, cancellationToken: default);
            await unitOfWork.SaveChangesAsync();

            _poolReady = true;
        }
    }

    private async Task<bool> PathExistsAsync(string path)
    {
        var (scope, _, _, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            return await fileItems.GetByPathAsync(path) is not null;
        }
    }
}
