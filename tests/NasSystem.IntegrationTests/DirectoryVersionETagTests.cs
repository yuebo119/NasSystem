// =============================================================================
// NasSystem.IntegrationTests - DirectoryVersionETagTests.cs
// 目录内容版本号与 HTTP 条件请求的行为锁定
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using NasSystem.Application.Files;
using Xunit;

/// <summary>
/// 锁住 browse 端点的 ETag / 304 语义。
/// </summary>
/// <remarks>
/// 这三条各自守一个独立的失效环节，任一环节坏了另两条未必发现：
/// 版本号本身（DirectoryVersion）、提交后递增（UnitOfWork.BumpDirectoryVersions）、
/// 端点比对 If-None-Match（FileEndpoints.GetDirectoryContents）。
/// 特别是第二条：若漏了某个写入口，症状是"改了文件但客户端缓存不失效"，
/// 单看任一条测试都是绿的。
/// </remarks>
public class DirectoryVersionETagTests : ApiTestBase
{
    public DirectoryVersionETagTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public void DirectoryVersion_BumpChangesValue_Monotonically()
    {
        // 用唯一路径避免与其他用例的共享静态字典互相干扰
        var path = $"/etag-test-{Guid.NewGuid():N}";

        DirectoryVersion.Get(path).Should().Be(0, "从未变更过的目录版本为 0");

        DirectoryVersion.Bump(path);
        var afterFirst = DirectoryVersion.Get(path);
        afterFirst.Should().BeGreaterThan(0);

        DirectoryVersion.Bump(path);
        DirectoryVersion.Get(path).Should().BeGreaterThan(afterFirst, "再次变更后版本号必须继续增大");
    }

    [Fact]
    public void DirectoryVersion_BumpForFile_TargetsParentDirectory()
    {
        var root = $"/etag-file-{Guid.NewGuid():N}";

        DirectoryVersion.BumpForFile($"{root}/sub/file.txt");

        DirectoryVersion.Get($"{root}/sub").Should().BeGreaterThan(0, "文件变更应计入其所在目录");
        DirectoryVersion.Get(root).Should().Be(0, "不应波及上级目录");
    }

    [Fact]
    public async Task Browse_SecondRequestWithETag_ReturnsNotModified()
    {
        // Arrange
        var token = await GetAuthTokenAsync("etaguser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        await SeedTestFolderAsync("/documents", "etaguser");

        // Act 1：首次请求，拿到 ETag
        var first = await Client.GetAsync("/api/v1/files/browse/");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull("browse 响应必须带 ETag");

        // Act 2：带 If-None-Match 再请求同一目录
        var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/v1/files/browse/");
        conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag!.Tag));
        var second = await Client.SendAsync(conditional);

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.NotModified, "版本号未变应回 304");
    }

    [Fact]
    public async Task Browse_Subdirectory_AfterWrite_ETagChanges()
    {
        // 2026-10-19 E2E 走查实证的回归：路由参数是裸路径（"etagsub"），
        // 而 bump 的键带前导斜杠（"/etagsub"）。端点曾直接拿裸路径查版本，
        // 恒为 0，子目录写入后条件请求无限 304 重放陈旧列表（根目录因
        // 两种形态都是 "/" 一直正常，既有测试因此全绿）。这条锁子目录
        var token = await GetAuthTokenAsync("etagsubuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var sub = $"/etagsub-{Guid.NewGuid():N}";
        await SeedTestFolderAsync(sub, "etagsubuser");

        var first = await Client.GetAsync($"/api/v1/files/browse{sub}");
        var etagBefore = first.Headers.ETag?.Tag;
        etagBefore.Should().NotBeNull();

        // 子目录内建文件夹：bump 的键是 "/etagsub-xxx"（带斜杠）
        var created = await Client.PostAsJsonAsync("/api/v1/files/folder",
            new { parentPath = sub, name = "child" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, "建文件夹应成功，否则后续断言无意义");

        var second = await Client.GetAsync($"/api/v1/files/browse{sub}");
        var etagAfter = second.Headers.ETag?.Tag;
        etagAfter.Should().NotBe(etagBefore, "子目录写入后 ETag 必须变化（键形态对齐）");

        // 旧 ETag 的条件请求不得 304：否则重放写入前的陈旧列表
        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/browse{sub}");
        conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etagBefore!));
        var stale = await Client.SendAsync(conditional);
        stale.StatusCode.Should().Be(HttpStatusCode.OK, "写入后旧 ETag 不得命中 304");
        var body = await stale.Content.ReadAsStringAsync();
        body.Should().Contain("child", "响应体应包含新写入的条目");
    }
}
