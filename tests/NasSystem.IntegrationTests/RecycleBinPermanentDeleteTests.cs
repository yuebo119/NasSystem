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
        var soft = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { folderPath }, permanent = false });
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
}
