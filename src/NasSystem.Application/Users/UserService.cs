// =============================================================================
// NasSystem.Application - UserService.cs
// 用户服务实现
// =============================================================================

namespace NasSystem.Application.Users;

using Microsoft.Extensions.Options;
using NasSystem.Application.Common;
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 用户服务实现
/// </summary>
public sealed class UserService(
    IUserRepository userRepository,
    IFileItemRepository fileItemRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtService jwtService,
    IOptions<JwtSettings> jwtSettings) : IUserService
{
    public async Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure(UserErrors.NotFound(id));

        // 已用空间取实时聚合：User.UsedBytes 字段没有生产维护者，直接映射永远是 0
        var usedBytes = await fileItemRepository.CalculateUserUsedSpaceAsync(id, cancellationToken);
        return Result<UserDto>.Success(MapToDto(user, usedBytes));
    }

    public async Task<Result<PagedResult<UserDto>>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        // 分页夹取（与 FileService.Browse 同范式）：SQLite 的 LIMIT 负值
        // 等于无上限、OFFSET 负值静默归零，pageSize=-1 会把整表 Users
        // 一次返回。同仓 FileItemRepository.SearchAsync 也夹，
        // 唯独用户列表这条从端点到 SQL 直通（2026-10-21 审计 P2）
        var safePage = page > 0 ? page : 1;
        var safePageSize = pageSize > 0 ? pageSize : 20;
        if (safePageSize > 200) safePageSize = 200;

        var (users, totalCount) = await userRepository.GetPagedAsync(safePage, safePageSize, searchTerm, cancellationToken);

        // 已用空间批量实时聚合（一条 GROUP BY）：User.UsedBytes 字段没有生产维护者，
        // 直接映射永远是 0，用户列表的"已使用空间/总使用空间/配额条"全显示 0
        var usedSpaceByUser = users.Count > 0
            ? await fileItemRepository.CalculateUsersUsedSpaceAsync(
                users.Select(u => u.Id).ToList(), cancellationToken)
            : new Dictionary<Guid, long>();

        var dtos = users
            .Select(u => MapToDto(u, usedSpaceByUser.TryGetValue(u.Id, out var used) ? used : 0))
            .ToList();
        // 报出去的 Page/PageSize 用夹取后的值：与真实取回数一致，
        // 否则调用方按回显值翻页会错位（FileService.Browse 同款取舍）
        var result = PagedResult<UserDto>.Create(dtos, safePage, safePageSize, totalCount);

        return Result<PagedResult<UserDto>>.Success(result);
    }

    public async Task<Result<UserStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        // 全站口径两条聚合：用户表计数 + 文件表总字节。
        // 前端不再对当前页求和（2026-10-21 审计 P1：翻页数字就变）
        var stats = await userRepository.GetStatsAsync(cancellationToken);
        var totalUsedBytes = await fileItemRepository.CalculateAllUsersUsedSpaceAsync(cancellationToken);

        return Result<UserStatsDto>.Success(new UserStatsDto(
            stats.Total,
            stats.Admins,
            stats.Enabled,
            totalUsedBytes));
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default)
    {
        // 检查用户名是否已存在
        if (await userRepository.UsernameExistsAsync(dto.Username, cancellationToken))
            return Result<UserDto>.Failure(UserErrors.UsernameExists(dto.Username));

        // 检查邮箱是否已存在
        if (!string.IsNullOrEmpty(dto.Email) && await userRepository.EmailExistsAsync(dto.Email, cancellationToken))
            return Result<UserDto>.Failure(UserErrors.EmailExists(dto.Email));

        // 哈希密码
        var (hash, salt) = passwordHasher.HashPassword(dto.Password);

        // 解析角色
        var role = Enum.TryParse<UserRole>(dto.Role, out var parsedRole) ? parsedRole : UserRole.User;

        // 创建用户
        var user = User.Create(
            username: dto.Username,
            passwordHash: hash,
            passwordSalt: salt,
            role: role,
            displayName: dto.DisplayName,
            email: dto.Email);

        // 设置配额
        if (dto.QuotaBytes.HasValue)
            user.SetQuota(dto.QuotaBytes.Value);

        await userRepository.AddAsync(user, cancellationToken);
        // ITM-N7：并发创建同名用户由 Users.Username 唯一索引兜底
        var saved = await SaveChangesWithUniqueGuardAsync(UserErrors.UsernameExists(dto.Username), cancellationToken);
        if (saved.IsFailure)
            return Result<UserDto>.Failure(saved.Error);

        return Result<UserDto>.Success(MapToDto(user));
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure(UserErrors.NotFound(id));

        // 更新显示名称和邮箱
        if (dto.DisplayName != null || dto.Email != null)
        {
            // 检查邮箱唯一性
            if (dto.Email != null && dto.Email != user.Email)
            {
                if (await userRepository.EmailExistsAsync(dto.Email, cancellationToken))
                    return Result<UserDto>.Failure(UserErrors.EmailExists(dto.Email));
            }
            user.UpdateProfile(
                dto.DisplayName ?? user.DisplayName,
                dto.Email ?? user.Email);
        }

        // 更新角色
        if (!string.IsNullOrEmpty(dto.Role) && Enum.TryParse<UserRole>(dto.Role, out var role))
            user.UpdateRole(role);

        // 更新启用状态
        if (dto.IsEnabled.HasValue)
            user.SetEnabled(dto.IsEnabled.Value);

        // 更新配额
        if (dto.QuotaBytes.HasValue)
            user.SetQuota(dto.QuotaBytes.Value);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<UserDto>.Success(MapToDto(user));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.Failure(UserErrors.NotFound(id));

        // 不能删除超级管理员
        if (user.Role == UserRole.SuperAdmin)
            return Result.Failure(new Error("User.CannotDeleteSuperAdmin", "不能删除超级管理员账户"));

        await userRepository.DeleteAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<LoginResultDto>> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByUsernameAsync(dto.Username, cancellationToken);
        if (user is null)
            return Result<LoginResultDto>.Failure(UserErrors.InvalidCredentials);

        // 检查用户是否启用
        if (!user.IsEnabled)
            return Result<LoginResultDto>.Failure(UserErrors.AccountDisabled);

        // 验证密码
        if (!passwordHasher.VerifyPassword(dto.Password, user.PasswordHash, user.PasswordSalt))
            return Result<LoginResultDto>.Failure(UserErrors.InvalidCredentials);

        // 更新最后登录时间
        user.RecordLogin();
        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 生成令牌。"记住我"拉长有效期：勾选时按 RememberMeExpirationDays
        // （默认 7 天）签发，否则回落到 AccessTokenExpirationMinutes。
        // 2026-10-21 审计前 LoginDto.RememberMe 全链路无人读取，前端勾选框
        // 是死控件，勾与不勾行为零差异
        var lifetime = dto.RememberMe
            ? TimeSpan.FromDays(jwtSettings.Value.RememberMeExpirationDays)
            : (TimeSpan?)null;
        var accessToken = jwtService.GenerateAccessToken(user, lifetime);
        var refreshToken = jwtService.GenerateRefreshToken();
        var expiresAt = jwtService.GetTokenExpiration(accessToken) ?? DateTime.UtcNow.AddHours(1);

        var result = new LoginResultDto(
            Token: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: expiresAt,
            User: MapToDto(user));

        return Result<LoginResultDto>.Success(result);
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(UserErrors.NotFound(userId));

        // 验证当前密码
        if (!passwordHasher.VerifyPassword(currentPassword, user.PasswordHash, user.PasswordSalt))
            return Result.Failure(UserErrors.PasswordMismatch);

        // 设置新密码
        var (hash, salt) = passwordHasher.HashPassword(newPassword);
        user.UpdatePassword(hash, salt);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(UserErrors.NotFound(userId));

        // 设置新密码
        var (hash, salt) = passwordHasher.HashPassword(newPassword);
        user.UpdatePassword(hash, salt);

        await userRepository.UpdateAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// ITM-N7：保存更改的唯一约束兜底。并发写入撞唯一索引时返回调用方提供的冲突错误
    /// </summary>
    private async Task<Result> SaveChangesWithUniqueGuardAsync(
        Error conflictError,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure(conflictError);
        }
    }

    /// <summary>
    /// 映射实体到 DTO。<paramref name="usedBytesOverride"/> 非空时用实时聚合值
    /// 覆盖实体字段（读路径）；写路径（创建/更新/登录）不传，用实体值
    /// </summary>
    private static UserDto MapToDto(User user, long? usedBytesOverride = null) => new(
        Id: user.Id,
        Username: user.Username,
        DisplayName: user.DisplayName,
        Email: user.Email,
        Role: user.Role.ToString(),
        IsEnabled: user.IsEnabled,
        LastLoginAt: user.LastLoginAt,
        QuotaBytes: user.QuotaBytes,
        UsedBytes: usedBytesOverride ?? user.UsedBytes,
        QuotaDisplay: FormatSize(user.QuotaBytes),
        UsedDisplay: FormatSize(usedBytesOverride ?? user.UsedBytes),
        UsagePercentage: user.QuotaBytes > 0
            ? Math.Round((double)(usedBytesOverride ?? user.UsedBytes) / user.QuotaBytes.Value * 100, 2)
            : null,
        CreatedAt: user.CreatedAt);

    // 【单一真源】尺标逻辑委托 ByteSizeFormatter；保留 0/无配额返回 null 的既有显示语义
    private static string? FormatSize(long? bytes) =>
        bytes is > 0 ? ByteSizeFormatter.FormatSize(bytes.Value) : null;

}
