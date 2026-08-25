// ═══════════════════════════════════════════════════════════════
// ITM-N26 复现测试：软删除 × 扫描同步 × 恢复 的交互链
//
// 缺陷链（修复前）：
//   ① 软删 /x（IsDeleted=true，物理文件保留）
//   ② 扫描同步：GetAllAsync 过滤软删 → 物理发现 → 重建新记录（文件"复活"）
//   ③ 恢复回收站旧记录 → 撞 Path 唯一部分索引 → 500
//
// 修复后语义：
//   ② 扫描识别软删记录，跳过重建（保持回收站态）
//   ③ 恢复成功；若并发已有同路径新记录则转业务错误而非 500
// ═══════════════════════════════════════════════════════════════
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Application.Files.Dtos;
using NasSystem.Shared;
using Xunit;

namespace NasSystem.IntegrationTests;

public class SoftDeleteScanInteractionTests : ApiTestBase
{
    public SoftDeleteScanInteractionTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Scan_AfterSoftDelete_DoesNotResurrectDeletedFile()
    {
        // Arrange：经 API 创建目录（数据库+物理双真实）→ 软删 → 扫描同步
        var token = await GetAuthTokenAsync("softdelscanuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var folderName = "sdscan" + Guid.NewGuid().ToString("N")[..6];
        var createResponse = await Client.PostAsJsonAsync("/api/v1/files/folder",
            new { parentPath = "/", name = folderName });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new DeleteItemsRequest { Paths = [$"/{folderName}"], Permanent = false });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act：扫描同步（软删记录的物理文件仍存在于挂载点）
        var scanResponse = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert①：浏览根目录不应看到被软删的文件夹"复活"
        var browseRaw = await Client.GetStringAsync("/api/v1/files/browse/");
        browseRaw.Should().NotContain(folderName,
            "扫描不得把软删记录当作物理新发现重建（否则软删除语义失效）");

        // Assert②：回收站仍保留该记录
        var recycleRaw = await Client.GetStringAsync("/api/v1/files/recycle-bin");
        recycleRaw.Should().Contain(folderName, "软删记录必须保留在回收站中");
    }

    [Fact]
    public async Task Restore_AfterScanWithoutResurrection_Succeeds()
    {
        // Arrange：经 API 创建 → 软删 → 扫描 → 恢复
        var token = await GetAuthTokenAsync("sdrestoreuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var folderName = "sdrest" + Guid.NewGuid().ToString("N")[..6];
        await Client.PostAsJsonAsync("/api/v1/files/folder",
            new { parentPath = "/", name = folderName });
        await Client.PostAsJsonAsync("/api/v1/files/delete",
            new DeleteItemsRequest { Paths = [$"/{folderName}"], Permanent = false });
        await Client.PostAsJsonAsync("/api/v1/files/scan", new { });

        // Act：从回收站恢复（修复前若扫描重建了同路径活跃记录则撞唯一索引返回 500）
        var recycleRaw = await Client.GetStringAsync("/api/v1/files/recycle-bin");
        recycleRaw.Should().Contain(folderName);
        var id = ExtractRecycleId(recycleRaw, folderName);

        var restore = await Client.PostAsync($"/api/v1/files/restore/{id}", null);

        // Assert
        restore.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    /// <summary>从回收站 JSON 中提取指定文件夹的记录 Id</summary>
    private static Guid ExtractRecycleId(string json, string folderName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (item.TryGetProperty("path", out var p) &&
                p.GetString()?.Contains(folderName) == true)
            {
                return item.GetProperty("id").GetGuid();
            }
        }
        throw new InvalidOperationException($"回收站中未找到 {folderName}");
    }

}
