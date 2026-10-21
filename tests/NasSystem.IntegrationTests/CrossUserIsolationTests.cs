// =============================================================================
// NasSystem.IntegrationTests - CrossUserIsolationTests.cs
// 多用户文件隔离回归（.ai/lessons.md 记载的"FileService 不过滤 ownerId"缺口）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// 2026-10-19 E2E 走查实证：新注册用户能浏览到他人全部文件，下载/改名/删除
/// 同理（OCR/媒体/语义/转码早已校验归属，核心文件链路是漏装的那一环）。
/// 这里锁四条主链路：浏览、下载、改名、删除。策略一律"按不存在处理"——
/// 不确认也不否认他人条目存在性
/// </summary>
public class CrossUserIsolationTests : ApiTestBase
{
    public CrossUserIsolationTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task Browse_OtherUsersRoot_DoesNotLeakTheirItems()
    {
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var secret = "secret" + Guid.NewGuid().ToString("N")[..6];
        await SeedTestFolderAsync($"/{secret}", ownerUsername);

        // 属主能看到自己的目录
        SetAuthToken(ownerToken);
        var ownerBody = await GetStringAsync("/api/v1/files/browse/");
        ownerBody.Should().Contain(secret, "属主应看到自己的目录");

        // 他人看不到：根列表按归属过滤
        SetAuthToken(otherToken);
        var otherBody = await GetStringAsync("/api/v1/files/browse/");
        otherBody.Should().NotContain(secret, "他人根列表不应出现我的目录");

        // 他人直穿路径按不存在处理（同时兜住"不存在路径回落根列表"的旧怪癖）
        var response = await Client.GetAsync($"/api/v1/files/browse/{secret}");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("File.NotFound", "浏览他人目录应按不存在处理");
    }

    [Fact]
    public async Task Download_OtherUsersFile_ReturnsNotFound()
    {
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var name = "iso" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        await SeedTestFileAsync($"/{name}", ownerUsername);

        SetAuthToken(otherToken);
        var response = await Client.GetAsync($"/api/v1/files/download/{name}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "下载他人文件应按不存在处理");
    }

    [Fact]
    public async Task Rename_OtherUsersFile_ReturnsNotFound()
    {
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var name = "iso" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        await SeedTestFileAsync($"/{name}", ownerUsername);

        SetAuthToken(otherToken);
        var response = await Client.PutAsJsonAsync("/api/v1/files/rename",
            new { path = $"/{name}", newName = "hijacked.txt" });
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("File.NotFound", "改名他人文件应按不存在处理");
    }

    [Fact]
    public async Task Delete_OtherUsersFile_ReportsPathNotFound()
    {
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var name = "iso" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        await SeedTestFileAsync($"/{name}", ownerUsername);

        SetAuthToken(otherToken);
        var response = await Client.PostAsJsonAsync("/api/v1/files/delete",
            new { paths = new[] { $"/{name}" }, permanent = false });
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("路径不存在", "删除他人文件应按不存在处理");
        body.Should().Contain("\"deletedCount\":0", "他人文件不应被删动");
    }

    [Fact]
    public async Task Browse_StaleCrossUserETag_DoesNotReplayCachedBody()
    {
        // 2026-10-19 E2E 走查实证的第二个漏口：浏览 ETag 只含路径+目录版本，
        // 304 判断还在归属校验之前——他人（或修复前的泄漏响应）的缓存体
        // 会被 If-None-Match 重放给当前用户。ETag 现含 userId，跨用户必 miss
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var secret = "etag" + Guid.NewGuid().ToString("N")[..6];
        await SeedTestFolderAsync($"/{secret}", ownerUsername);

        // 属主先浏览：拿到属主口径的 ETag（其响应体含 secret）
        SetAuthToken(ownerToken);
        var ownerResponse = await Client.GetAsync("/api/v1/files/browse/");
        var ownerEtag = ownerResponse.Headers.ETag?.ToString();
        ownerEtag.Should().NotBeNullOrEmpty("浏览响应应带 ETag");
        var ownerBody = await ownerResponse.Content.ReadAsStringAsync();
        ownerBody.Should().Contain(secret);

        // 他人带属主的 ETag 条件请求：不得 304 重放，必须拿到本人口径的空列表
        SetAuthToken(otherToken);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files/browse/");
        request.Headers.TryAddWithoutValidation("If-None-Match", ownerEtag!);
        var otherResponse = await Client.SendAsync(request);
        otherResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "跨用户 ETag 不得命中 304（否则重放他人缓存体）");
        var otherBody = await otherResponse.Content.ReadAsStringAsync();
        otherBody.Should().NotContain(secret, "重放的缓存体不得含他人条目");
    }

