// =============================================================================
// NasSystem.UnitTests - Services/SnapshotServiceBatchTests.cs
// 快照条目分批落库：峰值内存不随全树条目数线性堆叠
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Moq;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Infrastructure.Services;
using Xunit;

/// <summary>
/// 锁定快照创建的批次上界：12000 条必须拆成 3 次登记（5000/5000/2000），
/// 而不是一次性把全量条目塞给仓储。
/// </summary>
public sealed class SnapshotServiceBatchTests
{
    [Fact]
    public async Task CreateAsync_MoreThanBatchSize_WritesInBoundedBatches()
    {
        const int total = 12_000;
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, total)
            .Select(i => new FileItemSnapshotEntry(
                $"/root/file-{i}.txt", FileItemType.File, 1, "hash", now, now))
            .ToList();

        var snapshots = new Mock<ISnapshotRepository>();
        var fileItems = new Mock<IFileItemRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var batches = new List<List<SnapshotEntry>>();

        snapshots
            .Setup(s => s.AddAsync(It.IsAny<DirectorySnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        snapshots
            .Setup(s => s.AddEntriesAsync(
                It.IsAny<IReadOnlyList<SnapshotEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<SnapshotEntry>, CancellationToken>(
                (batch, _) => batches.Add(batch.ToList()))
            .Returns(Task.CompletedTask);
        fileItems
            .Setup(r => r.GetSnapshotEntriesAsync("/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = new SnapshotService(snapshots.Object, fileItems.Object, unitOfWork.Object);

        var result = await sut.CreateAsync("/", "batch", Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.FileCount.Should().Be(total);
        batches.Should().HaveCount(3);
        batches.Select(b => b.Count).Should().Equal(5_000, 5_000, 2_000);
        batches.SelectMany(b => b).Should().HaveCount(total);
    }
}
