// =============================================================================
// NasSystem.Api - ZipDirectoryResult.cs
// 流式 ZIP 响应结果
// =============================================================================

namespace NasSystem.Api.Endpoints;

using System.IO.Compression;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

/// <summary>
/// 把物理目录边扫边压缩直接写入 HTTP 响应体的 IResult。
/// </summary>
/// <remarks>
/// 取代"整体打进 MemoryStream 再返回"的旧实现：匿名公开分享端点此前可被
/// 大目录触发 OOM 与内存尖峰。压缩级别取 Fastest（分享下载以体积换 CPU 不划算）。
/// </remarks>
internal sealed class ZipDirectoryResult : IResult
{
    /// <summary>压缩复制的缓冲区，与 FileSystemService 同规格（80KB）</summary>
    private const int BufferSize = 81920;

    private readonly string _directory;
    private readonly string _fileName;

    public ZipDirectoryResult(string directory, string fileName)
    {
        _directory = directory;
        _fileName = fileName;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.ContentType = "application/zip";
        response.Headers[HeaderNames.ContentDisposition] =
            new ContentDispositionHeaderValue("attachment") { FileName = _fileName }.ToString();

        // leaveOpen: ZipArchive 释放时不关闭响应体，后续中间件仍可写入
        using var archive = new ZipArchive(response.Body, ZipArchiveMode.Create, leaveOpen: true);
        await AddDirectoryToZipAsync(archive, _directory, entryPrefix: "", httpContext.RequestAborted);
        await response.Body.FlushAsync(httpContext.RequestAborted);
    }

    private static async Task AddDirectoryToZipAsync(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        CancellationToken cancellationToken)
    {
        // Enumerate 而非 Get：Get* 每次物化整个目录的名字符串数组，
        // 深目录树上万目录时是成比例的白分配（与扫描同步同一取舍）
        foreach (var filePath in Directory.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var entryName = string.IsNullOrEmpty(entryPrefix) ? fileName : $"{entryPrefix}/{fileName}";

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await fileStream.CopyToAsync(entryStream, BufferSize, cancellationToken);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subDir);
            var newPrefix = string.IsNullOrEmpty(entryPrefix) ? dirName : $"{entryPrefix}/{dirName}";
            await AddDirectoryToZipAsync(archive, subDir, newPrefix, cancellationToken);
        }
    }
}
