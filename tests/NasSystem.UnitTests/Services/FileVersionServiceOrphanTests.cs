// =============================================================================
// NasSystem.UnitTests - Services/FileVersionServiceOrphanTests.cs
// 版本号冲突重试路径的孤儿快照清理
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NasSystem.Application.Configuration;
using NasSystem.Application.FileVersions;
using NasSystem.Application.Notifications;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using Xunit;

/// <summary>
/// 锁定"先建物理快照、后落库"的补偿语义：并发版本号冲突时第一次尝试的
/// 快照没有记录引用，重试前必须删掉；第二次成功落库的快照不能被误删。
/// </summary>
public sealed class FileVersionServiceOrphanTests
{
    [Fact]
    public async Task CreateAutoVersionAsync_VersionNumberConflict_DeletesSupersededSnapshot()
    {
        var versionRepository = new Mock<IFileVersionRepository>();
        versionRepository
            .Setup(r => r.GetByFileItemIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FileVersion>)Array.Empty<FileVersion>());

        var fileRepository = new Mock<IFileItemRepository>();

        var saveCalls = 0;
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                if (Interlocked.Increment(ref saveCalls) == 1)
                    throw new UniqueConstraintException(
                        "IX_FileVersions_FileItemId_VersionNumber", "版本号并发冲突");
                await Task.CompletedTask;
                return 1;
            });

        var createdSnapshots = new List<string>();
        var deletedFiles = new List<string>();
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
        fileSystem
            .Setup(f => f.CreateHardLink(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((linkPath, _) => createdSnapshots.Add(linkPath));
        fileSystem
            .Setup(f => f.DeleteFile(It.IsAny<string>()))
            .Callback<string>(deletedFiles.Add);

        var versionStore = new FileVersionStore(fileSystem.Object);

        var first = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var dateTime = new Mock<IDateTimeProvider>();
        dateTime.SetupSequence(d => d.UtcNow).Returns(first).Returns(first.AddSeconds(1));

        var config = new Mock<ISystemConfigService>();
        config.Setup(c => c.GetIntAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var service = new FileVersionService(
            versionRepository.Object,
            fileRepository.Object,
            unitOfWork.Object,
            NullLogger<FileVersionService>.Instance,
            fileSystem.Object,
            versionStore,
            dateTime.Object,
            config.Object,
            new Mock<IHubNotificationService>().Object);

        var item = FileItem.CreateFile(
            name: "report.pdf", path: "/report.pdf", physicalPath: "/mnt/pool/report.pdf",
            sizeBytes: 100, mimeType: "application/pdf",
            storagePoolId: Guid.NewGuid(), ownerId: Guid.NewGuid(), hash: "hash-report");

        await service.CreateAutoVersionAsync(item, "/mnt/pool", Guid.NewGuid());

        createdSnapshots.Should().HaveCount(2, "唯一约束冲突后应换名重试一次");
        deletedFiles.Should().Contain(createdSnapshots[0],
            "第一次尝试的快照没有记录引用，重试前必须删掉，否则成为永久孤儿");
        deletedFiles.Should().NotContain(createdSnapshots[1],
            "第二次尝试的快照已落库，不能被当孤儿删掉");
    }
}