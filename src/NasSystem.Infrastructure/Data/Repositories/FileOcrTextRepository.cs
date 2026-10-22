// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/FileOcrTextRepository.cs
// OCR 文本仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>OCR 文本仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
/// <remarks>
/// FTS 双写（ROADMAP Phase 2 全文检索升级）：业务表写入的同一意图队列里
/// 附带 FTS5 镜像表同步，一个事务落两张表——镜像与业务表不可能出现
/// "有文本无索引"的中间态。FTS 表不是 PalORM 行模型，同步走裸 SQL；
/// ExecuteAsync 的 FormattableString 不转参数化（实测：内插 Guid 会被
/// 拼成裸 token），故文本与 Guid 一律手工加引号字面量，转义规则见
/// <see cref="EscapeSqlLiteral"/>。
/// </remarks>
public sealed class FileOcrTextRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IFileOcrTextRepository
{
    /// <summary>FTS5 镜像表名（与 InitializeDatabaseAsync 的幂等建表同源）</summary>
    private const string FtsTable = "FileOcrTextsFts";

    public Task AddAsync(FileOcrText ocrText, CancellationToken cancellationToken = default)
    {
        var row = ocrText.ToRow();
        unitOfWork.EnqueueInsert(ocrText, async (db, ct) =>
        {
            await db.InsertAsync(row, ct);
            await SyncFtsAsync(db, row.FileItemId, row.Text, ct);
        });
        return Task.CompletedTask;
    }

    public void Update(FileOcrText ocrText)
    {
        var row = ocrText.ToRow();
        unitOfWork.EnqueueUpdate(ocrText, async (db, ct) =>
        {
            await db.UpdateAsync(row, ct);
            await SyncFtsAsync(db, row.FileItemId, row.Text, ct);
        });
    }

    /// <summary>
    /// 原子 upsert：判定交给数据库的 ON CONFLICT，避免并发首次识别
    /// 双双走 INSERT 撞 FileItemId 唯一索引。冲突时只覆盖文本相关列，
    /// 保留首次识别的 CreatedAt（与 UpdateText 的语义一致）。
    /// </summary>
    public Task UpsertAsync(FileOcrText ocrText, CancellationToken cancellationToken = default)
    {
        var row = ocrText.ToRow();
        unitOfWork.EnqueueCommand(async (db, ct) =>
        {
            await db.ExecuteAsync(
                System.Runtime.CompilerServices.FormattableStringFactory.Create(
                    "INSERT INTO \"FileOcrTexts\" " +
                    "(\"Id\",\"FileItemId\",\"Text\",\"Regions\",\"CharCount\",\"Model\",\"ElapsedMs\",\"CreatedAt\") " +
                    "VALUES ({0},{1},{2},{3},{4},{5},{6},{7}) " +
                    "ON CONFLICT(\"FileItemId\") DO UPDATE SET " +
                    "\"Text\"=excluded.\"Text\",\"Regions\"=excluded.\"Regions\"," +
                    "\"CharCount\"=excluded.\"CharCount\",\"Model\"=excluded.\"Model\"," +
                    "\"ElapsedMs\"=excluded.\"ElapsedMs\"",
                    row.Id, row.FileItemId, row.Text, (object?)row.Regions ?? DBNull.Value,
                    row.CharCount, row.Model, row.ElapsedMs, row.CreatedAt), ct);
            await SyncFtsAsync(db, row.FileItemId, row.Text, ct);
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// FTS 镜像同步：先删后插（upsert 语义——重复识别覆盖，与业务表
    /// FileItemId 唯一索引同口径）。删插两条都在同一意图队列里，
    /// 与业务表写入同事务
    /// </summary>
    private static async Task SyncFtsAsync(
        DataSession<SqliteProvider> db, Guid fileItemId, string text, CancellationToken ct)
    {
        // FtsTable 是编译期常量，字符串拼接不进参数；值一律走参数化绑定
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "DELETE FROM " + FtsTable + " WHERE FileItemId = {0}", fileItemId), ct);
        await db.ExecuteAsync(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                "INSERT INTO " + FtsTable + "(FileItemId, Text) VALUES ({0}, {1})", fileItemId, text), ct);
    }

    public Task DeleteAsync(FileOcrText ocrText, CancellationToken cancellationToken = default)
    {
        var id = ocrText.Id;
        unitOfWork.EnqueueDelete(ocrText, async (db, ct) => await db.DeleteAsync<FileOcrTextRow>(id, ct));
        return Task.CompletedTask;
    }

    public async Task<FileOcrText?> GetByFileItemIdAsync(
        Guid fileItemId,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<FileOcrTextRow>()
            .Where($"FileItemId = {fileItemId}")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<FileOcrText>> GetByFileItemIdsAsync(
        IReadOnlyList<Guid> fileItemIds,
        CancellationToken cancellationToken = default)
    {
        if (fileItemIds.Count == 0)
            return [];

        var db = await sessions.GetAsync(cancellationToken);

        // 逐 Id 占位（同 FileShareRepository.LoadFileItemsByIds 的口径）。
        // 曾经的 string.Join 把整串当一个插值孔，PalORM 整体参数化为
        // 单个字符串 → FileItemId IN ('a,b,c') 恒不匹配，方法永远返回空
        // （2026-10-19 审计实证；当时无调用方，属埋雷）
        var placeholders = string.Join(", ", fileItemIds.Select((_, i) => $"{{{i}}}"));
        var rows = await db.QueryAsync<FileOcrTextRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"SELECT * FROM FileOcrTexts WHERE FileItemId IN ({placeholders})",
                fileItemIds.Cast<object>().ToArray()),
            false,
            cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }
}
