// =============================================================================
// NasSystem.IntegrationTests - OcrEndpointTests.cs
// 图片 OCR 端到端（ROADMAP Phase 2，SimdPaddleOCR）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Domain.Entities;
using SkiaSharp;
using Xunit;

/// <summary>
/// 真跑推理的端到端锁定：造一张带文字的 PNG → 上传 → 识别 → 搜索命中。
/// </summary>
/// <remarks>
/// 这是 OCR 功能唯一的执行证据：识别管线（解码→像素→推理→落库→搜索）
/// 任何一环断掉，症状都是"识别不出文字"，而只有真跑才能发现。
/// 首次识别含模型加载（嵌入资源解析，秒级），单测试可接受。
/// 断言用"包含关键子串"而非全文相等：PP-OCR 对渲染字体的个别字符
/// （尤其是标点与数字）可能有小偏差，锁子串既证明链路通又不脆
/// </remarks>
public class OcrEndpointTests : ApiTestBase, IDisposable
{
    private static readonly string ClassStorageRoot =
        Path.Combine(Path.GetTempPath(), $"ocr-{Guid.NewGuid():N}");

    private static bool _poolReady;

    static OcrEndpointTests()
    {
        Directory.CreateDirectory(ClassStorageRoot);
    }

    public OcrEndpointTests(NasSystemWebApplicationFactory factory)
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
    public async Task Recognize_ThenSearch_FindsTextInsideImage()
    {
        var (username, token) = await SeedOcrUserAsync();
        await EnsureClassPoolAsync();

        // ① 造图：白底黑字标准无衬线，PP-OCR 对清晰印刷体识别率最高
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{suffix}-scan.png";
        CreateTextImage(Path.Combine(ClassStorageRoot, fileName), "库存盘点单 2026");

        // ② 经标准上传端点入库（与真实用户行为一致）
        await UploadAsync(token, fileName);

        // ③ 识别
        var recognize = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/v1/files/ocr/{fileName}", token));
        recognize.StatusCode.Should().Be(HttpStatusCode.OK, "识别请求应成功");
        var recognizeBody = await recognize.Content.ReadAsStringAsync();
        recognizeBody.Should().Contain("库存", "图中文字必须被识别出来");

        // ④ 查询已识别文本（GET 分支）
        var getText = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, $"/api/v1/files/ocr/{fileName}", token));
        getText.StatusCode.Should().Be(HttpStatusCode.OK);
        (await getText.Content.ReadAsStringAsync()).Should().Contain("库存");

        // ⑤ 关键链路：文件名不含"盘点"，只有图内文字含——搜索必须命中。
        // 这一步同时验证 OCR 落库与搜索的子查询 UNION 两个改动
        var search = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/files/search?query=盘点", token));
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchBody = await search.Content.ReadAsStringAsync();
        searchBody.Should().Contain(fileName, "搜图内文字必须能找到该图片（文件名并无此词）");
    }

    [Fact]
    public async Task Recognize_NonImage_Returns400()
    {
        var (username, token) = await SeedOcrUserAsync();
        await EnsureClassPoolAsync();

        // 文本文件不是图片：400 而不是 500
        var textFile = $"{Guid.NewGuid().ToString("N")[..8]}-note.txt";
        await File.WriteAllTextAsync(Path.Combine(ClassStorageRoot, textFile), "not an image");
        await UploadAsync(token, textFile);

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/v1/files/ocr/{textFile}", token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task<(string Username, string Token)> SeedOcrUserAsync()
    {
        var username = "ocruser" + Guid.NewGuid().ToString("N")[..8];
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

            var pool = StoragePool.Create("OcrPool", ClassStorageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            _poolReady = true;
        }
    }

    /// <summary>SkiaSharp 画白底黑字图（复用缩略图管线同款解码器能读的格式）</summary>
    private static void CreateTextImage(string filePath, string text)
    {
        using var bitmap = new SKBitmap(600, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        // SkiaSharp 3.x：文本绘制走 SKFont + SKPaint 组合，
        // SKPaint 上的 TextSize/Typeface 属性已移除（3.0 破坏性变更）
        using var typeface = SKTypeface.FromFamilyName(
            "Microsoft YaHei", SKFontStyle.Bold) ?? SKTypeface.FromFamilyName("sans-serif");
        using var font = new SKFont(typeface, 40);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 30, 110, font, paint);
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

    private static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
