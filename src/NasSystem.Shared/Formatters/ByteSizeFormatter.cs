// =============================================================================
// NasSystem.Shared - Formatting/ByteSizeFormatter.cs
// 字节容量的人读格式化（全项目唯一真源）
// =============================================================================

namespace NasSystem.Shared;

/// <summary>
/// 字节容量的统一人读格式化。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么这个类必须存在（保留论证）</b>：2026-08 审计确认同一逻辑曾以六份私有拷贝散布于
/// FileService / FileShareService / StorageService / UserService(×2) / SystemEndpoints，
/// 且行为已经漂移——TB 封顶 vs PB 档并存、负数入参行为未定义。展示层各处对同一文件
/// 可能渲染出不同单位档，属于典型的"复制粘贴语义漂移"（误判库 NPD-2）。
/// </para>
/// <para>
/// <b>口径以最强版为准</b>：PB 档（系统盘与 NAS 池容量已进入 PB 量级）+ 零值显式返回
/// "0 B" + 负数按 0 处理（防御上游脏数据导致格式化抛异常）。
/// </para>
/// <para>
/// <b>为什么放 Shared 而非各层私有</b>：Domain 实体的 SizeDisplay、Application 各服务 DTO、
/// Api 的 SystemEndpoints 都需要它——Shared 是唯一被所有层引用且自身零依赖的位置，
/// 符合 Clean Architecture 的共享内核（Shared Kernel）定位。
/// </para>
/// </remarks>
public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// 将字节数格式化为人读字符串（1024 进制，最多两位小数）。
    /// </summary>
    /// <param name="bytes">字节数；负数视为 0（防御脏数据，不抛异常打断渲染路径）。</param>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";

        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < Units.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {Units[order]}";
    }
}
