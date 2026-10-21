// =============================================================================
// NasSystem.IntegrationTests - VersionAtomicityRegressionTests.cs
// 审计 B3 批（版本域原子性）锁定测试：恢复前置校验/删版本保记录/并发版本号
// =============================================================================

namespace NasSystem.IntegrationTests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NasSystem.Application;
using NasSystem.Application.Common;
using NasSystem.Application.FileVersions;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>推送替身：服务级测试不起 SignalR 宿主，通知走 no-op</summary>
internal sealed class NoopNotifier : NasSystem.Application.Notifications.IHubNotificationService
{
    public Task NotifyFileChangedAsync(string path, NasSystem.Application.Notifications.FileChangeType changeType, object? data = null)
        => Task.CompletedTask;
}

/// <summary>
/// 锁 2026-10-22 审计 B3 批修复：
/// F-008 删版本先删盘后删库：库提交失败留僵尸记录（记录在、文件没了）
/// F-010 版本号先读后写竞态：并发覆盖只落一个版本，旧内容无可回
/// F-016 恢复可写回已消失/软删的 FileItem，记录消失时仍返回成功
/// </summary>
public class VersionAtomicityRegressionTests
{
    private static async Task<(ServiceProvider Provider, Guid UserId, Guid FileItemId, string StorageRoot)>
        BuildWorldAsync(string dbPath, string storageRoot)
    {
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
        // FileVersionService 依赖 IHubNotificationService（SignalR 推送）。
        // 服务级测试不起宿主，注册 no-op 替身（与测试工厂的 Restart 替身同思路）
        services.AddScoped<NasSystem.Application.Notifications.IHubNotificationService, NoopNotifier>();

        var provider = services.BuildServiceProvider();
        provider.InitializeDatabaseAsync().GetAwaiter().GetResult();

        Guid userId;
        Guid fileItemId;
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
            var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var (hash, salt) = hasher.HashPassword("password123");
            var user = User.Create("veratomic", hash, salt);
            await users.AddAsync(user);

            var pool = StoragePool.Create("VerPool", storageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);

            var physicalPath = Path.Combine(storageRoot, "target.txt");
            await File.WriteAllTextAsync(physicalPath, "v1 content");
            var file = FileItem.CreateFile(
                name: "target.txt",
                path: "/target.txt",
                physicalPath: physicalPath,
                sizeBytes: 11,
                mimeType: "text/plain",
                storagePoolId: pool.Id,
                ownerId: user.Id,
                hash: "v1hash");
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            userId = user.Id;
            fileItemId = file.Id;
        }

        return (provider, userId, fileItemId, storageRoot);
    }

    [Fact]
    public async Task Restore_WhenFileItemGone_ReturnsFailureNotFakeSuccess()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"VerRes_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"VerResStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId, _) = await BuildWorldAsync(dbPath, storageRoot);
        try
        {
            // 造一个版本
            using (var scope = provider.CreateScope())
            {
                var versions = scope.ServiceProvider.GetRequiredService<IFileVersionService>();
                var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                var item = (await fileItems.GetByIdAsync(fileItemId))!;
                await versions.CreateAutoVersionAsync(item, storageRoot, userId);
            }

            // 硬删 FileItem 记录（模拟回收站已清空）：GetByIdAsync 返回 null
            using (var scope = provider.CreateScope())
            {
                var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var item = (await fileItems.GetByIdAsync(fileItemId))!;
                await fileItems.DeleteAsync(item);
                await unitOfWork.SaveChangesAsync();
            }

            // 恢复：修复前把版本标成 IsCurrent 后返回 Success（假成功）
            using (var scope = provider.CreateScope())
            {
                var versions = scope.ServiceProvider.GetRequiredService<IFileVersionService>();
                var restored = await versions.RestoreVersionAsync(fileItemId, 1, userId);

                restored.IsFailure.Should().BeTrue(
                    "文件记录已不存在时恢复必须失败：标完 IsCurrent 返回成功会让用户以为内容已落地");
            }
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task DeleteVersion_WhenSnapshotLocked_KeepsRecord()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"VerDel_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"VerDelStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId, _) = await BuildWorldAsync(dbPath, storageRoot);
        try
        {
            using (var scope = provider.CreateScope())
            {
                var versions = scope.ServiceProvider.GetRequiredService<IFileVersionService>();
                var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                var versionRepo = scope.ServiceProvider.GetRequiredService<IFileVersionRepository>();
                var item = (await fileItems.GetByIdAsync(fileItemId))!;
                await versions.CreateAutoVersionAsync(item, storageRoot, userId);

                var created = await versionRepo.GetByFileItemIdAsync(fileItemId);
                created.Count.Should().Be(1);
                var storagePath = created[0].StoragePath;

                // 占住版本文件（模拟文件被占用/IO 失败）：File.Delete 必然失败
                using var lockStream = new FileStream(
                    storagePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.None);

                var deleted = await versions.DeleteVersionAsync(fileItemId, 1, userId);

                // 库记录必须保留：删了记录文件还在 = 反向孤儿，永远扫不回来
                var after = await versionRepo.GetByFileItemIdAsync(fileItemId);
                after.Count.Should().Be(1,
                    "物理删除失败时不得删库记录（记录没了文件还在，.versions 被扫描排除，孤儿永存）");
                deleted.IsFailure.Should().BeTrue(
                    "删除失败必须明确报错（文件被占用），而不是假装删干净");
            }
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task CreateAutoVersion_Concurrent_BothVersionsLand()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"VerConc_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"VerConcStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId, _) = await BuildWorldAsync(dbPath, storageRoot);
        try
        {
            // 两路并发产版本（模拟两个覆盖上传同时命中同一文件）。
            // 每任务独立 scope：共享 scope 会被 DataSession 串行化，测不出竞态
            var tasks = Enumerable.Range(0, 2)
                .Select(_ => Task.Run(async () =>
                {
                    using var scope = provider.CreateScope();
                    var versions = scope.ServiceProvider.GetRequiredService<IFileVersionService>();
                    var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                    var item = (await fileItems.GetByIdAsync(fileItemId))!;
                    await versions.CreateAutoVersionAsync(item, storageRoot, userId);
                }))
                .ToArray();
            await Task.WhenAll(tasks);

            using (var scope = provider.CreateScope())
            {
                var versionRepo = scope.ServiceProvider.GetRequiredService<IFileVersionRepository>();
                var created = await versionRepo.GetByFileItemIdAsync(fileItemId);

                // 修复前：版本号先读后写撞唯一索引，其中一个被 catch 吞掉只留日志
                created.Count.Should().Be(2,
                    "并发覆盖的两份旧内容都必须有版本可回（重试一次后仍败才降级）");
            }
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }
}
