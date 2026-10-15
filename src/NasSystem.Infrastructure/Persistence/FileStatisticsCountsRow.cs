// =============================================================================
// NasSystem.Infrastructure - Persistence/FileStatisticsCountsRow.cs
// 文件统计四项计数的单行聚合投影
// =============================================================================

using PalORM;

namespace NasSystem.Infrastructure.Persistence;

/// <summary>
/// 文件统计的单行聚合结果：四个计数一次查完。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么合成一条</b>：统计端点原先发四条独立 SQL，其中三条的 WHERE 完全同形
/// （IsDeleted = 0 AND ItemType = 0），等于把同一批非删行扫三遍。合成单行聚合后
/// 只扫一遍，SQLite 的页缓存也能被同一条语句内的四次聚合复用。
/// </para>
/// <para>
/// <b>为什么用 CASE 而不是 FILTER</b>：SQLite 3.30+ 才支持 FILTER (WHERE ...)。
/// 原生引擎版本由 PalORM.Sqlite 的 SQLite3MC 包决定，用 CASE 写法在任何版本上
/// 语义一致，不必为一行 SQL 背上引擎版本前提。
/// </para>
/// <para>
/// <b>列序契约</b>：PalORM 投影按属性声明序做序号映射，SELECT 的列序必须与这里
/// 逐列对齐，错位不报错、只静默交换数据。仓储里的 SQL 已按同一顺序书写。
/// </para>
/// </remarks>
[Projection]
public partial class FileStatisticsCountsRow
{
    // SUM 在零行时返回 NULL，故四项都是可空 long；调用方 ?? 0 归一
    [Column("TotalFiles")] public long? TotalFiles { get; set; }
    [Column("TotalFolders")] public long? TotalFolders { get; set; }
    [Column("TotalSizeBytes")] public long? TotalSizeBytes { get; set; }
    [Column("RecentFiles")] public long? RecentFiles { get; set; }
}
