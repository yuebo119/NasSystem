// =============================================================================
// NasSystem.Infrastructure - UnitOfWork.cs
// 工作单元实现
// =============================================================================

namespace NasSystem.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NasSystem.Domain.Common;
using NasSystem.Domain.Services;

/// <summary>
/// 工作单元实现
/// </summary>
public sealed class UnitOfWork(
    NasDbContext context,
    IDomainEventDispatcher? eventDispatcher = null) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 发布领域事件
        await DispatchDomainEventsAsync(cancellationToken);

        try
        {
            // 保存更改
            return await context.SaveChangesAsync(cancellationToken);
        }
        // 【分层论证】为什么在这里转译而不是让 Application 层直接 catch SqliteException：
        // Clean Architecture 中 Application 层禁止引用基础设施包（gate NAS-G3 强制），
        // Microsoft.Data.Sqlite 属于 Infrastructure 专有依赖。提供方异常在跨出基础设施层
        // 的边界处统一转译为 Domain 异常，Application 只认知领域语义——提供方更换
        // （SQLite→PostgreSQL）时仅需修改本文件，服务层零改动。
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // ITM-N7：并发插入撞上唯一索引时，把提供方异常转译为领域异常，
            // 服务层据此返回 AlreadyExists 业务错误而非 500
            throw new UniqueConstraintException(ex.Message, "唯一约束冲突：相同记录已被并发请求创建", ex);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            
            if (_transaction is not null)
            {
                await _transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <summary>
    /// 发布所有待处理的领域事件
    /// </summary>
    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (eventDispatcher is null)
            return;

        // 获取所有包含领域事件的实体
        var entities = context.ChangeTracker
            .Entries<Entity<Guid>>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        // 收集所有事件
        var domainEvents = entities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // 清除事件
        entities.ForEach(e => e.ClearDomainEvents());

        // 发布事件
        await eventDispatcher.DispatchAsync(domainEvents, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _transaction?.Dispose();
        context.Dispose();
        _disposed = true;
    }
}
