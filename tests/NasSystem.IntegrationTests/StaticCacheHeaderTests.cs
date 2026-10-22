// =============================================================================
// NasSystem.IntegrationTests - StaticCacheHeaderTests.cs
// 静态资源缓存头锁定（2026-10-22 优化轮）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using FluentAssertions;
using Xunit;

/// <summary>
/// 锁静态资源的 Cache-Control 策略（与 nginx.conf 同口径，直连 Kestrel 时由应用发）。
///
/// 为什么需要它：Vite 产物是内容哈希文件名（index-HASH.js），文件名变则内容必变，
/// 可以对 /assets/ 无限期强缓存；而 index.html / sw.js 必须每次回源校验，
/// 否则发版后浏览器拿旧 HTML 引用已删除的哈希资源，页面白屏。
///
/// 优化前直连 Kestrel 只发 ETag/Last-Modified，浏览器按启发式缓存（约文件年龄的
/// 10%），每次回访都可能重新校验近 1MB 的 JS。nginx 生产 profile 本来就有
/// immutable，但那个 profile 是可选的，未启用时这条是唯一保障。
/// </summary>
public class StaticCacheHeaderTests : ApiTestBase
{
    public StaticCacheHeaderTests(NasSystemWebApplicationFactory factory)
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    [Fact]
    public async Task HashedAsset_IsImmutableLongCache()
    {
        // 从 wwwroot 里取一个真实存在的哈希产物
        var indexHtml = await Client.GetStringAsync("/");
        var match = System.Text.RegularExpressions.Regex.Match(indexHtml, @"assets/[a-zA-Z0-9._-]+\.js");
        match.Success.Should().BeTrue("入口 HTML 必须引用构建产物，实际解析失败");

        var response = await Client.GetAsync("/" + match.Value);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.CacheControl.Should().NotBeNull("静态产物必须有显式缓存策略，否则浏览器按启发式缓存");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        response.Headers.CacheControl.Extensions.Should().Contain(e => e.Name == "immutable",
            "内容哈希文件名可无限期缓存，回访不必重新校验");
    }

    [Fact]
    public async Task SpaEntryAndClientRoute_AreNoCache()
    {
        var root = await Client.GetAsync("/");
        root.Headers.CacheControl?.NoCache.Should().BeTrue("index.html 长缓存会让发版后的浏览器引用已删除的哈希资源");

        // 客户端路由走 MapFallback 返回同一份 index.html，也必须 no-cache
        var route = await Client.GetAsync("/files");
        route.StatusCode.Should().Be(HttpStatusCode.OK);
        route.Headers.CacheControl?.NoCache.Should().BeTrue("SPA 回退同样返回 HTML 入口");
    }

    [Fact]
    public async Task ServiceWorker_IsNoCache()
    {
        var response = await Client.GetAsync("/sw.js");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoCache.Should().BeTrue(
            "SW 脚本长缓存会让新版本迟迟不生效，更新逻辑失效");
    }
}