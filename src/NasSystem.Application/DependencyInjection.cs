// =============================================================================
// NasSystem.Application - DependencyInjection.cs
// 应用层服务注册
// =============================================================================

namespace NasSystem.Application;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// 注册应用层服务
    /// </summary>
    /// <param name="configuration">
    /// 配置源。与 AddInfrastructureServices 同模式显式传入：
    /// 在 Configure 委托里 BuildServiceProvider 会造出第二个容器，
    /// 单例被复制两份、释放语义破裂，是明确的 DI 反模式
    /// </param>
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
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

        // 版本保留策略（ROADMAP Phase 1）：未配置时取 FileVersionOptions 默认值。
        // 显式读键而非 Bind：Bind/GetValue 在 Configuration.Binder 包里，
        // 应用层只引 Abstractions（CPM 统一管的版本用不上），两个键手工映射
        // 比为此引一个包划算。新增配置项时同步补这里
        services.AddOptions<FileVersionOptions>()
            .Configure(options =>
            {
                var section = configuration.GetSection(FileVersionOptions.SectionName);
                if (int.TryParse(section["MaxVersionsPerFile"], out var maxVersions) && maxVersions > 0)
                    options.MaxVersionsPerFile = maxVersions;
                if (int.TryParse(section["MaxVersionAgeDays"], out var maxDays) && maxDays > 0)
                    options.MaxVersionAgeDays = maxDays;
            });

        return services;
    }
}
