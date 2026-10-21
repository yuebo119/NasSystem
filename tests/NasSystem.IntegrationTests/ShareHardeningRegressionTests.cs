// =============================================================================
// NasSystem.IntegrationTests - ShareHardeningRegressionTests.cs
// 审计 B2 批（分享域加固）锁定测试：下载计数原子性/令牌绑口令/Update 不变量
// =============================================================================

namespace NasSystem.IntegrationTests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NasSystem.Application;
using NasSystem.Application.FileShares;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Infrastructure;
using Xunit;

/// <summary>
/// 锁 2026-10-22 审计 B2 批三条修复：
/// F-006 下载计数读改写竞态 + MaxDownloads TOCTOU（闸内串行化）
/// F-018 访问令牌不随口令轮换失效（payload 绑口令指纹）
/// F-019 Update 可把密码型分享改成永锁死（不变量覆盖写入点）
/// </summary>
public class ShareHardeningRegressionTests
{
    private static async Task<(ServiceProvider Provider, Guid UserId, Guid FileItemId)> BuildWorld(string dbPath, string storageRoot)
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
            var user = User.Create("sharehard", hash, salt);
            await users.AddAsync(user);

            var pool = StoragePool.Create("ShareHardPool", storageRoot);
            pool.SetAsDefault(true);
            await pools.AddAsync(pool);

            var physicalPath = Path.Combine(storageRoot, "shared.txt");
            await File.WriteAllTextAsync(physicalPath, "hardening probe");
            var file = FileItem.CreateFile(
                name: "shared.txt",
                path: "/shared.txt",
                physicalPath: physicalPath,
                sizeBytes: 16,
                mimeType: "text/plain",
                storagePoolId: pool.Id,
                ownerId: user.Id,
                hash: "seed");
            await fileItems.AddAsync(file);
            await unitOfWork.SaveChangesAsync();
            userId = user.Id;
            fileItemId = file.Id;
        }

        return (provider, userId, fileItemId);
    }

    [Fact]
    public async Task Download_ConcurrentMaxOne_OnlyOneSucceedsAndCountIsOne()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ShareHard_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"ShareHardStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId) = await BuildWorld(dbPath, storageRoot);
        try
        {
            string shareCode;
            using (var scope = provider.CreateScope())
            {
                var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var created = await shares.CreateAsync(
                    new CreateFileShareRequest { FileItemId = fileItemId, MaxDownloads = 1 },
                    userId,
                    "https://nas.local");
                created.IsSuccess.Should().BeTrue();
                shareCode = created.Value!.ShareCode;
            }

            // 20 路并发下载同一分享（MaxDownloads=1）：每任务独立 scope，
            // 模拟 20 个并发 HTTP 请求（共享 scope 会被 DataSession 串行化，
            // 测不出真实竞态）。修复前多个请求读到同一个旧计数，多数成功
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(async () =>
                {
                    using var scope = provider.CreateScope();
                    var shareService = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                    return await shareService.GetDownloadStreamAsync(shareCode, null, null);
                }))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            results.Count(r => r.IsSuccess).Should().Be(1,
                "MaxDownloads=1 的分享只允许一次成功下载，并发不得打穿");
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task DownloadToken_AfterPasswordRotation_OldTokenStopsWorking()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ShareTok_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"ShareTokStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId) = await BuildWorld(dbPath, storageRoot);
        try
        {
            string shareCode;
            string token;
            using (var scope = provider.CreateScope())
            {
                var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var created = await shares.CreateAsync(
                    new CreateFileShareRequest
                    {
                        FileItemId = fileItemId,
                        AccessType = ShareAccessType.Password,
                        Password = "oldpass123"
                    },
                    userId,
                    "https://nas.local");
                created.IsSuccess.Should().BeTrue();
                shareCode = created.Value!.ShareCode;

                var verified = await shares.VerifyPasswordAsync(shareCode, "oldpass123");
                verified.IsSuccess.Should().BeTrue();
                token = verified.Value!;
            }

            // 旧令牌先能用
            using (var scope = provider.CreateScope())
            {
                var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var before = await shares.GetDownloadStreamAsync(shareCode, token, null);
                before.IsSuccess.Should().BeTrue("轮换前旧令牌有效");
            }

            // Owner 改口令（止损动作）
            using (var scope = provider.CreateScope())
            {
                var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var mine = await shares.GetMySharesAsync(userId, "https://nas.local");
                var id = mine.Value!.First().Id;
                var updated = await shares.UpdateAsync(
                    id, new UpdateFileShareRequest { Password = "newpass123" }, userId, "https://nas.local");
                updated.IsSuccess.Should().BeTrue();
            }

            // 旧令牌必须失效
            using (var scope = provider.CreateScope())
            {
                var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
                var after = await shares.GetDownloadStreamAsync(shareCode, token, null);
                after.IsFailure.Should().BeTrue(
                    "改口令后旧访问令牌应立即失效（止损才有效）");
            }
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task Update_ToPasswordWithoutPassword_IsRejected()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ShareUpd_{Guid.NewGuid():N}.db");
        var storageRoot = Path.Combine(Path.GetTempPath(), $"ShareUpdStore_{Guid.NewGuid():N}");
        var (provider, userId, fileItemId) = await BuildWorld(dbPath, storageRoot);
        try
        {
            using var scope = provider.CreateScope();
            var shares = scope.ServiceProvider.GetRequiredService<IFileShareService>();
            var created = await shares.CreateAsync(
                new CreateFileShareRequest { FileItemId = fileItemId },
                userId,
                "https://nas.local");
            created.IsSuccess.Should().BeTrue();

            // 改成密码型但不给口令：修复前 200 + 分享对所有人永久 403（死分享）
            var updated = await shares.UpdateAsync(
                created.Value!.Id,
                new UpdateFileShareRequest { AccessType = ShareAccessType.Password },
                userId,
                "https://nas.local");

            updated.IsFailure.Should().BeTrue(
                "密码型分享必须带口令：空哈希会让 VerifyPassword 判 NotRequired，分享永锁");
        }
        finally
        {
            try { Directory.Delete(storageRoot, recursive: true); } catch { }
            try { File.Delete(dbPath); } catch { }
        }
    }
}
