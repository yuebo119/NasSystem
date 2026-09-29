// =============================================================================
// NasSystem.Infrastructure - FileShareConfiguration.cs
// 文件分享实体配置
// =============================================================================

namespace NasSystem.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NasSystem.Domain.Entities;

/// <summary>
/// 文件分享实体配置
/// </summary>
public sealed class FileShareConfiguration : IEntityTypeConfiguration<FileShare>
{
    public void Configure(EntityTypeBuilder<FileShare> builder)
    {
        builder.ToTable("FileShares");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ShareCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(e => e.ShareCode)
            .IsUnique();

        builder.Property(e => e.PasswordHash)
            .HasMaxLength(256);

        builder.HasOne(e => e.FileItem)
            .WithMany()
            .HasForeignKey(e => e.FileItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.FileItemId);
        builder.HasIndex(e => e.CreatedByUserId);
    }
}
