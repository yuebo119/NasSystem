// =============================================================================
// NasSystem.Infrastructure - Data/Repositories/UserRepository.cs
// 用户仓储（PalORM 实现）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Repositories;

using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;

/// <summary>用户仓储实现。查询形态说明见 <see cref="FileItemRepository"/> 的remarks。</summary>
public sealed class UserRepository(
    PalormSessionHolder sessions,
    PalormUnitOfWork unitOfWork) : IUserRepository
{
    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.ToLowerInvariant();
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.From<UserRow>()
            .Where($"Username = {normalizedUsername}")
            .FirstOrDefaultAsync(cancellationToken);
        return row?.ToDomain();
    }

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.ToLowerInvariant();
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<UserRow>($"Username = {normalizedUsername}", cancellationToken);
        return count > 0;
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var count = await db.CountAsync<UserRow>($"Email = {email}", cancellationToken);
        return count > 0;
    }

    public async Task<(IReadOnlyList<User> Users, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var conditions = new List<string>();
        var args = new List<object>();

            // 搜索过滤：EF 的 string.Contains 翻译为 LIKE '%' || @p || '%'
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLowerInvariant();
                // 三处 LIKE 都要配转义子句。ESCAPE '#' 是字面量文本直接写进格式串：
                // 这里要交给 FormattableStringFactory 当格式串，插值会被算进参数位
            conditions.Add(
                "(lower(Username) LIKE {0} ESCAPE '#'" +
                " OR (DisplayName IS NOT NULL AND lower(DisplayName) LIKE {0} ESCAPE '#')" +
                " OR (Email IS NOT NULL AND lower(Email) LIKE {0} ESCAPE '#'))");
                args.Add("%" + EscapeLike(term) + "%");
            }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var totalCount = (int)await db.ScalarAsync<long>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT COUNT(*) FROM Users {where}", args.ToArray()),
            false,
            cancellationToken);

        var rows = await db.QueryAsync<UserRow>(
            System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT * FROM Users {where} ORDER BY Username LIMIT {pageSize} OFFSET {(page - 1) * pageSize}", args.ToArray()),
            false,
            cancellationToken);

        return (rows.Select(r => r.ToDomain()).ToList(), totalCount);
    }

    public async ValueTask<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var row = await db.GetAsync<UserRow>(id, cancellationToken);
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var db = await sessions.GetAsync(cancellationToken);
        var rows = await db.From<UserRow>().ToListAsync(cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueInsert(entity, async (db, ct) => await db.InsertAsync(row, ct));
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        var row = entity.ToRow();
        unitOfWork.EnqueueUpdate(entity, async (db, ct) => await db.UpdateAsync(row, ct));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(User entity, CancellationToken cancellationToken = default)
    {
        var id = entity.Id;
        unitOfWork.EnqueueDelete(entity, async (db, ct) => await db.DeleteAsync<UserRow>(id, ct));
        return Task.CompletedTask;
    }

    private static string EscapeLike(string value) =>
        value.Replace("#", "##").Replace("%", "#%").Replace("_", "#_");
}
