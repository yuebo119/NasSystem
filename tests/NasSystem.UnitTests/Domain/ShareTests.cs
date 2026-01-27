// =============================================================================
// NasSystem.UnitTests - ShareTests.cs
// 共享实体单元测试
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;
using Xunit;

/// <summary>
/// Share 实体单元测试
/// </summary>
public class ShareTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storagePoolId = Guid.NewGuid();

    #region Create Tests

    [Fact]
    public void Create_WithValidParameters_CreatesShare()
    {
        // Act
        var share = Share.Create(
            name: "Documents",
            path: "/shares/documents",
            physicalPath: "/mnt/storage/shares/documents",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId,
            description: "Document share");

        // Assert
        share.Should().NotBeNull();
        share.Name.Should().Be("Documents");
        share.Path.Should().Be("/shares/documents");
        share.PhysicalPath.Should().Be("/mnt/storage/shares/documents");
        share.ShareType.Should().Be(ShareType.Smb);
        share.Description.Should().Be("Document share");
        share.IsEnabled.Should().BeTrue();
        share.IsPublic.Should().BeFalse();
        share.IsReadOnly.Should().BeFalse();
        share.EnableRecycleBin.Should().BeTrue();
    }

    [Fact]
    public void Create_SetsSmShareName()
    {
        // Act
        var share = Share.Create(
            name: "My Documents",
            path: "/shares/docs",
            physicalPath: "/mnt/storage/shares/docs",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId);

        // Assert
        share.SmbShareName.Should().Be("My_Documents");
    }

    [Fact]
    public void Create_WithPublicAccess_SetsIsPublic()
    {
        // Act
        var share = Share.Create(
            name: "Public",
            path: "/shares/public",
            physicalPath: "/mnt/storage/shares/public",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId,
            isPublic: true);

        // Assert
        share.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void Create_WithReadOnly_SetsIsReadOnly()
    {
        // Act
        var share = Share.Create(
            name: "ReadOnly",
            path: "/shares/readonly",
            physicalPath: "/mnt/storage/shares/readonly",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId,
            isReadOnly: true);

        // Assert
        share.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Create_AddsShareCreatedEvent()
    {
        // Act
        var share = Share.Create(
            name: "Test",
            path: "/shares/test",
            physicalPath: "/mnt/storage/shares/test",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId);

        // Assert
        share.DomainEvents.Should().ContainSingle();
        share.DomainEvents.First().Should().BeOfType<ShareCreatedEvent>();
    }

    #endregion

    #region Update Tests

    [Fact]
    public void Update_ChangesNameAndDescription()
    {
        // Arrange
        var share = Share.Create(
            name: "OldName",
            path: "/shares/test",
            physicalPath: "/mnt/storage/shares/test",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId,
            description: "Old description");

        // Act
        share.Update(name: "NewName", description: "New description");

        // Assert
        share.Name.Should().Be("NewName");
        share.Description.Should().Be("New description");
        share.SmbShareName.Should().Be("NewName");
    }

    [Fact]
    public void Update_WithNullValues_KeepsExistingValues()
    {
        // Arrange
        var share = Share.Create(
            name: "KeepMe",
            path: "/shares/test",
            physicalPath: "/mnt/storage/shares/test",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId,
            description: "Keep this");

        // Act
        share.Update(name: null, description: null);

        // Assert
        share.Name.Should().Be("KeepMe");
        share.Description.Should().Be("Keep this");
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public void SetEnabled_ToFalse_DisablesShare()
    {
        // Arrange
        var share = CreateTestShare();

        // Act
        share.SetEnabled(false);

        // Assert
        share.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ToTrue_EnablesShare()
    {
        // Arrange
        var share = CreateTestShare();
        share.SetEnabled(false);

        // Act
        share.SetEnabled(true);

        // Assert
        share.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region SetAccessMode Tests

    [Fact]
    public void SetAccessMode_SetsPublicAndReadOnly()
    {
        // Arrange
        var share = CreateTestShare();

        // Act
        share.SetAccessMode(isPublic: true, isReadOnly: true);

        // Assert
        share.IsPublic.Should().BeTrue();
        share.IsReadOnly.Should().BeTrue();
    }

    #endregion

    #region SetQuota Tests

    [Fact]
    public void SetQuota_SetsQuotaBytes()
    {
        // Arrange
        var share = CreateTestShare();
        var quotaBytes = 1024L * 1024 * 1024 * 100; // 100GB

        // Act
        share.SetQuota(quotaBytes);

        // Assert
        share.QuotaBytes.Should().Be(quotaBytes);
    }

    [Fact]
    public void SetQuota_WithNull_RemovesQuota()
    {
        // Arrange
        var share = CreateTestShare();
        share.SetQuota(1024);

        // Act
        share.SetQuota(null);

        // Assert
        share.QuotaBytes.Should().BeNull();
    }

    #endregion

    #region SetRecycleBin Tests

    [Fact]
    public void SetRecycleBin_ToFalse_DisablesRecycleBin()
    {
        // Arrange
        var share = CreateTestShare();

        // Act
        share.SetRecycleBin(false);

        // Assert
        share.EnableRecycleBin.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private Share CreateTestShare()
    {
        return Share.Create(
            name: "TestShare",
            path: "/shares/test",
            physicalPath: "/mnt/storage/shares/test",
            shareType: ShareType.Smb,
            createdByUserId: _userId,
            storagePoolId: _storagePoolId);
    }

    #endregion
}
