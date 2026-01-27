// =============================================================================
// NasSystem.Domain - IUnitOfWork.cs
// 工作单元接口
// =============================================================================

namespace NasSystem.Domain.Common;

/// <summary>
/// 工作单元接口 - 用于事务管理
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// 保存所有更改
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始事务
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交事务
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚事务
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
