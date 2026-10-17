// =============================================================================
// NasSystem.Infrastructure - Persistence/UserUsedSpaceRow.cs
// 用户已用空间分组统计的投影行
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;

/// <summary>
/// 按 OwnerId 分组求和的结果行，供用户列表展示口径批量取已用空间。
/// </summary>
/// <remarks>
/// 建类动因同 <see cref="ExtensionCountRow"/>：QueryAsync&lt;T&gt; 要求 T 已在生成器注册，
/// 非表投影挂 [Projection]。属性名与 SQL 列别名对应（OwnerId / UsedBytes）。
/// </remarks>
[Projection]
public partial class UserUsedSpaceRow
{
    public Guid OwnerId { get; set; }
    public long UsedBytes { get; set; }
}
