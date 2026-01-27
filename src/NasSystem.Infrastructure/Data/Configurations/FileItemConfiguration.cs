// =============================================================================
// NasSystem.Infrastructure - FileItemConfiguration.cs
// 文件项实体 EF Core 配置
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 文件项实体配置
/// </summary>
public sealed class FileItemConfiguration : IEntityTypeConfiguration<FileItem>
{
    public void Configure(EntityTypeBuilder<FileItem> builder)
    {
        builder.ToTable("FileItems");

        // 主键
        builder.HasKey(f => f.Id);

        // 属性配置
        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(f => f.Path)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(f => f.PhysicalPath)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(f => f.ItemType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(f => f.MimeType)
            .HasMaxLength(100);

        builder.Property(f => f.Extension)
            .HasMaxLength(50);

        builder.Property(f => f.Hash)
            .HasMaxLength(128);

        // 关系配置
        builder.HasOne(f => f.Owner)
            .WithMany()
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.StoragePool)
            .WithMany()
            .HasForeignKey(f => f.StoragePoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.Parent)
            .WithMany(f => f.Children)
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 索引
        builder.HasIndex(f => f.Path);
        builder.HasIndex(f => f.ParentId);
        builder.HasIndex(f => f.OwnerId);
        builder.HasIndex(f => f.StoragePoolId);
        builder.HasIndex(f => f.IsDeleted);
        builder.HasIndex(f => f.Hash);
        builder.HasIndex(f => new { f.ParentId, f.Name });

        // 忽略领域事件
        builder.Ignore(f => f.DomainEvents);
    }
}
