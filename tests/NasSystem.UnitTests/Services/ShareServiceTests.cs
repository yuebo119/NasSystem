// =============================================================================
// NasSystem.UnitTests - ShareServiceTests.cs
// 共享服务单元测试
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Moq;
using NasSystem.Application.Shares;
using NasSystem.Application.Shares.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using Xunit;

/// <summary>
/// ShareService 单元测试
/// </summary>
public class ShareServiceTests
{
    private readonly Mock<IShareRepository> _shareRepositoryMock;
    private readonly Mock<ISharePermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IStoragePoolRepository> _storagePoolRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly ShareService _sut;

    private readonly Guid _testUserId = Guid.NewGuid();
    private readonly Guid _testPoolId = Guid.NewGuid();

    public ShareServiceTests()
    {
        _shareRepositoryMock = new Mock<IShareRepository>();
        _permissionRepositoryMock = new Mock<ISharePermissionRepository>();
        _storagePoolRepositoryMock = new Mock<IStoragePoolRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        _sut = new ShareService(
            _shareRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _storagePoolRepositoryMock.Object,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object);
    }

    #region GetSharesAsync Tests

    [Fact]
    public async Task GetSharesAsync_ReturnsAllShares()
    {
        // Arrange
        var shares = new List<Share>
        {
            CreateTestShare("Share1", "/shares/share1"),
            CreateTestShare("Share2", "/shares/share2")
        };

        _shareRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(shares);

        // Act
        var result = await _sut.GetSharesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSharesAsync_WhenNoShares_ReturnsEmptyList()
    {
        // Arrange
        _shareRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Share>());

        // Act
        var result = await _sut.GetSharesAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithExistingShare_ReturnsShare()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var share = CreateTestShare("TestShare", "/shares/test");
        SetEntityId(share, shareId);

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await _sut.GetByIdAsync(shareId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("TestShare");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingShare_ReturnsNotFoundError()
    {
        // Arrange
        var shareId = Guid.NewGuid();

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Share?)null);

        // Act
        var result = await _sut.GetByIdAsync(shareId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesShare()
    {
        // Arrange
        var request = new CreateShareRequest
        {
            Name = "NewShare",
            Path = "/shares/newshare",
            StoragePoolId = _testPoolId,
            ShareType = ShareType.Smb
        };

        var storagePool = StoragePool.Create("TestPool", "/mnt/storage");
        SetEntityId(storagePool, _testPoolId);

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync("NewShare", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storagePoolRepositoryMock
            .Setup(x => x.GetByIdAsync(_testPoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storagePool);

        // Act
        var result = await _sut.CreateAsync(request, _testUserId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("NewShare");
        result.Value.Path.Should().Be("/shares/newshare");

        _shareRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Share>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithExistingName_ReturnsConflictError()
    {
        // Arrange
        var request = new CreateShareRequest
        {
            Name = "ExistingShare",
            Path = "/shares/existing",
            StoragePoolId = _testPoolId
        };

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync("ExistingShare", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreateAsync(request, _testUserId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("ExistingShare");

        _shareRepositoryMock.Verify(x => x.AddAsync(It.IsAny<Share>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithNonExistingStoragePool_ReturnsError()
    {
        // Arrange
        var nonExistentPoolId = Guid.NewGuid();
        var request = new CreateShareRequest
        {
            Name = "NewShare",
            Path = "/shares/new",
            StoragePoolId = nonExistentPoolId
        };

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storagePoolRepositoryMock
            .Setup(x => x.GetByIdAsync(nonExistentPoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoragePool?)null);

        // Act
        var result = await _sut.CreateAsync(request, _testUserId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task CreateAsync_WithPublicAccess_CreatesPublicShare()
    {
        // Arrange
        var request = new CreateShareRequest
        {
            Name = "PublicShare",
            Path = "/shares/public",
            StoragePoolId = _testPoolId,
            IsPublic = true
        };

        var storagePool = StoragePool.Create("TestPool", "/mnt/storage");
        SetEntityId(storagePool, _testPoolId);

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storagePoolRepositoryMock
            .Setup(x => x.GetByIdAsync(_testPoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storagePool);

        // Act
        var result = await _sut.CreateAsync(request, _testUserId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsPublic.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_WithReadOnly_CreatesReadOnlyShare()
    {
        // Arrange
        var request = new CreateShareRequest
        {
            Name = "ReadOnlyShare",
            Path = "/shares/readonly",
            StoragePoolId = _testPoolId,
            IsReadOnly = true
        };

        var storagePool = StoragePool.Create("TestPool", "/mnt/storage");
        SetEntityId(storagePool, _testPoolId);

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _storagePoolRepositoryMock
            .Setup(x => x.GetByIdAsync(_testPoolId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storagePool);

        // Act
        var result = await _sut.CreateAsync(request, _testUserId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsReadOnly.Should().BeTrue();
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithValidRequest_UpdatesShare()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var share = CreateTestShare("OldName", "/shares/test");
        SetEntityId(share, shareId);

        var request = new UpdateShareRequest
        {
            Name = "NewName",
            Description = "Updated description"
        };

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync("NewName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.UpdateAsync(shareId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("NewName");
        result.Value.Description.Should().Be("Updated description");
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistingShare_ReturnsNotFoundError()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var request = new UpdateShareRequest { Name = "NewName" };

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Share?)null);

        // Act
        var result = await _sut.UpdateAsync(shareId, request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateName_ReturnsConflictError()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var share = CreateTestShare("OriginalName", "/shares/test");
        SetEntityId(share, shareId);

        var request = new UpdateShareRequest { Name = "ExistingName" };

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        _shareRepositoryMock
            .Setup(x => x.NameExistsAsync("ExistingName", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.UpdateAsync(shareId, request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("ExistingName");
    }

    [Fact]
    public async Task UpdateAsync_WithEnabledChange_UpdatesEnabled()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var share = CreateTestShare("TestShare", "/shares/test");
        SetEntityId(share, shareId);

        var request = new UpdateShareRequest { IsEnabled = false };

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await _sut.UpdateAsync(shareId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingShare_DeletesShare()
    {
        // Arrange
        var shareId = Guid.NewGuid();
        var share = CreateTestShare("TestShare", "/shares/test");
        SetEntityId(share, shareId);

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);

        // Act
        var result = await _sut.DeleteAsync(shareId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _permissionRepositoryMock.Verify(x => x.DeleteByShareAsync(shareId, It.IsAny<CancellationToken>()), Times.Once);
        _shareRepositoryMock.Verify(x => x.DeleteAsync(share, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingShare_ReturnsNotFoundError()
    {
        // Arrange
        var shareId = Guid.NewGuid();

        _shareRepositoryMock
            .Setup(x => x.GetByIdAsync(shareId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Share?)null);

        // Act
        var result = await _sut.DeleteAsync(shareId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region GetAccessibleSharesAsync Tests

    [Fact]
    public async Task GetAccessibleSharesAsync_ReturnsUserAccessibleShares()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var shares = new List<Share>
        {
            CreateTestShare("AccessibleShare", "/shares/accessible")
        };

        _shareRepositoryMock
            .Setup(x => x.GetAccessibleByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shares);

        // Act
        var result = await _sut.GetAccessibleSharesAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("AccessibleShare");
    }

    #endregion

    #region Helper Methods

    private Share CreateTestShare(string name, string path)
    {
        return Share.Create(
            name: name,
            path: path,
            physicalPath: $"/mnt/storage{path}",
            shareType: ShareType.Smb,
            createdByUserId: _testUserId,
            storagePoolId: _testPoolId);
    }

    private static void SetEntityId<T>(T entity, Guid id) where T : Entity
    {
        var idProperty = typeof(Entity).GetProperty("Id");
        idProperty?.SetValue(entity, id);
    }

    #endregion
}
