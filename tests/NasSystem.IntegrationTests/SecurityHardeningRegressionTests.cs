// =============================================================================
// NasSystem.IntegrationTests - SecurityHardeningRegressionTests.cs
// 审计 B1 批（安全收紧）的锁定测试：软删分享/备份跨用户/吊销旁路/扫描权限
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Enums;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 B1 批四条修复：
/// F-004 软删文件经分享仍可下载/浏览（授权锚不论删除态）
/// F-005 备份路径无用户维度，同名设备跨用户共享备份根
/// F-011 扫描同步任意登录用户可触发，无主文件归属归触发者
/// F-023 吊销设备不拦截 sync-complete
/// </summary>
public class SecurityHardeningRegressionTests : ApiTestBase
{
    public SecurityHardeningRegressionTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Share_Download_AfterOwnerSoftDeletes_ReturnsNotFound()
    {
        var (ownerToken, _, ownerUsername) = await TwoUsersAsync();
        var fileName = "b1share" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var fileId = await SeedPhysicalFileAsync($"/{fileName}", "b1 seed content", ownerUsername);

        // 属主建公开分享（accessType 数值 0 = Public）
        SetAuthToken(ownerToken);
        var createShare = await Client.PostAsJsonAsync("/api/v1/file-shares",
            new { fileItemId = fileId, accessType = 0, permission = 1 });
        createShare.StatusCode.Should().Be(HttpStatusCode.Created);
        var shareCode = await ReadShareCodeAsync(createShare);

        // 属主软删（进回收站，物理保留）
        var delete = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { $"/{fileName}" }, permanent = false });
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        // 匿名访问分享：修复前 200 + 完整内容，修复后 404
        Client.DefaultRequestHeaders.Authorization = null;
        var download = await Client.GetAsync($"/api/v1/public-shares/{shareCode}/download");
        download.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "文件已进回收站，分享链接应按不存在处理");

        var info = await Client.GetAsync($"/api/v1/public-shares/{shareCode}");
        info.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "分享信息同样不得暴露已删除文件");
    }

    [Fact]
    public async Task Backup_SameDeviceName_TwoUsers_GetIsolatedRoots()
    {
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        const string deviceName = "MyPhone";

        SetAuthToken(ownerToken);
        var ownerDevice = await RegisterDeviceAsync(deviceName);
        var ownerRoot = await ReadBackupRootAsync(ownerDevice);

        SetAuthToken(otherToken);
        var otherDevice = await RegisterDeviceAsync(deviceName);
        var otherRoot = await ReadBackupRootAsync(otherDevice);

        ownerRoot.Should().NotBe(otherRoot,
            "备份根必须按用户隔离：共享根会让一方 diff 回包收录另一方全部文件，且同路径文件互相占位");

        // 属主往自己根里放一个文件（库+物理双真实），旁观者的计划回包不得出现它。
        // 不经上传端点：上传要求父目录记录存在（F-025 登记项），这里测的是
        // 路径隔离本身，种文件即可
        var ownerFileName = "owneronly" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        await SeedPhysicalFileAsync($"{ownerRoot}/{ownerFileName}", "owner data", ownerUsername);

        SetAuthToken(otherToken);
        var plan = await Client.PostAsJsonAsync(
            $"/api/v1/backup/devices/{otherDevice}/plan",
            new { entries = Array.Empty<object>() });
        plan.StatusCode.Should().Be(HttpStatusCode.OK);
        var planBody = await plan.Content.ReadAsStringAsync();
        planBody.Should().NotContain(ownerFileName,
            "他人备份根内的文件不得出现在我的同步计划回包里");
    }

    [Fact]
    public async Task Scan_NonAdmin_ReturnsForbidden()
    {
        var (_, otherToken, _) = await TwoUsersAsync();
        SetAuthToken(otherToken);
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "扫描会把无主物理文件记给触发者，必须是管理员动作");
    }

    [Fact]
    public async Task Scan_Admin_StillSucceeds()
    {
        var token = await GetAuthTokenAsync("b1scanadmin" + Guid.NewGuid().ToString("N")[..6], UserRole.Admin);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var scan = await Client.PostAsJsonAsync("/api/v1/files/scan", new { });
        scan.StatusCode.Should().Be(HttpStatusCode.OK, "管理员扫描不受权限收紧影响");
    }

    [Fact]
    public async Task Backup_RevokedDevice_SyncComplete_ReturnsForbidden()
    {
        var (ownerToken, _, _) = await TwoUsersAsync();
        SetAuthToken(ownerToken);
        var deviceId = await RegisterDeviceAsync("b1revoked" + Guid.NewGuid().ToString("N")[..6]);

        var revoke = await Client.DeleteAsync($"/api/v1/backup/devices/{deviceId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        // uploadedFileCount 是查询参数而非请求体（端点签名 int uploadedFileCount）
        var syncComplete = await Client.PostAsync(
            $"/api/v1/backup/devices/{deviceId}/sync-complete?uploadedFileCount=3", null);
        syncComplete.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "吊销语义必须覆盖 sync-complete：否则被吊销设备继续写同步统计");
    }

    // ─── 辅助 ───

    private async Task<(string OwnerToken, string OtherToken, string OwnerUsername)> TwoUsersAsync()
    {
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ownerUsername = "b1-owner-" + suffix;
        var ownerToken = await GetAuthTokenAsync(ownerUsername);
        var otherToken = await GetAuthTokenAsync("b1-other-" + suffix);
        return (ownerToken, otherToken, ownerUsername);
    }

    /// <summary>种一个物理+库双真实的文件（属主为指定用户），返回 FileItemId</summary>
    private async Task<string> SeedPhysicalFileAsync(string path, string content, string ownerUsername)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var pool = await pools.GetDefaultAsync();
            pool.Should().NotBeNull("测试前置已种下默认池");
            var owner = (await users.GetAllAsync()).First(u => u.Username == ownerUsername);
            var physicalPath = Path.Combine(pool!.MountPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllTextAsync(physicalPath, content, Encoding.UTF8);

            var existing = await fileItems.GetByPathAsync(path);
            if (existing is not null) return existing.Id.ToString();

            var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                name: Path.GetFileName(path),
                path: path,
                physicalPath: physicalPath,
                sizeBytes: Encoding.UTF8.GetByteCount(content),
                mimeType: "text/plain",
                storagePoolId: pool.Id,
                ownerId: owner.Id,
                hash: "b1-hash");
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            return file.Id.ToString();
        }
    }

    private async Task<string> RegisterDeviceAsync(string name)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/backup/devices", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("id").GetString()!;
    }

    private async Task<string> ReadBackupRootAsync(string deviceId)
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/v1/backup/devices/{deviceId}/plan",
            new { entries = Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("backupPath").GetString()!;
    }

    private async Task<HttpResponseMessage> UploadAsync(string destinationPath, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(destinationPath), "path");
        form.Add(new StringContent("true"), "overwrite");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);
        return await Client.PostAsync("/api/v1/files/upload", form);
    }

    private static async Task<string> ReadShareCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("shareCode").GetString()!;
    }
}
