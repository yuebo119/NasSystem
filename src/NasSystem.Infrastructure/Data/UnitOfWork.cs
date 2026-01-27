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

        // 保存更改
        return await context.SaveChangesAsync(cancellationToken);
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
