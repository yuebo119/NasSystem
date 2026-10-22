// =============================================================================
// NasSystem.Application - DependencyInjection.cs
// 应用层服务注册
// =============================================================================

namespace NasSystem.Application;

using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Backups;
using NasSystem.Application.Files;
using NasSystem.Application.FileShares;
using NasSystem.Application.FileVersions;
using NasSystem.Application.Storage;
using NasSystem.Application.Users;

/// <summary>
/// 应用层依赖注入扩展
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册应用层服务。无 IConfiguration 参数：应用层曾经的唯一配置读取点
    /// （FileVersionOptions 手工读键）已迁到 ISystemConfigService 动态配置，
    /// 接口由基础设施层实现，这里只注册映射
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 注册应用服务
        services.AddScoped<IUserService, UserService>();
        // ITM-T3：ISP 窄接口——调用方只依赖自己需要的用例域
        services.AddScoped<IFileBrowseService, FileService>();
        services.AddScoped<IFileTransferService, FileService>();
        services.AddScoped<IFileRecycleService, FileService>();
        services.AddScoped<IFileSyncService, FileService>();
        services.AddScoped<IFileVersionService, FileVersionService>();
        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IFileShareService, FileShareService>();
        services.AddScoped<IBackupService, BackupService>();

        // 虚拟路径 -> 属主校验后的物理路径。端点不再各自注入仓储做属主比较
        services.AddScoped<IFileAccessResolver, FileAccessResolver>();

        // 版本库物理存储（F-027 收口：文件系统访问经 IFileSystemService，
        // 从静态类改为可注入服务）。单例：无状态，只有路径运算与 IO 委派
        services.AddSingleton<FileVersionStore>();

        // 磁盘物理剩余探测（F-027 收口：从静态 Directory/DriveInfo 直调改为
        // 可注入服务，容量预检的边界路径因此可注入替身测试）。单例：无状态
        services.AddSingleton<DiskSpace>();

        return services;
    }
}
