// =============================================================================
// NasSystem.UnitTests - StorageServiceTests.cs
// 存储服务单元测试（简化版）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Moq;
using NasSystem.Application.Storage;
using NasSystem.Application.Storage.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using Xunit;

/// <summary>
/// StorageService 单元测试（简化版）
/// </summary>
public class StorageServiceTests
{
    private readonly Mock<IStoragePoolRepository> _poolRepositoryMock;
    private readonly Mock<IFileItemRepository> _fileItemRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly StorageService _sut;

    public StorageServiceTests()
    {
        _poolRepositoryMock = new Mock<IStoragePoolRepository>();
        _fileItemRepositoryMock = new Mock<IFileItemRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();

        _sut = new StorageService(
            _poolRepositoryMock.Object,
            _fileItemRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _fileSystemServiceMock.Object,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
    }

    #region GetOverviewAsync Tests

    [Fact]
    public async Task GetOverviewAsync_ReturnsStorageOverview()
    {
        // Arrange
        var pools = new List<StoragePool>
        {
            CreateTestPool("Pool1", "/mnt/pool1"),
            CreateTestPool("Pool2", "/mnt/pool2")
        };

        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pools);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.GetOverviewAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PoolCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_WhenNoPoolsExist_ReturnsZeroValues()
    {
        // Arrange
        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoragePool>());

        // Act
        var result = await _sut.GetOverviewAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PoolCount.Should().Be(0);
        result.Value.FolderSizeBytes.Should().Be(0);
        result.Value.UsedBytes.Should().Be(0);
        result.Value.UsagePercentage.Should().Be(0);
        result.Value.HasCapacityLimit.Should().BeFalse();
    }

    [Fact]
    public async Task GetOverviewAsync_PoolAccounting_SumsQuotaPoolsOnly()
    {
        // 锁定池账目口径：已分配/剩余只累计设了上限的启用池，与物理磁盘剩余
        // 严格分开（Dashboard 的存储池卡与物理磁盘卡分开展示的数据来源）
        var pool1 = StoragePool.Create("Pool1", "/mnt/pool1", null, maxCapacityBytes: 1000);
        var pool2 = StoragePool.Create("Pool2", "/mnt/pool2", null, maxCapacityBytes: 2000);

        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoragePool> { pool1, pool2 });

