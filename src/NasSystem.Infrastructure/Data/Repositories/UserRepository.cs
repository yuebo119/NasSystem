// =============================================================================
// NasSystem.Infrastructure - UserRepository.cs
// 用户仓储实现
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;

/// <summary>
/// 用户仓储实现
/// </summary>
public sealed class UserRepository(NasDbContext context) : RepositoryBase<User>(context), IUserRepository
{
    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.ToLowerInvariant();
        return await QueryNoTracking()
            .FirstOrDefaultAsync(u => u.Username == normalizedUsername, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.ToLowerInvariant();
        return await QueryNoTracking()
            .AnyAsync(u => u.Username == normalizedUsername, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .AnyAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetEnabledUsersAsync(CancellationToken cancellationToken = default)
    {
        return await QueryNoTracking()
            .Where(u => u.IsEnabled)
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var query = QueryNoTracking();

        // 搜索过滤
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLowerInvariant();
            query = query.Where(u =>
                u.Username.Contains(term) ||
                (u.DisplayName != null && u.DisplayName.Contains(term)) ||
                (u.Email != null && u.Email.Contains(term)));
        }

        // 获取总数
        var totalCount = await query.CountAsync(cancellationToken);

        // 分页
        var users = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (users, totalCount);
    }
}
