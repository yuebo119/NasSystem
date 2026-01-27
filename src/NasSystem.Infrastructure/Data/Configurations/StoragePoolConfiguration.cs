// =============================================================================
// NasSystem.Infrastructure - StoragePoolConfiguration.cs
// 存储池实体 EF Core 配置
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 存储池实体配置
/// </summary>
public sealed class StoragePoolConfiguration : IEntityTypeConfiguration<StoragePool>
{
    public void Configure(EntityTypeBuilder<StoragePool> builder)
    {
        builder.ToTable("StoragePools");

        // 主键
        builder.HasKey(sp => sp.Id);

        // 属性配置
        builder.Property(sp => sp.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(sp => sp.Description)
            .HasMaxLength(500);

        builder.Property(sp => sp.MountPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(sp => sp.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(sp => sp.RaidLevel)
            .IsRequired()
            .HasConversion<int>();

        // 关系配置
        builder.HasMany(sp => sp.Disks)
            .WithOne(d => d.StoragePool)
            .HasForeignKey(d => d.StoragePoolId)
            .OnDelete(DeleteBehavior.Cascade);

        // 索引
        builder.HasIndex(sp => sp.Name).IsUnique();
        builder.HasIndex(sp => sp.IsDefault);
        builder.HasIndex(sp => sp.IsEnabled);

        // 忽略领域事件
        builder.Ignore(sp => sp.DomainEvents);
    }
}

/// <summary>
/// 存储池磁盘实体配置
/// </summary>
public sealed class StoragePoolDiskConfiguration : IEntityTypeConfiguration<StoragePoolDisk>
{
    public void Configure(EntityTypeBuilder<StoragePoolDisk> builder)
    {
        builder.ToTable("StoragePoolDisks");

        // 主键
        builder.HasKey(d => d.Id);

        // 属性配置
        builder.Property(d => d.SerialNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Model)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.DevicePath)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.DiskType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<int>();

        // 索引
        builder.HasIndex(d => d.SerialNumber).IsUnique();
        builder.HasIndex(d => d.DevicePath).IsUnique();
        builder.HasIndex(d => d.StoragePoolId);
        builder.HasIndex(d => d.Status);

        // 忽略领域事件
        builder.Ignore(d => d.DomainEvents);
    }
}
