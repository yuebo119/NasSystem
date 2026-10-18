// =============================================================================
// NasSystem.Infrastructure - Services/SnapshotService.cs
// 目录清单快照服务实现（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.Infrastructure.Services;

using NasSystem.Application.Snapshots;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>清单快照服务实现。采集基于库内目录树，不做物理扫描。</summary>
/// <remarks>
/// 采集与完成标记在同一事务：快照头（Running）+ 全部条目 + 完成态一次
/// SaveChanges。中途异常时事务整体回滚，不会留下"Running 且条目不全"的
/// 中间态——要么完整要么没有，对比时看到的每份快照都是自洽的。
/// 保留策略在创建成功后紧接着执行（同请求内）：超限的旧快照连条目删除。
/// IUnitOfWork 与仓储注入的是同一个作用域实例（DI 注册具体类型），
/// 仓储登记的写入意图经它统一提交。
/// </remarks>
public sealed class SnapshotService(
    ISnapshotRepository snapshots,
    IFileItemRepository fileItems,
    IUnitOfWork unitOfWork) : ISnapshotService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public async Task<Result<SnapshotDto>> CreateAsync(
        string rootPath,
        string? name,
        Guid operatorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return Result<SnapshotDto>.Failure(Error.Validation("快照根路径不能为空"));

        var normalizedRoot = NormalizeRoot(rootPath);

        var snapshot = DirectorySnapshot.Create(normalizedRoot, operatorUserId, name);
        await snapshots.AddAsync(snapshot, cancellationToken);

        // 采集库内子树（含删除态记录，回收站形态也是"那一刻的状态"）。
        // 根自身不入库：快照对象就是根，条目从根的子孙开始
        var subtree = await fileItems.GetSnapshotEntriesAsync(normalizedRoot, cancellationToken);
        var entries = new List<SnapshotEntry>(subtree.Count);
        var fileCount = 0;
        var folderCount = 0;
        var totalSize = 0L;
        foreach (var item in subtree)
        {
            if (string.Equals(item.Path, normalizedRoot, StringComparison.Ordinal))
                continue;
            var relative = item.Path.Length > normalizedRoot.Length + 1
                ? item.Path[(normalizedRoot.Length + 1)..]
                : item.Path;
            entries.Add(SnapshotEntry.Create(
                snapshot.Id, relative, item.ItemType == Domain.Enums.FileItemType.Folder,
                item.SizeBytes, item.Hash, item.UpdatedAt ?? item.CreatedAt));
            if (item.ItemType == Domain.Enums.FileItemType.Folder)
                folderCount++;
            else
            {
                fileCount++;
                totalSize += item.SizeBytes;
            }
        }

        await snapshots.AddEntriesAsync(entries, cancellationToken);
        snapshot.MarkCompleted(fileCount, folderCount, totalSize);
        snapshots.Update(snapshot);
        await SaveChangesAsync(cancellationToken);

        return Result<SnapshotDto>.Success(ToDto(snapshot));
    }

    public async Task<Result<PagedResult<SnapshotDto>>> GetListAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var (items, total) = await snapshots.GetPagedAsync(page, pageSize, cancellationToken);
        return Result<PagedResult<SnapshotDto>>.Success(
            PagedResult<SnapshotDto>.Create(items.Select(ToDto).ToList(), page, pageSize, total));
    }

    public async Task<Result<SnapshotDto>> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshots.GetByIdAsync(id, cancellationToken);
        return snapshot is null
            ? Result<SnapshotDto>.Failure(Error.NotFound(nameof(DirectorySnapshot), id))
            : Result<SnapshotDto>.Success(ToDto(snapshot));
    }

    public async Task<Result<PagedResult<SnapshotEntryDto>>> GetEntriesAsync(
        Guid id, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshots.GetByIdAsync(id, cancellationToken);
        if (snapshot is null)
            return Result<PagedResult<SnapshotEntryDto>>.Failure(Error.NotFound(nameof(DirectorySnapshot), id));

        (page, pageSize) = NormalizePaging(page, pageSize);
        var (items, total) = await snapshots.GetEntriesPagedAsync(id, page, pageSize, cancellationToken);
        var dtos = items.Select(e => new SnapshotEntryDto(
            e.RelativePath, e.IsFolder, e.SizeBytes, e.ContentHash, e.LastModified)).ToList();
        return Result<PagedResult<SnapshotEntryDto>>.Success(
            PagedResult<SnapshotEntryDto>.Create(dtos, page, pageSize, total));
    }

    public async Task<Result<SnapshotDiffDto>> DiffAsync(
        Guid baseSnapshotId, Guid targetSnapshotId,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var baseSnapshot = await snapshots.GetByIdAsync(baseSnapshotId, cancellationToken);
        if (baseSnapshot is null)
            return Result<SnapshotDiffDto>.Failure(Error.NotFound(nameof(DirectorySnapshot), baseSnapshotId));
        var targetSnapshot = await snapshots.GetByIdAsync(targetSnapshotId, cancellationToken);
        if (targetSnapshot is null)
            return Result<SnapshotDiffDto>.Failure(Error.NotFound(nameof(DirectorySnapshot), targetSnapshotId));
        if (baseSnapshotId == targetSnapshotId)
            return Result<SnapshotDiffDto>.Failure(Error.Validation("对比的两个快照不能相同"));

        (page, pageSize) = NormalizePaging(page, pageSize);
        var diff = await snapshots.DiffAsync(baseSnapshotId, targetSnapshotId, page, pageSize, cancellationToken);
        var items = diff.Items.Select(r => new SnapshotDiffItemDto(
            r.RelativePath, r.IsFolder, (SnapshotDiffKindDto)r.Kind,
            r.BaseSizeBytes, r.TargetSizeBytes, r.BaseHash, r.TargetHash)).ToList();
        return Result<SnapshotDiffDto>.Success(new SnapshotDiffDto(
            items, diff.TotalCount, diff.AddedCount, diff.RemovedCount,
            diff.ModifiedCount, diff.UnchangedCount));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var snapshot = await snapshots.GetByIdAsync(id, cancellationToken);
        if (snapshot is null)
            return Result.Failure(Error.NotFound(nameof(DirectorySnapshot), id));

        snapshots.Delete(snapshot);
        await SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<int> ApplyRetentionAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        if (keepCount < 0)
            keepCount = 0;
        var oldest = await snapshots.GetOldestAsync(keepCount, cancellationToken);
        if (oldest.Count == 0)
            return 0;

        foreach (var snapshot in oldest)
            snapshots.Delete(snapshot);
        await SaveChangesAsync(cancellationToken);
        return oldest.Count;
    }

    private Task SaveChangesAsync(CancellationToken cancellationToken) =>
        unitOfWork.SaveChangesAsync(cancellationToken);

    private static SnapshotDto ToDto(DirectorySnapshot s) => new(
        s.Id, s.Name, s.RootPath, s.Status, s.FileCount, s.FolderCount,
        s.TotalSizeBytes, s.CreatedByUserId, s.Error, s.CreatedAt);

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }

    /// <summary>根路径规范化：统一 / 开头、去尾斜杠（空根按 / 处理）</summary>
    private static string NormalizeRoot(string rootPath)
    {
        var trimmed = rootPath.Trim().Replace('\\', '/');
        if (!trimmed.StartsWith('/'))
            trimmed = "/" + trimmed;
        if (trimmed.Length > 1)
            trimmed = trimmed.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}
