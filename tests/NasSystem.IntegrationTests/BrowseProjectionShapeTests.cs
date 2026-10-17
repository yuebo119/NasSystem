// =============================================================================
// NasSystem.IntegrationTests - BrowseProjectionShapeTests.cs
// 浏览投影的列序对齐与 DTO 字段值锁定
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住浏览目录响应的字段值与列序对齐。
/// </summary>
/// <remarks>
/// 为什么需要值级断言：浏览走 <c>[Projection]</c> 类型的序号映射，SELECT 列序与
/// <c>FileItemBrowseRow</c> 属性声明序错位时不会报错，只会静默交换数据
/// （2026-10-17 从投影里移除 Hash 一列后这个风险面仍在）。现有浏览测试只断言
/// 状态码，列序错位照样全绿。本测试每列用互不相同的 distinctive 值，
/// 任一列错位都会断言失败。
/// 同时锁住 2026-10-17 的死载荷移除：hash 与 sizeDisplay 不再出现在响应里。
/// </remarks>
public class BrowseProjectionShapeTests : ApiTestBase
{
    public BrowseProjectionShapeTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Browse_ItemFieldsMapToSeededValues()
    {
        // Arrange
        var username = "projshape" + Guid.NewGuid().ToString("N")[..6];
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        // 每列一个 distinctive 值：错位后不可能恰好相等
        const long markerSize = 4093; // 质数，避开 1024/4096 等常见默认值
        const string markerMime = "text/x-proj-marker";
        var (parentId, ownerId) = await SeedMarkerFileAsync(username, "/projshape", "marker-7731.txt", markerSize, markerMime);

        // Act：读原始 JSON，逐字段取值
        var response = await Client.GetAsync("/api/v1/files/browse/projshape");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(raw);
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().Be(1, "只种了一个文件");

        var item = items[0];
        item.GetProperty("name").GetString().Should().Be("marker-7731.txt");
        item.GetProperty("path").GetString().Should().Be("/projshape/marker-7731.txt");
        // itemType 序列化为整数（未配 JsonStringEnumConverter）：File = 0，Folder = 1
        item.GetProperty("itemType").GetInt32().Should().Be(0);
        item.GetProperty("sizeBytes").GetInt64().Should().Be(markerSize);
        item.GetProperty("mimeType").GetString().Should().Be(markerMime);
        item.GetProperty("extension").GetString().Should().Be(".txt");
        item.GetProperty("parentId").GetGuid().Should().Be(parentId);
        item.GetProperty("ownerId").GetGuid().Should().Be(ownerId);
        item.GetProperty("isHidden").GetBoolean().Should().BeFalse();
        item.GetProperty("isReadOnly").GetBoolean().Should().BeFalse();

        // createdAt 必须是合理的时间戳：与 sizeBytes 错位时会读到 tick 数（天文数字），
        // 与 UpdatedAt 错位时会读到 null（取不到属性直接抛，也算失败）
        var createdAt = item.GetProperty("createdAt").GetDateTime();
        createdAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));

        // S4 死载荷移除：hash 与 sizeDisplay 不再下发（前端零使用）
        raw.Should().NotContain("\"hash\"");
        raw.Should().NotContain("\"sizeDisplay\"");
        raw.Should().NotContain("\"totalSizeDisplay\"");
    }

    /// <summary>种一个父文件夹加一个带 distinctive 值的文件，返回 (parentId, ownerId)</summary>
    private async Task<(Guid ParentId, Guid OwnerId)> SeedMarkerFileAsync(
        string ownerUsername,
        string folderPath,
        string fileName,
        long sizeBytes,
        string mimeType)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = (await users.GetAllAsync()).First(u => u.Username == ownerUsername);
            var pool = await pools.GetDefaultAsync()
                ?? throw new InvalidOperationException("默认存储池未种成功");

            var folder = FileItem.CreateFolder(
                name: folderPath.TrimStart('/'),
                path: folderPath,
                physicalPath: $"/mnt/storage{folderPath}",
                storagePoolId: pool.Id,
                ownerId: user.Id);
            await fileItems.AddAsync(folder);
            await unitOfWork.SaveChangesAsync();

            var file = FileItem.CreateFile(
                name: fileName,
                path: $"{folderPath}/{fileName}",
                physicalPath: $"/mnt/storage{folderPath}/{fileName}",
                sizeBytes: sizeBytes,
                mimeType: mimeType,
                storagePoolId: pool.Id,
                ownerId: user.Id,
                parentId: folder.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();

            return (folder.Id, user.Id);
        }
    }
}
