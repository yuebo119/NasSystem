// =============================================================================
// NasSystem.Application - StorageService.cs
// 存储服务实现
// =============================================================================

namespace NasSystem.Application.Storage;

using NasSystem.Application.Common;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;

/// <summary>
/// 存储服务实现
/// </summary>
public sealed class StorageService(
    IStoragePoolRepository poolRepository,
    IShareRepository shareRepository,
    IUnitOfWork unitOfWork,
    IFileSystemService fileSystemService) : IStorageService
{
    public async Task<Result<StorageOverviewDto>> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);
        
        long totalBytes = 0;
        long usedBytes = 0;
        int diskCount = 0;
        int healthyCount = 0;
        int warningCount = 0;
        int criticalCount = 0;

        foreach (var pool in pools)
        {
            totalBytes += pool.TotalBytes;
            usedBytes += pool.UsedBytes;
        }

        var availableBytes = totalBytes - usedBytes;
        var usagePercentage = totalBytes > 0 ? (double)usedBytes / totalBytes * 100 : 0;

        return Result<StorageOverviewDto>.Success(new StorageOverviewDto
        {
            TotalBytes = totalBytes,
            UsedBytes = usedBytes,
            AvailableBytes = availableBytes,
            UsagePercentage = Math.Round(usagePercentage, 2),
            TotalDisplay = FormatSize(totalBytes),
            UsedDisplay = FormatSize(usedBytes),
            AvailableDisplay = FormatSize(availableBytes),
            PoolCount = pools.Count,
            DiskCount = diskCount,
            HealthyDiskCount = healthyCount,
            WarningDiskCount = warningCount,
            CriticalDiskCount = criticalCount
        });
    }

    public async Task<Result<IReadOnlyList<StoragePoolDto>>> GetPoolsAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);
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

        return Result<StoragePoolDto>.Success(MapToDto(pool));
    }

    public async Task<Result<StoragePoolDto>> CreatePoolAsync(
        CreateStoragePoolRequest request,
        CancellationToken cancellationToken = default)
    {
        // 检查名称是否已存在
        if (await poolRepository.NameExistsAsync(request.Name, cancellationToken))
            return Result<StoragePoolDto>.Failure(StorageErrors.PoolNameExists(request.Name));

        // 创建挂载目录
        if (!fileSystemService.DirectoryExists(request.MountPath))
        {
            fileSystemService.CreateDirectory(request.MountPath);
        }

        // 创建存储池
        var pool = StoragePool.Create(
            name: request.Name,
            mountPath: request.MountPath,
            raidLevel: request.RaidLevel,
            description: request.Description);

        // 设置优化选项
        pool.SetOptimization(request.EnableDeduplication, request.EnableCompression);

        // 初始化状态为健康
        pool.UpdateStatus(StoragePoolStatus.Healthy);

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

        // 更新名称 - 需要在实体中添加方法或直接更新
        // 这里简化处理，假设有 UpdateInfo 方法

        // 更新状态
        if (request.IsEnabled.HasValue)
            pool.SetEnabled(request.IsEnabled.Value);

        if (request.EnableDeduplication.HasValue || request.EnableCompression.HasValue)
            pool.SetOptimization(
                request.EnableDeduplication ?? pool.EnableDeduplication,
                request.EnableCompression ?? pool.EnableCompression);

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

        // 检查是否有共享使用此存储池
        var shares = await shareRepository.GetByStoragePoolAsync(poolId, cancellationToken);
        if (shares.Count > 0)
            return Result.Failure(StorageErrors.CannotDeletePoolWithShares);

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

    public Task<Result<IReadOnlyList<SystemDiskDto>>> GetSystemDisksAsync(
        CancellationToken cancellationToken = default)
    {
        var disks = new List<SystemDiskDto>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                disks.Add(new SystemDiskDto
                {
                    DevicePath = drive.Name,
                    SerialNumber = "N/A",
                    Model = drive.DriveType.ToString(),
                    SizeBytes = drive.TotalSize,
                    SizeDisplay = FormatSize(drive.TotalSize),
                    DiskType = drive.DriveType == DriveType.Fixed ? DiskType.Hdd : DiskType.Usb,
                    Status = DiskStatus.Healthy,
                    HealthPercentage = 100,
                    TemperatureCelsius = null,
                    IsSystemDisk = drive.Name.Equals("C:\\", StringComparison.OrdinalIgnoreCase),
                    IsInUse = true
                });
            }
        }
        catch
        {
            // 忽略无法读取的磁盘
        }

        return Task.FromResult(Result<IReadOnlyList<SystemDiskDto>>.Success(disks));
    }

    public async Task<Result<IReadOnlyList<SystemDiskDto>>> GetAvailableDisksAsync(
        CancellationToken cancellationToken = default)
    {
        var allDisksResult = await GetSystemDisksAsync(cancellationToken);
        if (!allDisksResult.IsSuccess)
            return allDisksResult;

        var available = allDisksResult.Value
            .Where(d => !d.IsSystemDisk && !d.IsInUse)
            .ToList();

        return Result<IReadOnlyList<SystemDiskDto>>.Success(available);
    }

    public Task<Result<StoragePoolDiskDto>> AddDiskToPoolAsync(
        Guid poolId,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现添加磁盘到存储池 (需要底层存储支持)
        return Task.FromResult(Result<StoragePoolDiskDto>.Failure(
            new Error("NotImplemented", "磁盘管理功能需要底层存储支持")));
    }

    public Task<Result> RemoveDiskFromPoolAsync(
        Guid poolId,
        Guid diskId,
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现从存储池移除磁盘
        return Task.FromResult(Result.Failure(
            new Error("NotImplemented", "磁盘管理功能需要底层存储支持")));
    }

    public Task<Result> ScanDiskHealthAsync(
        CancellationToken cancellationToken = default)
    {
        // TODO: 实现 SMART 健康扫描
        return Task.FromResult(Result.Success());
    }

    public async Task<Result> RefreshUsageAsync(
        CancellationToken cancellationToken = default)
    {
        var pools = await poolRepository.GetAllAsync(cancellationToken);

        foreach (var pool in pools)
        {
            try
            {
                if (fileSystemService.DirectoryExists(pool.MountPath))
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(pool.MountPath) ?? pool.MountPath);
                    pool.UpdateUsage(driveInfo.TotalSize - driveInfo.AvailableFreeSpace);
                    await poolRepository.UpdateAsync(pool, cancellationToken);
                }
            }
            catch
            {
                // 忽略无法访问的存储池
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    #region 辅助方法

    private static StoragePoolDto MapToDto(StoragePool pool)
    {
        var availableBytes = pool.TotalBytes - pool.UsedBytes;
        var usagePercentage = pool.TotalBytes > 0 ? (double)pool.UsedBytes / pool.TotalBytes * 100 : 0;

        return new StoragePoolDto
        {
            Id = pool.Id,
            Name = pool.Name,
            Description = pool.Description,
            MountPath = pool.MountPath,
            Status = pool.Status,
            RaidLevel = pool.RaidLevel,
            TotalBytes = pool.TotalBytes,
            UsedBytes = pool.UsedBytes,
            AvailableBytes = availableBytes,
            UsagePercentage = Math.Round(usagePercentage, 2),
            TotalDisplay = FormatSize(pool.TotalBytes),
            UsedDisplay = FormatSize(pool.UsedBytes),
            AvailableDisplay = FormatSize(availableBytes),
            IsDefault = pool.IsDefault,
            IsEnabled = pool.IsEnabled,
            EnableDeduplication = pool.EnableDeduplication,
            EnableCompression = pool.EnableCompression,
            CreatedAt = pool.CreatedAt
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] sizes = ["B", "KB", "MB", "GB", "TB", "PB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion
}
