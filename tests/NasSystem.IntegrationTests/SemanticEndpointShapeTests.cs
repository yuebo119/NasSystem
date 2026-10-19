// =============================================================================
// NasSystem.IntegrationTests - SemanticEndpointShapeTests.cs
// 语义端点响应体形状回归（ApiResponse 包装）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using NasSystem.Application.Users.Dtos;
using Xunit;

/// <summary>
/// 锁 /semantic/status 与 /semantic/search 的 ApiResponse 包装。
/// 2026-10-19 E2E 走查实证：这两个端点曾返回裸匿名对象，前端按
/// success/data 统一判定，症状是语义卡整个不渲染、搜图静默无反馈——
/// 单元测试与集成测试都不看前端契约，全部漏过。形状回归只能在这里锁
/// </summary>
public class SemanticEndpointShapeTests : ApiTestBase
{
    public SemanticEndpointShapeTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task GetStatus_SuccessResponse_IsWrappedInApiResponse()
    {
        var token = await LoginAsAdminAsync();

        var response = await Client.SendAsync(CreateBearerRequest(
            HttpMethod.Get, "/api/v1/semantic/status", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();

        // 成功路径的包装：裸对象回归会直接死在这两条上
        raw.Should().Contain("\"success\":true");
        raw.Should().Contain("\"data\"");
        raw.Should().Contain("\"model\"");
        raw.Should().Contain("\"index_\"");
    }

    [Fact]
    public async Task Search_ErrorResponse_IsWrappedInApiResponse()
    {
        var token = await GetAuthTokenAsync("sem-shape-user");

        // 默认配置语义关闭：错误路径。错误侧走 ToHttpResult 一向有包装，
        // 这条防的是未来改端点时把错误路径也改成裸响应
        var response = await Client.SendAsync(CreateJsonRequest(
            HttpMethod.Post, "/api/v1/semantic/search", token, new { query = "测试" }));

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain("\"success\":false");
        raw.Should().Contain("\"error\"");
        raw.Should().Contain("Semantic.Disabled");
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
        request.Content = JsonContent.Create(payload);
        return request;
    }
}
