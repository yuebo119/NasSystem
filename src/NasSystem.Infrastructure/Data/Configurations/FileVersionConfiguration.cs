// =============================================================================
// NasSystem.Infrastructure - FileVersionConfiguration.cs
// 文件版本实体配置
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 文件版本实体 EF Core 配置
/// </summary>
public class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> builder)
    {
        builder.ToTable("FileVersions");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.VersionNumber)
            .IsRequired();

        builder.Property(v => v.StoragePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(v => v.FileSize)
            .IsRequired();

        builder.Property(v => v.FileHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(v => v.Comment)
            .HasMaxLength(500);

        builder.Property(v => v.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        // 关系配置
        builder.HasOne(v => v.FileItem)
            .WithMany()
            .HasForeignKey(v => v.FileItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.CreatedByUser)
            .WithMany()
            .HasForeignKey(v => v.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // 索引
        builder.HasIndex(v => v.FileItemId);
        builder.HasIndex(v => new { v.FileItemId, v.VersionNumber })
            .IsUnique();
        builder.HasIndex(v => new { v.FileItemId, v.IsCurrent });
    }
}
