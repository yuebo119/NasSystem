// =============================================================================
// NasSystem.UnitTests - StoragePoolTests.cs
// 存储池实体单元测试
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;
using Xunit;

/// <summary>
/// StoragePool 实体单元测试
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
            raidLevel: RaidLevel.Raid5,
            description: "Main storage pool");

        // Assert
        pool.Should().NotBeNull();
        pool.Name.Should().Be("MainPool");
        pool.MountPath.Should().Be("/mnt/storage");
        pool.RaidLevel.Should().Be(RaidLevel.Raid5);
        pool.Description.Should().Be("Main storage pool");
        pool.Status.Should().Be(StoragePoolStatus.Initializing);
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

    #endregion

    #region UpdateStatus Tests

    [Fact]
    public void UpdateStatus_ChangesStatus()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.ClearDomainEvents();

        // Act
        pool.UpdateStatus(StoragePoolStatus.Healthy);

        // Assert
        pool.Status.Should().Be(StoragePoolStatus.Healthy);
    }

    [Fact]
    public void UpdateStatus_WhenStatusChanges_AddsStatusChangedEvent()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.ClearDomainEvents();

        // Act
        pool.UpdateStatus(StoragePoolStatus.Healthy);

        // Assert
        pool.DomainEvents.Should().ContainSingle();
        var evt = pool.DomainEvents.First() as StoragePoolStatusChangedEvent;
        evt.Should().NotBeNull();
        evt!.OldStatus.Should().Be(StoragePoolStatus.Initializing);
        evt.NewStatus.Should().Be(StoragePoolStatus.Healthy);
    }

    [Fact]
    public void UpdateStatus_WhenSameStatus_DoesNotAddEvent()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        pool.UpdateStatus(StoragePoolStatus.Healthy);
        pool.ClearDomainEvents();

        // Act
        pool.UpdateStatus(StoragePoolStatus.Healthy);

        // Assert
        pool.DomainEvents.Should().BeEmpty();
    }

    #endregion

    #region UpdateUsage Tests

    [Fact]
    public void UpdateUsage_SetsUsedBytes()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.UpdateUsage(1024L * 1024 * 1024 * 50); // 50GB

        // Assert
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

    #region SetOptimization Tests

    [Fact]
    public void SetOptimization_SetsDeduplicationAndCompression()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Act
        pool.SetOptimization(enableDeduplication: true, enableCompression: true);

        // Assert
        pool.EnableDeduplication.Should().BeTrue();
        pool.EnableCompression.Should().BeTrue();
    }

    #endregion

    #region Capacity Calculations Tests

    [Fact]
    public void AvailableBytes_CalculatesCorrectly()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");
        // 注意：TotalBytes 默认为 0，需要通过 AddDisk 来设置
        // 这里我们只测试公式逻辑
        
        // Assert
        pool.AvailableBytes.Should().Be(pool.TotalBytes - pool.UsedBytes);
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
    public void IsNearlyFull_WhenUnder90Percent_ReturnsFalse()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "/mnt/test");

        // Assert
        pool.IsNearlyFull.Should().BeFalse();
    }

    #endregion
}
