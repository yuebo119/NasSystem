// =============================================================================
// NasSystem.Application - UserService.cs
// 用户服务实现
// =============================================================================

namespace NasSystem.Application.Users;

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
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtService jwtService) : IUserService
{
    public async Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure(UserErrors.NotFound(id));

        return Result<UserDto>.Success(MapToDto(user));
    }

    public async Task<Result<UserDto>> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByUsernameAsync(username, cancellationToken);
        if (user is null)
            return Result<UserDto>.Failure(new Error("User.NotFound", $"用户 '{username}' 不存在"));

        return Result<UserDto>.Success(MapToDto(user));
    }

    public async Task<Result<PagedResult<UserDto>>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var (users, totalCount) = await userRepository.GetPagedAsync(page, pageSize, searchTerm, cancellationToken);

        var dtos = users.Select(MapToDto).ToList();
        var result = PagedResult<UserDto>.Create(dtos, page, pageSize, totalCount);

        return Result<PagedResult<UserDto>>.Success(result);
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
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // ITM-N7：并发创建同名用户由 Users.Username 唯一索引兜底
            return Result<UserDto>.Failure(UserErrors.UsernameExists(dto.Username));
        }

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

        // 生成令牌
        var accessToken = jwtService.GenerateAccessToken(user);
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

    public async Task<Result<bool>> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByUsernameAsync(username, cancellationToken);
        if (user is null || !user.IsEnabled)
            return Result<bool>.Success(false);

        var isValid = passwordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt);
        return Result<bool>.Success(isValid);
    }

    /// <summary>
    /// 映射实体到 DTO
    /// </summary>
    private static UserDto MapToDto(User user) => new(
        Id: user.Id,
        Username: user.Username,
        DisplayName: user.DisplayName,
        Email: user.Email,
        Role: user.Role.ToString(),
        IsEnabled: user.IsEnabled,
        LastLoginAt: user.LastLoginAt,
        QuotaBytes: user.QuotaBytes,
        UsedBytes: user.UsedBytes,
        QuotaDisplay: FormatSize(user.QuotaBytes),
        UsedDisplay: FormatSize(user.UsedBytes),
        UsagePercentage: user.QuotaBytes > 0 ? Math.Round((double)user.UsedBytes / user.QuotaBytes.Value * 100, 2) : null,
        CreatedAt: user.CreatedAt);

    private static string? FormatSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value == 0) return null;
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes.Value;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

}
