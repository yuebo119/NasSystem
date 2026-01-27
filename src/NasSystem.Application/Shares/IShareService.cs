// =============================================================================
// NasSystem.Application - IShareService.cs
// 共享服务接口
// =============================================================================

namespace NasSystem.Application.Shares;

using NasSystem.Application.Shares.Dtos;
using NasSystem.Domain.Enums;
using NasSystem.Shared;

/// <summary>
/// 共享服务接口
/// </summary>
public interface IShareService
{
    /// <summary>
    /// 获取共享列表
    /// </summary>
    Task<Result<IReadOnlyList<ShareDto>>> GetSharesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户可访问的共享
    /// </summary>
    Task<Result<IReadOnlyList<ShareDto>>> GetAccessibleSharesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取共享
    /// </summary>
    Task<Result<ShareDto>> GetByIdAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建共享
    /// </summary>
    Task<Result<ShareDto>> CreateAsync(
        CreateShareRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新共享
    /// </summary>
    Task<Result<ShareDto>> UpdateAsync(
        Guid shareId,
        UpdateShareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除共享
    /// </summary>
    Task<Result> DeleteAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取共享权限
    /// </summary>
    Task<Result<IReadOnlyList<SharePermissionDto>>> GetPermissionsAsync(
        Guid shareId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置共享权限
    /// </summary>
    Task<Result> SetPermissionsAsync(
        Guid shareId,
        SetSharePermissionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户对共享的权限
    /// </summary>
    Task<Result<PermissionLevel>> CheckPermissionAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 启用/禁用共享
    /// </summary>
    Task<Result> SetEnabledAsync(
        Guid shareId,
        bool enabled,
        CancellationToken cancellationToken = default);
}
