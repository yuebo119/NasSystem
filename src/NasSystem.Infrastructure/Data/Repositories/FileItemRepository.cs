// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/FileItemRepository.cs
// 文件项仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>
/// 文件项仓储实现。
/// </summary>
/// <remarks>
/// 查询一律手写 SQL 片段而不是 LINQ：PalORM 没有 LINQ provider。
/// <c>Where</c> 收 <see cref="FormattableString"/>，插值里的值在编译期参数化，
/// 与 EF 翻译 LINQ 的防注入等价；列名与表名按存量库的 PascalCase 字面写出。
/// 软删过滤仍由仓储显式写 <c>IsDeleted = 0</c>，不用 PalORM 的 <c>[SoftDelete]</c>：
/// 该特性硬绑定 <c>deleted_at</c> 列，而本schema 是 <c>IsDeleted</c> 位加部分唯一索引
/// <c>WHERE [IsDeleted] = 0</c>，改列名会牵动索引与全部查询谓词。
/// </remarks>
public sealed class FileItemRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IFileItemRepository
{
    public async Task<FileItem?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileItemRow>()
            .Where($"Path = {normalizedPath} AND IsDeleted = 0")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<FileItem>> GetChildrenAsync(
        Guid? parentId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        // 根目录的 parentId 是 null，必须生成 `ParentId IS NULL`。
        // PalORM 的 FormattableString 格式化与值无关，null 实参只会变成 `@pN` 占位，
        // 而 SQLite 里 `x = NULL` 结果是 NULL（不匹配任何行），根目录会静默返回空。
        // EF 时代 LINQ 的 `f.ParentId == parentId` 由提供方翻译成 IS NULL，无此问题。
        var query = parentId.HasValue
            ? db.From<FileItemRow>().Where($"ParentId = {parentId.Value}")
            : db.From<FileItemRow>().Where(System.Runtime.CompilerServices.FormattableStringFactory.Create("ParentId IS NULL"));

        if (!includeDeleted)
            query = query.Where($"IsDeleted = 0");

        var rows = await query
            .OrderByDescending(f => f.ItemType) // 文件夹优先
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    /// <summary>
    /// 浏览专用子项查询：按需取 14 列 + IsHidden 下推 + 不排序。
    /// </summary>
    /// <remarks>
    /// 列序必须与 <see cref="FileItemBrowseRow"/> 的属性声明序逐列对齐（PalORM 投影
    /// 按序号映射，错位静默交换数据）。两个 WHERE 分支共用同一列序，只在前置条件上分叉：
    /// 根目录必须生成 <c>ParentId IS NULL</c>——FormattableString 的 null 实参只会变成
    /// <c>@pN</c> 占位，而 SQLite 里 <c>x = NULL</c> 恒不匹配，根目录会静默返回空。
    /// </remarks>
    public async Task<IReadOnlyList<FileItemSummary>> GetChildrenSummariesAsync(
        Guid? parentId,
        bool includeHidden = false,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        const string Columns =
            "Id, Name, Path, ItemType, SizeBytes, MimeType, Extension, " +
            "OwnerId, ParentId, IsHidden, IsReadOnly, CreatedAt, UpdatedAt";

        // 隐藏文件过滤下推：原先载回全部子项再内存 Where，这里直接少读少传。
        // 片段以字符串拼接并入，不能当 $"...{hiddenFilter}" 的插值孔——孔会被
        // PalORM 参数化，产出 "... IsDeleted = 0 @p1" 直接语法错误
        var hiddenFilter = includeHidden ? "" : " AND IsHidden = 0";

        // 根目录必须生成 ParentId IS NULL：FormattableString 的 null 实参只会变成
        // @pN 占位，而 SQLite 里 x = NULL 恒不匹配，根目录会静默返回空。
        // parentId 保留为 {0} 绑定参数，不内联 Guid 字面量（绕过参数绑定就绕过
        // Microsoft.Data.Sqlite 的 Guid 大写 D 归一）
        var parentClause = parentId.HasValue ? "ParentId = {0}" : "ParentId IS NULL";

        var sql = "SELECT " + Columns + " FROM FileItems WHERE " + parentClause +
            " AND IsDeleted = 0" + hiddenFilter;

        var rows = await db.QueryAsync<FileItemBrowseRow>(
            parentId.HasValue
                ? System.Runtime.CompilerServices.FormattableStringFactory.Create(sql, parentId.Value)
                : System.Runtime.CompilerServices.FormattableStringFactory.Create(sql),
            false,
            cancellationToken);

        return rows.Select(r => new FileItemSummary
        {
            Id = r.Id,
            Name = r.Name,
            Path = r.Path,
            ItemType = r.ItemType,
            SizeBytes = r.SizeBytes,
            MimeType = r.MimeType,
            Extension = r.Extension,
            OwnerId = r.OwnerId,
            ParentId = r.ParentId,
            IsHidden = r.IsHidden,
            IsReadOnly = r.IsReadOnly,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<FileItem>> GetByStoragePoolAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        // 不含软删记录：调用方（扫描同步）另取软删集并入对比表，双集 concat 后按 Path 建字典
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<FileItemRow>()
            .Where($"StoragePoolId = {storagePoolId} AND IsDeleted = 0")
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    public async Task<IReadOnlyList<FileItem>> GetDeletedItemsAsync(
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var query = db.From<FileItemRow>().Where($"IsDeleted = 1");

        if (ownerId.HasValue)
            query = query.Where($"OwnerId = {ownerId.Value}");
        if (storagePoolId.HasValue)
            query = query.Where($"StoragePoolId = {storagePoolId.Value}");

        var rows = await query
            .OrderByDescending(f => f.DeletedAt)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    public async Task<FileSearchResult> SearchAsync(
        string searchTerm,
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        string? basePath = null,
        string? extension = null,
        bool? isFolder = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // WHERE 片段集中构造一次，COUNT 与取数两条 SQL 共用，避免两边过滤条件漂移。
        // 每条条件一个绑定参数，占位符编号即 args 的当前下标。
        // 两个坑都用字符串拼接避开，不用 string.Format(template, index)：
        // 那会把模板里的 {0} 当成 string.Format 自己的格式孔吃掉，产出
        // "Name LIKE 0" 这种不报错但静默变味的 SQL（实测：条件变味后查询恒返回 0 行）
        var conditions = new List<string>();
        var args = new List<object>();

        string Slot() => "{" + args.Count + "}";

        conditions.Add($"IsDeleted = 0 AND Name LIKE {Slot()} ESCAPE '#'");
        args.Add("%" + EscapeLike(searchTerm) + "%");

        if (ownerId.HasValue)
        {
            conditions.Add($"OwnerId = {Slot()}");
            args.Add(ownerId.Value);
        }

        if (storagePoolId.HasValue)
        {
            conditions.Add($"StoragePoolId = {Slot()}");
            args.Add(storagePoolId.Value);
        }

        if (!string.IsNullOrEmpty(basePath))
        {
            var normalizedBasePath = NormalizePath(basePath);
            conditions.Add($"Path LIKE {Slot()} ESCAPE '#'");
            args.Add(EscapeLike(normalizedBasePath) + "%");
        }

        if (isFolder.HasValue)
        {
            var itemType = (int)(isFolder.Value ? FileItemType.Folder : FileItemType.File);
            conditions.Add($"ItemType = {Slot()}");
            args.Add(itemType);
        }

        // 扩展名过滤（支持多个扩展名，用逗号分隔）——下推 SQL。
        // EF 翻译集合 Contains 为 IN (...)。占位符从 args 当前长度起连续编号，
        // 与随后 AddRange 的顺序一一对应
        if (!string.IsNullOrEmpty(extension))
        {
            var extensions = extension.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().StartsWith('.') ? e.Trim().ToLowerInvariant() : "." + e.Trim().ToLowerInvariant())
                .ToHashSet();

            var start = args.Count;
            var placeholders = string.Join(", ", extensions.Select((_, i) => $"{{{start + i}}}"));
            conditions.Add($"Extension IS NOT NULL AND lower(Extension) IN ({placeholders})");
            args.AddRange(extensions.Cast<object>());
        }

        var where = "WHERE " + string.Join(" AND ", conditions);

        // 防御性夹取：page/pageSize 来自查询串，无上限时一个超大 pageSize 就能把整表拖回来
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        // 排序在此从可选变成必需：没有确定的全序，LIMIT/OFFSET 跨页可能重复或漏行
        var orderAndPage = $" ORDER BY ItemType DESC, Name LIMIT {pageSize} OFFSET {(long)(page - 1) * pageSize}";

        // 总数走同 WHERE 的 COUNT，不是取回条数（原实现报窗口计数，分页器总页数从第二页起是错的）
        var totalCount = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT COUNT(*) FROM FileItems {where}", args.ToArray()),
            false,
            cancellationToken);

        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT * FROM FileItems {where}{orderAndPage}", args.ToArray()),
            false,
            cancellationToken);

        return new FileSearchResult
        {
            Items = rows.ConvertAll(r => r.ToDomain()),
            TotalCount = (int)totalCount
        };
    }

    public async Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // ownerId 一律作为插值参数绑定，不拼 SQL 字面量：
        // Microsoft.Data.Sqlite 绑 Guid 参数时输出大写 D 格式，与写入侧一致。
        // 曾把小写 Guid（Guid.ToString() 的默认形态）拼进字面量，SQLite TEXT
        // 大小写敏感，带 owner 过滤的统计恒返回 0（2026-10-15 实测：小写字面量
        // 0 命中，参数化 34 命中）。这与本仓"Guid 必须大写 D"是同一坑的第六个
        // 实例，且最隐蔽：绕过参数绑定就绕过了大写归一。

        // 分类表（与原内存版口径一致：空扩展名归 Other）
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico" };
        var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv" };
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma" };
        var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".json", ".xml" };

        // 四项计数合成单行聚合：原实现发四条独立 SQL，其中三条 WHERE 完全同形
        // （IsDeleted = 0 AND ItemType = 0），等于把同一批非删行扫三遍。
        // 用 CASE 而非 FILTER：后者要 SQLite 3.30+，原生引擎版本由 SQLite3MC 包决定，
        // 没必要为一行 SQL 背上引擎版本前提。
        //
        // 时间边界做 SQL 字面量：它由本方法格式化的 DateTime 产生，不含用户输入。
        // 且必须用空格分隔——Microsoft.Data.Sqlite 绑定 DateTime 时用的格式是
        // "yyyy-MM-dd HH:mm:ss.FFFFFFF"，库里存的也是空格分隔。TEXT 是二进制比较，
        // 若写成 'T' 分隔，边界日当天所有记录在位置 10 上 ' '(0x20) < 'T'(0x54)，
        // 会被整日排除（实测：边界日中午建的记录，T 分隔符计数 0，空格分隔符计数 1）。
        //
        // ownerId 必须做绑定参数：拼字面量会绕过 Microsoft.Data.Sqlite 的 Guid 大写 D
        // 归一，SQLite TEXT 大小写敏感，带 owner 过滤的统计会恒返回 0
        //（2026-10-15 实测：小写字面量 0 命中，参数化 34 命中）。
        //
        // 拼 SQL 时不能用 $"...{某变量}" 的插值孔承载 SQL 片段：孔会被 PalORM
        // 参数化，产出 "... IsDeleted = 0 @p1" 直接语法错误。片段一律用 + 拼接，
        // 只有需要绑定的值才留 {N} 占位符交给 FormattableStringFactory.Create。
        // 同理不能用 string.Format(template, index) 重编号：那会把模板里的 {0}
        // 当成 string.Format 自己的格式孔吃掉，产出 "Name LIKE 0" 这种不报错
        // 但静默变味的 SQL（实测：条件变味后查询恒返回 0 行）。
        var recentLiteral = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss.fffffff");
        var ownerClause = ownerId.HasValue ? " AND OwnerId = {0}" : "";

        const string AggregateColumns =
            """
            SUM(CASE WHEN ItemType = 0 THEN 1 ELSE 0 END) AS TotalFiles,
            SUM(CASE WHEN ItemType = 1 THEN 1 ELSE 0 END) AS TotalFolders,
            COALESCE(SUM(CASE WHEN ItemType = 0 THEN SizeBytes ELSE 0 END), 0) AS TotalSizeBytes,
            SUM(CASE WHEN ItemType = 0 AND CreatedAt >= 'RECENT' THEN 1 ELSE 0 END) AS RecentFiles
            """;

        var countsSql = "SELECT " + AggregateColumns.Replace("RECENT", recentLiteral) +
            " FROM FileItems WHERE IsDeleted = 0" + ownerClause;

        var counts = await db.QueryAsync<FileStatisticsCountsRow>(
            ownerId.HasValue
                ? System.Runtime.CompilerServices.FormattableStringFactory.Create(countsSql, ownerId.Value)
                : System.Runtime.CompilerServices.FormattableStringFactory.Create(countsSql),
            false,
            cancellationToken);

        var row = counts.Count > 0 ? counts[0] : null;
        var totalFiles = row?.TotalFiles ?? 0;
        var totalFolders = row?.TotalFolders ?? 0;
        var totalSizeBytes = row?.TotalSizeBytes ?? 0;
        var recentFilesCount = row?.RecentFiles ?? 0;

        // 扩展名分组一次拿全（distinct 集合小，内存分类无放大风险）。
        // 与上面的计数共用同一个 ownerClause 片段，两条 SQL 的过滤条件不会漂移
        var extensionCounts = await db.QueryAsync<ExtensionCountRow>(
            ownerId.HasValue
                ? System.Runtime.CompilerServices.FormattableStringFactory.Create(
                    "SELECT CASE WHEN Extension IS NULL THEN NULL ELSE lower(Extension) END AS Extension, " +
                    "COUNT(*) AS Count FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0" + ownerClause + " GROUP BY 1",
                    ownerId.Value)
                : System.Runtime.CompilerServices.FormattableStringFactory.Create(
                    "SELECT CASE WHEN Extension IS NULL THEN NULL ELSE lower(Extension) END AS Extension, " +
                    "COUNT(*) AS Count FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 GROUP BY 1"),
            false,
            cancellationToken);

        long CountIn(string[] set) =>
            extensionCounts.Where(e => e.Extension != null && set.Contains(e.Extension)).Sum(e => e.Count);

        var imageCount = CountIn(imageExtensions);
        var videoCount = CountIn(videoExtensions);
        var audioCount = CountIn(audioExtensions);
        var documentCount = CountIn(documentExtensions);

        return new FileStatistics
        {
            TotalFiles = (int)totalFiles,
            TotalFolders = (int)totalFolders,
            TotalSizeBytes = totalSizeBytes,
            ImageCount = (int)imageCount,
            VideoCount = (int)videoCount,
            AudioCount = (int)audioCount,
            DocumentCount = (int)documentCount,
            OtherCount = (int)(totalFiles - imageCount - videoCount - audioCount - documentCount),
            RecentFilesCount = (int)recentFilesCount
        };
    }

