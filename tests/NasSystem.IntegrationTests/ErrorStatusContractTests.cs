// =============================================================================
// NasSystem.IntegrationTests - ErrorStatusContractTests.cs
// 错误状态码契约：客户端错误不落 500
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// 锁两条 2026-10-19 全链路审计发现的契约缺陷（修复前红，修复后绿）：
/// ① 必填参数缺失曾落 500：minimal API 绑定层抛 BadHttpRequestException，
///    全局异常处理器默认分支把它当内部错误，调用方会把"少传参数"当"服务端故障"重试
/// ② FileShare.FileNotFound 等"模块.实体NotFound"形态曾落 500：ResultExtensions
///    的 NotFound 后缀匹配带点（".NotFound"），漏掉 FileNotFound/DeviceNotFound
/// </summary>
public class ErrorStatusContractTests : ApiTestBase
{
    public ErrorStatusContractTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task MissingRequiredQueryParameter_Returns400Not500()
    {
        // Arrange：/files/recent 的 count 是必填 int，不带即触发绑定失败
        var token = await GetAuthTokenAsync($"contract-{Guid.NewGuid():N}");
        SetAuthToken(token);

        // Act
        var response = await Client.GetAsync("/api/v1/files/recent");

        // Assert：400 而不是 500。响应体不在测试宿主中断言：TestServer 对
        // BadHttpRequestException 有自己的转换（在应用异常处理器之前拦截），
        // 宿主里的响应是空体；带 ApiResponse 形状的 JSON 错误体已对真实
        // 实例实测（Error.BadRequest + 参数缺失原因）
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShareForNonexistentFile_Returns404Not500()
    {
        // Arrange：已认证用户给不存在的 fileItemId 建分享
        var token = await GetAuthTokenAsync($"contract-{Guid.NewGuid():N}");
        SetAuthToken(token);

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/file-shares", new
        {
            fileItemId = Guid.NewGuid().ToString()
        });

        // Assert：404（引用了不存在的东西），不是 500
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("FileShare.FileNotFound");
    }

    [Fact]
    public async Task PasswordShareDownloadWithoutToken_Returns400WithReason()
    {
        // Arrange：建一个密码型分享（先种子一个文件，再经 API 建分享）
        var username = $"contract-{Guid.NewGuid():N}";
        var token = await GetAuthTokenAsync(username);
        SetAuthToken(token);
        await SeedDefaultStoragePoolAsync();
        var fileId = await SeedFileAsync(username, "pwd-share-probe.txt");

        var created = await Client.PostAsJsonAsync("/api/v1/file-shares", new
        {
            fileItemId = fileId,
            accessType = 1,
            password = "pass123",
            permission = 1
        });
        var shareCode = await ReadShareCodeAsync(created);

        // Act：匿名不带 token 直接下载
        var response = await Client.GetAsync($"/api/v1/public-shares/{shareCode}/download");

        // Assert：400（客户端错误：缺密码令牌）且错误体带真实原因，
        // 不是 500"服务器错误"。此前 FileShare.PasswordRequired 落 500
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("FileShare.PasswordRequired");
    }

    /// <summary>种子一个文本文件，返回其 FileItemId（模式同 FileEndpointsIntegrationTests）</summary>
    private async Task<string> SeedFileAsync(string username, string fileName)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = (await users.GetAllAsync()).First(u => u.Username == username);
            var pool = await pools.GetDefaultAsync();
            var file = NasSystem.Domain.Entities.FileItem.CreateFile(
                name: fileName,
                path: "/" + fileName,
                physicalPath: $"/mnt/storage/{fileName}",
                sizeBytes: 16,
                mimeType: "text/plain",
                storagePoolId: pool!.Id,
                ownerId: user.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            return file.Id.ToString();
        }
    }

    private static async Task<string> ReadShareCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("shareCode").GetString()!;
    }
}
