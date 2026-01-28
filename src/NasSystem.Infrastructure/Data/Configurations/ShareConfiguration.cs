// =============================================================================
// NasSystem.Infrastructure - ShareConfiguration.cs
// 共享实体 EF Core 配置
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 共享实体配置
/// </summary>
public sealed class ShareConfiguration : IEntityTypeConfiguration<Share>
{
    public void Configure(EntityTypeBuilder<Share> builder)
    {
        builder.ToTable("Shares");

        // 主键
        builder.HasKey(s => s.Id);

        // 属性配置
        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.Path)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(s => s.PhysicalPath)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(s => s.ShareType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(s => s.SmbShareName)
            .HasMaxLength(100);

        // 关系配置
        builder.HasOne(s => s.CreatedByUser)
            .WithMany()
            .HasForeignKey(s => s.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.StoragePool)
            .WithMany()
            .HasForeignKey(s => s.StoragePoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Permissions)
            .WithOne(p => p.Share)
            .HasForeignKey(p => p.ShareId)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引
        builder.HasIndex(s => s.Name).IsUnique();
        builder.HasIndex(s => s.Path);
        builder.HasIndex(s => s.IsEnabled);
        builder.HasIndex(s => s.StoragePoolId);

        // 忽略领域事件
        builder.Ignore(s => s.DomainEvents);
    }
}

/// <summary>
/// 共享权限实体配置
/// </summary>
public sealed class SharePermissionConfiguration : IEntityTypeConfiguration<SharePermission>
{
    public void Configure(EntityTypeBuilder<SharePermission> builder)
    {
        builder.ToTable("SharePermissions");

        // 主键
        builder.HasKey(p => p.Id);

        // 属性配置
        builder.Property(p => p.GroupName)
            .HasMaxLength(100);

        builder.Property(p => p.Permission)
            .IsRequired()
            .HasConversion<int>();

        // 关系配置
        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引
        builder.HasIndex(p => p.ShareId);
        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => new { p.ShareId, p.UserId });

        // 忽略领域事件
        builder.Ignore(p => p.DomainEvents);
    }
}
