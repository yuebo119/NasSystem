// =============================================================================
// NasSystem.Domain - Common/UniqueConstraintException.cs
// 数据库唯一约束冲突的领域转译（ITM-N7：TOCTOU 兜底的最后一环）
// =============================================================================

namespace NasSystem.Domain.Common;

/// <summary>
/// 唯一约束冲突——应用层"查重后插入"窗口内的并发插入由数据库唯一索引拦截，
/// Infrastructure 将 SQLite SQLITE_CONSTRAINT(19) 转译为此异常，服务层捕获后转为业务错误。
/// </summary>
public sealed class UniqueConstraintException(string constraintName, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>触发冲突的索引/约束名（可能为空，取决于提供方是否给出）</summary>
    public string? ConstraintName { get; } = constraintName;
}
