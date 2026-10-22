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
    /// 移动文件。overwrite 控制目标已存在时是否替换（上传落位需要覆盖，
    /// 其余批量移动保持不覆盖以让目标冲突显式失败）
    /// </summary>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);

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
    /// 写入文件（流式拷贝到目标路径）
    /// </summary>
    Task WriteFileAsync(string path, Stream sourceStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取文件
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>打开写入流（FileMode.Create 语义）——上传写入走此抽象而非裸 File.Create</summary>
    Stream OpenWrite(string path);

    /// <summary>
    /// 创建硬链接：同一 inode 的第二个名字，零复制（版本快照的默认形态）。
    /// 跨卷或文件系统不支持时抛 IOException/PlatformNotSupportedException，
    /// 由调用方降级为 <see cref="CopyFile"/>
    /// </summary>
    void CreateHardLink(string linkPath, string existingFilePath);

    /// <summary>
    /// 同步复制单个文件（快照降级路径：硬链接不可用时的复制形态）
    /// </summary>
    void CopyFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// 原子替换目标文件（File.Replace 语义）：不存在「删了旧的、新的没移入」
    /// 的崩溃窗口，版本恢复走此形态
    /// </summary>
    void ReplaceFile(string tempPath, string targetPath);

    /// <summary>
    /// 枚举目录下的文件（Enumerate 语义：边遍历边产出，不物化整目录名字数组）
    /// </summary>
    IEnumerable<string> EnumerateFiles(string path);

    /// <summary>
    /// 枚举子目录（Enumerate 语义）
    /// </summary>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>
    /// 路径所在卷的物理剩余字节；目录不存在、取不到盘根或磁盘未就绪时返回 null
    /// </summary>
    long? GetAvailableFreeSpace(string path);

    /// <summary>
    /// 路径所在卷是否就绪（挂载点可达且磁盘 ready）
    /// </summary>
    bool IsVolumeReady(string path);
}
