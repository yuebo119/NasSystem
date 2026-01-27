// =============================================================================
// NasSystem.Application - DependencyInjection.cs
// 应用层服务注册
// =============================================================================

namespace NasSystem.Application;

using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Files;
using NasSystem.Application.FileVersions;
using NasSystem.Application.Shares;
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
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // 注册 FluentValidation 验证器
        services.AddValidatorsFromAssembly(assembly);

        // 注册应用服务
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<IFileVersionService, FileVersionService>();
        services.AddScoped<IShareService, ShareService>();
        services.AddScoped<IStorageService, StorageService>();

        return services;
    }
}
