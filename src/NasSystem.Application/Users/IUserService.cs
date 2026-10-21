// =============================================================================
// NasSystem.Application - IUserService.cs
// 用户服务接口
// =============================================================================

namespace NasSystem.Application.Users;

using NasSystem.Application.Users.Dtos;
using NasSystem.Shared;

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 获取用户列表 (分页)
    /// </summary>
    Task<Result<PagedResult<UserDto>>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户全站统计（管理页统计卡）。与分页解耦，永远全站口径
    /// </summary>
    Task<Result<UserStatsDto>> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取用户
    /// </summary>
    Task<Result<UserDto>> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建用户
    /// </summary>
    Task<Result<UserDto>> CreateAsync(
        CreateUserDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新用户
    /// </summary>
    Task<Result<UserDto>> UpdateAsync(
        Guid userId,
        UpdateUserDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除用户
    /// </summary>
    Task<Result> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 修改密码
    /// </summary>
    Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置密码 (管理员)
    /// </summary>
    Task<Result> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户登录
    /// </summary>
    Task<Result<LoginResultDto>> LoginAsync(
        LoginDto request,
        CancellationToken cancellationToken = default);
}
