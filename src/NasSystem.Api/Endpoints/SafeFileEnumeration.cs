// =============================================================================
// NasSystem.Api - Endpoints/SafeFileEnumeration.cs
// 不跟随重解析点的目录枚举（ZIP 分享与导出共用）
// =============================================================================

namespace NasSystem.Api.Endpoints;

using NasSystem.Shared.IO;

/// <summary>
/// 打包/导出用的目录枚举：跳过符号链接与 junction。
/// </summary>
/// <remarks>
/// 扫描层早已跳过重解析点（F-013），但 ZIP 分享与导出直接遍历物理目录，
/// 不经过库内记录：指向池外的 junction 会把外部文件打进分享包，指向父目录的
/// junction 会造成重复遍历。判定统一委托 <see cref="ReparsePointPolicy"/>，
/// 与扫描层共享同一策略（F-106），读取失败按"跳过"处理。
/// </remarks>
internal static class SafeFileEnumeration
{
    public static IEnumerable<string> EnumerateFiles(string directory) =>
        Directory.EnumerateFiles(directory).Where(path => !IsReparsePoint(path));

    public static IEnumerable<string> EnumerateDirectories(string directory) =>
        Directory.EnumerateDirectories(directory).Where(path => !IsReparsePoint(path));

    public static bool IsReparsePoint(string path) => ReparsePointPolicy.IsReparsePoint(path);
}