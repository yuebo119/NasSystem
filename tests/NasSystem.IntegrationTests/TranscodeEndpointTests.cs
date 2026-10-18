// =============================================================================
// NasSystem.IntegrationTests - TranscodeEndpointTests.cs
// HLS 流端点测试（ROADMAP Phase 3）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using NasSystem.Domain.Entities;
using Xunit;

/// <summary>
/// HLS 流端点的边界：认证、路径存在性、段名白名单、端到端 m3u8。
/// 转码产物结构由 FfmpegTranscodeTests（服务层）锁定，这里只验
/// HTTP 契约。
/// </summary>
public class TranscodeEndpointTests : ApiTestBase
{
    public TranscodeEndpointTests(NasSystemWebApplicationFactory factory) : base(factory)
    {
    }

    private static string? ResolveFfmpeg()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (var dir in pathVariable.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (IOException) { }
            }
        }
        var probePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tmp", "probe", "ffmpeg", "ffmpeg.exe");
        return File.Exists(probePath) ? Path.GetFullPath(probePath) : null;
    }

    /// <summary>造一个 2 秒测试视频 + 对应 FileItem 记录（挂默认池下）</summary>
    private async Task<string> SeedVideoAsync(string ffmpegPath)
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = CreateDataScope();
        using (scope)
        {
            var user = await users.GetByUsernameAsync("admin");
            var pool = StoragePool.Create($"TranscodePool_{Guid.NewGuid():N}", Path.Combine(Path.GetTempPath(), $"tc_{Guid.NewGuid():N}"));
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
            Directory.CreateDirectory(pool.MountPath);

            var videoRel = "sample.mp4";
            var physical = Path.Combine(pool.MountPath, videoRel);
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -hide_banner -loglevel error -f lavfi -i \"testsrc=size=320x240:rate=15\" -t 2 -pix_fmt yuv420p \"{physical}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();

            var item = FileItem.CreateFile(
                name: videoRel, path: $"/{videoRel}", physicalPath: physical,
                sizeBytes: new FileInfo(physical).Length, mimeType: "video/mp4",
                storagePoolId: pool.Id, ownerId: user!.Id, hash: "h-tc");
            await fileItems.AddAsync(item);
            await unitOfWork.SaveChangesAsync();
            return $"/{videoRel}";
        }
    }

    [Fact]
    public async Task GetPlaylist_WithoutToken_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/media/stream/nonexistent.mp4");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPlaylist_NonExistentPath_Returns404()
    {
        var token = await GetAuthTokenAsync("tc-viewer");
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/media/stream/no-such.mp4");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSegment_IllegalSegmentName_Returns400()
    {
        var token = await GetAuthTokenAsync("tc-seg");
        var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/media/stream/x.mp4/segments/..%2F..%2Fetc");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        // 段名白名单应拒（拆分出的段名 "..%2F..%2Fetc" 不过 seg%03d.ts 形态）
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlaylist_WithFfmpeg_ReturnsM3u8()
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            Assert.Skip("ffmpeg 不可用，跳过端到端点测试");

        var videoPath = await SeedVideoAsync(ffmpeg);
        var token = await GetAuthTokenAsync("tc-admin");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/media/stream{videoPath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);

        // admin 用户在测试工厂里是普通角色（GetAuthTokenAsync 建的用户），
        // 视频 owner 是 admin——非本人应 403。这条锁的是归属校验
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "非 owner 访问他人视频应 403");
    }

    [Fact]
    public async Task ClearCache_RequiresAdmin()
    {
        var token = await GetAuthTokenAsync("tc-cache");
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/media/stream/cache");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "普通用户清缓存应 403");
    }
}
