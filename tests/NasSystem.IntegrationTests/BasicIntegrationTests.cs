// =============================================================================
// NasSystem.IntegrationTests - BasicIntegrationTests.cs
// 基础集成测试 (不需要完整服务器启动)
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Infrastructure.Data;
using Xunit;

/// <summary>
/// 基础集成测试 - 测试数据访问层和业务逻辑
/// </summary>
public class BasicIntegrationTests
{
    private readonly DbContextOptions<NasDbContext> _dbOptions;

    public BasicIntegrationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<NasDbContext>()
            .UseInMemoryDatabase("TestDb_" + Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task CanCreateAndRetrieveUser()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var user = User.Create(
            username: "testuser",
            passwordHash: "hash123",
            passwordSalt: "salt456",
            role: UserRole.User);

        // Act
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Assert
        var retrievedUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "testuser");
        retrievedUser.Should().NotBeNull();
        retrievedUser!.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task CanCreateAndRetrieveStoragePool()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var pool = StoragePool.Create(
            name: "TestPool",
            mountPath: "/mnt/test",
            raidLevel: RaidLevel.None);

        // Act
        context.StoragePools.Add(pool);
        await context.SaveChangesAsync();

        // Assert
        var retrievedPool = await context.StoragePools.FirstOrDefaultAsync(p => p.Name == "TestPool");
        retrievedPool.Should().NotBeNull();
        retrievedPool!.MountPath.Should().Be("/mnt/test");
    }

    [Fact]
    public async Task CanCreateAndRetrieveFileItem()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var user = User.Create("owner", "hash", "salt");
        var pool = StoragePool.Create("Pool", "/mnt/pool");
        context.Users.Add(user);
        context.StoragePools.Add(pool);
        await context.SaveChangesAsync();

        var folder = FileItem.CreateFolder(
            name: "Documents",
            path: "/documents",
            physicalPath: "/mnt/pool/documents",
            storagePoolId: pool.Id,
            ownerId: user.Id);

        // Act
        context.FileItems.Add(folder);
        await context.SaveChangesAsync();

        // Assert
        var retrievedFolder = await context.FileItems.FirstOrDefaultAsync(f => f.Name == "Documents");
        retrievedFolder.Should().NotBeNull();
        retrievedFolder!.ItemType.Should().Be(FileItemType.Folder);
        retrievedFolder.Path.Should().Be("/documents");
    }

    [Fact]
    public async Task CanCreateAndRetrieveShare()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var user = User.Create("shareowner", "hash", "salt");
        var pool = StoragePool.Create("SharePool", "/mnt/sharepool");
        context.Users.Add(user);
        context.StoragePools.Add(pool);
        await context.SaveChangesAsync();

        var share = Share.Create(
            name: "PublicShare",
            path: "/shares/public",
            physicalPath: "/mnt/sharepool/shares/public",
            shareType: ShareType.Smb,
            createdByUserId: user.Id,
            storagePoolId: pool.Id);

        // Act
        context.Shares.Add(share);
        await context.SaveChangesAsync();

        // Assert
        var retrievedShare = await context.Shares.FirstOrDefaultAsync(s => s.Name == "PublicShare");
        retrievedShare.Should().NotBeNull();
        retrievedShare!.ShareType.Should().Be(ShareType.Smb);
    }

    [Fact]
    public async Task UserCanHaveMultipleFiles()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var user = User.Create("fileowner", "hash", "salt");
        var pool = StoragePool.Create("FilePool", "/mnt/filepool");
        context.Users.Add(user);
        context.StoragePools.Add(pool);
        await context.SaveChangesAsync();

        var file1 = FileItem.CreateFile(
            name: "doc1.txt",
            path: "/doc1.txt",
            physicalPath: "/mnt/filepool/doc1.txt",
            sizeBytes: 1024,
            mimeType: "text/plain",
            storagePoolId: pool.Id,
            ownerId: user.Id);

        var file2 = FileItem.CreateFile(
            name: "doc2.txt",
            path: "/doc2.txt",
            physicalPath: "/mnt/filepool/doc2.txt",
            sizeBytes: 2048,
            mimeType: "text/plain",
            storagePoolId: pool.Id,
            ownerId: user.Id);

        // Act
        context.FileItems.AddRange(file1, file2);
        await context.SaveChangesAsync();

        // Assert
        var userFiles = await context.FileItems
            .Where(f => f.OwnerId == user.Id)
            .ToListAsync();
        userFiles.Should().HaveCount(2);
        userFiles.Sum(f => f.SizeBytes).Should().Be(3072);
    }

    [Fact]
    public async Task SoftDeleteWorksCorrectly()
    {
        // Arrange
        using var context = new NasDbContext(_dbOptions);
        await context.Database.EnsureCreatedAsync();

        var user = User.Create("deleteowner", "hash", "salt");
        var pool = StoragePool.Create("DeletePool", "/mnt/deletepool");
        context.Users.Add(user);
        context.StoragePools.Add(pool);
        await context.SaveChangesAsync();

        var file = FileItem.CreateFile(
            name: "todelete.txt",
            path: "/todelete.txt",
            physicalPath: "/mnt/deletepool/todelete.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: pool.Id,
            ownerId: user.Id);

        context.FileItems.Add(file);
        await context.SaveChangesAsync();

        // Act
        file.SoftDelete();
        await context.SaveChangesAsync();

        // Assert
        var deletedFile = await context.FileItems.FirstOrDefaultAsync(f => f.Name == "todelete.txt");
        deletedFile.Should().NotBeNull();
        deletedFile!.IsDeleted.Should().BeTrue();
        deletedFile.DeletedAt.Should().NotBeNull();
    }
}
