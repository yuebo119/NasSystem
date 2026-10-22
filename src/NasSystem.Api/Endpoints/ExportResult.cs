// =============================================================================
// NasSystem.Api - Endpoints/ExportResult.cs
// 3-2-1 导出的流式响应结果（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Api.Endpoints;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

/// <summary>
/// 把物理目录边扫边压缩写入 HTTP 响应体的 IResult，可选 AES-256-CBC 加密。
/// </summary>
/// <remarks>
/// 设计动因（ROADMAP Phase 2「3-2-1 导出」）：
/// 一、无状态流式：不落临时文件、不起后台任务，一个请求边扫边压边发，
/// 客户端边收边写盘。大导出不占服务端磁盘，也没有"任务残留清理"这道
/// 永远对不齐的账。
/// 二、加密选 openssl enc 兼容格式而非自造格式：产出是
/// Salted__(8 字节 salt) + 密文，密钥由 PBKDF2-HMAC-SHA256（openssl 默认
/// 10000 轮）从用户密码派生 48 字节（key 32 + iv 16）。用户侧解密就是
/// 一条 openssl 命令，不需要本项目提供解密工具，也不引入第三方加密库。
/// 三、密码来自请求体一次性使用：服务端不存储、不记日志。KDF 轮数锁
/// 10000 与 openssl 默认对齐（提高轮数会破坏互操作）。
/// </remarks>
internal sealed class ExportResult : IResult
{
    /// <summary>压缩复制的缓冲区，与 ZipDirectoryResult 同规格（80KB）</summary>
    private const int BufferSize = 81920;

    /// <summary>PBKDF2 派生长度：key 32 + iv 16 = AES-256-CBC 全套</summary>
    private const int DerivedKeyBytes = 48;

    /// <summary>openssl enc -pbkdf2 的默认迭代轮数（提轮会破坏互操作）</summary>
    private const int Pbkdf2Iterations = 10000;

    private readonly string _directory;

    /// <summary>ZIP 顶层文件夹名（逻辑目录名，非物理目录名）</summary>
    private readonly string _entryPrefix;

    private readonly string _fileName;
    private readonly string? _password;

    public ExportResult(string directory, string entryPrefix, string fileName, string? password)
    {
        _directory = directory;
        _entryPrefix = string.IsNullOrWhiteSpace(entryPrefix) ? "export" : entryPrefix;
        _fileName = fileName;
        _password = string.IsNullOrEmpty(password) ? null : password;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var encrypted = _password is not null;
        response.ContentType = encrypted ? "application/octet-stream" : "application/zip";
        response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("attachment")
        {
            FileName = encrypted ? $"{_fileName}.zip.enc" : $"{_fileName}.zip"
        }.ToString();

        if (!encrypted)
        {
            await WriteZipStreamAsync(response.Body, httpContext.RequestAborted);
            return;
        }

        // 加密链路：响应体 ← CryptoStream(加密) ← ZipArchive(压缩) ← 文件读取。
        // 层次自外向内：最外层是密文流，解密的输入是密文，密文的内容是 zip
        var salt = RandomNumberGenerator.GetBytes(8);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            _password!, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, DerivedKeyBytes);
        var key = derived[..32];
        var iv = derived[32..48];

        // Salted__ 魔数 + salt 前置，与 openssl enc 输出同构
        var header = new byte[16];
        Encoding.ASCII.GetBytes("Salted__").CopyTo(header, 0);
        salt.CopyTo(header, 8);
        await response.Body.WriteAsync(header, httpContext.RequestAborted);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor(key, iv);
        await using var cryptoStream = new CryptoStream(
            response.Body, encryptor, CryptoStreamMode.Write);
        await WriteZipStreamAsync(cryptoStream, httpContext.RequestAborted);
        // CryptoStream 无 FlushFinalBlock 之外的显式完成 API：await using
        // 的 DisposeAsync 负责末块填充与刷送到响应体
        await cryptoStream.DisposeAsync();
    }

    private async Task WriteZipStreamAsync(Stream output, CancellationToken cancellationToken)
    {
        // ZipArchive 的同步 Write（entries dispose + central directory）落在
        // AsyncPumpStream 的内存缓冲上，由它泵送到 output——Kestrel 禁同步 IO
        // （.NET 6+ 无 AllowSynchronousIO 开关），直接让 ZipArchive 写响应体
        // 或 CryptoStream 会抛 "Synchronous operations are disallowed"（实测）
        await using var pump = new AsyncPumpStream(output);
        using var archive = new ZipArchive(pump, ZipArchiveMode.Create, leaveOpen: true);
        // 逻辑目录名作顶层前缀：解压得到一个文件夹而不是散落当前目录。
        // 用 item.Name 而非物理目录名——池挂载点即目录自身时物理名没有意义
        await AddDirectoryToZipAsync(archive, _directory, entryPrefix: _entryPrefix, cancellationToken);
        archive.Dispose();
        // ZipArchive dispose 后 pump 里可能还有未泵送的 central directory 尾块，
        // 显式 FlushAsync 把它送往下游（pump 的 Dispose 也会兜底同步泵送）
        await pump.FlushAsync(cancellationToken);
    }

    private static async Task AddDirectoryToZipAsync(
        ZipArchive archive,
        string sourceDir,
        string entryPrefix,
        CancellationToken cancellationToken)
    {
        // 与 ZipDirectoryResult 同口径：Enumerate 而非 Get（深目录树不成比例
        // 物化名字符串数组），空目录也入包（CreateEntry 一个以 / 结尾的条目），
        // 否则解出来目录结构丢了
        var hasEntries = false;
        foreach (var filePath in SafeFileEnumeration.EnumerateFiles(sourceDir))
        {
            hasEntries = true;
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var entryName = string.IsNullOrEmpty(entryPrefix) ? fileName : $"{entryPrefix}/{fileName}";

            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            // await using：条目流 dispose 时 flush deflate 缓冲也必须走异步
            // Write（同步写被 Kestrel 禁同步 IO 策略拒绝）
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await fileStream.CopyToAsync(entryStream, BufferSize, cancellationToken);
        }

        foreach (var subDir in SafeFileEnumeration.EnumerateDirectories(sourceDir))
        {
            hasEntries = true;
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subDir);
            var newPrefix = string.IsNullOrEmpty(entryPrefix) ? dirName : $"{entryPrefix}/{dirName}";

            // 空目录入包：ZIP 用斜杠结尾的条目表达目录。枚举结果物化一次，
            // 既判空又给递归复用（递归内还会再枚举，但那遍历的是下一层）。
            // 重解析点排除在判空之外：只有 junction 的目录按空目录处理
            var subEntries = Directory.EnumerateFileSystemEntries(subDir)
                .Where(path => !SafeFileEnumeration.IsReparsePoint(path))
                .ToList();
            if (subEntries.Count == 0)
            {
                archive.CreateEntry($"{newPrefix}/", CompressionLevel.Fastest);
                continue;
            }

            await AddDirectoryToZipAsync(archive, subDir, newPrefix, cancellationToken);
        }

        // 根目录自身为空时也保留一个条目，解出来不是"什么都没有"
        if (!hasEntries && entryPrefix.Length == 0)
            archive.CreateEntry("./", CompressionLevel.Fastest);
    }
}
