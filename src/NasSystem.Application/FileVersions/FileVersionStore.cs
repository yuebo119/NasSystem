// =============================================================================
// NasSystem.Application - FileVersions/FileVersionStore.cs
// 文件版本物理存储（ROADMAP Phase 1）
// =============================================================================

namespace NasSystem.Application.FileVersions;

using NasSystem.Domain.Services;

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
///
/// 2026-10-22 审计 F-027：从静态类改为可注入服务，文件系统访问全部经
/// IFileSystemService（CONVENTIONS §1.5 收口）。纯路径运算保持静态无副作用。
/// </remarks>
public sealed class FileVersionStore
{
    private readonly IFileSystemService _fileSystem;

    public FileVersionStore(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>版本库目录名。扫描同步的排除清单与此常量同源</summary>
    public const string DirectoryName = ".versions";

    /// <summary>
    /// 版本恢复的临时文件名后缀。恢复中途进程退出会残留在用户目录里，
    /// 扫描同步必须把它挡在库外（否则半成品被当用户文件导入）
    /// </summary>
    public const string RestoreTempSuffix = ".restore.tmp";

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
    public static string GetVersionPath(string poolMountPath, Guid fileItemId, string fileName, DateTime now)
    {
        var stamp = now.ToString("yyyyMMddHHmmssfff");
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
    /// 版本内容会被悄悄改掉，届时必须改为复制语义。
    /// 哨兵测试：VersionAtomicityRegressionTests.VersionSnapshot_SurvivesSubsequentOverwrite
    /// </remarks>
    public void CreateSnapshot(string sourcePhysicalPath, string versionPath)
    {
        var directory = Path.GetDirectoryName(versionPath)!;
        _fileSystem.CreateDirectory(directory);

        try
        {
            _fileSystem.CreateHardLink(versionPath, sourcePhysicalPath);
        }
        catch (IOException)
        {
            // 跨卷或文件系统不支持：降级复制（版本路径已保证唯一，不会撞已存在文件）
            _fileSystem.CopyFile(sourcePhysicalPath, versionPath, overwrite: false);
        }
        catch (PlatformNotSupportedException)
        {
            _fileSystem.CopyFile(sourcePhysicalPath, versionPath, overwrite: false);
        }
    }

    /// <summary>
    /// 清理版本文件本身。返回是否删除成功（文件本就不存在也算成功）：
    /// 调用方据此决定是否删库记录——物理删失败时保留记录并报错，
    /// 避免"记录没了文件还在"的反向孤儿（.versions 被扫描排除，扫不回来）
    /// </summary>
    public bool TryDeleteSnapshot(string versionPath)
    {
        try
        {
            if (_fileSystem.FileExists(versionPath))
                _fileSystem.DeleteFile(versionPath);
            return true;
        }
        catch
        {
            return false;
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
