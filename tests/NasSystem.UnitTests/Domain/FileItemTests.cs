// =============================================================================
// NasSystem.UnitTests - FileItemTests.cs
// 文件项实体单元测试
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;
using Xunit;

/// <summary>
/// FileItem 实体单元测试
/// </summary>
public class FileItemTests
{
    private readonly Guid _storagePoolId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    #region CreateFolder Tests

    [Fact]
    public void CreateFolder_WithValidParameters_CreatesFolder()
    {
        // Act
        var folder = FileItem.CreateFolder(
            name: "Documents",
            path: "/documents",
            physicalPath: "/mnt/storage/documents",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Assert
        folder.Should().NotBeNull();
        folder.Name.Should().Be("Documents");
        folder.Path.Should().Be("/documents");
        folder.PhysicalPath.Should().Be("/mnt/storage/documents");
        folder.ItemType.Should().Be(FileItemType.Folder);
        folder.IsFolder.Should().BeTrue();
        folder.IsFile.Should().BeFalse();
        folder.SizeBytes.Should().Be(0);
        folder.ParentId.Should().BeNull();
    }

    [Fact]
    public void CreateFolder_WithParentId_SetsParent()
    {
        // Arrange
        var parentId = Guid.NewGuid();

        // Act
        var folder = FileItem.CreateFolder(
            name: "SubFolder",
            path: "/documents/subfolder",
            physicalPath: "/mnt/storage/documents/subfolder",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId,
            parentId: parentId);

        // Assert
        folder.ParentId.Should().Be(parentId);
    }

    [Fact]
    public void CreateFolder_AddsFolderCreatedEvent()
    {
        // Act
        var folder = FileItem.CreateFolder(
            name: "Documents",
            path: "/documents",
            physicalPath: "/mnt/storage/documents",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Assert
        folder.DomainEvents.Should().ContainSingle();
        folder.DomainEvents.First().Should().BeOfType<FolderCreatedEvent>();
    }

    #endregion

    #region CreateFile Tests

    [Fact]
    public void CreateFile_WithValidParameters_CreatesFile()
    {
        // Act
        var file = FileItem.CreateFile(
            name: "document.pdf",
            path: "/documents/document.pdf",
            physicalPath: "/mnt/storage/documents/document.pdf",
            sizeBytes: 1024 * 1024,
            mimeType: "application/pdf",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Assert
        file.Should().NotBeNull();
        file.Name.Should().Be("document.pdf");
        file.Path.Should().Be("/documents/document.pdf");
        file.ItemType.Should().Be(FileItemType.File);
        file.IsFile.Should().BeTrue();
        file.IsFolder.Should().BeFalse();
        file.SizeBytes.Should().Be(1024 * 1024);
        file.MimeType.Should().Be("application/pdf");
        file.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void CreateFile_WithHash_SetsHash()
    {
        // Arrange
        var hash = "abc123def456";

        // Act
        var file = FileItem.CreateFile(
            name: "document.pdf",
            path: "/documents/document.pdf",
            physicalPath: "/mnt/storage/documents/document.pdf",
            sizeBytes: 1024,
            mimeType: "application/pdf",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId,
            hash: hash);

        // Assert
        file.Hash.Should().Be(hash);
    }

    [Fact]
    public void CreateFile_AddsFileCreatedEvent()
    {
        // Act
        var file = FileItem.CreateFile(
            name: "document.pdf",
            path: "/documents/document.pdf",
            physicalPath: "/mnt/storage/documents/document.pdf",
            sizeBytes: 1024,
            mimeType: "application/pdf",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Assert
        file.DomainEvents.Should().ContainSingle();
        file.DomainEvents.First().Should().BeOfType<FileCreatedEvent>();
    }

    #endregion

    #region Rename Tests

    [Fact]
    public void Rename_UpdatesNameAndPath()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "old.txt",
            path: "/documents/old.txt",
            physicalPath: "/mnt/storage/documents/old.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        // Act
        file.Rename("new.txt");

        // Assert
        file.Name.Should().Be("new.txt");
        file.Path.Should().Contain("new.txt");
        file.Extension.Should().Be(".txt");
    }

    [Fact]
    public void Rename_AddsFileRenamedEvent()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "old.txt",
            path: "/documents/old.txt",
            physicalPath: "/mnt/storage/documents/old.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        // Act
        file.Rename("new.txt");

        // Assert
        file.DomainEvents.Should().ContainSingle();
        var evt = file.DomainEvents.First() as FileRenamedEvent;
        evt.Should().NotBeNull();
        evt!.OldName.Should().Be("old.txt");
        evt.NewName.Should().Be("new.txt");
    }

    #endregion

    #region MoveTo Tests

    [Fact]
    public void MoveTo_UpdatesPathAndParent()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        var newParentId = Guid.NewGuid();

        // Act
        file.MoveTo(
            newPath: "/archive/test.txt",
            newPhysicalPath: "/mnt/storage/archive/test.txt",
            newParentId: newParentId);

        // Assert
        file.Path.Should().Be("/archive/test.txt");
        file.PhysicalPath.Should().Be("/mnt/storage/archive/test.txt");
        file.ParentId.Should().Be(newParentId);
    }

    [Fact]
    public void MoveTo_AddsFileMovedEvent()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        // Act
        file.MoveTo("/archive/test.txt", "/mnt/storage/archive/test.txt", Guid.NewGuid());

        // Assert
        file.DomainEvents.Should().ContainSingle();
        file.DomainEvents.First().Should().BeOfType<FileMovedEvent>();
    }

    #endregion

    #region SoftDelete and Restore Tests

    [Fact]
    public void SoftDelete_SetsIsDeletedAndDeletedAt()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        // Act
        file.SoftDelete();

        // Assert
        file.IsDeleted.Should().BeTrue();
        file.DeletedAt.Should().NotBeNull();
        file.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SoftDelete_AddsFileDeletedEvent()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.ClearDomainEvents();

        // Act
        file.SoftDelete();

        // Assert
        file.DomainEvents.Should().ContainSingle();
        file.DomainEvents.First().Should().BeOfType<FileDeletedEvent>();
    }

    [Fact]
    public void Restore_ClearsIsDeletedAndDeletedAt()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);
        file.SoftDelete();
        file.ClearDomainEvents();

        // Act
        file.Restore();

        // Assert
        file.IsDeleted.Should().BeFalse();
        file.DeletedAt.Should().BeNull();
    }

    #endregion

    #region UpdateSize and UpdateHash Tests

    [Fact]
    public void UpdateSize_UpdatesSizeBytes()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Act
        file.UpdateSize(2048);

        // Assert
        file.SizeBytes.Should().Be(2048);
    }

    [Fact]
    public void UpdateHash_UpdatesHash()
    {
        // Arrange
        var file = FileItem.CreateFile(
            name: "test.txt",
            path: "/documents/test.txt",
            physicalPath: "/mnt/storage/documents/test.txt",
            sizeBytes: 100,
            mimeType: "text/plain",
            storagePoolId: _storagePoolId,
            ownerId: _ownerId);

        // Act
        file.UpdateHash("newhash123");

        // Assert
        file.Hash.Should().Be("newhash123");
    }

    #endregion
}
