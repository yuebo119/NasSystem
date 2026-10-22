// =============================================================================
// NasSystem.IntegrationTests - ZipReparsePointTests.cs
// ZIP 分享与导出不得跟随 junction/symlink（池外内容不进包）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Diagnostics;
using System.IO.Compression;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NasSystem.Api.Endpoints;
using Xunit;

/// <summary>
/// 在共享目录里放一个指向池外的 junction/symlink，分别走 ZIP 分享与导出，
/// 断言包内只有共享目录自身的文件。修复前两条路径都直接遍历物理目录，
/// 会把池外文件打进包；扫描层的同类保护见 F-013。
/// </summary>
public sealed class ZipReparsePointTests : IDisposable
{
    private readonly string _root;
    private readonly string _shared;
    private readonly string _outside;
    private readonly string _link;

    public ZipReparsePointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"zip-reparse-{Guid.NewGuid():N}");
        _shared = Path.Combine(_root, "shared");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_shared);
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_shared, "normal.txt"), "ok");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");
        _link = Path.Combine(_shared, "leak");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_link))
                Directory.Delete(_link);
        }
        catch (IOException)
        {
            // 链接已不存在或平台不允许单独删除，交给下面的递归清理
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留给系统清理
        }
    }

    [Fact]
    public async Task ZipDirectory_ContainingJunction_ExcludesOutsideFiles()
    {
        if (!TryCreateDirectoryLink(_link, _outside))
            Assert.Skip("当前环境无法创建 junction/symlink");

        var body = await ExecuteAsync(new ZipDirectoryResult(_shared, "test.zip"));

        var names = ReadZipNames(body);
        names.Should().Contain("normal.txt");
        names.Should().NotContain(
            name => name.Contains("secret", StringComparison.OrdinalIgnoreCase),
            "共享目录里的链接不能把池外文件带进 ZIP");
    }

    [Fact]
    public async Task Export_ContainingJunction_ExcludesOutsideFiles()
    {
        if (!TryCreateDirectoryLink(_link, _outside))
            Assert.Skip("当前环境无法创建 junction/symlink");

        var body = await ExecuteAsync(
            new ExportResult(_shared, "shared", "export.zip", password: null));

        var names = ReadZipNames(body);
        names.Should().Contain("shared/normal.txt");
        names.Should().NotContain(
            name => name.Contains("secret", StringComparison.OrdinalIgnoreCase),
            "导出同样不能跟随链接读取池外文件");
    }

    private static async Task<byte[]> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        return body.ToArray();
    }

    private static List<string> ReadZipNames(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                process!.WaitForExit(5000);
                return process.ExitCode == 0 && Directory.Exists(link);
            }

            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch (Exception)
        {
            return false;
        }
    }
}