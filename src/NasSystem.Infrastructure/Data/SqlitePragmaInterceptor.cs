// =============================================================================
// NasSystem.Infrastructure - SqlitePragmaInterceptor.cs
// SQLite 连接级 PRAGMA 拦截器
// =============================================================================

namespace NasSystem.Infrastructure.Data;

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

/// <summary>
/// 每次物理连接打开后重设连接级 PRAGMA。
/// </summary>
/// <remarks>
/// busy_timeout 与 synchronous 是连接级参数（journal_mode=WAL 持久化在库文件，只需设一次）。
/// 历史上只在启动初始化的那一个连接上执行过，连接池后续开出的物理连接回退默认值：
/// busy_timeout=0（并发写立即 SQLITE_BUSY）、synchronous=FULL（每次写多 fsync）。
/// 本拦截器在 ConnectionOpened 钩子补齐，覆盖池内全部连接（CONVENTIONS §3.1）。
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string BusyTimeoutPragma = "PRAGMA busy_timeout=5000;";
    private const string SynchronousPragma = "PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        base.ConnectionOpened(connection, eventData);
        ApplyPragmas(connection).GetAwaiter().GetResult();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        await ApplyPragmasAsync(connection, cancellationToken);
    }

    // 集成测试走 InMemory provider，连接非 SqliteConnection，直接跳过
    private static bool IsSqlite(DbConnection connection) => connection is SqliteConnection;

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (!IsSqlite(connection))
            return;

        foreach (var pragma in new[] { BusyTimeoutPragma, SynchronousPragma })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ApplyPragmas(DbConnection connection)
    {
        if (!IsSqlite(connection))
            return;

        foreach (var pragma in new[] { BusyTimeoutPragma, SynchronousPragma })
        {
            using var command = connection.CreateCommand();
            command.CommandText = pragma;
            command.ExecuteNonQuery();
        }
    }
}
