// =============================================================================
// NasSystem.Infrastructure - Persistence/PalormSessionHolder.cs
// 按请求作用域持有 PalORM 会话（相当于原 DbContext 的角色）
// =============================================================================

namespace NasSystem.Infrastructure.Persistence;

using PalORM;
using PalORM.Sqlite;

/// <summary>
/// 每个依赖作用域一个 PalORM 会话，惰性创建。
/// </summary>
/// <remarks>
/// 之所以需要这一层：<see cref="DataSession{TProvider}.CreateAsync"/> 是异步的，
/// 而 DI 容器只能同步构造实例。仓储通过它取得同一作用域内的同一个会话，
/// 从而沿用"一个作用域一个上下文"的原 DbContext 生命周期语义。
/// 同时实现 IDisposable：默认 DI 容器同步释放作用域服务，
/// 只实现 IAsyncDisposable 会直接抛 InvalidOperationException。
/// </remarks>
public sealed class PalormSessionHolder : IDisposable, IAsyncDisposable
{
    private readonly DbOptions _options;
    private DataSession<SqliteProvider>? _session;

    public PalormSessionHolder(DbOptions options) => _options = options;

    public async ValueTask<DataSession<SqliteProvider>> GetAsync(CancellationToken cancellationToken = default)
    {
        _session ??= await DataSession<SqliteProvider>.CreateAsync(_options, cancellationToken);
        return _session;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }
    }

    public void Dispose()
    {
        // DataSession 只有 DisposeAsync，而默认 DI 容器同步释放作用域服务
        // （实测：仅实现 IAsyncDisposable 的服务在同步释放作用域时会被容器直接抛
        // InvalidOperationException，故 IDisposable 这条路径必须给）。
        // 非泛型 ValueTask 没有 Result 可取，只能 GetAwaiter().GetResult()。
        // 先判 IsCompletedSuccessfully：会话创建后没有未完成的写操作，任务通常
        // 当场就完成，不会真的阻塞线程；只有不常见的未完成时才取值等待。
        // 这是 CONVENTIONS §2.2 明文认可的快路径豁免形态。
        if (_session is not null)
        {
            var pending = _session.DisposeAsync();
            if (!pending.IsCompletedSuccessfully)
                pending.GetAwaiter().GetResult();
            _session = null;
        }
    }
}
