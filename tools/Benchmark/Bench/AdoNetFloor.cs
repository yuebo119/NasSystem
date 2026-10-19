// =============================================================================
// NasSystem.Benchmark - AdoNetFloor.cs
// 地板臂：与两个 ORM 都无关的裸 ADO.NET 等价实现
// =============================================================================

using Microsoft.Data.Sqlite;
using NasSystem.Domain.Enums;

namespace NasSystem.Benchmark.Bench;

/// <summary>地板臂读取行的轻量投影，不构造领域对象（地板的定义就是最少映射）。</summary>
public readonly record struct FileItemLite(
    Guid Id, string Name, string Path, FileItemType ItemType, long SizeBytes,
    Guid StoragePoolId, Guid OwnerId, Guid? ParentId, bool IsDeleted,
    string? Extension, DateTime CreatedAt);

/// <summary>
/// 裸 ADO.NET 数据访问，作为三臂对比的地板上限参照。
/// </summary>
/// <remarks>
/// SQL 与仓储的 LINQ 语义逐条对齐：软删谓词、ItemType 排序、LIKE 形态、
/// 统计的 5 条聚合 + 1 条分组，都按 FileItemRepository 的翻译结果手写。
/// 连接打开后设 busy_timeout/synchronous，与生产连接级 PRAGMA 一致。
/// </remarks>
public sealed class AdoNetFloor : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private AdoNetFloor(SqliteConnection connection) => _connection = connection;

    public static async Task<AdoNetFloor> OpenAsync(string dbPath)
    {
        var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync();
        }
        return new AdoNetFloor(connection);
    }

    public async Task<FileItemLite?> GetByPathAsync(string path)
    {
        const string sql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "Path" = $p AND "IsDeleted" = 0 LIMIT 1
            """;
        var rows = await QueryAsync(sql, ("$p", path));
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<List<FileItemLite>> GetChildrenAsync(Guid parentId)
    {
        const string sql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "ParentId" = $p AND "IsDeleted" = 0
            ORDER BY "ItemType" DESC, "Name"
            """;
        return await QueryAsync(sql, ("$p", parentId));
    }

    /// <summary>
    /// 浏览投影的地板臂：只取目录列表 DTO 用得到的 15 列，与仓储的
    /// GetChildrenSummariesAsync 同口径（默认 includeHidden=false，故带 IsHidden = 0），
    /// 用于量化"砍掉 PhysicalPath 等 4 列"的收益。
    /// 只数行数不映射：FileItemLite 的列序是硬编码的 11 列，与投影列序不同，
    /// 而这条用例要量的正是"数据库读出这些列并传回驱动"的成本
    /// </summary>
    public async Task<long> GetChildrenSummariesAsync(Guid? parentId, Guid ownerId)
    {
        const string columns =
            "\"Id\",\"Name\",\"Path\",\"ItemType\",\"SizeBytes\",\"MimeType\",\"Extension\",\"Hash\"," +
            "\"OwnerId\",\"ParentId\",\"IsHidden\",\"IsReadOnly\",\"CreatedAt\",\"UpdatedAt\"";

        var sql = parentId.HasValue
            ? $"SELECT COUNT(*) FROM (SELECT {columns} FROM \"FileItems\" WHERE \"ParentId\" = $p AND \"IsDeleted\" = 0 AND \"IsHidden\" = 0 AND \"OwnerId\" = $o)"
            : $"SELECT COUNT(*) FROM (SELECT {columns} FROM \"FileItems\" WHERE \"ParentId\" IS NULL AND \"IsDeleted\" = 0 AND \"IsHidden\" = 0 AND \"OwnerId\" = $o)";

        await using var cmd = Command(sql);
        cmd.Parameters.AddWithValue("$p", parentId ?? Guid.Empty);
        cmd.Parameters.AddWithValue("$o", ownerId);
        var count = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(count);
    }

    public async Task<List<FileItemLite>> SearchAsync(string term)
    {
        const string sql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "IsDeleted" = 0 AND "Name" LIKE '%' || $t || '%'
            ORDER BY "ItemType" DESC, "Name" LIMIT 500
            """;
        return await QueryAsync(sql, ("$t", term));
    }

    public async Task<List<FileItemLite>> GetByStoragePoolAsync(Guid poolId)
    {
        const string sql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "StoragePoolId" = $p AND "IsDeleted" = 0 ORDER BY "Path"
            """;
        return await QueryAsync(sql, ("$p", poolId));
    }

    public async Task<List<FileItemLite>> GetDeletedItemsAsync()
    {
        const string sql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "IsDeleted" = 1 ORDER BY "DeletedAt" DESC
            """;
        return await QueryAsync(sql);
    }

    public async Task<List<FileItemLite>> GetDescendantsAsync(Guid parentId)
    {
        const string parentSql = """SELECT "Path" FROM "FileItems" WHERE "Id" = $id LIMIT 1""";
        string? parentPath = null;
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = parentSql;
            cmd.Parameters.AddWithValue("$id", parentId);
            var result = await cmd.ExecuteScalarAsync();
            parentPath = result as string;
        }
        if (parentPath is null)
            return [];

        const string childSql = """
            SELECT "Id","Name","Path","ItemType","SizeBytes","StoragePoolId","OwnerId","ParentId","IsDeleted","Extension","CreatedAt"
            FROM "FileItems" WHERE "Path" LIKE $prefix || '%' AND "IsDeleted" = 0 ORDER BY "Path"
            """;
        return await QueryAsync(childSql, ("$prefix", parentPath + "/"));
    }

    public async Task<(int TotalFiles, int TotalFolders, long TotalSizeBytes, int RecentFiles)> GetStatisticsAsync(DateTime recentFrom)
    {
        await using var countFiles = Command("""
            SELECT COUNT(*) FROM "FileItems" WHERE "IsDeleted" = 0 AND "ItemType" = 0
            """);
        var totalFiles = Convert.ToInt32(await countFiles.ExecuteScalarAsync());

        await using var countFolders = Command("""
            SELECT COUNT(*) FROM "FileItems" WHERE "IsDeleted" = 0 AND "ItemType" = 1
            """);
        var totalFolders = Convert.ToInt32(await countFolders.ExecuteScalarAsync());

        await using var sumSize = Command("""
            SELECT COALESCE(SUM("SizeBytes"), 0) FROM "FileItems" WHERE "IsDeleted" = 0 AND "ItemType" = 0
            """);
        var totalSize = Convert.ToInt64(await sumSize.ExecuteScalarAsync());

        await using var countRecent = Command("""
            SELECT COUNT(*) FROM "FileItems" WHERE "IsDeleted" = 0 AND "ItemType" = 0 AND "CreatedAt" >= $d
            """);
        countRecent.Parameters.AddWithValue("$d", recentFrom.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"));
        var recent = Convert.ToInt32(await countRecent.ExecuteScalarAsync());

        return (totalFiles, totalFolders, totalSize, recent);
    }

    public async Task<List<(string? Extension, int Count)>> GetExtensionCountsAsync()
    {
        const string sql = """
            SELECT "Extension", COUNT(*) FROM "FileItems"
            WHERE "IsDeleted" = 0 AND "ItemType" = 0
            GROUP BY CASE WHEN "Extension" IS NULL THEN NULL ELSE lower("Extension") END
            """;
        var result = new List<(string?, int)>();
        await using var cmd = Command(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1)));
        return result;
    }

    public async Task<(List<(Guid Id, string Username, int Role)> Users, int Total)> GetPagedUsersAsync(int page, int pageSize)
    {
        await using var countCmd = Command("""SELECT COUNT(*) FROM "Users" WHERE "IsEnabled" = 1""");
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        var users = new List<(Guid, string, int)>();
        await using var cmd = Command("""
            SELECT "Id","Username","Role" FROM "Users" WHERE "IsEnabled" = 1
            ORDER BY "Username" LIMIT $take OFFSET $skip
            """);
        cmd.Parameters.AddWithValue("$take", pageSize);
        cmd.Parameters.AddWithValue("$skip", (page - 1) * pageSize);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            users.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt32(2)));

        return (users, total);
    }

    public async Task<long> SumUsedSpaceAsync(Guid ownerId)
    {
        await using var cmd = Command("""
            SELECT COALESCE(SUM("SizeBytes"), 0) FROM "FileItems"
            WHERE "OwnerId" = $o AND "IsDeleted" = 0 AND "ItemType" = 0
            """);
        cmd.Parameters.AddWithValue("$o", ownerId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<long> SumUsedSpaceByPoolAsync(Guid poolId)
    {
        await using var cmd = Command("""
            SELECT COALESCE(SUM("SizeBytes"), 0) FROM "FileItems"
            WHERE "StoragePoolId" = $p AND "IsDeleted" = 0 AND "ItemType" = 0
            """);
        cmd.Parameters.AddWithValue("$p", poolId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    public async Task<bool> PathExistsAsync(string path)
    {
        await using var cmd = Command("""
            SELECT EXISTS(SELECT 1 FROM "FileItems" WHERE "Path" = $p AND "IsDeleted" = 0)
            """);
        cmd.Parameters.AddWithValue("$p", path);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
    }

    // ---- 写用例：与仓储写路径等价的裸 SQL，单事务内提交（对齐 SaveChanges 的隐式事务语义）----

    public async Task<int> InsertBenchAsync(string parentPath, Guid parentId, Guid poolId, Guid ownerId, int count)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        for (var i = 0; i < count; i++)
        {
            var name = $"bench_{Guid.NewGuid():N}.txt";
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO "FileItems" ("Id","Name","Path","PhysicalPath","ItemType","SizeBytes","MimeType","Extension","StoragePoolId","OwnerId","ParentId","IsDeleted","IsHidden","IsSystem","IsReadOnly","CreatedAt")
                VALUES ($id,$name,$path,$phys,0,$size,'text/plain','.txt',$pool,$owner,$parent,0,0,0,0,$created)
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$path", $"{parentPath}/{name}");
            cmd.Parameters.AddWithValue("$phys", $"/storage{parentPath}/{name}");
            cmd.Parameters.AddWithValue("$size", 1024L);
            cmd.Parameters.AddWithValue("$pool", poolId);
            cmd.Parameters.AddWithValue("$owner", ownerId);
            cmd.Parameters.AddWithValue("$parent", parentId);
            cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"));
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return count;
    }

    public async Task<int> UpdateBenchAsync(Guid parentId, int count)
    {
        var rows = await GetChildrenAsync(parentId);
        await using var tx = await _connection.BeginTransactionAsync();
        var updated = 0;
        foreach (var row in rows.Take(count))
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = """
                UPDATE "FileItems" SET "Name" = $n, "UpdatedAt" = $u WHERE "Id" = $id
                """;
            cmd.Parameters.AddWithValue("$n", $"renamed_{Guid.NewGuid():N}.txt");
            cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"));
            cmd.Parameters.AddWithValue("$id", row.Id);
            await cmd.ExecuteNonQueryAsync();
            updated++;
        }
        await tx.CommitAsync();
        return updated;
    }

    /// <summary>删除基准产生的行（重置用，不计时）。</summary>
    public async Task<int> DeleteBenchRowsAsync(string parentPath, string namePrefix)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM "FileItems" WHERE "Path" LIKE $prefix
            """;
        cmd.Parameters.AddWithValue("$prefix", $"{parentPath}/{namePrefix}%");
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>按原路径补回被删的行（重置用，不计时）。</summary>
    public async Task<int> ReinsertBenchRowsAsync(
        string parentPath, Guid parentId, Guid poolId, Guid ownerId, int count)
    {
        await using var tx = await _connection.BeginTransactionAsync();
        var inserted = 0;
        for (var i = 0; i < count; i++)
        {
            var unique = Guid.NewGuid().ToString("N");
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = """
                INSERT INTO "FileItems" ("Id","Name","Path","PhysicalPath","ItemType","SizeBytes","MimeType","Extension","StoragePoolId","OwnerId","ParentId","IsDeleted","IsHidden","IsSystem","IsReadOnly","CreatedAt")
                VALUES ($id,$n,$p,$ph,0,1024,'text/plain','.txt',$pool,$owner,$parent,0,0,0,0,$cr)
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("$n", $"deltarget_{unique}.txt");
            cmd.Parameters.AddWithValue("$p", $"{parentPath}/deltarget_{unique}.txt");
            cmd.Parameters.AddWithValue("$ph", $"/storage{parentPath}/deltarget_{unique}.txt");
            cmd.Parameters.AddWithValue("$pool", poolId);
            cmd.Parameters.AddWithValue("$owner", ownerId);
            cmd.Parameters.AddWithValue("$parent", parentId);
            cmd.Parameters.AddWithValue("$cr", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffff"));
            await cmd.ExecuteNonQueryAsync();
            inserted++;
        }
        await tx.CommitAsync();
        return inserted;
    }

    public async Task<int> DeleteBenchAsync(Guid parentId, int count)
    {
        var rows = await GetChildrenAsync(parentId);
        await using var tx = await _connection.BeginTransactionAsync();
        var deleted = 0;
        foreach (var row in rows.Take(count))
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = """DELETE FROM "FileItems" WHERE "Id" = $id""";
            cmd.Parameters.AddWithValue("$id", row.Id);
            await cmd.ExecuteNonQueryAsync();
            deleted++;
        }
        await tx.CommitAsync();
        return deleted;
    }

    private SqliteCommand Command(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private async Task<List<FileItemLite>> QueryAsync(string sql, params (string Name, object Value)[] parameters)
    {
        var rows = new List<FileItemLite>();
        await using var cmd = Command(sql);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new FileItemLite(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                (FileItemType)reader.GetInt32(3),
                reader.GetInt64(4),
                Guid.Parse(reader.GetString(5)),
                Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                reader.GetInt32(8) == 1,
                reader.IsDBNull(9) ? null : reader.GetString(9),
                DateTime.Parse(reader.GetString(10))));
        }
        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
