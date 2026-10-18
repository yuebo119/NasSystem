// =============================================================================
// NasSystem.Domain - ProtectedDirectory.cs
// 受保护目录实体（ROADMAP Phase 2 关键目录只读保护）
// =============================================================================

namespace NasSystem.Domain.Entities;

using NasSystem.Domain.Common;

/// <summary>
/// 受保护目录：管理员标记的目录在应用层禁止覆盖/删除，防勒索软件经
/// WebDAV/网页端篡改（ROADMAP Phase 2「关键目录只读保护」）。
/// </summary>
/// <remarks>
/// 独立成表而不是 FileItem 加列：FileItems 是 hottest 表，加列要走
/// 表重建迁移（SQLite 不支持 DROP/ALTER 列语义有限），且保护标记是
/// 低频管理操作，单独一张个位数行的表用左连接判定，代价可忽略。
/// 判定语义是"目录自身或其任一祖先被标记"——服务层按路径前缀查询，
/// 不依赖父子物化路径之外的树遍历。
/// </remarks>
public sealed class ProtectedDirectory : Entity
{
    /// <summary>受保护的目录项</summary>
    public Guid FileItemId { get; private set; }

    /// <summary>标记的管理员</summary>
    public Guid MarkedByUserId { get; private set; }

    /// <summary>标记原因/备注（展示用，可空）</summary>
    public string? Note { get; private set; }

    public static ProtectedDirectory Create(
        Guid fileItemId,
        Guid markedByUserId,
        string? note = null)
    {
        if (fileItemId == Guid.Empty)
            throw new ArgumentException("目录 ID 不能为空", nameof(fileItemId));
        if (markedByUserId == Guid.Empty)
            throw new ArgumentException("用户 ID 不能为空", nameof(markedByUserId));

        return new ProtectedDirectory
        {
            FileItemId = fileItemId,
            MarkedByUserId = markedByUserId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
        };
    }

    /// <summary>从持久化状态恢复实体（供基础设施层影子模型映射使用）</summary>
    internal static ProtectedDirectory Restore(
        Guid id,
        Guid fileItemId,
        Guid markedByUserId,
        string? note,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        return new ProtectedDirectory
        {
            Id = id,
            FileItemId = fileItemId,
            MarkedByUserId = markedByUserId,
            Note = note,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }
}
