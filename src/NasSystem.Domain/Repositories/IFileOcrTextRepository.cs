// =============================================================================
// NasSystem.Domain - IFileOcrTextRepository.cs
// 图片 OCR 文本仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// OCR 文本仓储。与 FileItem 一对一（FileItemId 唯一）
/// </summary>
public interface IFileOcrTextRepository
{
    Task AddAsync(FileOcrText ocrText, CancellationToken cancellationToken = default);

    void Update(FileOcrText ocrText);

    /// <summary>
    /// 原子 upsert（按 FileItemId 冲突则覆盖文本字段，保留原 CreatedAt）。
    /// 并发首次识别同一张图时，读后写会双双 INSERT 撞唯一索引；
    /// 单条 ON CONFLICT 语句把判定交给数据库，两个请求都成功。
    /// </summary>
    Task UpsertAsync(FileOcrText ocrText, CancellationToken cancellationToken = default);

    Task DeleteAsync(FileOcrText ocrText, CancellationToken cancellationToken = default);

    Task<FileOcrText?> GetByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>按文件项批量取（批量识别时跳过已有文本的文件）</summary>
    Task<IReadOnlyList<FileOcrText>> GetByFileItemIdsAsync(
        IReadOnlyList<Guid> fileItemIds,
        CancellationToken cancellationToken = default);
}