        _fileItemRepositoryMock
            .Setup(x => x.CalculateStoragePoolUsedSpaceAsync(pool1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(400L);
        _fileItemRepositoryMock
            .Setup(x => x.CalculateStoragePoolUsedSpaceAsync(pool2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(600L);

        var result = await _sut.GetOverviewAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.PoolAllocatedBytes.Should().Be(3000L);
        result.Value.PoolRemainingBytes.Should().Be(2000L);
        result.Value.HasCapacityLimit.Should().BeTrue();
    }

    [Fact]
    public async Task GetOverviewAsync_WhenNoPoolHasQuota_PoolAccountingIsZero()
    {
        // 无配额的池没有"分配"概念：已分配与剩余都为 0，展示层据此显示未设上限
        var pool = StoragePool.Create("Pool1", "/mnt/pool1");

        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoragePool> { pool });

        _fileItemRepositoryMock
            .Setup(x => x.CalculateStoragePoolUsedSpaceAsync(pool.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(500L);

        var result = await _sut.GetOverviewAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.PoolAllocatedBytes.Should().Be(0L);
        result.Value.PoolRemainingBytes.Should().Be(0L);
        result.Value.HasCapacityLimit.Should().BeFalse();
    }

    #endregion

    #region GetPoolsAsync Tests

    [Fact]
    public async Task GetPoolsAsync_ReturnsAllPools()
    {
        // Arrange
        var pools = new List<StoragePool>
        {
            CreateTestPool("Pool1", "/mnt/pool1"),
            CreateTestPool("Pool2", "/mnt/pool2")
        };

        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pools);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.GetPoolsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPoolsAsync_WhenNoPools_ReturnsEmptyList()
    {
        // Arrange
        _poolRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StoragePool>());

        // Act
        var result = await _sut.GetPoolsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region GetPoolByIdAsync Tests

    [Fact]
    public async Task GetPoolByIdAsync_WithExistingPool_ReturnsPool()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("TestPool", "/mnt/test");
        SetEntityId(pool, poolId);

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.GetPoolByIdAsync(poolId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("TestPool");
        result.Value.MountPath.Should().Be("/mnt/test");
    }

    [Fact]
    public async Task GetPoolByIdAsync_WithNonExistingPool_ReturnsNotFoundError()
    {
        // Arrange
        var poolId = Guid.NewGuid();

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoragePool?)null);

        // Act
        var result = await _sut.GetPoolByIdAsync(poolId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region CreatePoolAsync Tests

    [Fact]
    public async Task CreatePoolAsync_WithValidRequest_CreatesPool()
    {
        // Arrange
        var request = new CreateStoragePoolRequest
        {
            Name = "NewPool",
            MountPath = "/mnt/newpool",
            Description = "Test pool"
        };

        _poolRepositoryMock
            .Setup(x => x.NameExistsAsync("NewPool", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _poolRepositoryMock
            .Setup(x => x.MountPathExistsAsync("/mnt/newpool", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists("/mnt/newpool"))
            .Returns(false);

        // Act
        var result = await _sut.CreatePoolAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("NewPool");

        _fileSystemServiceMock.Verify(x => x.CreateDirectory("/mnt/newpool"), Times.Once);
        _poolRepositoryMock.Verify(x => x.AddAsync(It.IsAny<StoragePool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePoolAsync_WithExistingName_ReturnsConflictError()
    {
        // Arrange
        var request = new CreateStoragePoolRequest
        {
            Name = "ExistingPool",
            MountPath = "/mnt/existing"
        };

        _poolRepositoryMock
            .Setup(x => x.NameExistsAsync("ExistingPool", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreatePoolAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("ExistingPool");

        _poolRepositoryMock.Verify(x => x.AddAsync(It.IsAny<StoragePool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePoolAsync_WithExistingDirectory_DoesNotCreateDirectory()
    {
        // Arrange
        var request = new CreateStoragePoolRequest
        {
            Name = "NewPool",
            MountPath = "/mnt/existing"
        };

        _poolRepositoryMock
            .Setup(x => x.NameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _poolRepositoryMock
            .Setup(x => x.MountPathExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists("/mnt/existing"))
            .Returns(true);

        // Act
        var result = await _sut.CreatePoolAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.CreateDirectory(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region UpdatePoolAsync Tests

    [Fact]
    public async Task UpdatePoolAsync_WithValidRequest_UpdatesPool()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("OldPool", "/mnt/pool");
        SetEntityId(pool, poolId);

        var request = new UpdateStoragePoolRequest
        {
            Name = "NewName",
            Description = "Updated description"
        };

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.UpdatePoolAsync(poolId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("NewName");
    }

    [Fact]
    public async Task UpdatePoolAsync_WithNonExistingPool_ReturnsNotFoundError()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var request = new UpdateStoragePoolRequest { IsEnabled = false };

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoragePool?)null);

        // Act
        var result = await _sut.UpdatePoolAsync(poolId, request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task UpdatePoolAsync_WithDisableRequest_DisablesPool()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("TestPool", "/mnt/pool");
        SetEntityId(pool, poolId);

        var request = new UpdateStoragePoolRequest { IsEnabled = false };

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.UpdatePoolAsync(poolId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region DeletePoolAsync Tests

    [Fact]
    public async Task DeletePoolAsync_WithExistingPool_DeletesPool()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("TestPool", "/mnt/pool");
        SetEntityId(pool, poolId);

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        // Act
        var result = await _sut.DeletePoolAsync(poolId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _poolRepositoryMock.Verify(x => x.DeleteAsync(pool, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePoolAsync_WithNonExistingPool_ReturnsNotFoundError()
    {
        // Arrange
        var poolId = Guid.NewGuid();

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoragePool?)null);

        // Act
        var result = await _sut.DeletePoolAsync(poolId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task DeletePoolAsync_WithDefaultPool_ReturnsError()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("DefaultPool", "/mnt/pool");
        SetEntityId(pool, poolId);
        pool.SetAsDefault(true);

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        // Act
        var result = await _sut.DeletePoolAsync(poolId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("CannotDeleteDefault");
    }

    #endregion

    #region SetDefaultPoolAsync Tests

    [Fact]
    public async Task SetDefaultPoolAsync_WithExistingPool_SetsAsDefault()
    {
        // Arrange
        var poolId = Guid.NewGuid();
        var pool = CreateTestPool("TestPool", "/mnt/pool");
        SetEntityId(pool, poolId);

        var currentDefault = CreateTestPool("OldDefault", "/mnt/olddefault");
        currentDefault.SetAsDefault(true);

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        _poolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentDefault);

        // Act
        var result = await _sut.SetDefaultPoolAsync(poolId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _poolRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<StoragePool>(), It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }

    [Fact]
    public async Task SetDefaultPoolAsync_WithNonExistingPool_ReturnsNotFoundError()
    {
        // Arrange
        var poolId = Guid.NewGuid();

        _poolRepositoryMock
            .Setup(x => x.GetByIdAsync(poolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoragePool?)null);

        // Act
        var result = await _sut.SetDefaultPoolAsync(poolId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region Helper Methods

    private static StoragePool CreateTestPool(string name, string mountPath)
    {
        return StoragePool.Create(name, mountPath);
    }

    private static void SetEntityId<T>(T entity, Guid id) where T : Entity
    {
        var idProperty = typeof(Entity).GetProperty("Id");
        idProperty?.SetValue(entity, id);
    }

    #endregion
}
