// =============================================================================
// NasSystem.Domain - SnapshotEntry.cs
// 快照条目实体（ROADMAP Phase 2 清单快照）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;

/// <summary>
/// 快照条目：快照根目录子树中的一个节点（文件或文件夹）在快照时刻的形态。
/// </summary>
/// <remarks>
/// RelativePath 相对快照根（根自身不入库）：diff 两个快照时按相对路径
/// 对齐，换根目录重拍不影响对比语义。ContentHash 复用 FileItem.Hash
/// （上传时已计算），快照本身不做 IO——hash 为空的条目视为"无内容指纹"
/// （文件夹或历史遗留无 hash 记录），diff 时按路径与大小判定。
/// </remarks>
public sealed class SnapshotEntry : Entity
{
    /// <summary>所属快照</summary>
    public Guid SnapshotId { get; private set; }

    /// <summary>相对快照根的路径（不以 / 开头，根自身不入库）</summary>
    public string RelativePath { get; private set; } = string.Empty;

    /// <summary>是否文件夹</summary>
    public bool IsFolder { get; private set; }

    /// <summary>文件字节数（文件夹为 0）</summary>
    public long SizeBytes { get; private set; }

    /// <summary>文件内容 hash（文件夹或未知为空）</summary>
    public string? ContentHash { get; private set; }

    /// <summary>库中记录的修改时间</summary>
    public DateTime LastModified { get; private set; }

    public static SnapshotEntry Create(
        Guid snapshotId,
        string relativePath,
        bool isFolder,
        long sizeBytes,
        string? contentHash,
        DateTime lastModified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("快照 ID 不能为空", nameof(snapshotId));
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        return new SnapshotEntry
        {
            SnapshotId = snapshotId,
            RelativePath = relativePath,
            IsFolder = isFolder,
            SizeBytes = sizeBytes,
            ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash,
            LastModified = lastModified,
        };
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static SnapshotEntry Restore(
        Guid id,
        Guid snapshotId,
        string relativePath,
        bool isFolder,
        long sizeBytes,
        string? contentHash,
        DateTime lastModified)
    {
        return new SnapshotEntry
        {
            Id = id,
            SnapshotId = snapshotId,
            RelativePath = relativePath,
            IsFolder = isFolder,
            SizeBytes = sizeBytes,
            ContentHash = contentHash,
            LastModified = lastModified,
        };
    }
}
