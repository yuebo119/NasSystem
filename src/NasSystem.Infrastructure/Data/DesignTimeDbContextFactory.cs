// =============================================================================
// NasSystem.Infrastructure - Data/DesignTimeDbContextFactory.cs
// dotnet-ef 设计时工厂——避免迁移工具依赖 Api 启动配置
// =============================================================================

namespace NasSystem.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// dotnet-ef 设计时工厂：`dotnet ef migrations add` 在本项目中解析此类型构造 DbContext。
/// 连接串仅为占位（生成迁移不访问数据库）；运行时连接串来自 Api 配置。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NasDbContext>
{
    public NasDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NasDbContext>()
            .UseSqlite("Data Source=design-time-placeholder.db")
            .Options;
        return new NasDbContext(options);
    }
}
