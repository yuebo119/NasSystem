// =============================================================================
// NasSystem.Domain - Repositories/IProtectedDirectoryRepository.cs
// 受保护目录仓储接口（ROADMAP Phase 2 关键目录只读保护）
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Entities;

/// <summary>
/// 受保护目录仓储。表是个位数行：标记少、变更多为管理动作，
/// 查询全部走全表或按路径前缀，不需要额外索引形态。
/// </summary>
public interface IProtectedDirectoryRepository
{
    Task AddAsync(ProtectedDirectory protectedDirectory, CancellationToken cancellationToken = default);

    void Delete(ProtectedDirectory protectedDirectory);

    Task<ProtectedDirectory?> GetByFileItemIdAsync(
        Guid fileItemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProtectedDirectory>> GetAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 路径是否落在任一受保护目录之内（含自身）。保护判定是前缀包含：
    /// 传 /Docs/a.txt 或 /Docs 本身，只要 /Docs 被标记都算受保护。
    /// 走全表 + 前缀比较而不物化祖先链：受保护目录个位数，
    /// 每次写操作多一次微秒级查询，换掉树遍历的复杂度
    /// </summary>
    Task<bool> IsPathProtectedAsync(
        string path, CancellationToken cancellationToken = default);
}
