// =============================================================================
// NasSystem.IntegrationTests - BackupEndpointTests.cs
// 移动备份端点行为锁定（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住备份功能的三类行为：
/// 一、设备注册（唯一名冲突 409、非法名 400）；
/// 二、同步计划 diff（缺路径/体积不同 → 待上传，体积相同 → 已是最新，
///     服务端多出的条目只上报不删）；
/// 三、吊销后计划被拒（403），陌生设备 404。
/// </summary>
/// <remarks>
/// 文件传输不在本功能内（客户端按计划走标准上传端点），
/// 所以这里不造物理文件，只种 FileItem 记录做 diff 输入
/// </remarks>
public class BackupEndpointTests : ApiTestBase, IDisposable
{
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"backup-{Guid.NewGuid():N}");

    private static bool _poolReady;

    static BackupEndpointTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public BackupEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    public new void Dispose()
    {
        try
        {
            Directory.Delete(ClassStorageRoot, recursive: true);
        }
        catch
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Register_ThenList_ReturnsDeviceWithBackupPath()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();

        var deviceId = await RegisterDeviceAsync(token, "testphone", "Pixel Test");

        var list = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/v1/backup/devices", token));
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadAsStringAsync();
        body.Should().Contain("testphone");
        body.Should().Contain("/Backups/testphone", "备份目录必须是 /Backups/{设备名}");
        body.Should().Contain(deviceId.ToString());
    }

    [Fact]
    public async Task Register_DuplicateName_Returns409()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();
        await RegisterDeviceAsync(token, "dupphone");

        var second = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, "/api/v1/backup/devices", token,
            new { name = "dupphone" }));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict, "同用户名下设备名唯一（备份目录同名会互相覆盖）");
    }

    [Fact]
    public async Task Register_InvalidName_Returns400()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();

        // "../escape" 经路径拼接会逃逸挂载根，名称不变量必须挡住
        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, "/api/v1/backup/devices", token,
            new { name = "../escape" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Plan_DiffByPathAndSize_ReturnsCorrectBuckets()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();
        var deviceId = await RegisterDeviceAsync(token, "planphone");

        // 种两个服务端已有文件：一个体积与清单一致（已是最新），
        // 一个体积不同（需重传），一个清单里没有（孤儿）
        await SeedBackupFileAsync(username, "/Backups/planphone/DCIM/a.jpg", 1000);
        await SeedBackupFileAsync(username, "/Backups/planphone/DCIM/b.jpg", 2000);
        await SeedBackupFileAsync(username, "/Backups/planphone/DCIM/gone.jpg", 500);

        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, $"/api/v1/backup/devices/{deviceId}/plan", token,
            new
            {
                entries = new[]
                {
                    new { relativePath = "DCIM/a.jpg", sizeBytes = 1000L },
                    new { relativePath = "DCIM/b.jpg", sizeBytes = 9999L },
                    new { relativePath = "DCIM/new.jpg", sizeBytes = 300L }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        // 已是最新：a.jpg（路径在、体积同）
        body.Should().Contain("\"upToDateCount\":1");
        // 待上传：b.jpg（体积不同）+ new.jpg（路径缺失），不含 a.jpg
        var plan = JsonDocument.Parse(body).RootElement.GetProperty("data");
        var toUpload = plan.GetProperty("toUpload");
        toUpload.GetArrayLength().Should().Be(2);
        var uploadPaths = toUpload.EnumerateArray()
            .Select(e => e.GetProperty("relativePath").GetString())
            .ToList();
        uploadPaths.Should().BeEquivalentTo(new[] { "DCIM/b.jpg", "DCIM/new.jpg" });
        // 孤儿只上报：gone.jpg 服务端有、客户端清单没有
        var orphaned = plan.GetProperty("orphanedOnServer");
        orphaned.GetArrayLength().Should().Be(1);
        orphaned[0].GetString().Should().Be("DCIM/gone.jpg");
    }

    [Fact]
    public async Task Plan_EscapesAreSkipped_NotUploaded()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();
        var deviceId = await RegisterDeviceAsync(token, "escapephone");

        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, $"/api/v1/backup/devices/{deviceId}/plan", token,
            new
            {
                entries = new[]
                {
                    new { relativePath = "../../etc/passwd", sizeBytes = 10L },
                    new { relativePath = "DCIM/../ok.jpg", sizeBytes = 20L }
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var toUpload = JsonDocument.Parse(body).RootElement
            .GetProperty("data").GetProperty("toUpload");
        var paths = toUpload.EnumerateArray()
            .Select(e => e.GetProperty("relativePath").GetString())
            .ToList();
        paths.Should().BeEquivalentTo(new[] { "ok.jpg" },
            "纯逃逸项跳过，含 .. 的合法相对路径折叠后保留");
    }

    [Fact]
    public async Task Plan_RevokedDevice_Returns403()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();
        var deviceId = await RegisterDeviceAsync(token, "revokephone");

        var revoke = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Delete, $"/api/v1/backup/devices/{deviceId}", token));
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);

        var plan = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, $"/api/v1/backup/devices/{deviceId}/plan", token,
            new { entries = Array.Empty<object>() }));
        plan.StatusCode.Should().Be(HttpStatusCode.Forbidden, "吊销设备不再接受同步计划");
    }

    [Fact]
    public async Task Plan_UnknownDevice_Returns404()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();

        var plan = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, $"/api/v1/backup/devices/{Guid.NewGuid()}/plan", token,
            new { entries = Array.Empty<object>() }));

        plan.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SyncComplete_UpdatesLastSync()
    {
        var (username, token) = await SeedBackupUserAsync();
        await EnsureClassPoolAsync();
        var deviceId = await RegisterDeviceAsync(token, "syncphone");

        var complete = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/v1/backup/devices/{deviceId}/sync-complete?uploadedFileCount=7", token));
        complete.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await Client.SendAsync(CreateBearerRequest(HttpMethod.Get, "/api/v1/backup/devices", token));
        var body = await list.Content.ReadAsStringAsync();
        body.Should().Contain("\"lastSyncFileCount\":7");
        body.Should().Contain("\"lastSyncAt\"", "登记后应回写最近同步时间");
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task<(string Username, string Token)> SeedBackupUserAsync()
    {
        var username = "bakuser" + Guid.NewGuid().ToString("N")[..8];
        var token = await GetAuthTokenAsync(username);
        return (username, token);
    }

    private async Task EnsureClassPoolAsync()
    {
        Directory.CreateDirectory(ClassStorageRoot);
        if (_poolReady) return;

        var (scope, _, pools, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var existing = await pools.GetDefaultAsync();
            if (existing is not null && existing.MountPath == ClassStorageRoot)
            {
                _poolReady = true;
                return;
            }

            var all = await pools.GetAllAsync();
            foreach (var stale in all)
            {
                await pools.DeleteAsync(stale);
            }
            await unitOfWork.SaveChangesAsync();

            var pool = StoragePool.Create("BackupPool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            _poolReady = true;
        }
    }

    private async Task<Guid> RegisterDeviceAsync(string token, string name, string? model = null)
    {
        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, "/api/v1/backup/devices", token,
            model is null ? new { name } : new { name, deviceModel = model }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"注册设备 {name} 应成功");

        var body = await response.Content.ReadAsStringAsync();
        var marker = "\"id\":\"";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        var idStart = start + marker.Length;
        var idEnd = body.IndexOf('"', idStart);
        return Guid.Parse(body[idStart..idEnd]);
    }

    /// <summary>在设备备份目录下种一个文件记录（diff 输入，不落物理文件）</summary>
    private async Task SeedBackupFileAsync(string ownerUsername, string virtualPath, long sizeBytes)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var allUsers = await users.GetAllAsync();
            var user = allUsers.First(u => u.Username == ownerUsername);
            var pool = await pools.GetDefaultAsync();
            if (pool is null) return;

            if (await fileItems.GetByPathAsync(virtualPath) is not null)
                return;

            var file = FileItem.CreateFile(
                name: virtualPath.Split('/')[^1],
                path: virtualPath,
                physicalPath: Path.Combine(ClassStorageRoot, virtualPath.TrimStart('/')),
                sizeBytes: sizeBytes,
                mimeType: "image/jpeg",
                ownerId: user.Id,
                storagePoolId: pool.Id,
                parentId: null,
                hash: "seedhash");
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, string token, object payload)
    {
        var request = CreateBearerRequest(method, url, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}
