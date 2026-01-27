// =============================================================================
// NasSystem.Application - ShareService.cs
// 共享服务实现
// =============================================================================

namespace NasSystem.Application.Shares;

using NasSystem.Application.Common;
using NasSystem.Application.Shares.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Shared;

/// <summary>
/// 共享服务实现
/// </summary>
public sealed class ShareService(
    IShareRepository shareRepository,
    ISharePermissionRepository permissionRepository,
    IStoragePoolRepository storagePoolRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork) : IShareService
{
    public async Task<Result<IReadOnlyList<ShareDto>>> GetSharesAsync(
        CancellationToken cancellationToken = default)
    {
        var shares = await shareRepository.GetAllAsync(cancellationToken);
        var dtos = await MapToDtosAsync(shares, cancellationToken);
        return Result<IReadOnlyList<ShareDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<ShareDto>>> GetAccessibleSharesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var shares = await shareRepository.GetAccessibleByUserAsync(userId, cancellationToken);
        var dtos = await MapToDtosAsync(shares, cancellationToken);
        return Result<IReadOnlyList<ShareDto>>.Success(dtos);
    }

    public async Task<Result<ShareDto>> GetByIdAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<ShareDto>.Failure(ShareErrors.NotFound(shareId));

        var dto = await MapToDtoAsync(share, cancellationToken);
        return Result<ShareDto>.Success(dto);
    }

    public async Task<Result<ShareDto>> CreateAsync(
        CreateShareRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // 检查名称是否已存在
        if (await shareRepository.NameExistsAsync(request.Name, cancellationToken))
            return Result<ShareDto>.Failure(ShareErrors.NameExists(request.Name));

        // 检查存储池
        var storagePool = await storagePoolRepository.GetByIdAsync(request.StoragePoolId, cancellationToken);
        if (storagePool is null)
            return Result<ShareDto>.Failure(StorageErrors.PoolNotFound(request.StoragePoolId));

        // 构建物理路径
        var physicalPath = Path.Combine(storagePool.MountPath, request.Path.TrimStart('/'));

        // 创建共享
        var share = Share.Create(
            name: request.Name,
            path: request.Path,
            physicalPath: physicalPath,
            shareType: request.ShareType,
            createdByUserId: userId,
            storagePoolId: request.StoragePoolId,
            description: request.Description,
            isPublic: request.IsPublic,
            isReadOnly: request.IsReadOnly);

        if (request.QuotaBytes.HasValue)
            share.SetQuota(request.QuotaBytes.Value);

        share.SetRecycleBin(request.EnableRecycleBin);

        await shareRepository.AddAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = await MapToDtoAsync(share, cancellationToken);
        return Result<ShareDto>.Success(dto);
    }

    public async Task<Result<ShareDto>> UpdateAsync(
        Guid shareId,
        UpdateShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<ShareDto>.Failure(ShareErrors.NotFound(shareId));

        // 更新名称和描述
        if (!string.IsNullOrEmpty(request.Name) || request.Description != null)
        {
            if (!string.IsNullOrEmpty(request.Name) && request.Name != share.Name)
            {
                if (await shareRepository.NameExistsAsync(request.Name, cancellationToken))
                    return Result<ShareDto>.Failure(ShareErrors.NameExists(request.Name));
            }
            share.Update(request.Name, request.Description);
        }

        // 更新状态
        if (request.IsEnabled.HasValue)
            share.SetEnabled(request.IsEnabled.Value);

        if (request.IsPublic.HasValue || request.IsReadOnly.HasValue)
            share.SetAccessMode(
                request.IsPublic ?? share.IsPublic,
                request.IsReadOnly ?? share.IsReadOnly);

        if (request.QuotaBytes.HasValue)
            share.SetQuota(request.QuotaBytes.Value);

        if (request.EnableRecycleBin.HasValue)
            share.SetRecycleBin(request.EnableRecycleBin.Value);

        await shareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = await MapToDtoAsync(share, cancellationToken);
        return Result<ShareDto>.Success(dto);
    }

    public async Task<Result> DeleteAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result.Failure(ShareErrors.NotFound(shareId));

        // 删除关联的权限
        await permissionRepository.DeleteByShareAsync(shareId, cancellationToken);

        await shareRepository.DeleteAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SharePermissionDto>>> GetPermissionsAsync(
        Guid shareId,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<IReadOnlyList<SharePermissionDto>>.Failure(ShareErrors.NotFound(shareId));

        var permissions = await permissionRepository.GetByShareAsync(shareId, cancellationToken);
        var dtos = await MapPermissionsToDtosAsync(permissions, cancellationToken);

        return Result<IReadOnlyList<SharePermissionDto>>.Success(dtos);
    }

    public async Task<Result> SetPermissionsAsync(
        Guid shareId,
        SetSharePermissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result.Failure(ShareErrors.NotFound(shareId));

        // 删除现有权限
        await permissionRepository.DeleteByShareAsync(shareId, cancellationToken);

        // 添加新权限
        foreach (var entry in request.Permissions)
        {
            SharePermission permission;
            if (entry.UserId.HasValue)
            {
                permission = SharePermission.CreateForUser(
                    shareId: shareId,
                    userId: entry.UserId.Value,
                    permission: entry.Permission,
                    isDeny: entry.IsDeny);
            }
            else if (!string.IsNullOrEmpty(entry.GroupName))
            {
                permission = SharePermission.CreateForGroup(
                    shareId: shareId,
                    groupName: entry.GroupName,
                    permission: entry.Permission,
                    isDeny: entry.IsDeny);
            }
            else
            {
                continue; // 跳过无效条目
            }

            await permissionRepository.AddAsync(permission, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<PermissionLevel>> CheckPermissionAsync(
        Guid shareId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result<PermissionLevel>.Failure(ShareErrors.NotFound(shareId));

        // 公开共享
        if (share.IsPublic)
        {
            return Result<PermissionLevel>.Success(
                share.IsReadOnly ? PermissionLevel.Read : PermissionLevel.ReadWrite);
        }

        // 获取有效权限
        var permissionLevel = await permissionRepository.GetEffectivePermissionAsync(
            shareId, userId, userGroups: null, cancellationToken);

        return Result<PermissionLevel>.Success(permissionLevel);
    }

    public async Task<Result> SetEnabledAsync(
        Guid shareId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var share = await shareRepository.GetByIdAsync(shareId, cancellationToken);
        if (share is null)
            return Result.Failure(ShareErrors.NotFound(shareId));

        share.SetEnabled(enabled);
        await shareRepository.UpdateAsync(share, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    #region 辅助方法

    private async Task<IReadOnlyList<ShareDto>> MapToDtosAsync(
        IEnumerable<Share> shares,
        CancellationToken cancellationToken)
    {
        var result = new List<ShareDto>();
        foreach (var share in shares)
        {
            result.Add(await MapToDtoAsync(share, cancellationToken));
        }
        return result;
    }

    private async Task<ShareDto> MapToDtoAsync(Share share, CancellationToken cancellationToken)
    {
        // 获取创建者信息
        string? createdByUserName = null;
        var creator = await userRepository.GetByIdAsync(share.CreatedByUserId, cancellationToken);
        if (creator != null)
            createdByUserName = creator.DisplayName ?? creator.Username;

        // 获取存储池信息
        string? storagePoolName = null;
        var storagePool = await storagePoolRepository.GetByIdAsync(share.StoragePoolId, cancellationToken);
        if (storagePool != null)
            storagePoolName = storagePool.Name;

        return new ShareDto
        {
            Id = share.Id,
            Name = share.Name,
            Description = share.Description,
            Path = share.Path,
            ShareType = share.ShareType,
            IsEnabled = share.IsEnabled,
            IsPublic = share.IsPublic,
            IsReadOnly = share.IsReadOnly,
            CreatedByUserId = share.CreatedByUserId,
            CreatedByUserName = createdByUserName,
            StoragePoolId = share.StoragePoolId,
            StoragePoolName = storagePoolName,
            QuotaBytes = share.QuotaBytes,
            QuotaDisplay = FormatSize(share.QuotaBytes),
            SmbShareName = share.SmbShareName,
            EnableRecycleBin = share.EnableRecycleBin,
            CreatedAt = share.CreatedAt,
            UpdatedAt = share.UpdatedAt
        };
    }

    private async Task<IReadOnlyList<SharePermissionDto>> MapPermissionsToDtosAsync(
        IEnumerable<SharePermission> permissions,
        CancellationToken cancellationToken)
    {
        var result = new List<SharePermissionDto>();
        foreach (var permission in permissions)
        {
            string? userName = null;
            if (permission.UserId.HasValue)
            {
                var user = await userRepository.GetByIdAsync(permission.UserId.Value, cancellationToken);
                userName = user?.DisplayName ?? user?.Username;
            }

            result.Add(new SharePermissionDto
            {
                Id = permission.Id,
                ShareId = permission.ShareId,
                UserId = permission.UserId,
                UserName = userName,
                GroupName = permission.GroupName,
                Permission = permission.Permission,
                IsDeny = permission.IsDeny
            });
        }
        return result;
    }

    private static string? FormatSize(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value == 0) return null;
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes.Value;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion
}
