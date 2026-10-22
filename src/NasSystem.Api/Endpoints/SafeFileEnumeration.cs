// =============================================================================
// NasSystem.Api - Endpoints/SafeFileEnumeration.cs
// 不跟随重解析点的目录枚举（ZIP 分享与导出共用）
// =============================================================================

namespace NasSystem.Api.Endpoints;

/// <summary>
/// 打包/导出用的目录枚举：跳过符号链接与 junction。
/// </summary>
/// <remarks>
/// 扫描层早已跳过重解析点（F-013），但 ZIP 分享与导出直接遍历物理目录，
/// 不经过库内记录：指向池外的 junction 会把外部文件打进分享包，指向父目录的
/// junction 会造成重复遍历。读取属性失败（文件中途消失、权限不足）按"跳过"
/// 处理，与扫描层同一安全默认。
/// </remarks>
internal static class SafeFileEnumeration
{
    public static IEnumerable<string> EnumerateFiles(string directory) =>
        Directory.EnumerateFiles(directory).Where(path => !IsReparsePoint(path));

    public static IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory.EnumerateDirectories(directory).Where(path => !IsReparsePoint(path));

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}