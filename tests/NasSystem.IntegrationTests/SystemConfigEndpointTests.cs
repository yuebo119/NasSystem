// =============================================================================
// NasSystem.IntegrationTests - SystemConfigEndpointTests.cs
// 系统动态配置端点（配置页数据源）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NasSystem.Application.Users.Dtos;
using Xunit;

/// <summary>
/// 锁住配置端点的四类行为：快照结构（descriptor + 生效值）、
/// 更新即生效、非法值整单拒绝、非管理员 403。
/// 更新后的生效验证走消费方视角（读回快照），不重复造轮子
/// </summary>
public class SystemConfigEndpointTests : ApiTestBase
{
    public SystemConfigEndpointTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task GetConfig_ReturnsDescriptorsWithDefaults()
    {
        var token = await LoginAsAdminAsync();

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/system/config", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        // descriptor 驱动的快照：分组、键、默认值都要在
        body.Should().Contain("fileversion.max-count");
        body.Should().Contain("ocr.auto-recognize");
        body.Should().Contain("webdav.enabled");
        body.Should().Contain("\"defaultValue\":\"10\"", "版本保留数默认 10");
        body.Should().Contain("\"type\":1", "Bool 型 descriptor 在（Int=0, Bool=1）");
    }

    [Fact]
    public async Task UpdateConfig_TakesEffectImmediately()
    {
        var token = await LoginAsAdminAsync();

        var update = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Put, "/api/v1/system/config", token,
            new { values = new Dictionary<string, string> { ["fileversion.max-count"] = "5" } }));
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        // 读回快照：生效值必须是新值（默认值仍是 10，两者对照证明读的是存储不是 descriptor）
        var body = await (await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/system/config", token))).Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var item = FindItem(doc, "fileversion.max-count");
        item.GetProperty("value").GetString().Should().Be("5");
        item.GetProperty("defaultValue").GetString().Should().Be("10");
    }

    [Fact]
    public async Task UpdateConfig_OutOfRange_RejectsWholeBatch()
    {
        var token = await LoginAsAdminAsync();

        // 一个合法一个越界：整单拒绝，合法的也不能落库（半组生效比全不生效更难排查）
        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Put, "/api/v1/system/config", token,
            new
            {
                values = new Dictionary<string, string>
                {
                    ["ocr.auto-recognize"] = "true",
                    ["fileversion.max-count"] = "9999"
                }
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("不能大于 100", "错误消息要指出哪个键、为什么");

        var ocrItem = FindItem(
            JsonDocument.Parse(await GetConfigBodyAsync(token)), "ocr.auto-recognize");
        ocrItem.GetProperty("value").GetString().Should().Be("false", "整单拒绝后合法项也不得生效");
    }

    [Fact]
    public async Task UpdateConfig_UnknownKey_Rejected()
    {
        var token = await LoginAsAdminAsync();

        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Put, "/api/v1/system/config", token,
            new { values = new Dictionary<string, string> { ["no.such.key"] = "1" } }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("未知配置项");
    }

    [Fact]
    public async Task GetConfig_NonAdmin_Forbidden()
    {
        var token = await GetAuthTokenAsync("cfguser" + Guid.NewGuid().ToString("N")[..8]);

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/system/config", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task<string> GetConfigBodyAsync(string token)
    {
        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/system/config", token));
        return await response.Content.ReadAsStringAsync();
    }

    private static JsonElement FindItem(JsonDocument doc, string key)
    {
        var groups = doc.RootElement.GetProperty("data").GetProperty("groups");
        foreach (var group in groups.EnumerateArray())
        {
            foreach (var item in group.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("key").GetString() == key)
                    return item;
            }
        }
        throw new KeyNotFoundException($"配置快照中找不到键: {key}");
    }

    private async Task<string> LoginAsAdminAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/users/login",
            new { username = "admin", password = "admin123" });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "种子 admin 账号应可登录");

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("admin 登录失败");
    }

    private static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, string token, object payload)
    {
        var request = CreateBearerRequest(method, url, token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return request;
    }
}
