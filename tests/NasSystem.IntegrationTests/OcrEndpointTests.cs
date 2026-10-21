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
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Recognize_ReturnsNormalizedRegions_MatchingDrawnPosition()
    {
        // 2026-10-21 OCR 叠加定位：识别响应必须带行区域（归一化四角），
        // 且坐标与画图时的已知位置吻合——CreateTextImage 把 40px 文字画在
        // (30,110)、画布 600×200，归一化后 min-x≈0.05、min-y≈0.35。
        // 锁坐标而不只锁"有区域"：位置错了前端叠加层会错位，肉眼才看得出来
        var (username, token) = await SeedOcrUserAsync();
        await EnsureClassPoolAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{suffix}-regions.png";
        CreateTextImage(Path.Combine(ClassStorageRoot, fileName), "库存盘点单 2026");
        await UploadAsync(token, fileName);

        var recognize = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, $"/api/v1/files/ocr/{fileName}", token));
        recognize.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await recognize.Content.ReadAsStringAsync());
        var regions = doc.RootElement.GetProperty("data").GetProperty("regions");
        regions.ValueKind.Should().Be(JsonValueKind.Array, "识别响应必须带 regions 数组");
        regions.GetArrayLength().Should().BeGreaterThan(0, "带文字的图至少有一个行区域");

        var first = regions[0];
        var points = first.GetProperty("points");
        points.GetArrayLength().Should().Be(8, "四角坐标 8 值");
        var xs = new List<float>();
        var ys = new List<float>();
        for (var i = 0; i < 8; i++)
        {
            var v = points[i].GetSingle();
            v.Should().BeInRange(0f, 1f, "归一化坐标必须在 0..1");
            (i % 2 == 0 ? xs : ys).Add(v);
        }
        // 与画图位置对账（容差 0.1：PP-OCR 检测框与字体度量框有出入）
        xs.Min().Should().BeInRange(0f, 0.15f, $"文字画在 x=30/600=0.05，实际 min-x={xs.Min()}");
        ys.Min().Should().BeInRange(0.25f, 0.45f, $"文字画在 y=70~110/200=0.35~0.55，实际 min-y={ys.Min()}");
        first.GetProperty("text").GetString().Should().Contain("库存");
    }

    [Fact]
    public async Task GetText_LegacyRowWithoutRegions_DegradesToEmptyArray()
    {
        // 存量行（识别于叠加特性之前）Regions 为 NULL：GET 必须降级为空数组
        // 而不是 null/500——位置信息是增强，缺了不能影响文本主流程
        var (username, token) = await SeedOcrUserAsync();
        await EnsureClassPoolAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"{suffix}-legacy.png";
        CreateTextImage(Path.Combine(ClassStorageRoot, fileName), "旧数据");
        await UploadAsync(token, fileName);

        // 绕过实体直插一行 Regions=NULL 的存量形态（新代码路径 Regions 存 null 即空列表）
        var fileItemId = await GetFileItemIdAsync(token, fileName);
        var (scope, _, _, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var sessions = scope.ServiceProvider
                .GetRequiredService<NasSystem.Infrastructure.Persistence.PalormSessionHolder>();
            var db = await sessions.GetAsync();
            await db.ExecuteAsync(
                System.Runtime.CompilerServices.FormattableStringFactory.Create(
                    """
                    INSERT INTO FileOcrTexts (Id, FileItemId, Text, CharCount, Model, ElapsedMs, CreatedAt)
                    VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})
                    """,
                    Guid.NewGuid(), fileItemId, "旧数据", 3, "legacy-model", 1, DateTime.UtcNow));
        }

        var getText = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, $"/api/v1/files/ocr/{fileName}", token));
        getText.StatusCode.Should().Be(HttpStatusCode.OK, "存量行不能把 GET 打挂");
        using var doc = JsonDocument.Parse(await getText.Content.ReadAsStringAsync());
        var regions = doc.RootElement.GetProperty("data").GetProperty("regions");
        regions.ValueKind.Should().Be(JsonValueKind.Array);
        regions.GetArrayLength().Should().Be(0, "无位置信息的存量行降级为空数组");
        doc.RootElement.GetProperty("data").GetProperty("text").GetString().Should().Be("旧数据");
    }

    private async Task<Guid> GetFileItemIdAsync(string token, string fileName)
    {
        var info = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, $"/api/v1/files/info/{fileName}", token));
        using var doc = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        return Guid.Parse(doc.RootElement.GetProperty("data").GetProperty("id").GetString()!);
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
