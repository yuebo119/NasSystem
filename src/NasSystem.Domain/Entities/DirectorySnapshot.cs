// =============================================================================
// NasSystem.Domain - DirectorySnapshot.cs
// 目录清单快照实体（ROADMAP Phase 2 清单快照）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;
using NasSystem.Domain.Enums;

/// <summary>
/// 目录清单快照：把某个目录子树在某一时刻的形态（路径/大小/hash/修改时间）
/// 物化到 <see cref="SnapshotEntry"/>，用于按时间点对比与恢复。
/// </summary>
/// <remarks>
/// 设计动因（对应 ROADMAP Phase 2「目录清单快照」验收项）：
/// 一、快照记的是库内目录树（FileItems），不是物理扫描。物理世界由
/// ScanAndSync 对齐，清单层再扫一遍物理目录等于把同一件事做两遍且两遍
/// 结论可能不一致；基于库内状态的快照是确定性的，可单测可复现。
/// 二、快照不含内容副本。内容级备份是 3-2-1 导出的职责（Phase 2-c），
/// 清单快照回答"目录变成什么样了、丢了什么、被改了什么"，恢复动作
/// 对版本库/回收站里有副本的条目生效——两者分工避免快照表无限膨胀。
/// 三、Status 见 <see cref="SnapshotStatus"/>：失败快照的条目保留，
/// 对比时能看出失败前记录到哪。
/// </remarks>
public sealed class DirectorySnapshot : Entity
{
    /// <summary>快照名（展示用，可空时由服务层按时间生成）</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>快照根目录的虚拟路径（如 /Docs）</summary>
    public string RootPath { get; private set; } = string.Empty;

    /// <summary>快照状态</summary>
    public SnapshotStatus Status { get; private set; }

    /// <summary>条目中的文件数（不含文件夹）</summary>
    public int FileCount { get; private set; }

    /// <summary>条目中的文件夹数</summary>
    public int FolderCount { get; private set; }

    /// <summary>条目中文件的总字节数（文件夹不计）</summary>
    public long TotalSizeBytes { get; private set; }

    /// <summary>触发快照的用户（定时任务触发时记系统操作者）</summary>
    public Guid CreatedByUserId { get; private set; }

    /// <summary>失败原因（Status=Failed 时非空，展示与排障用）</summary>
    public string? Error { get; private set; }

    public static DirectorySnapshot Create(
        string rootPath,
        Guid createdByUserId,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("用户 ID 不能为空", nameof(createdByUserId));

        return new DirectorySnapshot
        {
            RootPath = rootPath,
            Name = string.IsNullOrWhiteSpace(name) ? string.Empty : name,
            Status = SnapshotStatus.Running,
            CreatedByUserId = createdByUserId,
        };
    }

    /// <summary>完成快照并落账统计</summary>
    public void MarkCompleted(int fileCount, int folderCount, long totalSizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(folderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(totalSizeBytes);
        Status = SnapshotStatus.Completed;
        FileCount = fileCount;
        FolderCount = folderCount;
        TotalSizeBytes = totalSizeBytes;
        Error = null;
        MarkAsModified();
    }

    /// <summary>标记失败并保留原因</summary>
    public void MarkFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        Status = SnapshotStatus.Failed;
        Error = error;
        MarkAsModified();
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static DirectorySnapshot Restore(
        Guid id,
        string name,
        string rootPath,
        SnapshotStatus status,
        int fileCount,
        int folderCount,
        long totalSizeBytes,
        Guid createdByUserId,
        string? error,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        return new DirectorySnapshot
        {
            Id = id,
            Name = name,
            RootPath = rootPath,
            Status = status,
            FileCount = fileCount,
            FolderCount = folderCount,
            TotalSizeBytes = totalSizeBytes,
            CreatedByUserId = createdByUserId,
            Error = error,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }
}
