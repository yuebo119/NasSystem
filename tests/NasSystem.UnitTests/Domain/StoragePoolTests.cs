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

    #region UpdateCapacity Tests

    [Fact]
    public void UpdateCapacity_SetsTotalAndUsedBytes()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 50);  // 50GB

        // Assert
        pool.TotalBytes.Should().Be(1024L * 1024 * 1024 * 100);
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
    public void AvailableBytes_CalculatesCorrectly()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 30);  // 30GB

        // Assert
        pool.AvailableBytes.Should().Be(1024L * 1024 * 1024 * 70); // 70GB
    }

    [Fact]
    public void EffectiveTotalBytes_WithMaxCapacity_ReturnsMaxCapacity()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50); // 50GB limit
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB actual
            usedBytes: 0);

        // Assert
        pool.EffectiveTotalBytes.Should().Be(1024L * 1024 * 1024 * 50); // Should be limited to 50GB
    }

    [Fact]
    public void UsagePercentage_WhenEmpty_ReturnsZero()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Assert
        pool.UsagePercentage.Should().Be(0);
    }

    [Fact]
    public void UsagePercentage_CalculatesCorrectly()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 50);  // 50GB

        // Assert
        pool.UsagePercentage.Should().Be(50);
    }

    [Fact]
    public void IsNearlyFull_WhenUnder90Percent_ReturnsFalse()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 50);  // 50GB

        // Assert
        pool.IsNearlyFull.Should().BeFalse();
    }

    [Fact]
    public void IsNearlyFull_WhenOver90Percent_ReturnsTrue()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 95);  // 95GB

        // Assert
        pool.IsNearlyFull.Should().BeTrue();
    }

    [Fact]
    public void HasEnoughSpace_WhenSufficientSpace_ReturnsTrue()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 50);  // 50GB

        // Assert
        pool.HasEnoughSpace(1024L * 1024 * 1024 * 10).Should().BeTrue(); // 10GB file
    }

    [Fact]
    public void HasEnoughSpace_WhenInsufficientSpace_ReturnsFalse()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 95);  // 95GB

        // Assert
        pool.HasEnoughSpace(1024L * 1024 * 1024 * 10).Should().BeFalse(); // 10GB file won't fit
    }

    [Fact]
    public void AvailableBytesWithin_PhysicalFreeBelowAccounting_ReturnsPhysicalFree()
    {
        // 池只是盘上的目录：记账说还有 70GB，但盘上其他内容占着，
        // 物理剩余只有 20GB 时，可用必须是 20GB。曾用记账值直接展示，
        // 首页"可用"因此虚高（实测虚高约 6 倍）
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100, // 100GB
            usedBytes: 1024L * 1024 * 1024 * 30);  // 30GB

        pool.AvailableBytesWithin(1024L * 1024 * 1024 * 20)
            .Should().Be(1024L * 1024 * 1024 * 20);
    }

    [Fact]
    public void AvailableBytesWithin_PhysicalFreeAboveAccounting_ReturnsAccounting()
    {
        // 有配额时记账是更紧的约束：配额 50GB 用掉 10GB，盘上还很空，
        // 可用应是配额的 40GB 而不是盘的一大片剩余
        var pool = StoragePool.Create("TestPool", "/mnt/test", maxCapacityBytes: 1024L * 1024 * 1024 * 50);
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100,
            usedBytes: 1024L * 1024 * 1024 * 10);

        pool.AvailableBytesWithin(1024L * 1024 * 1024 * 500)
            .Should().Be(1024L * 1024 * 1024 * 40);
    }

    [Fact]
    public void AvailableBytesWithin_NullPhysicalFree_FallsBackToAccounting()
    {
        // 挂载点不可达时拿不到物理剩余，退回记账口径；
        // 可达性由健康状态另行暴露，不在这里猜
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100,
            usedBytes: 1024L * 1024 * 1024 * 30);

        pool.AvailableBytesWithin(null).Should().Be(1024L * 1024 * 1024 * 70);
    }

    [Fact]
    public void AvailableBytesWithin_ZeroPhysicalFree_ReturnsZero()
    {
        // 盘满了时可用必须是 0，不能是记账的大数字
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateCapacity(
            totalBytes: 1024L * 1024 * 1024 * 100,
            usedBytes: 1024L * 1024 * 1024 * 30);

        pool.AvailableBytesWithin(0).Should().Be(0);
    }

    #endregion
}
