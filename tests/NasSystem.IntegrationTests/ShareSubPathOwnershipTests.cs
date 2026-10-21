// =============================================================================
// NasSystem.IntegrationTests - ShareSubPathOwnershipTests.cs
// 分享子路径的归属校验回归（本轮审计 Critical：跨用户未授权读取）
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application;
using NasSystem.Application.FileShares;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 锁本轮审计发现的 Critical：分享下载/浏览的 subPath 经
/// ResolveShareSubPathAsync 只做前缀校验（ITM-N16），随后按全库 Path 直查
/// 目标文件。软删记录不占唯一索引，路径可被他人重建——匿名请求持旧分享码
/// 即可读到同名路径上新属主的文件内容。
///
/// 攻击链：A 分享 /sroot → A 软删（Path 让出）→ B 建同名 /sroot 并放入
/// secret.txt → 匿名 GET download/secret.txt。修复前返回 B 的文件流，
/// 修复后按不存在拒绝。
/// </summary>
public class ShareSubPathOwnershipTests
{
    [Fact]
    public async Task DownloadShareSubPath_AfterPathReusedByOtherUser_IsRejected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ShareOwn_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"ShareOwnStore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                ["JwtSettings:SecretKey"] = "integration-test-secret-key-with-adequate-length-32",
                ["JwtSettings:Issuer"] = "NasSystem",
                ["JwtSettings:Audience"] = "NasSystemUsers",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructureServices(config);
        services.AddApplicationServices();

        var provider = services.BuildServiceProvider();
        try
        {
            provider.InitializeDatabaseAsync().GetAwaiter().GetResult();

            var ownerId = await SeedUserAsync(provider, "share-owner");
            var otherId = await SeedUserAsync(provider, "share-squatter");
            var poolId = await SeedPoolAsync(provider, storageRoot);
            var rootId = await SeedFolderAsync(provider, "/sroot", poolId, ownerId);
            await SeedFileAsync(provider, "/sroot/secret.txt", poolId, ownerId, rootId, "owner A content");

            string shareCode;
            using (var scope = provider.CreateScope())
            {
                var shareService = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var created = await shareService.CreateAsync(
                    new CreateFileShareRequest { FileItemId = rootId, AccessType = ShareAccessType.Public },
                    ownerId,
                    "https://nas.local");
                created.IsSuccess.Should().BeTrue("公开分享创建应成功");
                shareCode = created.Value!.ShareCode;
            }

            // A 软删整棵子树：记录 IsDeleted=1、物理保留、Path 让出唯一索引
            using (var scope = provider.CreateScope())
            {
                var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
                await fileItems.SoftDeleteSubtreeAsync("/sroot", DateTime.UtcNow);
                await unitOfWork.SaveChangesAsync();
            }

            // B 用同一路径重建目录并放入同名文件（内容不同）
            var newRootId = await SeedFolderAsync(provider, "/sroot", poolId, otherId);
            await SeedFileAsync(provider, "/sroot/secret.txt", poolId, otherId, newRootId, "user B content");

            // Act：匿名持旧分享码下载子路径
            using (var scope = provider.CreateScope())
            {
                var shareService = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var download = await shareService.GetDownloadStreamAsync(
                    shareCode, accessToken: null, subPath: "secret.txt", isAuthenticated: false);

                // 断言：不得读到 B 的文件（修复前 IsSuccess 且流内容为 "user B content"）
                download.IsFailure.Should().BeTrue(
                    "路径被他人重建后，旧分享码不得下载到新属主的文件");
            }
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    // ─── 辅助 ───

    private static async Task<Guid> SeedUserAsync(IServiceProvider provider, string username)
    {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var (hash, salt) = hasher.HashPassword("password123");
        var user = User.Create(username, hash, salt);
        await users.AddAsync(user);
        await unitOfWork.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> SeedPoolAsync(IServiceProvider provider, string mountPath)
    {
        using var scope = provider.CreateScope();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
        var pool = StoragePool.Create("ShareOwnPool", mountPath);
        pool.SetAsDefault(true);
        await pools.AddAsync(pool);
        await unitOfWork.SaveChangesAsync();
        return pool.Id;
    }

    private static async Task<Guid> SeedFolderAsync(
        IServiceProvider provider, string path, Guid poolId, Guid ownerId, Guid? parentId = null)
    {
        using var scope = provider.CreateScope();
        var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
        var folder = FileItem.CreateFolder(
            name: Path.GetFileName(path),
            path: path,
            physicalPath: Path.Combine(FindMountPath(provider), path.TrimStart('/')),
            ownerId: ownerId,
            storagePoolId: poolId,
            parentId: parentId);
        await fileItems.AddAsync(folder);
        await unitOfWork.SaveChangesAsync();
        return folder.Id;
    }

    private static async Task<Guid> SeedFileAsync(
        IServiceProvider provider, string path, Guid poolId, Guid ownerId, Guid parentId, string content)
    {
        using var scope = provider.CreateScope();
        var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Common.IUnitOfWork>();
        var physicalPath = Path.Combine(FindMountPath(provider), path.TrimStart('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllTextAsync(physicalPath, content, Encoding.UTF8);
        var file = FileItem.CreateFile(
            name: Path.GetFileName(path),
            path: path,
            physicalPath: physicalPath,
            sizeBytes: Encoding.UTF8.GetByteCount(content),
            mimeType: "text/plain",
            storagePoolId: poolId,
            ownerId: ownerId,
            parentId: parentId,
            hash: "seed-hash");
        await fileItems.AddAsync(file);
        await unitOfWork.SaveChangesAsync();
        return file.Id;
    }

    private static string FindMountPath(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
        return pools.GetDefaultAsync().GetAwaiter().GetResult()!.MountPath;
    }
}
