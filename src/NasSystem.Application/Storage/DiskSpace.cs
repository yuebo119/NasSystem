// =============================================================================
// NasSystem.Application - Storage/DiskSpace.cs
// 磁盘物理剩余探测（池容量口径的共享底座）
// =============================================================================

namespace NasSystem.Application.Storage;

/// <summary>
/// 磁盘物理剩余探测。文件池是虚拟池（一个池一个文件夹），文件夹能长多大
/// 最终受所在磁盘约束：上传预检与可用容量都以此为上限，避免把盘写爆
/// </summary>
/// <remarks>
/// 用静态 Directory/DriveInfo 而不是注入 IFileSystemService：这是纯环境查询，
/// 没有可伪造的业务语义；挂载点不可达时返回 null，由调用方退回配额口径
/// </remarks>
internal static class DiskSpace
{
    /// <summary>
    /// 挂载点所在磁盘的物理剩余字节。目录不存在、路径取不到盘根或磁盘未就绪
    /// 时返回 null
    /// </summary>
    public static long? GetPhysicalFreeBytes(string mountPath)
    {
        try
        {
            if (!Directory.Exists(mountPath))
                return null;
            var pathRoot = Path.GetPathRoot(mountPath);
            if (string.IsNullOrEmpty(pathRoot))
                return null;
            var driveInfo = new DriveInfo(pathRoot);
            return driveInfo.IsReady ? driveInfo.AvailableFreeSpace : null;
        }
        catch
        {
            // 与刷新同策略：无法访问时不当异常抛给读端点
            return null;
        }
    }

    /// <summary>
    /// 池是否可达：挂载点目录存在且所在磁盘就绪。不可达时容量数字已经不可信
    /// </summary>
    public static bool IsPoolReachable(string mountPath)
    {
        try
        {
            if (!Directory.Exists(mountPath))
                return false;
            var pathRoot = Path.GetPathRoot(mountPath);
            return !string.IsNullOrEmpty(pathRoot) && new DriveInfo(pathRoot).IsReady;
        }
        catch
        {
            return false;
        }
    }
}
