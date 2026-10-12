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
        var query = db.From<FileItemRow>().Where($"ParentId = {parentId}");

        if (!includeDeleted)
            query = query.Where($"IsDeleted = 0");

        var rows = await query
            .OrderByDescending(f => f.ItemType) // 文件夹优先
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
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

    public async Task<IReadOnlyList<FileItem>> SearchAsync(
        string searchTerm,
        Guid? ownerId = null,
        Guid? storagePoolId = null,
        string? basePath = null,
        string? extension = null,
        bool? isFolder = null,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var query = db.From<FileItemRow>()
            .Where($"IsDeleted = 0 AND Name LIKE {'%' + EscapeLike(searchTerm) + '%'}");

        if (ownerId.HasValue)
            query = query.Where($"OwnerId = {ownerId.Value}");
        if (storagePoolId.HasValue)
            query = query.Where($"StoragePoolId = {storagePoolId.Value}");

        if (!string.IsNullOrEmpty(basePath))
        {
            var normalizedBasePath = NormalizePath(basePath);
            query = query.Where($"Path LIKE {EscapeLike(normalizedBasePath) + '%'}");
        }

        if (isFolder.HasValue)
        {
            var itemType = (int)(isFolder.Value ? FileItemType.Folder : FileItemType.File);
            query = query.Where($"ItemType = {itemType}");
        }

        // 扩展名过滤（支持多个扩展名，用逗号分隔）——下推 SQL。
        // 原实现先 Take(500) 截断再内存过滤：截断后的过滤既丢结果也让分页失真
        if (!string.IsNullOrEmpty(extension))
        {
            var extensions = extension.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim().StartsWith('.') ? e.Trim().ToLowerInvariant() : "." + e.Trim().ToLowerInvariant())
                .ToHashSet();

            // EF 翻译集合 Contains 为 IN (...)。这里用 FormattableStringFactory 显式给格式与参数：
            // 若把拼好的 "{0}, {1}" 直接插进 $"...{inList}"，它会被当成参数值而不是占位符
            var placeholders = string.Join(", ", extensions.Select((_, i) => $"{{{i}}}"));
            query = query.Where(System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"Extension IS NOT NULL AND lower(Extension) IN ({placeholders})",
                extensions.Cast<object>().ToArray()));
        }

        // 结果集上限（服务端分页为独立事项：当前服务层做客户端分页，TotalCount
        // 取的是本窗口计数；此上限防止无参搜索拖全表）
        var rows = await query
            .OrderByDescending(f => f.ItemType)
            .ThenBy(f => f.Name)
            .Take(500)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
    }

    public async Task<FileStatistics> GetStatisticsAsync(
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);

        // ownerId 是 Guid（非用户文本），直接拼成 SQL 字面量；
        // 用 FormattableStringFactory 包成无插值的 FormattableString，
        // 避免 $"...{filter}" 把整段 WHERE 当成参数值
        var filter = ownerId.HasValue ? $"AND OwnerId = '{ownerId.Value}'" : string.Empty;

        // 分类表（与原内存版口径一致：空扩展名归 Other）
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico" };
        var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".webm", ".wmv", ".flv" };
        var audioExtensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma" };
        var documentExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".json", ".xml" };

        var recentLiteral = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss.fffffff");

        // 全部聚合下推 SQL：原实现 ToListAsync 全表载入后内存 Count/Sum，
        // 文件数上万时单接口即全表传输；此处每项均为服务端单行聚合
        var totalFiles = await db.ScalarAsync<long>(
            Literal($"SELECT COUNT(*) FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 {filter}"),
            false, cancellationToken);
        var totalFolders = await db.ScalarAsync<long>(
            Literal($"SELECT COUNT(*) FROM FileItems WHERE IsDeleted = 0 AND ItemType = 1 {filter}"),
            false, cancellationToken);
        var totalSizeBytes = await db.ScalarAsync<long>(
            Literal($"SELECT COALESCE(SUM(SizeBytes), 0) FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 {filter}"),
            false, cancellationToken);
        var recentFilesCount = await db.ScalarAsync<long>(
            Literal($"SELECT COUNT(*) FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 AND CreatedAt >= '{recentLiteral}' {filter}"),
            false, cancellationToken);

        // 扩展名分组一次拿全（distinct 集合小，内存分类无放大风险）
        var extensionCounts = await db.QueryAsync<ExtensionCountRow>(
            Literal($"SELECT CASE WHEN Extension IS NULL THEN NULL ELSE lower(Extension) END AS Extension, COUNT(*) AS Count FROM FileItems WHERE IsDeleted = 0 AND ItemType = 0 {filter} GROUP BY 1"),
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
            .Where($"Path LIKE {EscapeLike(parentPath + "/") + '%'} AND IsDeleted = 0")
            .OrderBy(f => f.Path)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => r.ToDomain());
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
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// 把已拼好的 SQL 包成无插值的 <see cref="FormattableString"/>。
    /// 只用于值已由调用方保证安全（Guid、枚举、已转义字面量）的场景；
    /// 凡是来自用户输入的值一律走 <c>$"..."</c> 插值或显式 format+args。
    /// </summary>
    private static FormattableString Literal(string sql) => System.Runtime.CompilerServices.FormattableStringFactory.Create(sql);

    /// <summary>
    /// 标准化路径（单一真源委托，见 FilePath.Normalize 的保留论证）
    /// </summary>
    private static string NormalizePath(string path) => Domain.ValueObjects.FilePath.Normalize(path);
}
