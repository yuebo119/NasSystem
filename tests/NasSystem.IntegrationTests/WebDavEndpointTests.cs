// =============================================================================
// NasSystem.IntegrationTests - WebDavEndpointTests.cs
// WebDAV 协议端点行为锁定（ROADMAP Phase 0）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住 WebDAV 端点的协议语义：能力协商、Basic 认证、PROPFIND 目录枚举、
/// PUT/GET 往返、MKCOL、软删除、同目录 MOVE 换名。
/// </summary>
/// <remarks>
/// 与网页端的两处关键差异在这里各有一条锁定：
/// 一、DELETE 走软删除进回收站（与网页端口径一致，不旁路应用层）；
/// 二、PUT 放开 Kestrel 500MB 全局上限（资源管理器不遵守网页上传约定）。
/// 认证失败锁定另有 WebDavAuthLockoutTests 独立类：10 次失败即锁的用例
/// 会污染同工厂后续请求的 401 预期，必须隔离。
/// </remarks>
public class WebDavEndpointTests : ApiTestBase, IDisposable
{
    /// <summary>本类共享的真实存储根（PUT/MKCOL 要落物理文件）</summary>
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"webdav-{Guid.NewGuid():N}");

    private static readonly string PathPrefix = "wd" + Guid.NewGuid().ToString("N")[..6];

    private static bool _poolReady;

    static WebDavEndpointTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public WebDavEndpointTests(NasSystemWebApplicationFactory factory)
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

    // -------------------------------------------------------------------------
    // 能力协商与认证
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Options_WithoutCredentials_AdvertisesDavCapabilities()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/dav/");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "OPTIONS 探测必须免认证（RFC 4918 实践）");
        // Allow 在 .NET HttpClient 侧归入 content header 类（历史分类），
        // 从 Content.Headers 读，response.Headers.GetValues 会抛 Misused header name
        var allow = response.Content.Headers.GetValues("Allow");
        string.Join(",", allow).Should().Contain("PROPFIND").And.Contain("PUT");
        response.Headers.Contains("DAV").Should().BeTrue("必须声明 DAV class 能力");
    }

    [Fact]
    public async Task PropFind_WithoutCredentials_Returns401WithChallenge()
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/dav/");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.GetValues("WWW-Authenticate");
        string.Join(" ", challenge).Should().Contain("Basic", "必须提示客户端走 Basic 认证");
    }

    [Fact]
    public async Task Put_WithWrongPassword_Returns401()
    {
        var (username, _) = await SeedDavUserAsync();
        var request = CreateDavRequest(
            HttpMethod.Put,
            $"/dav/{PathPrefix}-wrong.txt",
            username,
            password: "wrong-password",
            content: new StringContent("should not land"));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        File.Exists(Path.Combine(ClassStorageRoot, $"{PathPrefix}-wrong.txt")).Should().BeFalse("认证失败的请求不得落盘");
    }

    [Fact]
    public async Task Lock_Verb_Returns405WithAllowHeader()
    {
        // LOCK 未实现：路由注册了该动词，认证通过后必须回 405 + Allow，
        // 而不是落进 SPA fallback 返回 index.html。
        // 无凭证时会先吃 401（认证先于方法分发），故这里带合法 Basic 凭证
        var (username, _) = await SeedDavUserAsync();
        var request = CreateDavRequest(new HttpMethod("LOCK"), "/dav/", username);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var allow = response.Content.Headers.GetValues("Allow");
        string.Join(",", allow).Should().Contain("PROPFIND");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("<!DOCTYPE html", "WebDAV 动词绝不能拿到 SPA HTML");
    }

    // -------------------------------------------------------------------------
    // PROPFIND
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PropFind_Root_Returns207MultistatusWithCollection()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        await SeedDavFolderAsync($"/{PathPrefix}-docs", username);

        var response = await Client.SendAsync(CreateDavRequest(new HttpMethod("PROPFIND"), "/dav/", username));

        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus, "PROPFIND 按 RFC 4918 回 207");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("multistatus");
        body.Should().Contain("DAV:");
        body.Should().Contain("collection", "根目录是集合类型");
        body.Should().Contain($"{PathPrefix}-docs", "Depth 默认枚举应包含种子目录");
    }

    [Fact]
    public async Task PropFind_OnSingleFile_ReturnsOnlyThatEntry()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-single.txt";
        await PutDavFileAsync(username, fileName, "single file content");

        var request = CreateDavRequest(new HttpMethod("PROPFIND"), $"/dav/{fileName}", username);
        request.Headers.Add("Depth", "0");
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(fileName);
        body.Should().Contain("getcontentlength", "单文件属性必须带内容长度");
    }

    [Fact]
    public async Task PropFind_NonexistentPath_Returns404InMultistatus()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();

        var request = CreateDavRequest(new HttpMethod("PROPFIND"), $"/dav/{PathPrefix}-missing-{Guid.NewGuid():N}", username);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.MultiStatus, "WebDAV 的 404 也必须包在 multistatus 里");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("404");
    }

    // -------------------------------------------------------------------------
    // 写操作
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MkCol_CreatesFolder_ThenVisibleInPropFind()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var folderName = $"{PathPrefix}-mkcol";

        var response = await Client.SendAsync(CreateDavRequest(new HttpMethod("MKCOL"), $"/dav/{folderName}", username));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var propFind = await Client.SendAsync(CreateDavRequest(new HttpMethod("PROPFIND"), "/dav/", username));
        var body = await propFind.Content.ReadAsStringAsync();
        body.Should().Contain(folderName, "MKCOL 成功后 PROPFIND 必须能枚举到");
    }

    [Fact]
    public async Task MkCol_ExistingFolder_Returns405()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var folderName = $"{PathPrefix}-dup";
        await Client.SendAsync(CreateDavRequest(new HttpMethod("MKCOL"), $"/dav/{folderName}", username));

        var second = await Client.SendAsync(CreateDavRequest(new HttpMethod("MKCOL"), $"/dav/{folderName}", username));

        second.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "RFC 4918：MKCOL 目标已存在回 405");
    }

    [Fact]
    public async Task Put_NewFile_Returns201_AndGetRoundTrips()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-roundtrip.txt";
        const string Content = "webdav roundtrip 内容";

        var put = await PutDavFileAsync(username, fileName, Content);
        put.StatusCode.Should().Be(HttpStatusCode.Created, "新资源 PUT 回 201");

        var get = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/dav/{fileName}", username));
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloaded = await get.Content.ReadAsStringAsync();
        downloaded.Should().Be(Content);
    }

    [Fact]
    public async Task Put_Overwrite_Returns204_AndContentReplaced()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-overwrite.txt";
        await PutDavFileAsync(username, fileName, "first version");

        var second = await PutDavFileAsync(username, fileName, "second version");

        second.StatusCode.Should().Be(HttpStatusCode.NoContent, "覆盖已存在资源 PUT 回 204");
        var get = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/dav/{fileName}", username));
        (await get.Content.ReadAsStringAsync()).Should().Be("second version");
    }

    [Fact]
    public async Task Delete_SoftDeletesToRecycleBin_NotPhysicalDelete()
    {
        var (username, token) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-softdelete.txt";
        await PutDavFileAsync(username, fileName, "to be soft deleted");

        var delete = await Client.SendAsync(CreateDavRequest(HttpMethod.Delete, $"/dav/{fileName}", username));

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/dav/{fileName}", username));
        get.StatusCode.Should().Be(HttpStatusCode.NotFound, "删除后 GET 应 404");

        // 关键口径：WebDAV DELETE 与网页端一致走软删除，回收站能捞回
        var recycleBin = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, "/api/v1/files/recycle-bin", username, token: token));
        recycleBin.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await recycleBin.Content.ReadAsStringAsync();
        body.Should().Contain(fileName, "WebDAV 删除必须进回收站而非物理删除");
    }

    [Fact]
    public async Task Move_SameDirectory_RenamesFile()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();
        var sourceName = $"{PathPrefix}-src.txt";
        var targetName = $"{PathPrefix}-dst.txt";
        await PutDavFileAsync(username, sourceName, "move me");

        var move = CreateDavRequest(new HttpMethod("MOVE"), $"/dav/{sourceName}", username);
        move.Headers.Add("Destination", $"/dav/{targetName}");
        var response = await Client.SendAsync(move);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var oldGet = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/dav/{sourceName}", username));
        oldGet.StatusCode.Should().Be(HttpStatusCode.NotFound, "换名后旧路径应不存在");

        var newGet = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/dav/{targetName}", username));
        newGet.StatusCode.Should().Be(HttpStatusCode.OK);
        (await newGet.Content.ReadAsStringAsync()).Should().Be("move me");
    }

    [Fact]
    public async Task Put_ExceedingQuota_Returns507InsufficientStorage()
    {
        var (username, _) = await SeedDavUserAsync();
        await EnsureClassPoolAsync();

        // 给用户压一个 1KB 配额：流式上传拿不到声明大小，超限由写入循环
        // 边写边检发现（同网页上传管道），WebDAV 侧必须映射成 507
        var (scope, users, _, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var all = await users.GetAllAsync();
            var user = all.First(u => u.Username == username);
            user.SetQuota(1024);
            users.UpdateAsync(user);
            await unitOfWork.SaveChangesAsync();
        }

        var big = new string('x', 4096);
        var response = await PutDavFileAsync(username, $"{PathPrefix}-overquota.txt", big);

        response.StatusCode.Should().Be(HttpStatusCode.InsufficientStorage,
            "配额耗尽按 RFC 4918 回 507，客户端才会保留文件待重试");
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    /// <summary>
    /// 建用户并返回用户名与 JWT。密码固定 password123（GetAuthTokenAsync 的种子口径），
    /// WebDAV Basic 认证用同一组凭证，网页端接口（回收站）用 JWT
    /// </summary>
    private async Task<(string Username, string Token)> SeedDavUserAsync()
    {
        var username = "davuser" + Guid.NewGuid().ToString("N")[..8];
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

            var pool = StoragePool.Create("WebDavPool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            _poolReady = true;
        }
    }

    private async Task SeedDavFolderAsync(string path, string ownerUsername)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var allUsers = await users.GetAllAsync();
            var user = allUsers.FirstOrDefault(u => u.Username == ownerUsername);
            if (user is null) return;

            var pool = await pools.GetDefaultAsync();
            if (pool is null) return;

            if (await fileItems.GetByPathAsync(path) is not null)
                return;

            var folder = FileItem.CreateFolder(
                name: path.TrimStart('/').Split('/').Last(),
                path: path,
                physicalPath: Path.Combine(ClassStorageRoot, path.TrimStart('/')),
                storagePoolId: pool.Id,
                ownerId: user.Id);

            await fileItems.AddAsync(folder);
            await unitOfWork.SaveChangesAsync();
        }
    }

    private async Task<HttpResponseMessage> PutDavFileAsync(string username, string fileName, string content)
    {
        var request = CreateDavRequest(
            HttpMethod.Put,
            $"/dav/{fileName}",
            username,
            content: new StringContent(content, Encoding.UTF8, "text/plain"));
        return await Client.SendAsync(request);
    }
}

/// <summary>
/// WebDAV Basic 认证失败锁定。独立于 WebDavEndpointTests：
/// 10 次失败即锁会污染同工厂内其他用例的 401 预期，
/// IClassFixture 保证每个测试类一个独立工厂（独立 guard 单例）
/// </summary>
public class WebDavAuthLockoutTests : ApiTestBase
{
    public WebDavAuthLockoutTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task RepeatedFailedAuth_LocksOutAfterTenFailures()
    {
        // 用不存在的用户名：失败路径最短，不依赖种子数据
        var clientKey = "lockout-test";

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/dav/");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"no-such-user-{clientKey}:bad-password"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            var response = await Client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"第 {attempt} 次失败应仍回 401（未达锁定阈值）");
        }

        // 第 11 次：即使凭证碰巧有效也必须先过锁定关
        var locked = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/dav/");
        var validCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("anyone:anything"));
        locked.Headers.Authorization = new AuthenticationHeaderValue("Basic", validCredentials);
        var lockedResponse = await Client.SendAsync(locked);

        lockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "同客户端 10 次失败后必须锁定，防 Basic 认证暴力破解");
    }
}
