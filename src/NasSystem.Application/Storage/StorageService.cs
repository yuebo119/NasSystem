// =============================================================================
// NasSystem.Application - StorageService.cs
// 存储服务实现（简化版）
// =============================================================================

namespace NasSystem.Application.Storage;

using NasSystem.Application.Common;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 存储服务实现（简化版）
/// </summary>
public sealed class StorageService(
    IStoragePoolRepository poolRepository,
    IFileItemRepository fileItemRepository,
    IUnitOfWork unitOfWork,
    IFileSystemService fileSystemService) : IStorageService
{
    public async Task<Result<StorageOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);
        
        // 刷新所有存储池的容量信息
        foreach (var pool in pools)
        {
            if (await RefreshPoolCapacityAsync(pool, cancellationToken))
                await poolRepository.UpdateAsync(pool, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 使用有效总容量（考虑 maxCapacityBytes）
        long totalBytes = pools.Sum(p => p.EffectiveTotalBytes);
        long usedBytes = pools.Sum(p => p.UsedBytes);
        long availableBytes = totalBytes - usedBytes;
        double usagePercentage = totalBytes > 0 ? (double)usedBytes / totalBytes * 100 : 0;

        return Result<StorageOverviewDto>.Success(new StorageOverviewDto
        {
            TotalBytes = totalBytes,
            UsedBytes = usedBytes,
            AvailableBytes = availableBytes,
            UsagePercentage = Math.Round(usagePercentage, 2),
            TotalDisplay = FormatSize(totalBytes),
            UsedDisplay = FormatSize(usedBytes),
            AvailableDisplay = FormatSize(availableBytes),
            PoolCount = pools.Count
        });
    }

    public async Task<Result<IReadOnlyList<StoragePoolDto>>> GetPoolsAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);
        
        // 刷新容量信息
        foreach (var pool in pools)
        {
            if (await RefreshPoolCapacityAsync(pool, cancellationToken))
                await poolRepository.UpdateAsync(pool, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var dtos = pools.Select(MapToDto).ToList();
        return Result<IReadOnlyList<StoragePoolDto>>.Success(dtos);
    }

    public async Task<Result<StoragePoolDto>> GetPoolByIdAsync(
        Guid poolId,
        CancellationToken cancellationToken = default)
    {
        var pool = await poolRepository.GetByIdAsync(poolId, cancellationToken);
        if (pool is null)
            return Result<StoragePoolDto>.Failure(StorageErrors.PoolNotFound(poolId));

        if (await RefreshPoolCapacityAsync(pool, cancellationToken))
            await poolRepository.UpdateAsync(pool, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StoragePoolDto>.Success(MapToDto(pool));
    }

    public async Task<Result<StoragePoolDto>> CreatePoolAsync(
        CreateStoragePoolRequest request,
        CancellationToken cancellationToken = default)
    {
        // 检查名称是否已存在
        if (await poolRepository.NameExistsAsync(request.Name, cancellationToken))
            return Result<StoragePoolDto>.Failure(StorageErrors.PoolNameExists(request.Name));

        // 检查路径是否已存在
        if (await poolRepository.MountPathExistsAsync(request.MountPath, cancellationToken))
            return Result<StoragePoolDto>.Failure(StorageErrors.MountPathExists(request.MountPath));

        // 创建挂载目录
        if (!fileSystemService.DirectoryExists(request.MountPath))
        {
            fileSystemService.CreateDirectory(request.MountPath);
        }

        // 创建存储池
        var pool = StoragePool.Create(
            name: request.Name,
            mountPath: request.MountPath,
            description: request.Description,
            maxCapacityBytes: request.MaxCapacityBytes);

        // 刷新容量信息
        await RefreshPoolCapacityAsync(pool, cancellationToken);

        await poolRepository.AddAsync(pool, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StoragePoolDto>.Success(MapToDto(pool));
    }

    public async Task<Result<StoragePoolDto>> UpdatePoolAsync(
        Guid poolId,
        UpdateStoragePoolRequest request,
        CancellationToken cancellationToken = default)
    {
        var pool = await poolRepository.GetByIdAsync(poolId, cancellationToken);
        if (pool is null)
            return Result<StoragePoolDto>.Failure(StorageErrors.PoolNotFound(poolId));

        // 更新名称、描述和最大容量
        pool.Update(
            request.Name ?? pool.Name,
            request.Description ?? pool.Description,
            request.MaxCapacityBytes);

        // 更新启用状态
        if (request.IsEnabled.HasValue)
            pool.SetEnabled(request.IsEnabled.Value);

        await RefreshPoolCapacityAsync(pool, cancellationToken);
        await poolRepository.UpdateAsync(pool, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<StoragePoolDto>.Success(MapToDto(pool));
    }

    public async Task<Result> DeletePoolAsync(
        Guid poolId,
        CancellationToken cancellationToken = default)
    {
        var pool = await poolRepository.GetByIdAsync(poolId, cancellationToken);
        if (pool is null)
            return Result.Failure(StorageErrors.PoolNotFound(poolId));

        // 检查是否是默认池
        if (pool.IsDefault)
            return Result.Failure(StorageErrors.CannotDeleteDefaultPool);

        await poolRepository.DeleteAsync(pool, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> SetDefaultPoolAsync(
        Guid poolId,
        CancellationToken cancellationToken = default)
    {
        var pool = await poolRepository.GetByIdAsync(poolId, cancellationToken);
        if (pool is null)
            return Result.Failure(StorageErrors.PoolNotFound(poolId));

        // 清除当前默认池
        var currentDefault = await poolRepository.GetDefaultAsync(cancellationToken);
        if (currentDefault != null && currentDefault.Id != poolId)
        {
            currentDefault.SetAsDefault(false);
            await poolRepository.UpdateAsync(currentDefault, cancellationToken);
        }

        // 设置新的默认池
        pool.SetAsDefault(true);
        await poolRepository.UpdateAsync(pool, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> RefreshCapacityAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);

        foreach (var pool in pools)
        {
            // 显式刷新端点：绕过节流窗口强制重算
            await RefreshPoolCapacityAsync(pool, cancellationToken, force: true);
            await poolRepository.UpdateAsync(pool, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CheckStorageSpaceResponse>> CheckStorageSpaceAsync(
        CheckStorageSpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        StoragePool? pool;
        
        if (request.StoragePoolId.HasValue)
        {
            pool = await poolRepository.GetByIdAsync(request.StoragePoolId.Value, cancellationToken);
        }
        else
        {
            pool = await poolRepository.GetDefaultAsync(cancellationToken);
        }

        if (pool is null)
        {
            return Result<CheckStorageSpaceResponse>.Success(new CheckStorageSpaceResponse
            {
                HasEnoughSpace = false,
                AvailableBytes = 0,
                RequiredBytes = request.FileSize,
                Message = "未找到存储池"
            });
        }

        // 刷新容量信息。这里也要落库：UpdatedAt 是节流窗口的唯一依据，
        // 不落库则窗口永不推进，每次上传预检都重算一次全池 SUM
        if (await RefreshPoolCapacityAsync(pool, cancellationToken))
        {
            await poolRepository.UpdateAsync(pool, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var hasEnoughSpace = pool.HasEnoughSpace(request.FileSize);
        
        return Result<CheckStorageSpaceResponse>.Success(new CheckStorageSpaceResponse
        {
            HasEnoughSpace = hasEnoughSpace,
            AvailableBytes = pool.AvailableBytes,
            RequiredBytes = request.FileSize,
            Message = hasEnoughSpace 
                ? null 
                : $"存储空间不足，需要 {FormatSize(request.FileSize)}，可用 {FormatSize(pool.AvailableBytes)}"
        });
    }

    #region 辅助方法

    /// <summary>
    /// 容量刷新节流窗口：读端点（GET overview/pools）高频触发，
    /// 物理容量不会秒级变化；窗口内已刷新过的池直接复用存量数据，GET 不写库。
    /// 显式 RefreshCapacityAsync 与写路径传 force=true 绕过窗口
    /// </summary>
    private static readonly TimeSpan CapacityRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 刷新单个池的容量信息（读路径受节流窗口保护）
    /// </summary>
    private async Task<bool> RefreshPoolCapacityAsync(
        StoragePool pool,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!force && DateTime.UtcNow - (pool.UpdatedAt ?? pool.CreatedAt) < CapacityRefreshInterval)
            return false;

        try
        {
            // 获取磁盘总容量
            if (fileSystemService.DirectoryExists(pool.MountPath))
            {
                var pathRoot = Path.GetPathRoot(pool.MountPath);
                if (!string.IsNullOrEmpty(pathRoot))
                {
                    var driveInfo = new DriveInfo(pathRoot);
                    if (driveInfo.IsReady)
                    {
                        // 计算存储池内所有文件的总大小作为已用空间
                        var usedBytes = await fileItemRepository.CalculateStoragePoolUsedSpaceAsync(pool.Id, cancellationToken);
                        pool.UpdateCapacity(driveInfo.TotalSize, usedBytes);
                        return true;
                    }
                }
            }
        }
        catch
        {
            // 忽略无法访问的存储池
        }
        return false;
    }

    private static StoragePoolDto MapToDto(StoragePool pool)
    {
        return new StoragePoolDto
        {
            Id = pool.Id,
            Name = pool.Name,
            Description = pool.Description,
            MountPath = pool.MountPath,
            TotalBytes = pool.EffectiveTotalBytes,
            UsedBytes = pool.UsedBytes,
            AvailableBytes = pool.AvailableBytes,
            UsagePercentage = Math.Round(pool.UsagePercentage, 2),
            MaxCapacityBytes = pool.MaxCapacityBytes,
            MaxCapacityDisplay = pool.MaxCapacityBytes.HasValue ? FormatSize(pool.MaxCapacityBytes.Value) : null,
            TotalDisplay = FormatSize(pool.EffectiveTotalBytes),
            UsedDisplay = FormatSize(pool.UsedBytes),
            AvailableDisplay = FormatSize(pool.AvailableBytes),
            IsDefault = pool.IsDefault,
            IsEnabled = pool.IsEnabled,
            IsNearlyFull = pool.IsNearlyFull,
            CreatedAt = pool.CreatedAt
        };
    }


    #endregion
}
