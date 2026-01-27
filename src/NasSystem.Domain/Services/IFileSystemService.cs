// =============================================================================
// NasSystem.Domain - IFileSystemService.cs
// 文件系统服务接口
// =============================================================================

namespace NasSystem.Domain.Services;

/// <summary>
/// 文件系统服务接口
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// 检查路径是否存在
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// 检查目录是否存在
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// 检查是否为目录
    /// </summary>
    bool IsDirectory(string path);

    /// <summary>
    /// 创建目录
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>
    /// 删除文件
    /// </summary>
    void DeleteFile(string path);

    /// <summary>
    /// 删除目录
    /// </summary>
    void DeleteDirectory(string path, bool recursive = true);

    /// <summary>
    /// 移动文件/目录
    /// </summary>
    void Move(string sourcePath, string destinationPath);

    /// <summary>
    /// 移动文件
    /// </summary>
    void MoveFile(string sourcePath, string destinationPath);

    /// <summary>
    /// 移动目录
    /// </summary>
    void MoveDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// 复制文件
    /// </summary>
    Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文件信息
    /// </summary>
    FileInfo GetFileInfo(string path);

    /// <summary>
    /// 获取目录信息
    /// </summary>
    DirectoryInfo GetDirectoryInfo(string path);

    /// <summary>
    /// 获取目录内容
    /// </summary>
    IEnumerable<FileSystemInfo> GetDirectoryContents(string path);

    /// <summary>
    /// 获取 MIME 类型
    /// </summary>
    string GetMimeType(string fileName);

    /// <summary>
    /// 写入文件
    /// </summary>
    Task WriteFileAsync(string path, Stream sourceStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文件
    /// </summary>
    Stream OpenRead(string path);
}
