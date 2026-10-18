// =============================================================================
// NasSystem.Domain - IUserRepository.cs
// 用户仓储接口
// =============================================================================

namespace NasSystem.Domain.Repositories;

using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;

/// <summary>
/// 用户仓储接口
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// 根据用户名获取用户
    /// </summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取首个超级管理员的 Id。供系统级后台动作（定时快照）归属操作人：
    /// 快照不属于任何请求用户，审计上记系统管理员。无超管返回 null
    /// （调用方按"跳过并记日志"处理，不抛异常）
    /// </summary>
    Task<Guid?> GetFirstSuperAdminIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户名是否已存在
    /// </summary>
    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查邮箱是否已存在
    /// </summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页获取用户
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default);
}
