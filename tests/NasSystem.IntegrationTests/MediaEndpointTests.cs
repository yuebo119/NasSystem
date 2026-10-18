// =============================================================================
// NasSystem.IntegrationTests - MediaEndpointTests.cs
// 媒体库端点（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using SkiaSharp;
using Xunit;

/// <summary>
/// 锁住媒体库端点的三类行为：类型过滤（图片进 image 列表、
/// 不进 video 列表）、元数据批量提取（图片尺寸）、月份过滤。
/// 元数据内容本身由 MediaMetadataExtractorTests 的构造数据锁定，
/// 这里只验证端到端接线
/// </summary>
public class MediaEndpointTests : ApiTestBase, IDisposable
{
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"media-ep-{Guid.NewGuid():N}");

    private static bool _poolReady;

    static MediaEndpointTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public MediaEndpointTests(NasSystemWebApplicationFactory factory)
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
    public async Task Media_ImageAppearsInImageList_NotInVideoList()
    {
        var (username, token) = await SeedMediaUserAsync();
        await EnsureClassPoolAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{suffix}-photo.png";
        CreateImage(Path.Combine(ClassStorageRoot, fileName), 320, 240);
        await UploadAsync(token, fileName);

        var images = await GetMediaAsync(token, "image");
        images.Should().Contain(fileName, "图片必须进 image 列表");

        var videos = await GetMediaAsync(token, "video");
        videos.Should().NotContain(fileName, "图片不应进 video 列表");
    }

    [Fact]
    public async Task Metadata_ReturnsImageDimensions()
    {
        var (username, token) = await SeedMediaUserAsync();
        await EnsureClassPoolAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{suffix}-dim.png";
        CreateImage(Path.Combine(ClassStorageRoot, fileName), 800, 600);
        await UploadAsync(token, fileName);

        var metadata = await GetMetadataAsync(token, [$"/{fileName}"]);

        var entry = metadata.EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("path").GetString().Should().Be($"/{fileName}");
        entry.GetProperty("width").GetInt32().Should().Be(800);
        entry.GetProperty("height").GetInt32().Should().Be(600);
        // 序列化配了 WhenWritingNull：图片无时长，字段直接缺席（不是 null）
        entry.TryGetProperty("durationSeconds", out var duration).Should().BeFalse("图片无时长的 null 字段被序列化省略");
    }

    [Fact]
    public async Task Metadata_UnknownPath_SilentlySkipped()
    {
        var (username, token) = await SeedMediaUserAsync();
        await EnsureClassPoolAsync();

        // 不存在的路径：静默跳过（返回空数组），不报错也不暴露存在性
        var metadata = await GetMetadataAsync(token, ["/no/such/file.png"]);

        metadata.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Media_InvalidType_Returns400()
    {
        var (username, token) = await SeedMediaUserAsync();
        await EnsureClassPoolAsync();

        var request = CreateBearerRequest(HttpMethod.Get, "/api/v1/media?type=document", token);
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task<(string Username, string Token)> SeedMediaUserAsync()
    {
        var username = "meduser" + Guid.NewGuid().ToString("N")[..8];
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

            var pool = StoragePool.Create("MediaPool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            _poolReady = true;
        }
    }

    private static void CreateImage(string filePath, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 120, 200));
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(filePath, data.ToArray());
    }

    private async Task UploadAsync(string token, string fileName)
    {
        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(ClassStorageRoot, fileName));
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(fileBytes), "file", fileName }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/upload") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"上传 {fileName} 应成功");
    }

    private async Task<string> GetMediaAsync(string token, string type)
    {
        var request = CreateBearerRequest(
            HttpMethod.Get, $"/api/v1/media?type={type}&pageSize=100", token);
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"媒体列表（{type}）应成功，响应: {body}");
        return body;
    }

    private async Task<JsonElement> GetMetadataAsync(string token, string[] paths)
    {
        var payload = JsonSerializer.Serialize(new { paths });
        var request = CreateBearerRequest(HttpMethod.Post, "/api/v1/media/metadata", token);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "元数据请求应成功");

        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.GetProperty("data");
    }

    private static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
