// =============================================================================
// NasSystem.IntegrationTests - ThumbnailEndpointTests.cs
// 缩略图端点的物理路径解析与缓存头
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住缩略图端点两件事：虚拟路径到物理路径的解析、条件请求缓存头。
/// </summary>
/// <remarks>
/// 物理路径解析这条曾经是坏的：端点把 URL 里的虚拟路径直接交给服务，
/// 服务用 File.Exists 判断，恒为 false，缩略图功能整体不工作。
/// 这类缺陷数据断言抓不到（库里记录是对的），只有真的发一次 HTTP 才暴露。
/// </remarks>
public class ThumbnailEndpointTests : ApiTestBase
{
    public ThumbnailEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    /// <summary>
    /// 用 SkiaSharp 生成一个 4x4 的有效 PNG。不手写字节数组：手工拼的 PNG
    /// 极易在 CRC 或 zlib 流上出错，症状是"文件找到了但解码失败"，
    /// 把路径解析的问题掩盖成图片格式问题
    /// </summary>
    private static byte[] CreateTinyPng()
    {
        using var bitmap = new SkiaSharp.SKBitmap(4, 4);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task GetThumbnail_ResolvesVirtualPathToPhysicalFile()
    {
        // Arrange：在临时目录放一个真实 PNG，并在库里登记它的虚拟路径
        var token = await GetAuthTokenAsync("thumbuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), $"thumb-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var virtualPathForTest1 = "";
        try
        {
            var physicalFile = Path.Combine(storageRoot, "probe.png");
            await File.WriteAllBytesAsync(physicalFile, CreateTinyPng());

            var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
            using (scope)
            {
                var user = (await users.GetAllAsync()).First(u => u.Username == "thumbuser");
                var pool = await pools.GetDefaultAsync();
                // 路径带 GUID：工厂的库在同 fixture 内跨用例共享，固定路径会撞
                // FileItems.Path 的唯一部分索引
                var virtualPath = $"/probe-{Guid.NewGuid():N}.png";
                await fileItems.AddAsync(FileItem.CreateFile(
                    Path.GetFileName(virtualPath), virtualPath, physicalFile, CreateTinyPng().Length,
                    "image/png", pool!.Id, user.Id));
                await unitOfWork.SaveChangesAsync();
                virtualPathForTest1 = virtualPath;
            }

            // Act
            var response = await Client.GetAsync($"/api/v1/thumbnails{virtualPathForTest1}?width=128&height=128");

            // Assert：必须真的解析到物理文件并生成缩略图，而不是 FileNotFound
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "虚拟路径应被解析为库里登记的物理路径；原先直接传虚拟路径，File.Exists 恒 false");
            response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetThumbnail_SecondRequestWithETag_ReturnsNotModified()
    {
        var token = await GetAuthTokenAsync("thumbetaguser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), $"thumb-etag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var virtualPath = $"/probe-{Guid.NewGuid():N}.png";
        try
        {
            var physicalFile = Path.Combine(storageRoot, "probe.png");
            await File.WriteAllBytesAsync(physicalFile, CreateTinyPng());

            var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
            using (scope)
            {
                var user = (await users.GetAllAsync()).First(u => u.Username == "thumbetaguser");
                var pool = await pools.GetDefaultAsync();
                await fileItems.AddAsync(FileItem.CreateFile(
                    Path.GetFileName(virtualPath), virtualPath, physicalFile, CreateTinyPng().Length,
                    "image/png", pool!.Id, user.Id));
                await unitOfWork.SaveChangesAsync();
            }

            var url = $"/api/v1/thumbnails{virtualPath}?width=128&height=128";

            var first = await Client.GetAsync(url);
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            var etag = first.Headers.ETag;
            etag.Should().NotBeNull("缩略图响应必须带 ETag");
            first.Content.Headers.ContentLength.Should().BeGreaterThan(0);
            first.Headers.CacheControl?.ToString().Should().Contain("max-age");

            var conditional = new HttpRequestMessage(HttpMethod.Get, url);
            conditional.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag!.Tag));
            var second = await Client.SendAsync(conditional);

            second.StatusCode.Should().Be(HttpStatusCode.NotModified, "ETag 未变应回 304");
            var body = await second.Content.ReadAsByteArrayAsync();
            body.Should().BeEmpty("304 不应带响应体");
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetThumbnail_WithoutDatabaseRecord_ReturnsFailureNotCrash()
    {
        // 库里没有对应记录时退回原路径，服务给出 FileNotFound 错误而不是崩
        var token = await GetAuthTokenAsync("thumbmissuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var response = await Client.GetAsync("/api/v1/thumbnails/nonexistent.png?width=128&height=128");

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.NotFound, HttpStatusCode.InternalServerError);
        // 关键是"有响应"而不是连接被重置或挂起
    }

    [Fact]
    public async Task GetThumbnail_WithNonAsciiFileName_ReturnsThumbnailNotHeaderCrash()
    {
        // 2026-10-19 E2E 走查实证的回归：ETag 用裸路径拼，中文文件名
        // （"副本"等）让 Kestrel 抛 Invalid non-ASCII character in header，
        // 缩略图整个 500。ETag 现经 EscapeDataString 转 ASCII
        var token = await GetAuthTokenAsync("thumbcnuser");
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), $"thumb-cn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            var physicalFile = Path.Combine(storageRoot, "中文 图片.png");
            await File.WriteAllBytesAsync(physicalFile, CreateTinyPng());

            var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
            using (scope)
            {
                var user = (await users.GetAllAsync()).First(u => u.Username == "thumbcnuser");
                var pool = await pools.GetDefaultAsync();
                var virtualPath = $"/中文-{Guid.NewGuid():N} 图片.png";
                await fileItems.AddAsync(FileItem.CreateFile(
                    Path.GetFileName(virtualPath), virtualPath, physicalFile, CreateTinyPng().Length,
                    "image/png", pool!.Id, user.Id));
                await unitOfWork.SaveChangesAsync();

                // URL 编码后发送（浏览器/img 标签也是这样发的）
                var encoded = Uri.EscapeDataString(virtualPath.TrimStart('/'));
                var response = await Client.GetAsync(
                    $"/api/v1/thumbnails/{encoded}?width=128&height=128");

                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    "中文文件名的缩略图应正常返回；ETag 头不允许非 ASCII 是曾经的崩因");
                response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
                response.Headers.ETag.Should().NotBeNull("响应要带 ETag（转义后的 ASCII 形态）");
            }
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }
}
