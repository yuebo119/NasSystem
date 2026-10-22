// =============================================================================
// NasSystem.IntegrationTests - ResponseCompressionTests.cs
// 响应压缩的配置与行为锁定（2026-10-22 优化轮）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// 锁 2026-10-22 优化轮的压缩改动与它依赖的两个前提。
///
/// 改动：把 Brotli/Gzip 的压缩等级从默认 Fastest 提到 Optimal——实测
/// element-plus 636,602 字节的资源从 br 258,218 降到 201,296（−22%）。
/// 等级是唯一的旋钮，而静态产物的压缩只付一次。
///
/// 两个前提也必须锁住，否则优化会被静默抵消：
/// 一、响应确实带 Vary: Accept-Encoding——反向代理的缓存按它区分编码变体，
///     丢了它会让不支持 br 的客户端命中 br 缓存（页面直接坏掉）
/// 二、压缩确实发生在应用层——若 nginx 剥掉 Accept-Encoding（微软文档
///     记有此行为），压缩就由 nginx 承担，等级配置在我们这边空转
/// </summary>
public class ResponseCompressionTests : ApiTestBase
{
    public ResponseCompressionTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public void CompressionProviders_UseOptimalLevel()
    {
        using var scope = Factory.Services.CreateScope();
        var brotli = scope.ServiceProvider.GetRequiredService<IOptions<BrotliCompressionProviderOptions>>().Value;
        var gzip = scope.ServiceProvider.GetRequiredService<IOptions<GzipCompressionProviderOptions>>().Value;

        brotli.Level.Should().Be(CompressionLevel.Optimal,
            "默认的 Fastest 让 br 只压到 40%，Optimal 能到 31%（实测 element-plus 258KB → 201KB）");
        gzip.Level.Should().Be(CompressionLevel.Optimal);
    }

    [Fact]
    public async Task CompressibleResponse_IsCompressed_AndCarriesVaryAcceptEncoding()
    {
        // 未认证也能取到的 JSON 端点（health），走的是同一条压缩中间件
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Content.Headers.ContentEncoding.Should().Contain("br",
            "应用层压缩中间件必须真的生效，否则等级配置是空转");
        response.Headers.Vary.Should().Contain("Accept-Encoding",
            "缺 Vary 会让反向代理把 br 变体发给不支持 br 的客户端");
    }

    [Fact]
    public async Task NoAcceptEncoding_ResponseIsUncompressed_ButStillVaries()
    {
        var response = await Client.GetAsync("/api/v1/system/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Content.Headers.ContentEncoding.Should().BeEmpty("客户端没声明编码时不得压缩");
        // .NET 11 起无论是否压缩都发 Vary（docs/breaking-changes/11/response-compression-always-vary）
        response.Headers.Vary.Should().Contain("Accept-Encoding");
    }
}