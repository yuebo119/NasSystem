// =============================================================================
// NasSystem.Infrastructure - Persistence/PalormUnitOfWork.cs
// 工作单元：意图队列 + 单事务刷新 + 领域事件分发
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using NasSystem.Domain.Common;
using NasSystem.Domain.Services;
using PalORM;
using PalORM.Sqlite;

/// <summary>
/// 基于 PalORM 的工作单元。
/// </summary>
/// <remarks>
/// 为什么要意图队列而不是让仓储各自立即执行：应用层有 28 处
/// "改几个仓储，最后 SaveChangesAsync 一次" 的调用形态，依赖"一次调用 = 一个隐式事务"。
/// 若改成每条语句独立提交，中途失败会留下半成品状态，且 Application 层要大改。
/// 因而仓储把写入意图登记到这里，SaveChangesAsync 时在同一个事务里按登记顺序刷完。
/// 领域事件也在此分发：EF 时代靠 ChangeTracker 扫待发布实体，现在由队列自己记录。
/// </remarks>
public sealed class PalormUnitOfWork : IUnitOfWork
{
    private readonly PalormSessionHolder _sessions;
    private readonly IDomainEventDispatcher? _eventDispatcher;

    private readonly List<Func<DataSession<SqliteProvider>, CancellationToken, Task>> _commands = [];
    private readonly List<NasSystem.Domain.Common.Entity<Guid>> _trackedEntities = [];

    public PalormUnitOfWork(PalormSessionHolder sessions, IDomainEventDispatcher? eventDispatcher = null)
    {
        _sessions = sessions;
        _eventDispatcher = eventDispatcher;
    }

    /// <summary>登记一条插入意图。</summary>
    public void EnqueueInsert<TEntity>(TEntity entity, Func<DataSession<SqliteProvider>, CancellationToken, Task> command)
    {
        _commands.Add(command);
        Track(entity);
    }

    /// <summary>登记一条更新意图。</summary>
    public void EnqueueUpdate<TEntity>(TEntity entity, Func<DataSession<SqliteProvider>, CancellationToken, Task> command)
    {
        _commands.Add(command);
        Track(entity);
    }

    /// <summary>登记一条删除意图。</summary>
    public void EnqueueDelete<TEntity>(TEntity entity, Func<DataSession<SqliteProvider>, CancellationToken, Task> command)
    {
        _commands.Add(command);
        // 删除也要跟踪：目录版本号在这里统一递增（见 SaveChangesAsync），
        // 删除不进跟踪表的话，永久删除后浏览缓存不会失效
        Track(entity);
    }

    private void Track<TEntity>(TEntity entity)
    {
        if (entity is NasSystem.Domain.Common.Entity<Guid> tracked)
            _trackedEntities.Add(tracked);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_commands.Count == 0)
            return 0;

        await DispatchDomainEventsAsync(cancellationToken);

        var db = await _sessions.GetAsync(cancellationToken);
        try
        {
            var affected = await db.WithTransaction(
                async innerToken =>
                {
                    var affected = 0;
                    foreach (var command in _commands)
                    {
                        await command(db, innerToken);
                        affected++;
                    }
                    return affected;
                },
                null,
                cancellationToken);

            // 提交成功后统一递增受影响目录的版本号。放在事务内做不到：
            // 事务可能回滚，版本号不能为未生效的写入而变。放在事务外、
            // 且仅在命令真的执行完后，才与"库里确实变了"对齐。
            //
            // 为什么在这里而不是各用例里手动 bump：会改目录内容的入口有十几个，
            // 逐个枚举漏一个就是浏览缓存陈旧。所有写入都经过 SaveChangesAsync，
            // 且上面已跟踪被写实体，这里一次覆盖全部入口。
            BumpDirectoryVersions();

            return affected;
        }
        catch (UniqueConstraintViolationException ex)
        {
            // 与 EF 时代 UnitOfWork 的转译位置保持一致：提供方异常在跨出基础设施层边界处
            // 转译为领域异常，Application 只认知领域语义
            throw new NasSystem.Domain.Common.UniqueConstraintException(
                ex.Message, "唯一约束冲突：相同记录已被并发请求创建", ex);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (IsIntegrityViolation(ex))
        {
            // 外键（787）与 NOT NULL（1299）违反同样要转译。
            // PalORM 的 IsUniqueViolation 只认 1555/2067，其余完整性错误原样抛 SqliteException，
            // 会穿透本层落到 GlobalExceptionHandler 的兜底分支变 500。
            // EF 时代 SqliteException 主码都是 19，一律转译为领域异常（消息虽千篇一律但非 500）。
            throw new NasSystem.Domain.Common.UniqueConstraintException(
                ex.Message, "数据完整性冲突：记录被并发修改或引用了不存在的关联记录", ex);
        }
        finally
        {
            _commands.Clear();
            _trackedEntities.Clear();
        }
    }

    /// <summary>
    /// 判断是否为完整性约束违反。扩展错误码 787=外键、1299=NOT NULL、275=CHECK，
    /// 加上主码 19 兜底（EF 时代只按主码 19 判断，这里收窄到确实影响数据完整性的几类）。
    /// </summary>
    private static bool IsIntegrityViolation(Microsoft.Data.Sqlite.SqliteException ex) =>
        ex.SqliteErrorCode == 19
        || ex.SqliteExtendedErrorCode == 787
        || ex.SqliteExtendedErrorCode == 1299
        || ex.SqliteExtendedErrorCode == 275;

    /// <summary>
    /// 按本次提交涉及的实体递增其所在目录的版本号。
    /// </summary>
    /// <remarks>
    /// 这里出现 <see cref="NasSystem.Domain.Entities.FileItem"/> 的具体类型判断：
    /// UoW 只知道"有实体被写了"，而目录版本需要的是实体的虚拟路径。
    /// FileItem 是唯一带 Path 的实体，其余实体（User/StoragePool/FileShare/FileVersion）
    /// 的变更不改变任何目录的条目集合，不需要 bump。
    /// </remarks>
    private void BumpDirectoryVersions()
    {
        foreach (var entity in _trackedEntities)
        {
            if (entity is NasSystem.Domain.Entities.FileItem fileItem)
                NasSystem.Application.Files.DirectoryVersion.BumpForFile(fileItem.Path);
        }
    }

    private async Task DispatchDomainEventsAsync(CancellationToken cancellationToken)
    {
        if (_eventDispatcher is null || _trackedEntities.Count == 0)
            return;

        var domainEvents = _trackedEntities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        if (domainEvents.Count == 0)
            return;

        foreach (var entity in _trackedEntities)
            entity.ClearDomainEvents();

        await _eventDispatcher.DispatchAsync(domainEvents, cancellationToken);
    }
}
