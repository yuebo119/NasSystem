// =============================================================================
// NasSystem.IntegrationTests - FileVersionAutoTests.cs
// 文件版本自动产出与保留策略（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Entities;
using PalORM;
using Xunit;

/// <summary>
/// 锁住版本闭环的四类行为：
/// 一、覆盖上传自动产版本，旧内容可下载可恢复；
/// 二、永久删除前自动产版本，软删除不产（回收站已保住内容）；
/// 三、保留策略：超数量淘汰最旧；
/// 四、恢复版本后库中体积同步（防恢复后浏览显示旧大小的既存缺陷复发）。
/// </summary>
/// <remarks>
/// 造文件走 WebDAV PUT（Basic 认证）：比 multipart 表单少一层样板，
/// 同时交叉验证 Phase 0 协议层与 Phase 1 版本层协同工作。
/// </remarks>
public class FileVersionAutoTests : ApiTestBase, IDisposable
{
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"versions-{Guid.NewGuid():N}");

    private static readonly string PathPrefix = "ver" + Guid.NewGuid().ToString("N")[..6];

    private static bool _poolReady;

    static FileVersionAutoTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public FileVersionAutoTests(NasSystemWebApplicationFactory factory)
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
    public async Task OverwriteUpload_CreatesVersion_OldContentDownloadable()
    {
        var (username, token) = await SeedVersionUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-overwrite.txt";

        await PutDavFileAsync(username, fileName, "first version content");
        await PutDavFileAsync(username, fileName, "second version content");

        var fileId = await GetFileIdAsync(username, token, $"/{fileName}");
        var versions = await GetVersionsAsync(token, fileId);

        versions.Should().HaveCount(1, "覆盖一次产一个版本");
        var version = versions[0];
        version.VersionNumber.Should().Be(1);

        // 版本内容是覆盖前的旧内容
        var download = await Client.SendAsync(
            CreateVersionDownloadRequest(token, fileId, version.VersionNumber));
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsStringAsync()).Should().Be("first version content");
    }

    [Fact]
    public async Task PermanentDelete_CreatesVersion_SoftDeleteDoesNot()
    {
        var (username, token) = await SeedVersionUserAsync();
        await EnsureClassPoolAsync();

        // 软删除：内容进回收站，不产版本
        var softName = $"{PathPrefix}-soft.txt";
        await PutDavFileAsync(username, softName, "soft delete content");
        var softId = await GetFileIdAsync(username, token, $"/{softName}");
        await Client.SendAsync(CreateDavRequest(HttpMethod.Delete, $"/dav/{softName}", username));
        (await GetVersionsAsync(token, softId)).Should().BeEmpty("软删除有回收站兜底，不产版本");

        // 永久删除：内容不可逆，删除前快照进版本库
        var permName = $"{PathPrefix}-perm.txt";
        await PutDavFileAsync(username, permName, "permanent delete content");
        var permId = await GetFileIdAsync(username, token, $"/{permName}");

        var payload = JsonSerializer.Serialize(
            new { paths = new[] { $"/{permName}" }, permanent = true }, JsonOptions);
        var deleteRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/delete")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var delete = await Client.SendAsync(deleteRequest);
        delete.StatusCode.Should().Be(HttpStatusCode.OK);

        var versions = await GetVersionsAsync(token, permId, "永久删除后");
        versions.Should().HaveCount(1, "永久删除前必须快照");
        var download = await Client.SendAsync(
            CreateVersionDownloadRequest(token, permId, versions[0].VersionNumber));
        (await download.Content.ReadAsStringAsync()).Should().Be("permanent delete content");
    }

    [Fact]
    public async Task Retention_EvictsOldest_BeyondDefaultLimit()
    {
        var (username, token) = await SeedVersionUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-retention.txt";

        // 连续覆盖 12 次：首次 PUT 新建文件不产版本，其后 11 次覆盖各产一个，
        // 共 11 个版本；默认保留 10 个，第 11 个版本落库时应淘汰当时最旧的（版本 1）
        for (var i = 1; i <= 12; i++)
        {
            await PutDavFileAsync(username, fileName, $"content {i}");
        }

        var fileId = await GetFileIdAsync(username, token, $"/{fileName}");
        var versions = await GetVersionsAsync(token, fileId);

        versions.Should().HaveCount(10, "默认策略单文件最多 10 个版本");
        versions.Should().OnlyHaveUniqueItems(v => v.VersionNumber);
        // 最旧的版本 1 应被淘汰，剩余从 2 起
        versions.Min(v => v.VersionNumber).Should().Be(2, "淘汰顺序必须从最旧的开始");
    }

    [Fact]
    public async Task RestoreVersion_SyncsSizeInDatabase()
    {
        var (username, token) = await SeedVersionUserAsync();
        await EnsureClassPoolAsync();
        var fileName = $"{PathPrefix}-restore.txt";

        await PutDavFileAsync(username, fileName, "short");
        await PutDavFileAsync(username, fileName, "a much longer replacement content");

        var fileId = await GetFileIdAsync(username, token, $"/{fileName}");
        var versions = await GetVersionsAsync(token, fileId);
        versions.Should().HaveCount(1);

        var restore = await Client.SendAsync(CreateVersionRestoreRequest(token, fileId, versions[0].VersionNumber));
        restore.StatusCode.Should().Be(HttpStatusCode.OK);

        // 恢复后活动内容回到旧版（5 字节），库中体积必须同步：
        // 不同步的话文件信息仍报长内容的字节数（回归锁定，见服务端注释）
        var info = await Client.SendAsync(CreateDavRequest(HttpMethod.Get, $"/api/v1/files/info/{fileName}", username, token: token));
        var body = await info.Content.ReadAsStringAsync();
        body.Should().Contain("\"sizeBytes\":5", "恢复版本后库中体积必须与活动内容一致");
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task<(string Username, string Token)> SeedVersionUserAsync()
    {
        var username = "veruser" + Guid.NewGuid().ToString("N")[..8];
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

            var pool = StoragePool.Create("VersionPool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            _poolReady = true;
        }
    }

    /// <summary>经 WebDAV PUT 造文件，返回文件项 Id（浏览响应里取）</summary>
    private async Task<Guid> GetFileIdAsync(string username, string? token, string virtualPath)
    {
        var relative = virtualPath.TrimStart('/');
        var response = await Client.SendAsync(
            CreateDavRequest(HttpMethod.Get, $"/api/v1/files/info/{relative}", username, token: token));
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"取文件信息失败: {virtualPath}");

        var body = await response.Content.ReadAsStringAsync();
        // 响应形如 {"success":true,"data":{"id":"guid",...}}：按前缀定位 id 字段，
        // 避免为一条断言引入整个反序列化模型
        var marker = "\"id\":\"";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "文件信息响应必须含 id");
        var idStart = start + marker.Length;
        var idEnd = body.IndexOf('"', idStart);
        return Guid.Parse(body[idStart..idEnd]);
    }

    private async Task<List<FileVersionListEntry>> GetVersionsAsync(string token, Guid fileId, string? context = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/file-versions/{fileId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"版本列表请求应成功（{context}），响应: {body}");
        // 不在此处断言非空：软删除场景的合法期望就是空列表，
        // 数量意图由各调用点的计数断言表达
        return ParseVersions(body);
    }

    /// <summary>
    /// 从版本列表 JSON 里提取版本号。不为测试引入共享 DTO 依赖：
    /// 列表条目结构简单（versionNumber/isCurrent 两个关注字段），
    /// 手写定位比维护一份镜像模型更稳（模型随业务变，测试不该跟着碎）
    /// </summary>
    private static List<FileVersionListEntry> ParseVersions(string json)
    {
        var entries = new List<FileVersionListEntry>();
        var searchFrom = 0;
        while (true)
        {
            var marker = "\"versionNumber\":";
            var start = json.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (start < 0) break;
            var valueStart = start + marker.Length;
            var valueEnd = json.IndexOfAny([',', '}'], valueStart);
            if (int.TryParse(json[valueStart..valueEnd], out var number))
            {
                entries.Add(new FileVersionListEntry(number));
            }
            searchFrom = valueEnd;
        }
        return entries;
    }

    private sealed record FileVersionListEntry(int VersionNumber);

    private static HttpRequestMessage CreateVersionDownloadRequest(string token, Guid fileId, int versionNumber)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/file-versions/{fileId}/{versionNumber}/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage CreateVersionRestoreRequest(string token, Guid fileId, int versionNumber)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/file-versions/{fileId}/{versionNumber}/restore");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> PutDavFileAsync(string username, string fileName, string content)
    {
        var request = CreateDavRequest(
            HttpMethod.Put,
            $"/dav/{fileName}",
            username,
            content: new StringContent(content, Encoding.UTF8, "text/plain"));
        var response = await Client.SendAsync(request);
        // 新文件 201，覆盖已存在文件 204（RFC 4918）：两类都算成功
        var succeeded = response.StatusCode is HttpStatusCode.Created or HttpStatusCode.NoContent;
        succeeded.Should().BeTrue($"PUT {fileName} 应成功，实际 {response.StatusCode}");
        return response;
    }
}
