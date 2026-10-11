// =============================================================================
// NasSystem.IntegrationTests - BasicIntegrationTests.cs
// 基础集成测试 (不需要完整服务器启动)
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Persistence;
using Xunit;

/// <summary>
/// 基础集成测试 - 测试数据访问层和业务逻辑
/// </summary>
/// <remarks>
/// 从直连 DbContext + InMemory provider 改为真实 SQLite 文件库 + 仓储接口。
/// PalORM 没有内存 provider，而文件库还顺带覆盖了 InMemory 覆盖不到的行为：
/// Guid 的 TEXT 大小写敏感比较、部分唯一索引、外键约束。
/// </remarks>
public class BasicIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;

    public BasicIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"BasicIntegration_{Guid.NewGuid():N}.db");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructureServices(config);
        _provider = services.BuildServiceProvider();

        _provider.InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    private (IServiceScope Scope, IUserRepository Users, IStoragePoolRepository Pools,
        IFileItemRepository FileItems, IFileShareRepository Shares, IUnitOfWork UnitOfWork) Scope()
    {
        var scope = _provider.CreateScope();
        return (
            scope,
            scope.ServiceProvider.GetRequiredService<IUserRepository>(),
            scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileItemRepository>(),
            scope.ServiceProvider.GetRequiredService<IFileShareRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public async Task CanCreateAndRetrieveUser()
    {
        var (scope, users, _, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create(
                username: "testuser",
                passwordHash: "hash123",
                passwordSalt: "salt456",
                role: UserRole.User);

            await users.AddAsync(user);
            await unitOfWork.SaveChangesAsync();
        }

        var (readScope, readUsers, _, _, _, _) = Scope();
        using (readScope)
        {
            var retrievedUser = await readUsers.GetByUsernameAsync("testuser");
            retrievedUser.Should().NotBeNull();
            retrievedUser!.Username.Should().Be("testuser");
        }
    }

    [Fact]
    public async Task CanCreateAndRetrieveStoragePool()
    {
        var (scope, _, pools, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var pool = StoragePool.Create(name: "TestPool", mountPath: "/mnt/test");
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();
        }

        var (readScope, _, readPools, _, _, _) = Scope();
        using (readScope)
        {
            var retrievedPool = (await readPools.GetAllAsync()).FirstOrDefault(p => p.Name == "TestPool");
            retrievedPool.Should().NotBeNull();
            retrievedPool!.MountPath.Should().Be("/mnt/test");
        }
    }

    [Fact]
    public async Task CanCreateAndRetrieveFileItem()
    {
        Guid folderId;
        var (scope, users, pools, _, _, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("owner", "hash", "salt");
            var pool = StoragePool.Create("Pool", "/mnt/pool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var folder = FileItem.CreateFolder(
                name: "Documents",
                path: "/documents",
                physicalPath: "/mnt/pool/documents",
                storagePoolId: pool.Id,
                ownerId: user.Id);
            await scope.ServiceProvider.GetRequiredService<IFileItemRepository>().AddAsync(folder);
            await unitOfWork.SaveChangesAsync();
            folderId = folder.Id;
        }

        var (readScope, _, _, readFileItems, _, _) = Scope();
        using (readScope)
        {
            var retrievedFolder = await readFileItems.GetByIdAsync(folderId);
            retrievedFolder.Should().NotBeNull();
            retrievedFolder!.ItemType.Should().Be(FileItemType.Folder);
            retrievedFolder.Path.Should().Be("/documents");
        }
    }

    [Fact]
    public async Task CanCreateAndRetrieveFileShare()
    {
        Guid fileId;
        var (scope, users, pools, fileItems, shares, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("shareowner", "hash", "salt");
            var pool = StoragePool.Create("SharePool", "/mnt/sharepool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var file = FileItem.CreateFile(
                name: "shared.txt",
                path: "/shared.txt",
                physicalPath: "/mnt/sharepool/shared.txt",
                sizeBytes: 1024,
                mimeType: "text/plain",
                storagePoolId: pool.Id,
                ownerId: user.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            fileId = file.Id;

            var fileShare = FileShare.Create(
                fileItemId: file.Id,
                createdByUserId: user.Id,
                accessType: ShareAccessType.Public,
                permission: PermissionLevel.Read);

            await shares.AddAsync(fileShare);
            await unitOfWork.SaveChangesAsync();
        }

        var (readScope, _, _, _, readShares, _) = Scope();
        using (readScope)
        {
            var all = await readShares.GetAllAsync();
            var retrievedShare = all.FirstOrDefault(s => s.FileItemId == fileId);
            retrievedShare.Should().NotBeNull();
            retrievedShare!.AccessType.Should().Be(ShareAccessType.Public);
            retrievedShare.ShareCode.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task UserCanHaveMultipleFiles()
    {
        var (scope, users, pools, fileItems, _, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("fileowner", "hash", "salt");
            var pool = StoragePool.Create("FilePool", "/mnt/filepool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var file1 = FileItem.CreateFile(
                name: "doc1.txt", path: "/doc1.txt", physicalPath: "/mnt/filepool/doc1.txt",
                sizeBytes: 1024, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id);
            var file2 = FileItem.CreateFile(
                name: "doc2.txt", path: "/doc2.txt", physicalPath: "/mnt/filepool/doc2.txt",
                sizeBytes: 2048, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id);

            await fileItems.AddAsync(file1);
            await fileItems.AddAsync(file2);
            await unitOfWork.SaveChangesAsync();

            var userFiles = await fileItems.GetByStoragePoolAsync(pool.Id);
            userFiles.Should().HaveCount(2);
            userFiles.Sum(f => f.SizeBytes).Should().Be(3072);
        }
    }

    [Fact]
    public async Task SoftDeleteWorksCorrectly()
    {
        Guid fileId;
        var (scope, users, pools, fileItems, _, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("deleteowner", "hash", "salt");
            var pool = StoragePool.Create("DeletePool", "/mnt/deletepool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var file = FileItem.CreateFile(
                name: "todelete.txt", path: "/todelete.txt", physicalPath: "/mnt/deletepool/todelete.txt",
                sizeBytes: 100, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            fileId = file.Id;

            file.SoftDelete();
            await fileItems.UpdateAsync(file);
            await unitOfWork.SaveChangesAsync();
        }

        var (readScope, _, _, readFileItems, _, _) = Scope();
        using (readScope)
        {
            var deletedFile = await readFileItems.GetByIdAsync(fileId);
            deletedFile.Should().NotBeNull();
            deletedFile!.IsDeleted.Should().BeTrue();
            deletedFile.DeletedAt.Should().NotBeNull();

            // 软删后按路径查不到（部分唯一索引 + 仓储谓词共同保证）
            var byPath = await readFileItems.GetByPathAsync("/todelete.txt");
            byPath.Should().BeNull();
        }
    }

    [Fact]
    public async Task FileShareExpiration_WorksCorrectly()
    {
        var (scope, users, pools, fileItems, shares, unitOfWork) = Scope();
        using (scope)
        {
            var user = User.Create("shareuser", "hash", "salt");
            var pool = StoragePool.Create("Pool", "/mnt/pool");
            await users.AddAsync(user);
            await pools.AddAsync(pool);
            await unitOfWork.SaveChangesAsync();

            var file = FileItem.CreateFile(
                name: "expiring.txt", path: "/expiring.txt", physicalPath: "/mnt/pool/expiring.txt",
                sizeBytes: 100, mimeType: "text/plain", storagePoolId: pool.Id, ownerId: user.Id);
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();

            var expiredShare = FileShare.Create(
                fileItemId: file.Id, createdByUserId: user.Id,
                expiresAt: DateTime.UtcNow.AddDays(-1)); // Expired yesterday

            var validShare = FileShare.Create(
                fileItemId: file.Id, createdByUserId: user.Id,
                expiresAt: DateTime.UtcNow.AddDays(7)); // Expires in a week

            await shares.AddAsync(expiredShare);
            await shares.AddAsync(validShare);
            await unitOfWork.SaveChangesAsync();

            expiredShare.IsExpired.Should().BeTrue();
            expiredShare.IsAccessible.Should().BeFalse();
            validShare.IsExpired.Should().BeFalse();
            validShare.IsAccessible.Should().BeTrue();
        }
    }

    public void Dispose()
    {
        _provider.Dispose();

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try
            {
                if (File.Exists(_dbPath + suffix))
                    File.Delete(_dbPath + suffix);
            }
            catch
            {
                // 句柄未释放时留下，由系统临时目录清理
            }
        }
    }
}
