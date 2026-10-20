// =============================================================================
// NasSystem.IntegrationTests - RecycleBinPermanentDeleteTests.cs
// 回收站单条永久删除回归
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// 锁回收站"永久删除"按钮的真实链路：软删进回收站 → 按同一路径永久删。
/// 2026-10-19 E2E 走查实证：永久删的路径查找走可见口径
/// （GetByPathAsync 排 IsDeleted=1），回收站条目永远"路径不存在"，
/// 该按钮此前从未成功过。既有删除测试用"OK 或 NotFound 都行"的弱断言，
/// 正好放过这类故障——这里逐环强断言
/// </summary>
public class RecycleBinPermanentDeleteTests : ApiTestBase
{
    public RecycleBinPermanentDeleteTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task PermanentDelete_OfRecycledItem_SucceedsByPath()
    {
        var token = await GetAuthTokenAsync("recycleperm");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var folderName = "recycleperm" + Guid.NewGuid().ToString("N")[..6];
        var folderPath = $"/{folderName}";
        await SeedTestFolderAsync(folderPath, "recycleperm");

        // ① 软删：进回收站
        var soft = await Client.PostAsJsonAsync("/api/v1/files/delete",            new { paths = new[] { folderPath }, permanent = false });
        soft.StatusCode.Should().Be(HttpStatusCode.OK);
        var softBody = await soft.Content.ReadAsStringAsync();
        softBody.Should().Contain("\"deletedCount\":1", "软删应成功计数 1");

        // ② 回收站里能查到（确认条目处于 IsDeleted=1 状态）
        var bin = await Client.GetAsync("/api/v1/files/recycle-bin");
        var binBody = await bin.Content.ReadAsStringAsync();
        binBody.Should().Contain(folderPath, "软删后条目应在回收站");

        // ③ 永久删：按回收站给出的同一路径。修复前此步 failedItems 报
        // "路径不存在"（查找排除了软删记录），按钮实际不可用
        var hard = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { folderPath }, permanent = true });
        hard.StatusCode.Should().Be(HttpStatusCode.OK);
        var hardBody = await hard.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(hardBody);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("deletedCount").GetInt32().Should().Be(1, "回收站条目应可按路径永久删除");
        data.GetProperty("failedItems").GetArrayLength().Should().Be(0, "不应有失败项");

        // ④ 回收站不再有它（记录真没了，不是又藏回去）
        var binAfter = await Client.GetAsync("/api/v1/files/recycle-bin");
        var binAfterBody = await binAfter.Content.ReadAsStringAsync();
        binAfterBody.Should().NotContain(folderPath, "永久删后记录应消失");
    }

    [Fact]
    public async Task PermanentDelete_OfRecycledFolderWithChildren_RemovesWholeSubtree()
    {
        // 2026-10-19 数据库层审计实证的回归：永久删只删根行，后代查询
        // GetDescendantsAsync 带 IsDeleted=0 过滤——回收站里整棵子树都是
        // 软删态，后代取回空集，删父行时子的 ParentId 外键 RESTRICT 直接
        // 失败（深度≥1 的文件夹永久删全灭）。且后代即便取到也是
        // ORDER BY Path 升序（父是子的前缀，父先删照样撞外键）
        var token = await GetAuthTokenAsync("recyclepermnest");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var parentPath = $"/nest-{suffix}";
        var childPath = $"{parentPath}/child-{suffix}";
        await SeedTestFolderAsync(parentPath, "recyclepermnest");
        await SeedTestFolderAsync(childPath, "recyclepermnest");

        // 软删整棵树：父与子一同进回收站（同一 DeletedAt）
        var soft = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { parentPath }, permanent = false });
        soft.StatusCode.Should().Be(HttpStatusCode.OK);

        // 永久删父路径：必须把子树一起删掉，而不是 FK 失败留孤儿
        var hard = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { parentPath }, permanent = true });
        hard.StatusCode.Should().Be(HttpStatusCode.OK);
        var hardBody = await hard.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(hardBody);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("deletedCount").GetInt32().Should().Be(1, "父路径删成功");
        data.GetProperty("failedItems").GetArrayLength().Should().Be(0,
            "带子孙的回收站文件夹必须能永久删除（修复前 FK RESTRICT 全失败）");

        // 子的记录也必须消失：只删父行会留下引用已删父行的孤儿
        var binAfter = await Client.GetAsync("/api/v1/files/recycle-bin");
        var binAfterBody = await binAfter.Content.ReadAsStringAsync();
        binAfterBody.Should().NotContain(parentPath, "父记录应消失");
        binAfterBody.Should().NotContain(childPath, "子记录必须一并删除，不能留孤儿");
    }

    [Fact]
    public async Task EmptyRecycleBin_WithSoftDeletedSubtree_CleansBothLevels()
    {
        // 清空回收站的同族回归：软删文件夹时父子打同一 DeletedAt，按
        // DeletedAt 排序父子次序不定，父先删撞子的 ParentId 外键 RESTRICT。
        // 修复为按路径倒序（子先于父）后整批清理必须全成功
        var token = await GetAuthTokenAsync("recycleempty");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var parentPath = $"/empty-{suffix}";
        var childPath = $"{parentPath}/child-{suffix}";
        await SeedTestFolderAsync(parentPath, "recycleempty");
        await SeedTestFolderAsync(childPath, "recycleempty");

        var soft = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { parentPath }, permanent = false });
        soft.StatusCode.Should().Be(HttpStatusCode.OK);

        var empty = await Client.DeleteAsync("/api/v1/files/recycle-bin");
        empty.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyBody = await empty.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(emptyBody);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("failedItems").GetArrayLength().Should().Be(0,
            "父子同刻软删时清空回收站不得有失败项（修复前父先删撞外键）");
        data.GetProperty("deletedCount").GetInt32().Should().BeGreaterThanOrEqualTo(2,
            "父与子两条记录都应被清掉");

        var binAfter = await Client.GetAsync("/api/v1/files/recycle-bin");
        var binAfterBody = await binAfter.Content.ReadAsStringAsync();
        binAfterBody.Should().NotContain(parentPath);
        binAfterBody.Should().NotContain(childPath);
    }
}
