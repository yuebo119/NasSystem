// =============================================================================
// NasSystem.Infrastructure - Persistence/ExtensionCountRow.cs
// 扩展名分组统计的投影行
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>
/// 扩展名分组查询的结果行。
/// </summary>
/// <remarks>
/// PalORM 的 <c>QueryAsync&lt;T&gt;</c> 要求 T 已在生成器注册：实体挂 <c>[Table]</c>，
/// 非表投影挂 <c>[Projection]</c>。元组不满足约束，故为这一个聚合查询单独建投影类。
/// 属性名与 SQL 里的列别名对应（Extension / Count），列序契约同 ADR-A。
/// </remarks>
[Projection]
public partial class ExtensionCountRow
{
    public string? Extension { get; set; }
    public long Count { get; set; }
}
