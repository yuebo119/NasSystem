// =============================================================================
// NasSystem.Benchmark - Seeder.cs
// 确定性数据集生成：固定随机种子，两个 ORM 读同一份库
// =============================================================================

using Microsoft.Data.Sqlite;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure.Services;

namespace NasSystem.Benchmark.Seeding;

/// <summary>
/// 按固定种子生成基准数据集。
/// </summary>
/// <remarks>
/// 目录树形状贴近真实 NAS：约 20 个顶层目录，每个目录下分子目录与文件，
/// 文件名从受控词表生成，使搜索基准能稳定命中约 1% 行数。
/// 软删约占 5%，CreatedAt 跨 90 天，保证统计聚合与时间过滤有区分度。
/// </remarks>
public static class Seeder
{
    private const int SeedRandom = 20261012;

    // 受控词表：搜索基准用 "report" 命中约 1% 行，避免全表命中把差异抹平
    private static readonly string[] NameWords =
    [
        "report", "photo", "video", "backup", "config", "log", "archive",
        "draft", "final", "index", "cache", "media", "document", "snapshot",
    ];

    private static readonly string[] Extensions =
    [
        ".jpg", ".png", ".mp4", ".pdf", ".txt", ".mp3", ".docx", ".zip", ".json", ".md",
    ];

    public static async Task<int> SeedAsync(string dbPath, int targetRows, bool verbose = true)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
            if (File.Exists(dbPath + suffix))
                File.Delete(dbPath + suffix);

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var random = new Random(SeedRandom);
        var connectionString = $"Data Source={dbPath}";

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        foreach (var ddl in Schema.Tables)
            await ExecuteAsync(connection, ddl);
        foreach (var ddl in Schema.Indexes)
            await ExecuteAsync(connection, ddl);