    [Fact]
    public async Task Upload_ToOtherUsersPath_DoesNotReplaceTheirPhysicalContent()
    {
        // 多用户隔离的写路径缺口：UploadFileAsync 把"属他人"的既有记录置 null 后
        // 仍按新文件走完整写入管线——File.Move 覆盖物理路径在先，唯一索引冲突
        // 拒绝落库在后。受害者库记录（旧大小/哈希）与磁盘内容（攻击者字节）错位
        var (ownerToken, otherToken, ownerUsername) = await TwoUsersAsync();
        var name = "isoup" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        await SeedTestFileAsync($"/{name}", ownerUsername);
        var physicalPath = await GetPhysicalPathAsync($"/{name}");

        // 旁观者向同一路径上传（overwrite=true，网页冲突对话框与 WebDAV PUT 的形态）
        SetAuthToken(otherToken);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("/"), "path");
        form.Add(new StringContent("true"), "overwrite");
        var attackerContent = new ByteArrayContent(Encoding.UTF8.GetBytes("attacker payload"));
        attackerContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(attackerContent, "file", name);

        var response = await Client.PostAsync("/api/v1/files/upload", form);

        // 断言①：请求不得成功（他人路径按已存在/不存在处理）
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "向他人已占用的路径上传不应成功");

        // 断言②：受害者物理内容未被替换（核心：写盘先于冲突判定即数据销毁）
        File.Exists(physicalPath).Should().BeTrue("受害者物理文件必须仍在");
        (await File.ReadAllTextAsync(physicalPath)).Should().Be("isolation probe",
            "他人上传不得覆盖受害者文件内容");

        // 断言③：受害者库记录的哈希/大小未变（库与盘不错位）
        var (scope, _, _, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            var ownerItem = await fileItems.GetByPathAsync($"/{name}");
            ownerItem.Should().NotBeNull();
            ownerItem!.SizeBytes.Should().Be(16, "受害者记录大小不应被他人上传改动");
            ownerItem.Hash.Should().Be("iso-hash", "受害者记录哈希不应被他人上传改动");
        }
    }

    /// <summary>取默认池挂载点下某虚拟路径对应的物理路径</summary>
    private async Task<string> GetPhysicalPathAsync(string virtualPath)
    {
        var (scope, _, pools, _, _, _) = CreateDataScope();
        using (scope)
        {
            var pool = await pools.GetDefaultAsync();
            pool.Should().NotBeNull("测试前置已种下默认池");
            return Path.Combine(pool!.MountPath, virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    /// <summary>造属主与旁观者两个用户（每测法唯一名，共享工厂库不撞车），返回两者令牌</summary>
    private async Task<(string OwnerToken, string OtherToken, string OwnerUsername)> TwoUsersAsync()
    {
        await SeedDefaultStoragePoolAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ownerUsername = "iso-owner-" + suffix;
        var ownerToken = await GetAuthTokenAsync(ownerUsername);
        var otherToken = await GetAuthTokenAsync("iso-other-" + suffix);
        return (ownerToken, otherToken, ownerUsername);
    }

    /// <summary>给指定用户种一个文本文件（库记录 + 物理文件）</summary>
    private async Task SeedTestFileAsync(string path, string ownerUsername)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = (await users.GetAllAsync()).FirstOrDefault(u => u.Username == ownerUsername);
            if (user is null) return;
            var pool = await pools.GetDefaultAsync();
            if (pool is null) return;

            var physicalPath = Path.Combine(pool.MountPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
            await File.WriteAllTextAsync(physicalPath, "isolation probe");

            if (await fileItems.GetByPathAsync(path) is not null) return;
            var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                name: Path.GetFileName(path),
                path: path,
                physicalPath: physicalPath,
                sizeBytes: 16,
                mimeType: "text/plain",
                storagePoolId: pool.Id,
                ownerId: user.Id,
                parentId: null,
                hash: "iso-hash");
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private async Task<string> GetStringAsync(string url)
    {
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }
}
