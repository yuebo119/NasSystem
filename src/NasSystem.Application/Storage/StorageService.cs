// =============================================================================
// NasSystem.Application - StorageService.cs
// 存储服务实现（简化版）
// =============================================================================

namespace NasSystem.Application.Storage;

using System.Collections.Concurrent;
using NasSystem.Application.Common;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Shared;
using Microsoft.Extensions.Caching.Memory;
using static NasSystem.Shared.ByteSizeFormatter;

/// <summary>
/// 存储服务实现（简化版）
/// </summary>
public sealed class StorageService(
    IStoragePoolRepository poolRepository,
    IFileItemRepository fileItemRepository,
    IUnitOfWork unitOfWork,
    IFileSystemService fileSystemService,
    IMemoryCache cache) : IStorageService
{
    /// <summary>
    /// 池容量上次刷新时间的内存缓存键前缀。按池 Id 分键，
    /// 值是该池上次完成记账的 UTC 时间。
    /// </summary>
    private const string PoolRefreshKeyPrefix = "storageservice:pool-refresh:";

    /// <summary>
    /// 按池 Id 的刷新闸门：同一池的并发刷新请求合并为一次全盘遍历。
    /// </summary>
    /// <remarks>
    /// 前端首页的 overview 与 pools 两个端点由 Promise.all 并发发出，两者各自检查
    /// 60s 节流缓存都未命中（缓存只在遍历完成后才写），结果是同一池被完整遍历两遍。
    /// 闸门让第二个请求等第一个的结果，等待期间再双检一次节流缓存，命中即跳过。
    /// 静态字典：闸门必须跨请求共享才有意义，与 DirectoryVersion 同一先例。
    /// 无等待者时移除闸门，字典规模不随池的增删增长。
    /// </remarks>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RefreshGates = new();
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

        // 池大小 = 各池的库记账之和（2026-10-17 起以库记账为准，原文件夹
        // 实测口径废止）。与已用同源同值，两个数字保留是 API 兼容
        long folderSizeBytes = pools.Sum(p => p.FolderSizeBytes);
        long usedBytes = pools.Sum(p => p.UsedBytes);
        // 可用 = 文件夹的增长空间。同一磁盘上的多个池共享该盘剩余，
        // 按盘根去重后求和，避免把一块盘的剩余重复计几次
        long availableBytes = SumFreeBytesByDistinctDrive(pools);
        // 使用率只对设置了容量上限的池有意义。混合配置（部分池有上限）时
        // 凑不出一个诚实的聚合值，索性不展示
        var enabled = pools.Where(p => p.IsEnabled).ToList();
        bool hasCapacityLimit = enabled.Count > 0 && enabled.All(p => p.MaxCapacityBytes > 0);
        double usagePercentage = hasCapacityLimit
            ? (double)enabled.Sum(p => p.UsedBytes) / enabled.Sum(p => p.MaxCapacityBytes!.Value) * 100
            : 0;

        // 池账目口径：只累计设了上限的启用池，未设上限的池不进"已分配"。
        // 与物理磁盘容量严格分开，后者由 /system/drives 单独提供
        var quotaPools = enabled.Where(p => p.MaxCapacityBytes > 0).ToList();
        long poolAllocatedBytes = quotaPools.Sum(p => p.MaxCapacityBytes!.Value);
        long poolRemainingBytes = quotaPools.Sum(p =>
            Math.Max(0, p.MaxCapacityBytes!.Value - p.UsedBytes));

        return Result<StorageOverviewDto>.Success(new StorageOverviewDto
        {
            FolderSizeBytes = folderSizeBytes,
            UsedBytes = usedBytes,
            AvailableBytes = availableBytes,
            UsagePercentage = Math.Round(usagePercentage, 2),
            HasCapacityLimit = hasCapacityLimit,
            PoolAllocatedBytes = poolAllocatedBytes,
            PoolRemainingBytes = poolRemainingBytes,
            FolderSizeDisplay = FormatSize(folderSizeBytes),
            UsedDisplay = FormatSize(usedBytes),
            AvailableDisplay = FormatSize(availableBytes),
            PoolCount = pools.Count,
            DiskCount = CountReadyDrives(),
            HealthStatus = DeriveHealthStatus(pools)
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

        // 初始记账必须 force：节流窗口按 UpdatedAt ?? CreatedAt 判定，
        // 刚建出来的池两者都是当下，不 force 的话第一次刷新必然被窗口挡掉，
        // 新建一个有内容的池会先显示 0 B 满 60 秒
        await RefreshPoolCapacityAsync(pool, cancellationToken, force: true);

        await poolRepository.AddAsync(pool, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            // ITM-N7：预检与提交之间的竞态（两个同名/同路径建池请求同时通过
            // 预检）由 IX_StoragePools_Name / IX_StoragePools_MountPath 唯一
            // 索引兜底。不捕获会穿透到 GlobalExceptionHandler 变 500，
            // 而同仓 User/Backup/File/FileShare 四条路径都有守卫，
            // 此处是 2026-10-21 审计发现的唯一缺口
            return Result<StoragePoolDto>.Failure(StorageErrors.PoolNameExists(request.Name));
        }

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

        // 刷新容量信息。窗口内已刷新过则直接跳过：既省掉全池 SUM，
        // 也省掉一次无谓的 UPDATE + 提交。窗口判定在内存里，不依赖落库推进
        if (await RefreshPoolCapacityAsync(pool, cancellationToken))
        {
            await poolRepository.UpdateAsync(pool, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // 可用 = 这个文件夹还能往里长多少：配额剩余与磁盘物理剩余取小。
        // 上传预检按这个数判定才不会把盘写爆；两者都不可知时（无配额且挂载点
        // 不可达）判为放不下，由健康状态的 Warning 暴露真正原因
        var availableBytes = pool.AvailableBytesWithin(
            DiskSpace.GetPhysicalFreeBytes(pool.MountPath));
        var hasEnoughSpace = availableBytes >= request.FileSize;

        return Result<CheckStorageSpaceResponse>.Success(new CheckStorageSpaceResponse
        {
            HasEnoughSpace = hasEnoughSpace,
            AvailableBytes = availableBytes ?? 0,
            RequiredBytes = request.FileSize,
            Message = hasEnoughSpace
                ? null
                : $"存储空间不足，需要 {FormatSize(request.FileSize)}，可用 {FormatSize(availableBytes ?? 0)}"
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
    /// 窗口判定改走内存缓存，不再读池行的 UpdatedAt。
    /// </summary>
    /// <remarks>
    /// 原实现用 <c>pool.UpdatedAt ?? pool.CreatedAt</c> 判定，而首页的 overview 与
    /// pools 两个端点是并发发出的（前端 Promise.all），两个作用域各读一份旧 UpdatedAt，
    /// 都判定"超过窗口"，于是同一池被完整记账两遍、SUM 查两遍。
    /// 内存缓存在请求之间共享，第一个请求刷新后写入时间戳，第二个请求在窗口内直接跳过。
    /// 代价是进程重启后第一次请求会重新记账一次，这符合预期：新进程没有旧观测值。
    /// </remarks>
    private bool ShouldRefreshPool(StoragePool pool, bool force)
    {
        if (force)
            return true;

        if (cache.TryGetValue(PoolRefreshKeyPrefix + pool.Id.ToString("D"), out DateTime lastRefreshUtc))
            return DateTime.UtcNow - lastRefreshUtc >= CapacityRefreshInterval;

        return true;
    }

    private void MarkPoolRefreshed(StoragePool pool) =>
        cache.Set(
            PoolRefreshKeyPrefix + pool.Id.ToString("D"),
            DateTime.UtcNow,
            CapacityRefreshInterval);

    /// <summary>
    /// 就绪的物理磁盘数
    /// </summary>
    private static int CountReadyDrives()
    {
        try
        {
            return DriveInfo.GetDrives().Count(d => d.IsReady);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 可用容量按盘根去重后求和。同一磁盘上的多个池共享该盘剩余，
    /// 逐个池相加会把一块盘的剩余重复计几次
    /// </summary>
    private static long SumFreeBytesByDistinctDrive(IReadOnlyList<StoragePool> pools)
    {
        return pools
            .Where(p => p.IsEnabled)
            .GroupBy(p => Path.GetPathRoot(p.MountPath) ?? string.Empty)
            .Sum(g => DiskSpace.GetPhysicalFreeBytes(g.First().MountPath) ?? 0);
    }

    /// <summary>
    /// 存储健康状态，由池的真实可达性推导而非硬编码：任一启用池不可达
    /// 或使用率超过 90% 记 Warning
    /// </summary>
    private static string DeriveHealthStatus(IReadOnlyList<StoragePool> pools)
    {
        var enabled = pools.Where(p => p.IsEnabled).ToList();
        if (enabled.Count == 0)
            return "Healthy";

        if (enabled.Any(p => !DiskSpace.IsPoolReachable(p.MountPath)))
            return "Warning";

        return enabled.Any(p => p.IsNearlyFull) ? "Warning" : "Healthy";
    }

    /// <summary>
    /// 刷新单个池的大小与已用容量（读路径受节流窗口保护）。
    /// 2026-10-17 起大小与已用同源：都是库内记账 SUM（用户裁决"以库记账为准"，
    /// 原扫目录取大小的口径废止，MeasureFolderBytes 随之删除）
    /// </summary>
    private async Task<bool> RefreshPoolCapacityAsync(
        StoragePool pool,
        CancellationToken cancellationToken,
        bool force = false)
    {
        if (!ShouldRefreshPool(pool, force))
            return false;

        // 同一池的并发刷新合并：前端 overview/pools 两个端点并发打出时，
        // 两者都未命中节流缓存（缓存在刷新完成后才写），无闸门会各查一次 SUM
        var gate = RefreshGates.GetOrAdd(pool.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 双检：等待期间前一个请求可能已完成刷新并写了缓存
            if (!ShouldRefreshPool(pool, force))
                return false;

            return await MeasureAndUpdatePoolAsync(pool, cancellationToken);
        }
        finally
        {
            gate.Release();
            // CurrentCount 回 1 表示没有等待者，移除闸门防字典随池增删无限增长。
            // 竞态窗口内新等待者取到的是同一实例引用，最坏后果是多一次 SUM，不会错
            if (gate.CurrentCount == 1)
                RefreshGates.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(pool.Id, gate));
        }
    }

    /// <summary>
    /// 记账并更新单个池的大小与已用容量（调用方已持有该池的刷新闸门）。
    /// </summary>
    /// <remarks>
    /// S1b（用户 2026-10-17 裁决：以库记账为准）：池大小不再物理实测挂载点目录
    /// （十万文件的池要数秒且阻塞请求线程，是唯一随文件数线性增长并同步阻塞的
    /// 读路径操作），改为库内 SUM。语义变化：外部直接放到盘上、不经本应用的
    /// 文件不再计入池大小；这样的文件本来也不在库内，扫描同步会把它们收进来。
    /// 与原实测口径的差别由 StorageOverviewTests 的新口径用例锁住。
    /// </remarks>
    private async Task<bool> MeasureAndUpdatePoolAsync(
        StoragePool pool,
        CancellationToken cancellationToken)
    {
        // NAS 管理的文件总大小：大小与已用同源（见 StoragePool.UpdateUsage 注释）
        var managedBytes = await fileItemRepository.CalculateStoragePoolUsedSpaceAsync(
            pool.Id, cancellationToken);
        pool.UpdateUsage(managedBytes);
        MarkPoolRefreshed(pool);
        return true;
    }

    private static StoragePoolDto MapToDto(StoragePool pool)
    {
        // 可用 = 文件夹的增长空间（配额剩余与磁盘物理剩余取小），
        // 与概览、上传预检同一口径；不可知时显示 0
        var availableBytes = pool.AvailableBytesWithin(
            DiskSpace.GetPhysicalFreeBytes(pool.MountPath));
        return new StoragePoolDto
        {
            Id = pool.Id,
            Name = pool.Name,
            Description = pool.Description,
            MountPath = pool.MountPath,
            FolderSizeBytes = pool.FolderSizeBytes,
            UsedBytes = pool.UsedBytes,
            AvailableBytes = availableBytes ?? 0,
            UsagePercentage = Math.Round(pool.UsagePercentage, 2),
            MaxCapacityBytes = pool.MaxCapacityBytes,
            MaxCapacityDisplay = pool.MaxCapacityBytes.HasValue ? FormatSize(pool.MaxCapacityBytes.Value) : null,
            FolderSizeDisplay = FormatSize(pool.FolderSizeBytes),
            UsedDisplay = FormatSize(pool.UsedBytes),
            AvailableDisplay = FormatSize(availableBytes ?? 0),
            IsDefault = pool.IsDefault,
            IsEnabled = pool.IsEnabled,
            Status = DiskSpace.IsPoolReachable(pool.MountPath) ? 1 : 5,
            IsNearlyFull = pool.IsNearlyFull,
            CreatedAt = pool.CreatedAt
        };
    }


    #endregion
}
