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
public sealed class FileOcrTextRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IFileOcrTextRepository
{
    public Task AddAsync(FileOcrText ocrText, CancellationToken cancellationToken = default)
    {
        var row = ocrText.ToRow();
        unitOfWork.EnqueueInsert(ocrText, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.CompletedTask;
    }

    public void Update(FileOcrText ocrText)
    {
        var row = ocrText.ToRow();
        unitOfWork.EnqueueUpdate(ocrText, async (db, ct) => await db.UpdateAsync(row, ct));
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
        var rows = await db.From<FileOcrTextRow>()
            .Where($"FileItemId IN ({string.Join(",", fileItemIds)})")
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }
}
