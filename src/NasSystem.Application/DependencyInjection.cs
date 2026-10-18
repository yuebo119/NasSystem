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

        return services;
    }
}
