// =============================================================================
// NasSystem.IntegrationTests - SemanticIndexTriggerTests.cs
// 语义索引手动触发端点（POST /semantic/index/trigger）
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
/// 锁触发端点的四类行为：普通用户 403、功能关闭 400、模型未就位 404、
/// 就绪时 202（auto-index 关闭也放行——那是手动触发存在的意义）。
/// 模型就绪态由测试经工厂的临时模型目录填/清占位文件控制（端点只查
/// 就位不加载模型；后台轮次加载失败由服务自身降级，不影响 202）
/// </summary>
public class SemanticIndexTriggerTests : ApiTestBase
{
    /// <summary>工厂实例级临时模型目录（见 WebApplicationFactoryExtensions）</summary>
    private readonly string _modelDir;

    public SemanticIndexTriggerTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        _modelDir = factory.SemanticModelDirectory;
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task TriggerIndex_AsNormalUser_Forbidden()
    {
        var token = await GetAuthTokenAsync("sem-trigger-user");

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, "/api/v1/semantic/index/trigger", token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "建索引是管理员动作");
    }

    [Fact]
    public async Task TriggerIndex_WhenDisabled_BadRequest()
    {
        var token = await LoginAsAdminAsync();
        await SetSemanticConfigAsync(token, enabled: false, autoIndex: false);

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, "/api/v1/semantic/index/trigger", token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "功能关闭时触发应明确拒绝");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Semantic.Disabled", "错误码让前端区分'未启用'与'模型没下'");
    }

    [Fact]
    public async Task TriggerIndex_WhenModelNotReady_NotFound()
    {
        var token = await LoginAsAdminAsync();
        await SetSemanticConfigAsync(token, enabled: true, autoIndex: false);
        ClearModelDirectory();

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, "/api/v1/semantic/index/trigger", token));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "模型未就位应明确 404 而非 500");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Semantic.ModelNotReady");
    }

    [Fact]
    public async Task TriggerIndex_WhenReady_Accepted()
    {
        var token = await LoginAsAdminAsync();
        // auto-index 关闭仍放行：手动触发就是关掉自动轮之后唯一的驱动
        await SetSemanticConfigAsync(token, enabled: true, autoIndex: false);
        CreateDummyModelFiles();

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Post, "/api/v1/semantic/index/trigger", token));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "受理即返回，真正编码在后台");
    }

    // -------------------------------------------------------------------------
    // 辅助
    // -------------------------------------------------------------------------

    private async Task SetSemanticConfigAsync(string token, bool enabled, bool autoIndex)
    {
        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Put, "/api/v1/system/config", token,
            new
            {
                values = new Dictionary<string, string>
                {
                    ["semantic.enabled"] = enabled ? "true" : "false",
                    ["semantic.auto-index"] = autoIndex ? "true" : "false",
                }
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, "配置更新是前置条件，必须成功");
    }

    private void ClearModelDirectory()
    {
        if (!Directory.Exists(_modelDir)) return;
        foreach (var file in Directory.GetFiles(_modelDir))
        {
            File.Delete(file);
        }
    }

    /// <summary>四件套占位文件（非空即"就位"；端点不加载模型内容）</summary>
    private void CreateDummyModelFiles()
    {
        Directory.CreateDirectory(_modelDir);
        foreach (var name in new[] { "vision_model.onnx", "text_model.onnx", "vocab.json", "merges.txt" })
        {
            File.WriteAllText(Path.Combine(_modelDir, name), "placeholder");
        }
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
