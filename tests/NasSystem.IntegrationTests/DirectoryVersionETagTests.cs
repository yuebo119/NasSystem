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
    public async Task Browse_AfterWrite_ETagChanges()
    {
        // Arrange
        var token = await GetAuthTokenAsync("etagwriteuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        await SeedTestFolderAsync("/documents", "etagwriteuser");

        var first = await Client.GetAsync("/api/v1/files/browse/");
        var etagBefore = first.Headers.ETag?.Tag;
        etagBefore.Should().NotBeNull();

        // Act：在根目录建一个文件夹（走 UnitOfWork 提交，应递增根目录版本）
        var created = await Client.PostAsJsonAsync("/api/v1/files/folder",
            new { parentPath = "/", name = "etag-new-folder" });
        created.StatusCode.Should().Be(HttpStatusCode.OK, "建文件夹应成功，否则后续断言无意义");

        // Assert：版本号必须变，否则客户端会一直拿旧缓存
        var second = await Client.GetAsync("/api/v1/files/browse/");
        var etagAfter = second.Headers.ETag?.Tag;
        etagAfter.Should().NotBe(etagBefore, "写入提交后目录版本号必须递增，ETag 必须变");
    }
}
