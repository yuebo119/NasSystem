// =============================================================================
// NasSystem.Infrastructure - FileSystemService.cs
// 文件系统服务实现
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using NasSystem.Domain.Services;

/// <summary>
/// 文件系统服务实现
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    private const int DefaultBufferSize = 81920; // 80 KB

    // 常见 MIME 类型映射
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // 文档
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        
        // 图片
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        
        // 视频
        [".mp4"] = "video/mp4",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm",
        
        // 音频
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".flac"] = "audio/flac",
        [".aac"] = "audio/aac",
        [".ogg"] = "audio/ogg",
        
        // 压缩包
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        
        // 代码
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".ts"] = "application/typescript",
        [".cs"] = "text/x-csharp",
        [".py"] = "text/x-python",
    };

    public bool Exists(string path) => 
        File.Exists(path) || Directory.Exists(path);

    public bool FileExists(string path) => 
        File.Exists(path);

    public bool DirectoryExists(string path) => 
        Directory.Exists(path);

    public bool IsDirectory(string path) => 
        Directory.Exists(path);

    public void CreateDirectory(string path) => 
        Directory.CreateDirectory(path);

    public void DeleteFile(string path) => 
        File.Delete(path);

    public void DeleteDirectory(string path, bool recursive = true) => 
        Directory.Delete(path, recursive);

    public void Move(string sourcePath, string destinationPath)
    {
        // ITM-N22：源不存在必须显式失败——静默 no-op 会让调用方误判移动成功
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException($"源文件或目录不存在: {sourcePath}", sourcePath);

        if (File.Exists(sourcePath))
            File.Move(sourcePath, destinationPath, overwrite: false);
        else
            Directory.Move(sourcePath, destinationPath);
    }

    public void MoveFile(string sourcePath, string destinationPath) => 
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void MoveDirectory(string sourcePath, string destinationPath) => 
        Directory.Move(sourcePath, destinationPath);

    public async Task CopyFileAsync(
        string sourcePath, 
        string destinationPath, 
        CancellationToken cancellationToken = default)
    {
        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            DefaultBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var destinationStream = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            DefaultBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(destinationStream, DefaultBufferSize, cancellationToken);
    }

    public FileInfo GetFileInfo(string path) => new(path);

    public DirectoryInfo GetDirectoryInfo(string path) => new(path);

    public IEnumerable<FileSystemInfo> GetDirectoryContents(string path)
    {
        var directory = new DirectoryInfo(path);
        return directory.EnumerateFileSystemInfos();
    }

    public string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return MimeTypes.GetValueOrDefault(extension, "application/octet-stream");
    }

    public async Task WriteFileAsync(
        string path, 
        Stream sourceStream, 
        CancellationToken cancellationToken = default)
    {
        // 确保目录存在
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            DefaultBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // ITM-N22：CopyToAsync 内部即为缓冲复制循环，与手写 Pipe 管道语义等价且更简
        await sourceStream.CopyToAsync(fileStream, DefaultBufferSize, cancellationToken);
    }


    public Stream OpenRead(string path) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            DefaultBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public long GetAvailableSpace(string path)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
        return drive.AvailableFreeSpace;
    }

    public long GetTotalSpace(string path)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path) ?? path);
        return drive.TotalSize;
    }
}
