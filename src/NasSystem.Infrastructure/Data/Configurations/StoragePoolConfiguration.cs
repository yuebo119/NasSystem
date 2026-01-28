// =============================================================================
// NasSystem.Infrastructure - StoragePoolConfiguration.cs
// 存储池实体 EF Core 配置（简化版）
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 存储池实体配置（简化版）
/// </summary>
public sealed class StoragePoolConfiguration : IEntityTypeConfiguration<StoragePool>
{
    public void Configure(EntityTypeBuilder<StoragePool> builder)
    {
        builder.ToTable("StoragePools");

        builder.HasKey(sp => sp.Id);

        builder.Property(sp => sp.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(sp => sp.MountPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(sp => sp.Description)
            .HasMaxLength(500);

        // 索引
        builder.HasIndex(sp => sp.Name).IsUnique();
        builder.HasIndex(sp => sp.MountPath).IsUnique();
        builder.HasIndex(sp => sp.IsDefault);

        // 忽略领域事件
        builder.Ignore(sp => sp.DomainEvents);
    }
}
