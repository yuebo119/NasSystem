// =============================================================================
// NasSystem.Infrastructure - NasDbContext.cs
// EF Core 数据库上下文
// =============================================================================

namespace NasSystem.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using NasSystem.Domain.Entities;

/// <summary>
/// NAS 系统数据库上下文
/// </summary>
public sealed class NasDbContext(DbContextOptions<NasDbContext> options) : DbContext(options)
{
    /// <summary>
    /// 用户表
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// 文件项表
    /// </summary>
    public DbSet<FileItem> FileItems => Set<FileItem>();

    /// <summary>
    /// 共享表
    /// </summary>
    public DbSet<Share> Shares => Set<Share>();

    /// <summary>
    /// 共享权限表
    /// </summary>
    public DbSet<SharePermission> SharePermissions => Set<SharePermission>();

    /// <summary>
    /// 存储池表
    /// </summary>
    public DbSet<StoragePool> StoragePools => Set<StoragePool>();

    /// <summary>
    /// 存储池磁盘表
    /// </summary>
    public DbSet<StoragePoolDisk> StoragePoolDisks => Set<StoragePoolDisk>();

    /// <summary>
    /// 文件版本表
    /// </summary>
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 应用所有配置
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NasDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // 启用敏感数据日志 (仅开发环境)
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
        optionsBuilder.EnableDetailedErrors();
#endif
    }
}
