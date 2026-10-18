// =============================================================================
// NasSystem.Infrastructure - Semantic/SemanticIndexStore.cs
// sqlite-vec 向量索引存储（独立连接）
// =============================================================================

namespace NasSystem.Infrastructure.Semantic;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

/// <summary>
/// 向量索引存储：vec0 虚拟表（sqlite-vec v0.1.9 NuGet 移植包的
/// 原生件随 runtimes 分发，win-x64 vec0.dll / linux-x64 vec0.so）。
/// </summary>
/// <remarks>
/// 独立 SqliteConnection 而非 PalORM 会话：vec0 是 loadable 扩展，
/// PalORM 的 DataSession 不暴露扩展加载入口。本连接与业务连接同库
/// 文件不同引擎实例（e_sqlite3 + vec0 vs PalORM 默认），WAL 模式下
/// 多连接并发由 busy_timeout 兜住（PalORM 连接级 PRAGMA 已配，
/// 本连接显式再配一次——直连不过 PalORM 的调优路径）。
///
/// vec0 表 DDL：FileItemId TEXT PRIMARY KEY（与 FileItems.Id 同形态，
/// 参数化绑定避免 Guid 大小写归一差异——FTS5 双写踩过同款坑），
/// Embedding float[512]（CLIP ViT-B/32 的 512 维，探针实测）。
/// KNN：MATCH + ORDER BY distance，vec0 的 distance 是 L2（平方），
/// 单调性与余弦一致（CLIP 输出已 L2 归一），排序口径无偏差。
/// </remarks>
public sealed class SemanticIndexStore : IDisposable
{
    /// <summary>CLIP ViT-B/32 输出维度（探针实测确认）</summary>
    public const int EmbeddingDimensions = 512;

    private readonly string _connectionString;
    private readonly string _vecLibraryPath;
    private readonly ILogger<SemanticIndexStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public SemanticIndexStore(
        string connectionString,
        string vecLibraryPath,
        ILogger<SemanticIndexStore> logger)
    {
        _connectionString = connectionString;
        _vecLibraryPath = vecLibraryPath;
        _logger = logger;
    }

    private SqliteConnection OpenConnectionWithVec()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // WAL 与 busy_timeout 与业务连接对齐（PalORM 侧由 provider 自动配，
        // 直连路径显式配）；journal_mode 是持久设置，重复执行无害
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }
        connection.EnableExtensions();
        connection.LoadExtension(_vecLibraryPath);
        return connection;
    }

    /// <summary>幂等建 vec0 表。返回 false 表示 vec0 不可用（扩展缺失）</summary>
    public bool EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using var connection = OpenConnectionWithVec();
            using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE VIRTUAL TABLE IF NOT EXISTS ImageEmbeddings USING vec0(" +
                $"FileItemId TEXT PRIMARY KEY, Embedding float[{EmbeddingDimensions}])";
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "vec0 表创建失败（sqlite-vec 扩展不可用？）: {Path}", _vecLibraryPath);
            return false;
        }
    }

    /// <summary>已索引的图片数</summary>
    public int CountIndexed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var connection = OpenConnectionWithVec();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ImageEmbeddings";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// 写入/更新一个向量。vec0 虚拟表不支持 INSERT OR REPLACE（实测撞
    /// 主键 UNIQUE），更新语义用先删后插两条语句在同一连接同一事务内完成
    /// </summary>
    public async Task UpsertAsync(Guid fileItemId, float[] embedding, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenConnectionWithVec();
            using var transaction = connection.BeginTransaction();
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM ImageEmbeddings WHERE FileItemId = $id";
                delete.Parameters.AddWithValue("$id", fileItemId.ToString("D").ToUpperInvariant());
                delete.ExecuteNonQuery();
            }
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    "INSERT INTO ImageEmbeddings(FileItemId, Embedding) VALUES ($id, $vec)";
                insert.Parameters.AddWithValue("$id", fileItemId.ToString("D").ToUpperInvariant());
                insert.Parameters.AddWithValue("$vec", FormatVector(embedding));
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>已索引的 FileItemId 全集（后台建库算差集用；万级以下可接受）</summary>
    public async Task<IReadOnlyList<string>> ListIndexedIdsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ids = new List<string>();
        using var connection = OpenConnectionWithVec();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FileItemId FROM ImageEmbeddings";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>
    /// KNN 查询：返回 (FileItemId, Distance) 按距离升序。
    /// 查询向量经同一 FormatVector 序列化（vec0 的 JSON 数组文本形态）
    /// </summary>
    public async Task<IReadOnlyList<(Guid FileItemId, double Distance)>> KnnSearchAsync(
        float[] queryVector, int topK, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var results = new List<(Guid, double)>();
        using var connection = OpenConnectionWithVec();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT FileItemId, distance FROM ImageEmbeddings " +
            "WHERE Embedding MATCH $q ORDER BY distance LIMIT $k";
        command.Parameters.AddWithValue("$q", FormatVector(queryVector));
        command.Parameters.AddWithValue("$k", topK);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = Guid.Parse(reader.GetString(0));
            var distance = reader.GetDouble(1);
            results.Add((id, distance));
        }
        return results;
    }

    /// <summary>
    /// vec0 的向量序列化：JSON 数组文本（"[1.0, 0.0, ...]"）。
    /// 不变文化：小数点与分隔符必须稳定（vec0 解析 C locale 形态）
    /// </summary>
    private static string FormatVector(float[] vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _writeLock.Dispose();
    }
}
