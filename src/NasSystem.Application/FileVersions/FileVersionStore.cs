// =============================================================================
// NasSystem.Application - FileVersions/FileVersionStore.cs
// 文件版本物理存储（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Application.FileVersions;

/// <summary>
/// 版本库的物理布局与创建。版本存放在所属存储池挂载点下的 .versions 目录：
/// {池挂载点}/.versions/{fileItemId}/{时间戳}-{文件名}
/// </summary>
/// <remarks>
/// 放池内而非应用级目录的两个理由：
/// 一、与目标文件同卷才能建硬链接（跨卷硬链接直接失败，只能复制）；
/// 二、池可整体迁移/备份时版本随数据一起走，不会漏。
/// 扫描同步已显式跳过 .versions 目录（FileService.Sync），
/// 版本文件不会当用户文件入库；目录名以点开头，网页端默认也不显示隐藏项。
/// </remarks>
public static class FileVersionStore
{
    /// <summary>版本库目录名。扫描同步的排除清单与此常量同源</summary>
    public const string DirectoryName = ".versions";

    /// <summary>版本库根目录（池挂载点下）</summary>
    public static string GetVersionRoot(string poolMountPath) =>
        Path.Combine(poolMountPath, DirectoryName);

    /// <summary>某文件的版本存放目录</summary>
    public static string GetFileVersionDirectory(string poolMountPath, Guid fileItemId) =>
        Path.Combine(GetVersionRoot(poolMountPath), fileItemId.ToString("N"));

    /// <summary>
    /// 某文件一个新版本的存放路径。时间戳精确到毫秒再加短 Guid：
    /// 同一毫秒内的连续覆盖（批量场景）不会互相撞名
    /// </summary>
    public static string GetVersionPath(string poolMountPath, Guid fileItemId, string fileName)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var safeName = SanitizeFileName(fileName);
        return Path.Combine(GetFileVersionDirectory(poolMountPath, fileItemId), $"{stamp}-{suffix}-{safeName}");
    }

    /// <summary>
    /// 创建版本快照。优先硬链接：旧 inode 经版本路径继续存活，随后的
    /// 覆盖写入（临时文件 rename 替换）不影响版本内容，零 IO 零复制。
    /// 不支持硬链接的文件系统降级为复制
    /// </summary>
    /// <remarks>
    /// 硬链接的正确性前提：当前文件的所有写路径都是"临时文件 + rename 替换"
    /// （上传、版本恢复均是），不存在原地截断写。若未来新增原地写路径，
    /// 版本内容会被悄悄改掉，届时必须改为复制语义
    /// </remarks>
    public static void CreateSnapshot(string sourcePhysicalPath, string versionPath)
    {
        var directory = Path.GetDirectoryName(versionPath)!;
        Directory.CreateDirectory(directory);

        try
        {
            File.CreateHardLink(versionPath, sourcePhysicalPath);
        }
        catch (IOException)
        {
            // 跨卷或文件系统不支持：降级复制（版本路径已保证唯一，不会撞已存在文件）
            File.Copy(sourcePhysicalPath, versionPath, overwrite: false);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(sourcePhysicalPath, versionPath, overwrite: false);
        }
    }

    /// <summary>
    /// 清理版本文件本身（尽力而为：文件丢失或占用都不阻断记录删除，
    /// 残留文件由扫描同步的 .versions 排除挡在库外）
    /// </summary>
    public static void TryDeleteSnapshot(string versionPath)
    {
        try
        {
            if (File.Exists(versionPath))
                File.Delete(versionPath);
        }
        catch
        {
            // 删除失败留文件不删记录会导致恢复时找不到内容，由 DeleteVersionAsync
            // 的调用方决定是否仍删记录；这里只负责不抛异常
        }
    }

    /// <summary>
    /// 版本文件名清洗：去掉目录分隔符等危险字符。时间戳前缀已保证唯一，
    /// 清洗只防路径逃逸，不改写正常文件名
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = fileName.ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] == '/' || buffer[i] == '\\' || Array.IndexOf(invalid, buffer[i]) >= 0)
                buffer[i] = '_';
        }
        return new string(buffer);
    }
}
