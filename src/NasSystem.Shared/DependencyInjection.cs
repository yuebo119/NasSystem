// =============================================================================
// NasSystem.Shared - DependencyInjection.cs
// 共享库服务注册
// =============================================================================

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 共享库依赖注入扩展
/// </summary>
public static class SharedDependencyInjection
{
    /// <summary>
    /// 注册共享库服务
    /// </summary>
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        // 共享服务注册（如果需要）
        return services;
    }
}