        // 存储池：5 个，第一个为默认
        var poolIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var id = NewGuid(random);
            poolIds.Add(id);
            await ExecuteAsync(connection,
                """INSERT INTO "StoragePools" ("Id","Name","MountPath","Description","IsDefault","IsEnabled","TotalBytes","UsedBytes","MaxCapacityBytes","CreatedAt") VALUES ($id,$name,$mount,$desc,$def,$en,$total,$used,$max,$created)""",
                ("$id", Key(id)), ("$name", $"存储池{i + 1}"),
                ("$mount", $"/mnt/pool{i + 1}"), ("$desc", (string?)$"基准用存储池 {i + 1}"),
                ("$def", i == 0 ? 1 : 0), ("$en", 1),
                ("$total", 4L * 1024 * 1024 * 1024 * 1024), ("$used", 0L),
                ("$max", DBNull.Value), ("$created", Iso(DateTime.UtcNow.AddDays(-180))));
        }

        // 用户：targetRows/100，至少 5 个；admin 用真实 PBKDF2 哈希，供 API 基准登录
        var userCount = Math.Max(5, targetRows / 100);
        var userIds = new List<Guid>();
        var hasher = new PasswordHasher();
        var (adminHash, adminSalt) = hasher.HashPassword("admin123");

        for (var i = 0; i < userCount; i++)
        {
            var id = NewGuid(random);
            userIds.Add(id);
            var isAdmin = i == 0;
            object email = isAdmin
                ? "admin@bench.local"
                : i % 3 == 0 ? $"user{i}@bench.local" : DBNull.Value;

            await ExecuteAsync(connection,
                """INSERT INTO "Users" ("Id","Username","PasswordHash","PasswordSalt","DisplayName","Email","Role","IsEnabled","LastLoginAt","QuotaBytes","UsedBytes","HomeDirectory","CreatedAt") VALUES ($id,$un,$ph,$ps,$dn,$em,$role,$en,$ll,$quota,$used,$home,$created)""",
                ("$id", Key(id)),
                ("$un", isAdmin ? "admin" : $"user{i:D5}"),
                ("$ph", isAdmin ? adminHash : Convert.ToHexStringLower(RandomBytes(random, 32))),
                ("$ps", isAdmin ? adminSalt : Convert.ToHexStringLower(RandomBytes(random, 16))),
                ("$dn", isAdmin ? "基准管理员" : $"用户{i}"),
                ("$em", email),
                ("$role", isAdmin ? 3 : i % 4),
                ("$en", 1),
                ("$ll", (object?)Iso(DateTime.UtcNow.AddDays(-random.Next(1, 30)))),
                ("$quota", i % 2 == 0 ? 100L * 1024 * 1024 * 1024 : DBNull.Value),
                ("$used", (object)(random.Next(1, 500) * 1024L * 1024)),
                ("$home", isAdmin ? "/home/admin" : $"/home/user{i}"),
                ("$created", Iso(DateTime.UtcNow.AddDays(-random.Next(30, 180)))));
        }

        // 目录树：约 20 个顶层目录，逐层展开直到凑够 targetRows
        var topFolders = 20;
        var perTop = Math.Max(1, targetRows / topFolders);
        var inserted = 0;
        var shareTargets = new List<Guid>();
        var now = DateTime.UtcNow;

        for (var t = 0; t < topFolders && inserted < targetRows; t++)
        {
            var folderId = NewGuid(random);
            var folderName = $"folder{t:D3}";
            var folderPath = $"/{folderName}";
            await InsertFileItem(connection, random, folderId, folderName, folderPath,
                folderType: 1, parentId: null, poolIds, userIds, now, sizeBytes: 0, extension: null,
                deleted: false, ownerId: userIds[0]);
            inserted++;

            for (var f = 0; f < perTop && inserted < targetRows; f++)
            {
                var isFolder = f % 8 == 7;
                var name = isFolder
                    ? $"sub{t:D3}_{f:D4}"
                    : $"{NameWords[random.Next(NameWords.Length)]}_{t:D3}_{f:D4}{Extensions[random.Next(Extensions.Length)]}";
                var path = $"{folderPath}/{name}";
                var id = NewGuid(random);
                var deleted = random.Next(100) < 5;

                await InsertFileItem(connection, random, id, name, path,
                    folderType: isFolder ? 1 : 0, parentId: folderId, poolIds, userIds, now,
                    sizeBytes: isFolder ? 0 : random.Next(1024, 64 * 1024 * 1024),
                    extension: isFolder ? null : Path.GetExtension(name),
                    deleted: deleted, ownerId: userIds[0]);
                inserted++;

                // 每 20 条取一个非软删文件作分享目标（FileItemId 是非空外键）
                if (!deleted && !isFolder && inserted % 20 == 0)
                    shareTargets.Add(id);
            }
        }

        // 分享与版本：按比例少量，保证列表查询有内容
        var shareCount = Math.Min(Math.Max(1, targetRows / 50), shareTargets.Count);
        for (var s = 0; s < shareCount; s++)
        {
            var id = NewGuid(random);
            await ExecuteAsync(connection,
                """INSERT INTO "FileShares" ("Id","ShareCode","FileItemId","AccessType","PasswordHash","Permission","ExpiresAt","MaxDownloads","DownloadCount","CreatedByUserId","IsEnabled","CreatedAt") VALUES ($id,$code,$fid,$at,$ph,$perm,$exp,$maxd,$dc,$cb,$en,$created)""",
                ("$id", Key(id)),
                ("$code", $"share{s:D6}"),
                ("$fid", Key(shareTargets[s % shareTargets.Count])),
                ("$at", random.Next(3)),
                ("$ph", DBNull.Value),
                ("$perm", random.Next(1, 4)),
                ("$exp", (object?)Iso(DateTime.UtcNow.AddDays(random.Next(1, 90)))),
                ("$maxd", random.Next(2) == 0 ? random.Next(1, 100) : DBNull.Value),
                ("$dc", random.Next(0, 50)),
                ("$cb", Key(userIds[random.Next(userIds.Count)])),
                ("$en", 1),
                ("$created", Iso(DateTime.UtcNow.AddDays(-random.Next(1, 60)))));
        }

        await ExecuteAsync(connection, "PRAGMA optimize");
        await connection.CloseAsync();

        if (verbose)
            Console.WriteLine($"[seed] {dbPath}  行数={inserted}  用户={userCount}  分享={shareCount}");

        return inserted;
    }

    private static async Task InsertFileItem(
        SqliteConnection connection, Random random, Guid id, string name, string path,
        int folderType, Guid? parentId, List<Guid> poolIds, List<Guid> userIds, DateTime now,
        long sizeBytes, string? extension, bool deleted, Guid? ownerId = null)
    {
        await ExecuteAsync(connection,
            """INSERT INTO "FileItems" ("Id","Name","Path","PhysicalPath","ItemType","SizeBytes","MimeType","Extension","Hash","StoragePoolId","OwnerId","ParentId","IsDeleted","DeletedAt","IsHidden","IsSystem","IsReadOnly","CreatedAt") VALUES ($id,$name,$path,$phys,$type,$size,$mime,$ext,$hash,$pool,$owner,$parent,$del,$delat,$hid,$sys,$ro,$created)""",
            ("$id", Key(id)), ("$name", name), ("$path", path),
            ("$phys", $"/storage{path}"),
            ("$type", folderType), ("$size", sizeBytes),
            ("$mime", extension is null ? DBNull.Value : $"application/{extension.TrimStart('.')}"),
            ("$ext", (object?)extension ?? DBNull.Value),
            ("$hash", extension is null ? DBNull.Value : Convert.ToHexStringLower(RandomBytes(random, 16))),
            ("$pool", Key(poolIds[random.Next(poolIds.Count)])),
            ("$owner", Key(ownerId ?? userIds[random.Next(userIds.Count)])),
            ("$parent", (object?)(parentId.HasValue ? Key(parentId.Value) : null) ?? DBNull.Value),
            ("$del", deleted ? 1 : 0),
            ("$delat", deleted ? Iso(now.AddDays(-random.Next(1, 30))) : DBNull.Value),
            ("$hid", random.Next(50) == 0 ? 1 : 0),
            ("$sys", 0), ("$ro", random.Next(100) == 0 ? 1 : 0),
            ("$created", Iso(now.AddDays(-random.Next(0, 90)).AddMinutes(-random.Next(0, 1440)))));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static Guid NewGuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static byte[] RandomBytes(Random random, int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static string Iso(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");

    /// <summary>
    /// Guid 列的字面量格式。Microsoft.Data.Sqlite 绑定 Guid 参数时产出大写 D 格式，
    /// EF 写入存量库的 Id 全是大写（生产 nas.db 实测 C18376A4-...）。
    /// SQLite 的 TEXT 比较大小写敏感，种子数据若写小写，ORM 侧的外键检查会匹配不上父行
    /// （实测 SQLite Error 19 FOREIGN KEY constraint failed）。故此处与驱动保持一致。
    /// </summary>
    private static string Key(Guid value) => value.ToString().ToUpperInvariant();
}
