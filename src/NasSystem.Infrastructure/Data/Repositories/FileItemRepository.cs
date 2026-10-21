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

    public async Task<FileItem?> GetByPathIncludingDeletedAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(path);
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileItemRow>()
            .Where($"Path = {normalizedPath}")
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
    /// 浏览专用子项查询：按需取 14 列 + IsHidden 下推 + 不排序 + 归属过滤。
    /// </summary>
    /// <remarks>
    /// 列序必须与 <see cref="FileItemBrowseRow"/> 的属性声明序逐列对齐（PalORM 投影
    /// 按序号映射，错位静默交换数据）。两个 WHERE 分支共用同一列序，只在前置条件上分叉：
    /// 根目录必须生成 <c>ParentId IS NULL</c>——FormattableString 的 null 实参只会变成
    /// <c>@pN</c> 占位，而 SQLite 里 <c>x = NULL</c> 恒不匹配，根目录会静默返回空。
    /// OwnerId 过滤是多用户隔离的库层防线：2026-10-19 E2E 走查实证新用户能浏览到
    /// 他人全部文件（.ai/lessons.md 记载的"FileService 不过滤 ownerId"缺口），
    /// OCR/媒体/语义/转码四处都校验归属，核心文件链路不能是例外
    /// </remarks>
    public async Task<IReadOnlyList<FileItemSummary>> GetChildrenSummariesAsync(
        Guid? parentId,
        Guid ownerId,
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
        // parentId 与 ownerId 都保留为绑定参数，不内联 Guid 字面量（绕过参数绑定
        // 就绕过 Microsoft.Data.Sqlite 的 Guid 大写 D 归一）
        System.FormattableString query;
        if (parentId.HasValue)
        {
            var withParent = "SELECT " + Columns + " FROM FileItems WHERE ParentId = {0}" +
                " AND IsDeleted = 0" + hiddenFilter + " AND OwnerId = {1}";
            query = System.Runtime.CompilerServices.FormattableStringFactory.Create(
                withParent, parentId.Value, ownerId);
        }
        else
        {
            var atRoot = "SELECT " + Columns + " FROM FileItems WHERE ParentId IS NULL" +
                " AND IsDeleted = 0" + hiddenFilter + " AND OwnerId = {0}";
            query = System.Runtime.CompilerServices.FormattableStringFactory.Create(atRoot, ownerId);
        }

        var rows = await db.QueryAsync<FileItemBrowseRow>(query, false, cancellationToken);

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

        // 文件名命中或图片内文字命中（ROADMAP Phase 2 OCR → 全文检索升级）。
        // 子查询而不是 JOIN：FileOcrTexts 是稀疏表（只有识别过的图片有行），
        // JOIN 会把未识别的文件从结果里挤出去。
        // OCR 通道按查询长度分流：trigram FTS 按三字子串切分，少于 3 字符的
        // 查询没有可用的 trigram（实测 MATCH 对 1-2 字符查询不命中），
        // 回退 LIKE 全表扫——短查询的命中集本来就大，LIKE 的代价可接受。
        // 文件名通道恒定 LIKE（名称是精确包含语义，与分词无关）
        // 占位符显式写 {{0}}/{{1}} 而不再调 Slot()：Slot() 按 args.Count 编号，
        // 而本段的两个占位都在 args.Add 之前求值，连续调用会撞成同一个 {0}
        // （改造前两个 LIKE 恰好都用同一 searchTerm 值，撞号被掩盖；MATCH 与
        // LIKE 的参数形态不同后必须分开编号）
        conditions.Add(
            $"IsDeleted = 0 AND (Name LIKE {{0}} ESCAPE '#' " +
            (searchTerm.Length >= 3
                ? "OR Id IN (SELECT FileItemId FROM FileOcrTextsFts WHERE FileOcrTextsFts MATCH {1}))"
                : "OR Id IN (SELECT FileItemId FROM FileOcrTexts WHERE Text LIKE {1} ESCAPE '#'))"));
        args.Add("%" + EscapeLike(searchTerm) + "%");
        args.Add(searchTerm.Length >= 3
            ? "\"" + EscapeFtsLiteral(searchTerm) + "\""
            : "%" + EscapeLike(searchTerm) + "%");

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
            System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT COUNT(*) FROM FileItems " + where, args.ToArray()),
            false,
            cancellationToken);

        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT * FROM FileItems " + where + orderAndPage, args.ToArray()),
            false,
            cancellationToken);

        return new FileSearchResult
        {
            Items = rows.ConvertAll(r => r.ToDomain()),
            TotalCount = (int)totalCount
        };
    }

    /// <summary>
    /// 媒体聚合查询（ROADMAP Phase 3）。与 SearchAsync 共用 Slot 绑定模式，
    /// 差别：无关键词条件（媒体库是浏览语义），扩展名 IN 下推，
    /// 月份用 strftime 在 SQL 侧过滤（CreatedAt 是 ISO 文本时间戳，
    /// strftime 直接可解析；不在内存里筛是为了分页总数正确）
    /// </summary>
    public async Task<FileSearchResult> GetMediaAsync(
        IReadOnlyList<string> extensions,
        Guid ownerId,
        string? month = null,
        int page = 1,
        int pageSize = 60,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 60;
        if (pageSize > 200) pageSize = 200;

        var db = await sessions.GetAsync(cancellationToken);

        var conditions = new List<string> { "IsDeleted = 0" };
        var args = new List<object>();
        string Slot() => "{" + args.Count + "}";

        conditions.Add($"ItemType = {(int)FileItemType.File}");
        conditions.Add($"OwnerId = {Slot()}");
        args.Add(ownerId);

        // 扩展名 IN：占位符连续编号，与 extensions 顺序一一对应
        var normalized = extensions
            .Select(e => e.ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();
        if (normalized.Count == 0)
        {
            return new FileSearchResult { Items = [], TotalCount = 0 };
        }

        var start = args.Count;
        var placeholders = string.Join(", ", normalized.Select((_, i) => $"{{{start + i}}}"));
        conditions.Add($"Extension IS NOT NULL AND lower(Extension) IN ({placeholders})");
        args.AddRange(normalized.Cast<object>());

        if (!string.IsNullOrWhiteSpace(month))
        {
            conditions.Add($"strftime('%Y-%m', CreatedAt) = {Slot()}");
            args.Add(month);
        }

        var where = "WHERE " + string.Join(" AND ", conditions);
        // 时间线语义：新的在前。Id 作次序键兜底全序确定（同秒创建的批量文件）
        var orderAndPage = $" ORDER BY CreatedAt DESC, Id LIMIT {pageSize} OFFSET {(long)(page - 1) * pageSize}";

        var totalCount = await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT COUNT(*) FROM FileItems " + where, args.ToArray()),
            false,
            cancellationToken);

        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create("SELECT * FROM FileItems " + where + orderAndPage, args.ToArray()),
            false,
            cancellationToken);

        return new FileSearchResult
        {
            Items = rows.ConvertAll(r => r.ToDomain()),
            TotalCount = (int)totalCount
        };
    }

    public async Task<IReadOnlyList<FileItem>> GetRecentAsync(
        Guid ownerId,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count < 1) count = 1;
        if (count > 50) count = 50;

        var db = await sessions.GetAsync(cancellationToken);
        // CreatedAt 倒序取前 N 条。OwnerId 走插值参数绑定（不拼字面量），
        // 理由同 GetStatisticsAsync 的注释：小写 Guid 字面量在 SQLite TEXT
        // 大小写敏感下恒 0 命中
        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT * FROM FileItems WHERE IsDeleted = 0 AND OwnerId = {0} ORDER BY CreatedAt DESC LIMIT {1}",
                new object[] { ownerId, count }),
            false,
            cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
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

        // 不含 IsDeleted 过滤 + Path 倒序，两个约束都服务于永久删除：
        // ① 回收站里的文件夹整棵子树都是软删态（SoftDeleteSubtreeAsync 同刻
        //    标记），带 IsDeleted=0 会取回空集，删父行时子的 ParentId 外键
        //    RESTRICT 直接失败（2026-10-19 审计实证：深度≥1 全灭）
        // ② 父路径是子路径的前缀，升序时父排在前先删同样撞外键；倒序保证
        //    最深的先删、父最后删
        var rows = await db.From<FileItemRow>()
            .Where($"Path LIKE {DescendantPrefix(parentPath)} ESCAPE '#'")
            .OrderByDescending(f => f.Path)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    // ---- 语义索引的扩展名查询（ROADMAP Phase 2）----

    public async Task<int> CountByExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        return (int)await db.CountAsync<FileItemRow>(
            $"IsDeleted = 0 AND ItemType = {(int)FileItemType.File} AND lower(Extension) = {normalized}",
            cancellationToken);
    }

    public async Task<IReadOnlyList<FileItemPathEntry>> GetPathsByExtensionAsync(
        string extension,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var normalized = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        var rows = await db.QueryAsync<FileItemPathProjectionRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT Id, Path, PhysicalPath FROM FileItems " +
                "WHERE IsDeleted = 0 AND ItemType = {0} AND lower(Extension) = {1}",
                (int)FileItemType.File, normalized),
            false,
            cancellationToken);
        return rows.Select(r => new FileItemPathEntry(r.Id, r.Path, r.PhysicalPath)).ToList();
    }

    // ---- 子树批量操作（S3：单条 SQL 替代逐条实体 UPDATE）----

    public async Task<IReadOnlyList<FileItemSnapshotEntry>> GetSnapshotEntriesAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 与 GetSubtreeEntriesAsync 同口径：根自身 + 全部后代、不论删除态、
        // OR 两支都走参数绑定。多取 Hash/UpdatedAt/CreatedAt 三列入快照采集
        var prefix = DescendantPrefix(rootPath);
        var rows = await db.QueryAsync<FileItemSnapshotEntryRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT Path, ItemType, SizeBytes, Hash, UpdatedAt, CreatedAt FROM FileItems WHERE Path = {0} OR Path LIKE {1} ESCAPE '#'",
                rootPath, prefix),
            false,
            cancellationToken);

        return rows.Select(r => new FileItemSnapshotEntry(
            r.Path, r.ItemType, r.SizeBytes, r.Hash, r.UpdatedAt, r.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<FileItemEntry>> GetSubtreeEntriesAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // 根自身 + 全部后代，不论删除态。OR 的两支都走参数绑定：
        // 根路径等值比较与 LIKE 前缀各一个占位
        var prefix = DescendantPrefix(rootPath);
        var rows = await db.QueryAsync<FileItemEntryRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "SELECT Path, ItemType, SizeBytes FROM FileItems WHERE Path = {0} OR Path LIKE {1} ESCAPE '#'",
                rootPath, prefix),
            false,
            cancellationToken);

        return rows.Select(r => new FileItemEntry(r.Path, r.ItemType, r.SizeBytes)).ToList();
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
        var prefix = DescendantPrefix(rootPath);
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
        var prefix = DescendantPrefix(rootPath);
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
        var prefix = DescendantPrefix(oldVirtualBase);
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

    public async Task<IReadOnlyDictionary<Guid, long>> CalculateUsersUsedSpaceAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, long>();

        var db = await sessions.GetAsync(cancellationToken);
        var idList = userIds.Distinct().ToArray();
        var placeholders = string.Join(", ", idList.Select((_, i) => $"{{{i}}}"));
        var rows = await db.QueryAsync<UserUsedSpaceRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"SELECT OwnerId, SUM(SizeBytes) AS UsedBytes FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 AND OwnerId IN ({placeholders}) GROUP BY OwnerId",
                idList.Cast<object>().ToArray()),
            false,
            cancellationToken);

        return rows.ToDictionary(r => r.OwnerId, r => r.UsedBytes);
    }

    public async Task<long> CalculateAllUsersUsedSpaceAsync(CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        return await db.ScalarAsync<long>(
            $"SELECT COALESCE(SUM(SizeBytes), 0) FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0",
            false,
            cancellationToken);
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

    public async Task<IReadOnlyList<FileItem>> GetByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        var db = await sessions.GetAsync(cancellationToken);

        // 逐 Id 占位 + 参数化（同 FileShareRepository.LoadFileItemsByIds 的口径）：
        // 字符串拼接 Guid 集合会被整体参数化成一个字符串，IN 恒不匹配
        var distinct = ids.Distinct().ToList();
        var placeholders = string.Join(", ", distinct.Select((_, i) => $"{{{i}}}"));
        var rows = await db.QueryAsync<FileItemRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"SELECT * FROM FileItems WHERE Id IN ({placeholders})",
                distinct.Cast<object>().ToArray()),
            false,
            cancellationToken);
        return rows.ConvertAll(r => r.ToDomain());
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
    /// <summary>
    /// FTS5 trigram 字符串字面量转义：双引号双写。查询形态是
    /// MATCH '"文本"'（trigram 的字符串字面量语法），文本内的双引号
    /// 不转义会提前终止字面量。SQL 注入面：字面量在单引号的 SQL 参数里，
    /// 双引号不涉及 SQL 字符串边界，双写只为 FTS 语法正确
    /// </summary>
    private static string EscapeFtsLiteral(string term) => term.Replace("\"", "\"\"");

    private static string EscapeLike(string value) =>
        value.Replace("#", "##").Replace("%", "#%").Replace("_", "#_");

    /// <summary>
    /// 后代路径的 LIKE 模式。根 / 的后代是 /% 而不是 //%：根路径按
    /// 常规拼法会产出双斜杠，永不匹配任何库存路径（库存路径形如 /a/b），
    /// 症状是快照采集、子树删除/恢复、移动重写在根路径下静默无操作
    /// （2026-10-19 E2E 走查实证：根路径快照采出 0 条目）
    /// </summary>
    private static string DescendantPrefix(string rootPath) =>
        rootPath == "/" ? "/%" : EscapeLike(rootPath) + "/%";

    /// <summary>
    /// 标准化路径（单一真源委托，见 FilePath.Normalize 的保留论证）
    /// </summary>
    private static string NormalizePath(string path) => Domain.ValueObjects.FilePath.Normalize(path);
}
