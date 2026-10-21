// =============================================================================
// NasSystem.IntegrationTests - FileShareCodeRaceTests.cs
// 分享码唯一冲突重试的落盘验证（2026-10-21 全量审计 P1 回归）
// =============================================================================

namespace NasSystem.IntegrationTests;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application;
using NasSystem.Application.Common;
using NasSystem.Application.FileShares;
using NasSystem.Application.FileShares.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure;
using NasSystem.Infrastructure.Data.Repositories;
using NasSystem.Infrastructure.Persistence;
using PalORM;
using PalORM.Sqlite;
using Xunit;

/// <summary>
/// 锁 2026-10-21 审计发现的 P1：分享码唯一冲突的重试路径曾经只发
/// UpdateAsync，而 SaveChangesAsync 失败时事务已回滚、意图队列已清空，
/// UPDATE 打的是从未插入的行（0 行影响却按命令数计 1），方法返回成功、
/// API 报创建成功，但库里零记录。
///
/// 真实竞态窗口（ShareCodeExistsAsync 预检与提交之间）无法稳定复现，
/// 用装饰仓储在 AddAsync 首次触发时以原始 SQL 抢注同一分享码来等效制造：
/// 预检通过 → 提交时撞唯一索引 → 走重试分支。断言落盘结果而非中间过程。
/// </summary>
public class FileShareCodeRaceTests
{
    [Fact]
    public async Task CreateShare_WhenCodeSquattedByConcurrentInsert_PersistsRetriedShare()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ShareRace_{Guid.NewGuid():N}.db");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={dbPath}",
                // JwtSettings 是 FileShareService 的构造依赖（访问令牌签名复用主密钥）
                ["JwtSettings:SecretKey"] = "integration-test-secret-key-with-adequate-length-32",
                ["JwtSettings:Issuer"] = "NasSystem",
                ["JwtSettings:Audience"] = "NasSystemUsers",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddInfrastructureServices(config);
        services.AddApplicationServices();

        // 装饰 IFileShareRepository：最后一次注册赢解析。具体类型另行注册，
        // 装饰器与内部实现从同一作用域解析，共享同一个 PalormUnitOfWork
        services.AddScoped<FileShareRepository>();
        services.AddScoped<IFileShareRepository>(sp => new CodeSquattingRepository(
            sp.GetRequiredService<FileShareRepository>(),
            sp.GetRequiredService<PalormSessionHolder>()));

        var provider = services.BuildServiceProvider();
        try
        {
            provider.InitializeDatabaseAsync().GetAwaiter().GetResult();

            Guid userId;
            Guid fileItemId;
            using (var scope = provider.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var pools = scope.ServiceProvider.GetRequiredService<IStoragePoolRepository>();
                var fileItems = scope.ServiceProvider.GetRequiredService<IFileItemRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var passwordHasher = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IPasswordHasher>();

                var (hash, salt) = passwordHasher.HashPassword("password123");
                var user = User.Create("sharer", hash, salt);
                await users.AddAsync(user);

                var pool = StoragePool.Create("SharePool", "/mnt/sharepool");
                await pools.AddAsync(pool);

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
                userId = user.Id;
                fileItemId = file.Id;
            }

            using var actScope = provider.CreateScope();
            var shareService = actScope.ServiceProvider.GetRequiredService<IFileShareService>();
            var result = await shareService.CreateAsync(
                new CreateFileShareRequest { FileItemId = fileItemId },
                userId,
                "https://nas.local");

            result.IsSuccess.Should().BeTrue("抢注只制造唯一冲突，重试后应当成功");

            // 核心断言：库里有这条分享，且码就是返回的码。
            // 修复前重试发打空气的 UPDATE，这里查出来是 null
            var shares = actScope.ServiceProvider.GetRequiredService<IFileShareRepository>();
            var persisted = await shares.GetByShareCodeAsync(result.Value.ShareCode);
            persisted.Should().NotBeNull(
                $"重试后必须重新登记插入并落盘；码 {result.Value.ShareCode} 在库里不存在即为假成功");

            // 抢占行（并发赢家）也应在库里：证明冲突真的发生过，不是预检顺手放过
            persisted!.FileItemId.Should().Be(fileItemId);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); }
                catch { /* 句柄未释放时留下，由系统临时目录清理 */ }
            }
        }
    }

    /// <summary>
    /// 抢注型装饰仓储：首次 AddAsync 时先用原始 SQL 把同一分享码写进去
    /// （模拟并发赢家），之后放行。只抢一次：重试分支再触发就永远成功不了
    /// </summary>
    private sealed class CodeSquattingRepository(
        IFileShareRepository inner,
        PalormSessionHolder sessions) : IFileShareRepository
    {
        private bool _squatted;

        public async Task<FileShare> AddAsync(FileShare share, CancellationToken cancellationToken = default)
        {
            if (!_squatted)
            {
                _squatted = true;
                var db = await sessions.GetAsync(cancellationToken);
                // 独立提交（autocommit）：并发赢家不会随失败方的事务一起回滚
                await db.ExecuteAsync(
                    System.Runtime.CompilerServices.FormattableStringFactory.Create(
                        """
                        INSERT INTO FileShares
                            (Id, ShareCode, FileItemId, AccessType, Permission,
                             DownloadCount, CreatedByUserId, IsEnabled, CreatedAt)
                        VALUES ({0}, {1}, {2}, 0, 0, 0, {3}, 1, {4})
                        """,
                        Guid.NewGuid(), share.ShareCode, share.FileItemId,
                        share.CreatedByUserId, DateTime.UtcNow),
                    cancellationToken);
            }

            return await inner.AddAsync(share, cancellationToken);
        }

        public ValueTask<FileShare?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => inner.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<FileShare>> GetAllAsync(CancellationToken cancellationToken = default)
            => inner.GetAllAsync(cancellationToken);

        public Task UpdateAsync(FileShare entity, CancellationToken cancellationToken = default)
            => inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(FileShare entity, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(entity, cancellationToken);

        public Task<FileShare?> GetByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default)
            => inner.GetByShareCodeAsync(shareCode, cancellationToken);

        public Task<IReadOnlyList<FileShare>> GetByCreatorAsync(Guid userId, CancellationToken cancellationToken = default)
            => inner.GetByCreatorAsync(userId, cancellationToken);

        public Task<bool> ShareCodeExistsAsync(string shareCode, CancellationToken cancellationToken = default)
            => inner.ShareCodeExistsAsync(shareCode, cancellationToken);
    }
}
