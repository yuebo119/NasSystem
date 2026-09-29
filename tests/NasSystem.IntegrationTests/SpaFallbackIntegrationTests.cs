// =============================================================================
// NasSystem.IntegrationTests - SpaFallbackIntegrationTests.cs
// SPA 深链接回退与 API 404 语义锁定测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// 覆盖 SPA fallback 行为：前端路由深链接返回 index.html，
/// 未匹配的 API/hubs 路径保持 404 JSON（不吞进 HTML 回退）
/// </summary>
public class SpaFallbackIntegrationTests : IClassFixture<NasSystemWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly IWebHostEnvironment _env;

    public SpaFallbackIntegrationTests(NasSystemWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _env = factory.Services.GetRequiredService<IWebHostEnvironment>();
    }

    [Theory]
    [InlineData("/files")]
    [InlineData("/users")]
    [InlineData("/storage")]
    [InlineData("/shares")]
    [InlineData("/s/abc123")]
    public async Task DeepLink_ReturnsIndexHtml(string path)
    {
        await using var indexFixture = new SpaIndexFixture(_env.WebRootPath);
        await indexFixture.EnsureAsync();

        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        var body = await response.Content.ReadAsStringAsync();
        // 环境里已有前端构建产物时断言其 SPA 挂载点，测试自备文件时断言标记串
        body.Should().Contain(indexFixture.ExistedBefore ? "<div id=\"app\"" : SpaIndexFixture.Marker);
    }

    [Fact]
    public async Task UnknownApiPath_ReturnsJsonNotFound()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // 必须是 JSON 而非 SPA HTML：否则前端把错误页当接口响应解析
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task UnknownHubPath_ReturnsJsonNotFound()
    {
        var response = await _client.GetAsync("/hubs/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task RootPath_StillServed()
    {
        await using var indexFixture = new SpaIndexFixture(_env.WebRootPath);
        await indexFixture.EnsureAsync();

        var response = await _client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }
}

/// <summary>
/// 在 Web 根目录安放一个标记用 index.html：CI 未构建前端时该文件不存在，
/// 测试自备并在结束后清理，保证断言确定性且不污染仓库
/// </summary>
internal sealed class SpaIndexFixture : IAsyncDisposable
{
    public const string Marker = "nas-spa-test-marker";

    private readonly string _indexPath;
    private readonly bool _existedBefore;

    /// <summary>Web 根目录在测试前是否已有 index.html（前端构建产物）</summary>
    public bool ExistedBefore => _existedBefore;

    public SpaIndexFixture(string webRootPath)
    {
        Directory.CreateDirectory(webRootPath);
        _indexPath = Path.Combine(webRootPath, "index.html");
        _existedBefore = File.Exists(_indexPath);
    }

    public Task EnsureAsync() =>
        _existedBefore
            ? Task.CompletedTask
            : File.WriteAllTextAsync(_indexPath, $"<html><body>{Marker}</body></html>");

    public ValueTask DisposeAsync()
    {
        if (!_existedBefore && File.Exists(_indexPath))
            File.Delete(_indexPath);
        return ValueTask.CompletedTask;
    }
}
