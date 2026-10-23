// =============================================================================
// NasSystem.Shared - IO/ReparsePointPolicy.cs
// 重解析点（符号链接/junction）判定单一策略（F-106）
// =============================================================================

namespace NasSystem.Shared.IO;

/// <summary>
/// 重解析点判定。扫描同步、ZIP 分享与导出都依赖"不跟随链接"，
/// 两份实现各自维护 try/catch 迟早漂移，收敛到这里。
/// </summary>
/// <remarks>
/// 读取属性失败（文件中途消失、权限不足）返回 true，按"跳过"处理：
/// 不可判断的链接按不安全对待，与扫描层 F-013/F-091 的既有默认一致。
/// </remarks>
public static class ReparsePointPolicy
{
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