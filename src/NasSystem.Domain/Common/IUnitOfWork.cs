// =============================================================================
// NasSystem.Domain - IUnitOfWork.cs
// 工作单元接口
// =============================================================================

namespace NasSystem.Domain.Common;

/// <summary>
/// 工作单元接口 - 用于事务管理。
/// </summary>
/// <remarks>
/// 【生命周期论证】不继承 IDisposable：EF DbContext 由 DI 容器按 Scope 管理，
/// UnitOfWork 作为 Scoped 协作者由容器统一处置；服务层若 using 本接口会连带
/// 处置 DbContext，破坏同一 Scope 内其他协作者的后续使用（ITM-N29 批次清理）。
/// </remarks>
public interface IUnitOfWork
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
