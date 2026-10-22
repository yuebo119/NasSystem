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

    /// <summary>单次打包的条目数上限。匿名端点无准入控制，超限即中止</summary>
    private const int MaxEntries = 20000;

    /// <summary>单次打包的累计字节上限（8GB）。流式不 OOM，但要防单请求打满 IO 与带宽</summary>
    private const long MaxTotalBytes = 8L * 1024 * 1024 * 1024;

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

        // leaveOpen: ZipArchive 释放时不关闭响应体，后续中间件仍可写入。
        // ZipArchive 的同步 Write（entries dispose + central directory）经
        // AsyncPumpStream 泵送——Kestrel 禁同步 IO（.NET 6+ 无开关），
        // 直接写响应体抛 "Synchronous operations are disallowed"（实测）
        await using var pump = new AsyncPumpStream(response.Body);
        using var archive = new ZipArchive(pump, ZipArchiveMode.Create, leaveOpen: true);
        var budget = new ZipBudget();
        try
        {
            await AddDirectoryToZipAsync(archive, _directory, entryPrefix: "", budget, httpContext.RequestAborted);
        }
        catch (ZipBudgetExceededException)
        {
            // 已写入部分 ZIP 流，无法优雅回滚：抛异常由全局处理器记录，
            // 客户端拿到的是被截断的响应体（ZIP 中央目录缺失，解压必失败）
            throw new InvalidOperationException(
                $"分享打包超出上限（条目 {MaxEntries} 个或总量 {MaxTotalBytes} 字节），请缩小范围后重试");
        }
        archive.Dispose();
        await pump.FlushAsync(httpContext.RequestAborted);
    }

    /// <summary>打包预算：条目数与累计字节双上限</summary>
    private sealed class ZipBudget
    {
        public int Entries { get; set; }
        public long TotalBytes { get; set; }
    }

    private sealed class ZipBudgetExceededException : Exception;

    private static async Task AddDirectoryToZipAsync(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        ZipBudget budget,
        CancellationToken cancellationToken)
    {
        // Enumerate 而非 Get：Get* 每次物化整个目录的名字符串数组，
        // 深目录树上万目录时是成比例的白分配（与扫描同步同一取舍）
        foreach (var filePath in SafeFileEnumeration.EnumerateFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++budget.Entries > MaxEntries)
                throw new ZipBudgetExceededException();

            var fileName = Path.GetFileName(filePath);
            var entryName = string.IsNullOrEmpty(entryPrefix) ? fileName : $"{entryPrefix}/{fileName}";

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            // await using：条目流 dispose 时 flush deflate 缓冲也必须走异步
            // Write（同步写被 Kestrel 禁同步 IO 策略拒绝）
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            budget.TotalBytes += fileStream.Length;
            if (budget.TotalBytes > MaxTotalBytes)
                throw new ZipBudgetExceededException();
            await fileStream.CopyToAsync(entryStream, BufferSize, cancellationToken);
        }

        foreach (var subDir in SafeFileEnumeration.EnumerateDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subDir);
            var newPrefix = string.IsNullOrEmpty(entryPrefix) ? dirName : $"{entryPrefix}/{dirName}";
            await AddDirectoryToZipAsync(archive, subDir, newPrefix, budget, cancellationToken);
        }
    }
}
