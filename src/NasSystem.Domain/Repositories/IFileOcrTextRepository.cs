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

    Task DeleteAsync(FileOcrText ocrText, CancellationToken cancellationToken = default);

    Task<FileOcrText?> GetByFileItemIdAsync(Guid fileItemId, CancellationToken cancellationToken = default);

    /// <summary>按文件项批量取（批量识别时跳过已有文本的文件）</summary>
    Task<IReadOnlyList<FileOcrText>> GetByFileItemIdsAsync(
        IReadOnlyList<Guid> fileItemIds,
        CancellationToken cancellationToken = default);
}
