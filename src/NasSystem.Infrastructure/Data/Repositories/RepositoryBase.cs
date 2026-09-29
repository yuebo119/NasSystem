// =============================================================================
// NasSystem.Infrastructure - RepositoryBase.cs
// 仓储基类
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Common;

/// <summary>
/// 仓储基类
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
/// <typeparam name="TId">主键类型</typeparam>
public abstract class RepositoryBase<TEntity, TId>(NasDbContext context) : IRepository<TEntity, TId>
    where TEntity : Entity<TId>
    where TId : notnull
{
    protected readonly NasDbContext Context = context;
    protected readonly DbSet<TEntity> DbSet = context.Set<TEntity>();

    public virtual async ValueTask<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取可查询对象
    /// </summary>
    protected IQueryable<TEntity> Query() => DbSet.AsQueryable();

    /// <summary>
    /// 获取无追踪可查询对象
    /// </summary>
    protected IQueryable<TEntity> QueryNoTracking() => DbSet.AsNoTracking();
}

/// <summary>
/// 使用 Guid 主键的仓储基类
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public abstract class RepositoryBase<TEntity>(NasDbContext context) 
    : RepositoryBase<TEntity, Guid>(context), IRepository<TEntity>
    where TEntity : Entity<Guid>
{
}
