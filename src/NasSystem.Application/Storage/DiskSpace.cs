// =============================================================================
// NasSystem.Application - Storage/DiskSpace.cs
// 磁盘物理剩余探测（池容量口径的共享底座）
// =============================================================================

namespace NasSystem.Application.Storage;

using NasSystem.Domain.Services;

/// <summary>
/// 磁盘物理剩余探测。文件池是虚拟池（一个池一个文件夹），文件夹能长多大
/// 最终受所在磁盘约束：上传预检与可用容量都以此为上限，避免把盘写爆
/// </summary>
/// <remarks>
/// 2026-10-22 审计 F-027：从静态 Directory/DriveInfo 直调改为经
/// IFileSystemService（CONVENTIONS §1.5 收口）。这确实是纯环境查询，
/// 但收口的收益是测试可注入替身（容量预检的边界路径不必依赖真实盘符），
/// 且与全项目同一口径。挂载点不可达时仍返回 null，由调用方退回配额口径。
/// public：出现在 StorageService/FileService 的公共构造签名里（DI 注入）
/// </remarks>
public sealed class DiskSpace
{
    private readonly IFileSystemService _fileSystem;

    public DiskSpace(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// 挂载点所在磁盘的物理剩余字节。目录不存在、取不到盘根或磁盘未就绪
    /// 时返回 null
    /// </summary>
    public long? GetPhysicalFreeBytes(string mountPath)
    {
        if (!_fileSystem.DirectoryExists(mountPath))
            return null;
        return _fileSystem.GetAvailableFreeSpace(mountPath);
    }

    /// <summary>
    /// 池是否可达：挂载点目录存在且所在磁盘就绪。不可达时容量数字已经不可信
    /// </summary>
    public bool IsPoolReachable(string mountPath)
    {
        return _fileSystem.DirectoryExists(mountPath)
            && _fileSystem.IsVolumeReady(mountPath);
    }
}
