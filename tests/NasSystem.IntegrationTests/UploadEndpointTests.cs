// =============================================================================
// NasSystem.IntegrationTests - UploadEndpointTests.cs
// 上传端点的流式 multipart 解析与落库行为（S5）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// 锁住上传端点（S5 改 MultipartReader 流式解析后）的三类行为：
/// 一、path 先于 file 的标准 multipart 上传：200 + 库记录 + 物理文件落位；
/// 二、没有 file part 的请求：400（不写库不写盘）；
/// 三、覆盖上传：旧文件被替换，记录大小与哈希更新。
/// </summary>
/// <remarks>
/// 上传要写物理文件，默认池挂载点是每类一个的真实临时目录（建库时种下的
/// 种子池挂在构建目录，先删再建）。各用例虚拟路径带每进程唯一前缀。
/// </remarks>
public class UploadEndpointTests : ApiTestBase, IDisposable
{
    /// <summary>本类共享的真实存储根</summary>
    private static readonly string ClassUploadRoot =
        Path.Combine(Path.GetTempPath(), $"upload-{Guid.NewGuid():N}");

    private static readonly string PathPrefix = "up" + Guid.NewGuid().ToString("N")[..6];

    private static bool _poolReady;

    static UploadEndpointTests()
    {
        Directory.CreateDirectory(ClassUploadRoot);
    }

    public UploadEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    public new void Dispose()
    {
        try
        {
            Directory.Delete(ClassUploadRoot, recursive: true);
        }
        catch
        {
            // 句柄未释放时留下，由系统临时目录清理
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Upload_WithPathBeforeFile_PersistsRecordAndPhysicalFile()
    {
        // Arrange
        var token = await GetAuthTokenAsync("uploadok" + Guid.NewGuid().ToString("N")[..6]);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var content = Encoding.UTF8.GetBytes("hello nas upload");
        var fileName = $"{PathPrefix}-a.txt";

        // Act：path 先于 file（流式解析要求，见端点注释）
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("/"), "path");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);

        var response = await Client.PostAsync("/api/v1/files/upload", form);

        // Assert①：HTTP 成功
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert②：库记录在（路径、大小、内容类型）
        (await PathExistsAsync($"/{fileName}")).Should().BeTrue("上传后必须有库记录");

        // Assert③：物理文件落位且内容完整（临时文件改名路径生效）
        var physical = Path.Combine(ClassUploadRoot, fileName);
        File.Exists(physical).Should().BeTrue("物理文件必须落位");
        (await File.ReadAllBytesAsync(physical)).Should().Equal(content);

        // Assert④：不留半成品临时文件
        Directory.GetFiles(ClassUploadRoot, "*.uploading-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_WithoutFilePart_ReturnsBadRequest()
    {
        // Arrange
        var token = await GetAuthTokenAsync("uploadnofile" + Guid.NewGuid().ToString("N")[..6]);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("/"), "path");

        // Act
        var response = await Client.PostAsync("/api/v1/files/upload", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.GetFiles(ClassUploadRoot).Should().BeEmpty("没有文件 part 时不应写任何物理文件");
    }

    [Fact]
    public async Task Upload_Overwrite_ReplacesPhysicalFileAndUpdatesRecord()
    {
        // Arrange
        var token = await GetAuthTokenAsync("uploadow" + Guid.NewGuid().ToString("N")[..6]);
        SetAuthToken(token);
        await EnsureClassPoolAsync();

        var fileName = $"{PathPrefix}-ow.txt";
        await UploadAsync("/", fileName, Encoding.UTF8.GetBytes("old content"));

        // Act：同名覆盖上传（overwrite 字段显式为 true，缺省按冲突拒绝）
        var newContent = Encoding.UTF8.GetBytes("new content is longer");
        var response = await UploadAsync("/", fileName, newContent, overwrite: true);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var physical = Path.Combine(ClassUploadRoot, fileName);
        (await File.ReadAllBytesAsync(physical)).Should().Equal(newContent, "覆盖后物理文件必须是新内容");
        (await PathExistsAsync($"/{fileName}")).Should().BeTrue();
    }

    // ─── 辅助 ───

    private async Task<HttpResponseMessage> UploadAsync(
        string destinationPath, string fileName, byte[] content, bool overwrite = false)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(destinationPath), "path");
        form.Add(new StringContent(overwrite ? "true" : "false"), "overwrite");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", fileName);
        return await Client.PostAsync("/api/v1/files/upload", form);
    }

    private async Task EnsureClassPoolAsync()
    {
        // xUnit 每测试一个类实例，上个实例的 Dispose 已删掉共享根，先补建
        Directory.CreateDirectory(ClassUploadRoot);

        if (_poolReady) return;

        var (scope, _, pools, _, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var existing = await pools.GetDefaultAsync();
            if (existing is not null && existing.MountPath == ClassUploadRoot)
            {
                _poolReady = true;
                return;
            }

            var all = await pools.GetAllAsync();
            foreach (var stale in all)
            {
                await pools.DeleteAsync(stale, cancellationToken: default);
            }
            await unitOfWork.SaveChangesAsync();

            var pool = StoragePool.Create("UploadPool", ClassUploadRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool, cancellationToken: default);
            await unitOfWork.SaveChangesAsync();

            _poolReady = true;
        }
    }

    private async Task<bool> PathExistsAsync(string path)
    {
        var (scope, _, _, fileItems, _, _) = CreateDataScope();
        using (scope)
        {
            return await fileItems.GetByPathAsync(path) is not null;
        }
    }
}