    public async Task<IReadOnlyList<FileItem>> GetDescendantsAsync(
        Guid parentId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 取父路径。EF 时代用 NoTracking 是因为调用方常已 Attach 同 Id 实体做更新，
        // FindAsync 的跟踪加载会触发 IdentityMap 键冲突；PalORM 无 IdentityMap，该坑不存在
        var parentPath = await db.ScalarAsync<string?>(
            $"SELECT Path FROM FileItems WHERE Id = {parentId}",
            false, cancellationToken);
        if (parentPath is null)
            return [];

        var rows = await db.From<FileItemRow>()
            .Where($"Path LIKE {EscapeLike(parentPath + "/") + '%'} ESCAPE '#' AND IsDeleted = 0")
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    // ---- 子树批量操作（S3：单条 SQL 替代逐条实体 UPDATE）----

    public async Task<IReadOnlyList<FileItemEntry>> GetSubtreeEntriesAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 根自身 + 全部后代，不论删除态。OR 的两支都走参数绑定：
        // 根路径等值比较与 LIKE 前缀各一个占位
        var prefix = EscapeLike(rootPath) + "/%";
        var rows = await db.QueryAsync<FileItemEntryRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT Path, ItemType FROM FileItems WHERE Path = {0} OR Path LIKE {1} ESCAPE '#'",
                rootPath, prefix),
            false,
            cancellationToken);

        return rows.Select(r => new FileItemEntry(r.Path, r.ItemType)).ToList();
    }

    public Task SoftDeleteSubtreeAsync(
        string rootPath,
        DateTime deletedAt,
        CancellationToken cancellationToken = default)
    {
        // 经意图队列登记：与同批次其他写入在同一个事务里提交，
        // 中途失败整体回滚（逐条实体 Update 时代同样由 UoW 事务保证）。
        // UpdatedAt 一并写：原逐条实现经 SoftDelete 的 MarkAsModified 会写，
        // 不写则前端"修改时间"显示与旧行为不一致
        var prefix = EscapeLike(rootPath) + "/%";
        unitOfWork.EnqueueCommand(async (db, ct) => await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "UPDATE FileItems SET IsDeleted = 1, DeletedAt = {0}, UpdatedAt = {0} " +
                "WHERE (Path = {1} OR Path LIKE {2} ESCAPE '#') AND IsDeleted = 0",
                deletedAt, rootPath, prefix),
            ct));

        return Task.CompletedTask;
    }

    public Task RestoreSubtreeAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var prefix = EscapeLike(rootPath) + "/%";
        unitOfWork.EnqueueCommand(async (db, ct) => await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "UPDATE FileItems SET IsDeleted = 0, DeletedAt = NULL, UpdatedAt = {0} " +
                "WHERE Path = {1} OR Path LIKE {2} ESCAPE '#'",
                now, rootPath, prefix),
            ct));

        return Task.CompletedTask;
    }

    public Task RewriteSubtreePathsAsync(
        string oldVirtualBase,
        string newVirtualBase,
        string oldPhysicalBase,
        string newPhysicalBase,
        CancellationToken cancellationToken = default)
    {
        // 新路径 = 新基路径 + 旧路径去掉旧基前缀的相对部分。substr(Path, len+1)
        // 从旧基长度之后一位开始取（SQLite substr 从 1 起数），取出的相对段以
        // 路径分隔符开头，直接拼到新基后面。物理路径同法：库存 PhysicalPath
        // 用 OS 分隔符，相对段原样拼接即可，不需要 replace（旧基与新基同形态）。
        // 五个值全部走绑定参数：基路径含用户输入（名称），内联即绕过参数化；
        // 两个长度是本方法计算的 int，同样按参数传递保持口径统一
        var prefix = EscapeLike(oldVirtualBase) + "/%";
        unitOfWork.EnqueueCommand(async (db, ct) => await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "UPDATE FileItems SET " +
                "Path = {1} || substr(Path, {2}), " +
                "PhysicalPath = {3} || substr(PhysicalPath, {4}) " +
                "WHERE Path LIKE {0} ESCAPE '#'",
                prefix, newVirtualBase, oldVirtualBase.Length + 1,
                newPhysicalBase, oldPhysicalBase.Length + 1),
            ct));

        return Task.CompletedTask;
    }

    public async Task<long> CalculateUserUsedSpaceAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        return await db.ScalarAsync<long>(
            $"SELECT COALESCE(SUM(SizeBytes), 0) FROM FileItems WHERE OwnerId = {userId} AND IsDeleted = 0 AND ItemType = 0",
            false, cancellationToken);
    }

    public async Task<bool> PathExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<FileItemRow>(
            $"Path = {normalizedPath} AND IsDeleted = 0", cancellationToken);
        return count > 0;
    }

    public Task DeleteRangeAsync(
        IEnumerable<FileItem> items,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var id = item.Id;
            unitOfWork.EnqueueDelete(item, async (db, ct) => await db.DeleteAsync<FileItemRow>(id, ct));
        }
        return Task.CompletedTask;
    }

    public async Task<long> CalculateStoragePoolUsedSpaceAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        return await db.ScalarAsync<long>(
            $"SELECT COALESCE(SUM(SizeBytes), 0) FROM FileItems WHERE StoragePoolId = {storagePoolId} AND IsDeleted = 0 AND ItemType = 0",
            false, cancellationToken);
    }

    // ---- 扫描同步的轻量对比表与批量写（S2）----

    /// <summary>批量插入的每批行数。PalORM 的 BulkInsertAsync 按此分多值 INSERT 语句</summary>
    private const int BulkBatchSize = 500;

    public async Task<IReadOnlyList<FileItemScanEntry>> GetScanEntriesAsync(
        Guid storagePoolId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 含软删记录（ITM-N26）：其物理文件可能仍在挂载点，必须进对比表，
        // 否则会被当作"新发现"重建（软删文件复活）且恢复时撞 Path 唯一索引。
        // 列序与 FileItemScanRow 属性声明序逐列对齐。
        // storagePoolId 走绑定参数：拼字面量会绕过 Microsoft.Data.Sqlite 的
        // Guid 大写 D 归一，SQLite TEXT 大小写敏感，带池过滤的查询会恒返回空
        var rows = await db.QueryAsync<FileItemScanRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT Id, Path, ItemType, SizeBytes, ParentId, IsDeleted " +
                "FROM FileItems WHERE StoragePoolId = {0}",
                storagePoolId),
            false,
            cancellationToken);

        return rows.Select(r => new FileItemScanEntry(
            r.Id, r.Path, r.ItemType, r.SizeBytes, r.ParentId, r.IsDeleted)).ToList();
    }

    public Task BulkInsertAsync(
        IReadOnlyList<FileItem> entities,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
            return Task.CompletedTask;

        // 行在登记时快照：调用方（扫描同步）登记后不再改这些实体，
        // 与逐条 AddAsync 的 ToRow 时机一致
        var rows = entities.Select(e => e.ToRow()).ToList();

        // 经意图队列登记：与同批次其他写入在同一个事务里提交。
        // BulkInsertAsync 复用会话事务，不会自成一体
        unitOfWork.EnqueueCommand(async (db, ct) =>
            await db.BulkInsertAsync(rows, BulkBatchSize, ct));

        return Task.CompletedTask;
    }

    public Task BulkUpdateAsync(
        IReadOnlyList<FileItem> entities,
        CancellationToken cancellationToken = default)
    {
        if (entities.Count == 0)
            return Task.CompletedTask;

        var rows = entities.Select(e => e.ToRow()).ToList();
        unitOfWork.EnqueueCommand(async (db, ct) =>
            await db.BulkUpdateAsync(rows, ct));

        return Task.CompletedTask;
    }

    public Task BulkDeleteAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Task.CompletedTask;

        // 类型化键重载：IReadOnlyList<Guid> 到 Guid[] 全程零装箱
        unitOfWork.EnqueueCommand(async (db, ct) =>
            await db.BulkDeleteAsync<FileItemRow, Guid>(ids, ct));

        return Task.CompletedTask;
    }

    // ---- IRepository<FileItem> 基接口成员：写入走意图队列，读走会话 ----

    public async ValueTask<FileItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<FileItemRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<FileItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<FileItemRow>().ToListAsync(cancellationToken);
        return rows.ConvertAll(r => r.ToDomain());
    }

    public Task<FileItem> AddAsync(FileItem entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueInsert(entity, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(FileItem entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueUpdate(entity, async (db, ct) => await db.UpdateAsync(row, ct));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(FileItem entity, CancellationToken cancellationToken = default)
    {
        var id = entity.Id;
        unitOfWork.EnqueueDelete(entity, async (db, ct) => await db.DeleteAsync<FileItemRow>(id, ct));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 转义 LIKE 通配符。EF 翻译 <c>string.Contains</c> 时会转义 % 与 _，
    /// 手写 SQL 必须自己转义，否则用户输入的下划线被当成任意单字符匹配。
    /// </summary>
    /// <remarks>
    /// 两个容易踩的点：
    /// 一、SQLite 的 LIKE <b>没有默认转义字符</b>，只转义模式不配 ESCAPE 子句的话，
    /// 通配符照样生效，转义形同虚设。
    /// 二、转义字符选 <c>#</c> 而不是反斜杠。反斜杠在 C# 字符串里本身要转义，
    /// 多层嵌套后极易写成"运行期是空字符"的 ESCAPE ''，那种错法 SQLite 直接报语法错误；
    /// # 在 C# 字符串里无需转义，写出来就是运行期的样子。
    /// 每个用到这里产物的 LIKE 都必须配 <c>ESCAPE '#'</c> 子句（已由行为测试锁住）。
    /// </remarks>
    private static string EscapeLike(string value) =>
        value.Replace("#", "##").Replace("%", "#%").Replace("_", "#_");

    /// <summary>
    /// 标准化路径（单一真源委托，见 FilePath.Normalize 的保留论证）
    /// </summary>
    private static string NormalizePath(string path) => Domain.ValueObjects.FilePath.Normalize(path);
}
