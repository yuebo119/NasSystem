// =============================================================================
// NasSystem.Infrastructure - Semantic/ClipModelDownloader.cs
// CLIP 模型四件套下载器（hf-mirror 源）
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using Microsoft.Extensions.Logging;

/// <summary>
/// 下载 CLIP 四件套到数据目录。幂等：四件齐全即跳过（单文件校验大小，
/// 不校验内容 hash——上游镜像无稳定 checksum 分发，大小校验已能挡住
/// 截断的半成品；要严格校验的用户可手工替换文件）。
/// </summary>
/// <remarks>
/// 源默认 hf-mirror.com（国内可达；huggingface.co 本体在部分网络不可达，
/// 2026-10-18 实测）。仓库 Xenova/clip-vit-base-patch32：onnx 子目录下
/// vision_model.onnx（351,685,709 字节）与 text_model.onnx
/// （254,058,553 字节），仓库根 vocab.json（862,328）与 merges.txt
/// （524,619）——四个 URL 的目录层级不同（模型在 onnx/ 下、词表在根），
/// 照抄会 404，探针期实测过。
/// 下载到 .part 临时文件再原子改名：中断不留半成品伪装成完整模型。
/// </remarks>
public sealed class ClipModelDownloader
{
    /// <summary>模型四件套的文件名（与 ClipEngineHolder 的加载契约同源）</summary>
    public static readonly string[] RequiredFiles =
        ["vision_model.onnx", "text_model.onnx", "vocab.json", "merges.txt"];

    private const string Repo = "Xenova/clip-vit-base-patch32";
    private const string DefaultMirror = "https://hf-mirror.com";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ClipModelDownloader> _logger;

    public ClipModelDownloader(HttpClient httpClient, ILogger<ClipModelDownloader> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        // 大文件下载不设客户端超时：351MB 视带宽分钟级，超时中断比慢更糟
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    /// <summary>四件套是否就位（存在且非空）</summary>
    public static bool IsReady(string modelDirectory) =>
        RequiredFiles.All(f =>
        {
            var path = Path.Combine(modelDirectory, f);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        });

    public async Task DownloadAsync(
        string modelDirectory,
        string mirror,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(modelDirectory);
        var baseUrl = string.IsNullOrWhiteSpace(mirror) ? DefaultMirror : mirror.TrimEnd('/');

        // 四个文件的 URL：模型在 onnx/ 子目录，词表在仓库根
        var files = new (string Name, string Url)[]
        {
            ("vision_model.onnx", $"{baseUrl}/{Repo}/resolve/main/onnx/vision_model.onnx"),
            ("text_model.onnx", $"{baseUrl}/{Repo}/resolve/main/onnx/text_model.onnx"),
            ("vocab.json", $"{baseUrl}/{Repo}/resolve/main/vocab.json"),
            ("merges.txt", $"{baseUrl}/{Repo}/resolve/main/merges.txt"),
        };

        long completedBytes = 0;
        var totalBytes = files.Sum(f => ExistingOrZero(modelDirectory, f.Name));
        var grandTotal = 0L;
        foreach (var (_, url) in files)
        {
            var size = await PeekContentLengthAsync(url, cancellationToken);
            grandTotal += size > 0 ? size : 1;
        }

        foreach (var (name, url) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(modelDirectory, name);
            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                progress?.Report(Math.Min(1.0, (double)completedBytes / grandTotal));
                continue;
            }

            var temp = target + ".part";
            _logger.LogInformation("CLIP 模型下载: {Name} ← {Url}", name, url);
            using var response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(temp);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                completedBytes += read;
                progress?.Report(Math.Min(1.0, (double)completedBytes / grandTotal));
            }
            await file.FlushAsync(cancellationToken);
            file.Close();

            if (new FileInfo(temp).Length == 0)
            {
                File.Delete(temp);
                throw new InvalidOperationException($"模型文件 {name} 下载结果为空");
            }
            File.Move(temp, target, overwrite: true);
            _logger.LogInformation("CLIP 模型就位: {Name} ({Bytes} 字节)", name, new FileInfo(target).Length);
        }

        progress?.Report(1.0);
    }

    private static long ExistingOrZero(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private async Task<long> PeekContentLengthAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.Content.Headers.ContentLength ?? 0;
        }
        catch
        {
            // HEAD 不支持时按 1 计（进度分母失真但不阻断下载）
            return 0;
        }
    }
}
