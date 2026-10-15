// =============================================================================
// NasSystem.UnitTests - StoragePoolTests.cs
// 存储池实体单元测试（简化版）
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Events;
using Xunit;

/// <summary>
/// StoragePool 实体单元测试（简化版）
/// </summary>
public class StoragePoolTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidParameters_CreatesStoragePool()
    {
        // Act
        var pool = StoragePool.Create(
            name: "MainPool",
            mountPath: "/mnt/storage",
            description: "Main storage pool");

        // Assert
        pool.Should().NotBeNull();
        pool.Name.Should().Be("MainPool");
        pool.MountPath.Should().Be("/mnt/storage");
        pool.Description.Should().Be("Main storage pool");
        pool.IsEnabled.Should().BeTrue();
        pool.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Create_AddsStoragePoolCreatedEvent()
    {
        // Act
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Assert
        pool.DomainEvents.Should().ContainSingle();
        pool.DomainEvents.First().Should().BeOfType<StoragePoolCreatedEvent>();
    }

    [Fact]
    public void Create_WithMaxCapacity_SetsMaxCapacityBytes()
    {
        // Act
        var pool = StoragePool.Create(
            name: "LimitedPool",
            mountPath: "/mnt/limited",
            maxCapacityBytes: 1024L * 1024 * 1024 * 100); // 100GB

        // Assert
        pool.MaxCapacityBytes.Should().Be(1024L * 1024 * 1024 * 100);
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_ChangesNameAndDescription()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.Update("UpdatedPool", "Updated description");

        // Assert
        pool.Name.Should().Be("UpdatedPool");
        pool.Description.Should().Be("Updated description");
    }

    [Fact]
    public void Update_WithMaxCapacity_SetsMaxCapacityBytes()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.Update("TestPool", null, 1024L * 1024 * 1024 * 50); // 50GB

        // Assert
        pool.MaxCapacityBytes.Should().Be(1024L * 1024 * 1024 * 50);
    }

    #endregion

    #region UpdateUsage Tests

    [Fact]
    public void UpdateUsage_SetsFolderSizeAndUsedBytes()
    {
        // 池即文件夹：FolderSizeBytes 是文件夹实测大小，UsedBytes 是 NAS 文件记账，
        // 两者来源不同（前者扫目录，后者查数据库）
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        pool.UpdateUsage(
            folderSizeBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 50);       // 50GB

        pool.FolderSizeBytes.Should().Be(1024L * 1024 * 1024 * 100);
        pool.UsedBytes.Should().Be(1024L * 1024 * 1024 * 50);
    }

    #endregion

    #region SetAsDefault Tests

    [Fact]
    public void SetAsDefault_SetsIsDefault()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.SetAsDefault(true);

        // Assert
        pool.IsDefault.Should().BeTrue();
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public void SetEnabled_ToFalse_DisablesPool()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.SetEnabled(false);

        // Assert
        pool.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region Capacity Calculations Tests

    [Fact]
    public void QuotaRemainingBytes_WithoutQuota_ReturnsNull()
    {
        // 没设容量上限的池没有账目上限，是"无此概念"而不是"无限大"
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 1024L * 1024 * 1024 * 100, usedBytes: 0);

        pool.QuotaRemainingBytes.Should().BeNull();
    }

    [Fact]
    public void QuotaRemainingBytes_WithQuota_SubtractsUsed()
    {
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 10);

        pool.QuotaRemainingBytes.Should().Be(1024L * 1024 * 1024 * 40);
    }

    [Fact]
    public void QuotaRemainingBytes_WhenOverQuota_ClampsToZero()
    {
        // 已用超过上限（历史数据或手工改库）时剩余不能为负
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 60);

        pool.QuotaRemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AvailableBytesWithin_PhysicalFreeBelowQuota_ReturnsPhysicalFree()
    {
        // 配额说还能写 40GB，但盘上只剩 20GB：可用必须是 20GB。
        // 曾按"盘总 − NAS记账"算可用，把盘上其他内容算成可用，虚高约 6 倍
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 10);

        pool.AvailableBytesWithin(1024L * 1024 * 1024 * 20)
            .Should().Be(1024L * 1024 * 1024 * 20);
    }

    [Fact]
    public void AvailableBytesWithin_PhysicalFreeAboveQuota_ReturnsQuotaRemaining()
    {
        // 配额是更紧的约束：配额 50GB 用掉 10GB，盘上还很空，
        // 可用应是配额的 40GB 而不是盘的一大片剩余
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 10);

        pool.AvailableBytesWithin(1024L * 1024 * 1024 * 500)
            .Should().Be(1024L * 1024 * 1024 * 40);
    }

    [Fact]
    public void AvailableBytesWithin_NoQuota_ReturnsPhysicalFree()
    {
        // 无配额池的增长空间只受所在盘约束
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 1024L * 1024 * 1024, usedBytes: 0);

        pool.AvailableBytesWithin(1024L * 1024 * 1024 * 142)
            .Should().Be(1024L * 1024 * 1024 * 142);
    }

    [Fact]
    public void AvailableBytesWithin_NullPhysicalFree_WithQuota_ReturnsQuotaRemaining()
    {
        // 挂载点不可达拿不到物理剩余，有配额时退回配额剩余
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 10);

        pool.AvailableBytesWithin(null).Should().Be(1024L * 1024 * 1024 * 40);
    }

    [Fact]
    public void AvailableBytesWithin_NullPhysicalFree_NoQuota_ReturnsNull()
    {
        // 无配额又拿不到物理剩余时无从判断，返回 null 而不是编一个大数字
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 0);

        pool.AvailableBytesWithin(null).Should().BeNull();
    }

    [Fact]
    public void AvailableBytesWithin_ZeroPhysicalFree_ReturnsZero()
    {
        // 盘满了时可用必须是 0，不能是记账的大数字
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 0);

        pool.AvailableBytesWithin(0).Should().Be(0);
    }

    [Fact]
    public void HasEnoughSpace_WhenUnknownAndNoQuota_ReturnsFalse()
    {
        // 放不下的判定不能猜：物理剩余不可知且无配额时返回 false，
        // 由健康状态的 Warning 暴露挂载点不可达
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 0);

        pool.HasEnoughSpace(1024, null).Should().BeFalse();
    }

    [Fact]
    public void HasEnoughSpace_WhenQuotaSufficient_ReturnsTrue()
    {
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 10);

        pool.HasEnoughSpace(1024L * 1024 * 1024 * 10, null).Should().BeTrue();
    }

    [Fact]
    public void HasEnoughSpace_WhenQuotaInsufficient_ReturnsFalse()
    {
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 45);

        pool.HasEnoughSpace(1024L * 1024 * 1024 * 10, null).Should().BeFalse();
    }

    [Fact]
    public void UsagePercentage_WithoutQuota_ReturnsZero()
    {
        // 未设上限的池没有容量概念，使用率返回 0，展示层据此不渲染百分比
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 1024L * 1024 * 1024 * 100, usedBytes: 1024L * 1024 * 1024 * 50);

        pool.UsagePercentage.Should().Be(0);
    }

    [Fact]
    public void UsagePercentage_WithQuota_CalculatesAgainstQuota()
    {
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 100);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 50);

        pool.UsagePercentage.Should().Be(50);
    }

    [Fact]
    public void IsNearlyFull_WithoutQuota_ReturnsFalse()
    {
        // 无配额池不存在"即将满"：文件夹本身没有容量上限
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateUsage(folderSizeBytes: 1024L * 1024 * 1024 * 100, usedBytes: 1024L * 1024 * 1024 * 99);

        pool.IsNearlyFull.Should().BeFalse();
    }

    [Fact]
    public void IsNearlyFull_WithQuotaOver90Percent_ReturnsTrue()
    {
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 100);
        pool.UpdateUsage(folderSizeBytes: 0, usedBytes: 1024L * 1024 * 1024 * 95);

        pool.IsNearlyFull.Should().BeTrue();
    }

    #endregion
}
