// =============================================================================
// NasSystem.Infrastructure - Services/ProtectedDirectoryService.cs
// 受保护目录服务实现（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using NasSystem.Application.ProtectedDirectories;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>受保护目录服务实现。</summary>
/// <remarks>
/// 标记限定文件夹：保护语义是"这个目录及其全部子孙只读"，对单文件标记
/// 没有额外语义（文件自身的只读由 FileItem.IsReadOnly 管，那是另一条线）。
/// 幂等标记：重复标记返回现有标记而非报错——管理动作重试不该失败。
/// 路径展示经 FileItem 查询（个位数标记，逐条查可接受），不冗余存路径：
/// 路径会随重命名/移动变，冗余存即漂移源。
/// </remarks>
public sealed class ProtectedDirectoryService(
    IProtectedDirectoryRepository protectedDirectories,
    IFileItemRepository fileItems,
    IUnitOfWork unitOfWork) : IProtectedDirectoryService
{
    public async Task<Result<ProtectedDirectoryDto>> MarkAsync(
        Guid fileItemId,
        Guid operatorUserId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var item = await fileItems.GetByIdAsync(fileItemId, cancellationToken);
        if (item is null)
            return Result<ProtectedDirectoryDto>.Failure(
                Error.NotFound(nameof(FileItem), fileItemId));
        if (!item.IsFolder)
            return Result<ProtectedDirectoryDto>.Failure(
                Error.Validation("只能标记目录（文件夹）为受保护"));

        var existing = await protectedDirectories.GetByFileItemIdAsync(fileItemId, cancellationToken);
        if (existing is not null)
        {
            // 幂等：已标记直接返回现有标记
            return Result<ProtectedDirectoryDto>.Success(ToDto(existing, item.Path, item.Name));
        }

        var mark = ProtectedDirectory.Create(fileItemId, operatorUserId, note);
        await protectedDirectories.AddAsync(mark, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ProtectedDirectoryDto>.Success(ToDto(mark, item.Path, item.Name));
    }

    public async Task<Result> UnmarkAsync(Guid fileItemId, CancellationToken cancellationToken = default)
    {
        var existing = await protectedDirectories.GetByFileItemIdAsync(fileItemId, cancellationToken);
        if (existing is null)
            return Result.Failure(Error.NotFound(nameof(ProtectedDirectory), fileItemId));

        protectedDirectories.Delete(existing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ProtectedDirectoryDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var marks = await protectedDirectories.GetAllAsync(cancellationToken);
        var result = new List<ProtectedDirectoryDto>(marks.Count);
        foreach (var mark in marks)
        {
            var item = await fileItems.GetByIdAsync(mark.FileItemId, cancellationToken);
            if (item is null)
                continue; // 目录已被永久删除的悬空标记不展示（判定时也不命中）
            result.Add(ToDto(mark, item.Path, item.Name));
        }
        return Result<IReadOnlyList<ProtectedDirectoryDto>>.Success(result);
    }

    public Task<bool> IsProtectedAsync(string path, CancellationToken cancellationToken = default) =>
        protectedDirectories.IsPathProtectedAsync(path, cancellationToken);

    public async Task<bool> IsMarkedAsync(Guid fileItemId, CancellationToken cancellationToken = default)
    {
        var mark = await protectedDirectories.GetByFileItemIdAsync(fileItemId, cancellationToken);
        return mark is not null;
    }

    private static ProtectedDirectoryDto ToDto(ProtectedDirectory mark, string path, string name) => new(
        mark.FileItemId, path, name, mark.Note, mark.MarkedByUserId, mark.CreatedAt);
}
