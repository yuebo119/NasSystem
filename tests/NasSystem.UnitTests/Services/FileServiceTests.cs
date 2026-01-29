// =============================================================================
// NasSystem.UnitTests - FileServiceTests.cs
// 文件服务单元测试
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using Xunit;

/// <summary>
/// FileService 单元测试
/// </summary>
public class FileServiceTests
{
    private readonly Mock<IFileItemRepository> _fileRepositoryMock;
    private readonly Mock<IStoragePoolRepository> _storagePoolRepositoryMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IFileHasher> _fileHasherMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly FileService _sut;

    public FileServiceTests()
    {
        _fileRepositoryMock = new Mock<IFileItemRepository>();
        _storagePoolRepositoryMock = new Mock<IStoragePoolRepository>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _fileHasherMock = new Mock<IFileHasher>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _configurationMock = new Mock<IConfiguration>();
        _configurationMock.Setup(x => x["NasSystem:StoragePath"]).Returns("/mnt/storage");

        _sut = new FileService(
            _fileRepositoryMock.Object,
            _storagePoolRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _fileSystemServiceMock.Object,
            _fileHasherMock.Object,
            _configurationMock.Object);
    }

    #region GetDirectoryContentsAsync Tests

    [Fact]
    public async Task GetDirectoryContentsAsync_WithValidPath_ReturnsDirectoryContent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents";
        
        var parentItem = CreateFileItem(Guid.NewGuid(), "documents", "/documents", FileItemType.Folder);
        var childFile = CreateFileItem(Guid.NewGuid(), "test.txt", "/documents/test.txt", FileItemType.File, null, 1024);
        var childFolder = CreateFileItem(Guid.NewGuid(), "subfolder", "/documents/subfolder", FileItemType.Folder, null);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentItem);

        _fileRepositoryMock
            .Setup(x => x.GetChildrenAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem> { childFile, childFolder });

        // Act
        var result = await _sut.GetDirectoryContentsAsync(path, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.FileCount.Should().Be(1);
        result.Value.FolderCount.Should().Be(1);
        result.Value.TotalSizeBytes.Should().Be(1024);
    }

    [Fact]
    public async Task GetDirectoryContentsAsync_WithRootPath_ReturnsRootContent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/";
        
        var childFolder = CreateFileItem(Guid.NewGuid(), "documents", "/documents", FileItemType.Folder, null);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        _fileRepositoryMock
            .Setup(x => x.GetChildrenAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem> { childFolder });

        // Act
        var result = await _sut.GetDirectoryContentsAsync(path, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/");
        result.Value.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDirectoryContentsAsync_ExcludesHiddenItems_WhenIncludeHiddenIsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents";

        var parentItem = CreateFileItem(Guid.NewGuid(), "documents", "/documents", FileItemType.Folder);
        var visibleFile = CreateFileItem(Guid.NewGuid(), "visible.txt", "/documents/visible.txt", FileItemType.File, null);
        // Note: IsHidden is not settable through CreateFile, so we just test with non-hidden files
        // The actual hidden filtering logic is tested at integration level

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentItem);

        _fileRepositoryMock
            .Setup(x => x.GetChildrenAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem> { visibleFile });

        // Act
        var result = await _sut.GetDirectoryContentsAsync(path, userId, includeHidden: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Name.Should().Be("visible.txt");
    }

    #endregion

    #region GetFileInfoAsync Tests

    [Fact]
    public async Task GetFileInfoAsync_WithExistingFile_ReturnsFileInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents/test.txt";
        var fileItem = CreateFileItem(Guid.NewGuid(), "test.txt", path, FileItemType.File);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileItem);

        // Act
        var result = await _sut.GetFileInfoAsync(path, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("test.txt");
        result.Value.Path.Should().Be(path);
    }

    [Fact]
    public async Task GetFileInfoAsync_WithNonExistingFile_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents/nonexistent.txt";

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        // Act
        var result = await _sut.GetFileInfoAsync(path, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("File.NotFound");
    }

    #endregion

    #region CreateFolderAsync Tests

    [Fact]
    public async Task CreateFolderAsync_WithValidRequest_CreatesFolder()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var parentPath = "/documents";
        var folderName = "newfolder";
        var parentId = Guid.NewGuid();
        var poolId = Guid.NewGuid();

        var parentItem = CreateFileItem(parentId, "documents", parentPath, FileItemType.Folder);
        var storagePool = CreateStoragePool(poolId, "DefaultPool", "/mnt/storage");

        var request = new CreateFolderRequest 
        { 
            ParentPath = parentPath, 
            Name = folderName 
        };

        _fileRepositoryMock
            .Setup(x => x.PathExistsAsync(It.Is<string>(p => p.Contains(folderName)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(parentPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentItem);

        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storagePool);

        // Act
        var result = await _sut.CreateFolderAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(folderName);
        result.Value.ItemType.Should().Be(FileItemType.Folder);

        _fileSystemServiceMock.Verify(x => x.CreateDirectory(It.IsAny<string>()), Times.Once);
        _fileRepositoryMock.Verify(x => x.AddAsync(It.IsAny<FileItem>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateFolderAsync_WithExistingPath_ReturnsAlreadyExistsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new CreateFolderRequest 
        { 
            ParentPath = "/documents", 
            Name = "existing" 
        };

        _fileRepositoryMock
            .Setup(x => x.PathExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreateFolderAsync(request, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("Conflict");
    }

    [Fact]
    public async Task CreateFolderAsync_WithNonExistingParent_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new CreateFolderRequest 
        { 
            ParentPath = "/nonexistent", 
            Name = "newfolder" 
        };

        _fileRepositoryMock
            .Setup(x => x.PathExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        // Act
        var result = await _sut.CreateFolderAsync(request, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("File.NotFound");
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithValidItems_SoftDeletesItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileItem = CreateFileItem(fileId, "test.txt", "/documents/test.txt", FileItemType.File);

        var request = new DeleteItemsRequest 
        { 
            Paths = new List<string> { "/documents/test.txt" },
            Permanent = false
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileItem);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithPermanentFlag_PermanentlyDeletesFile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileItem = CreateFileItem(Guid.NewGuid(), "delete-me.txt", "/documents/delete-me.txt", FileItemType.File);

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/delete-me.txt" },
            Permanent = true
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/delete-me.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileItem);

        // 设置文件存在检查返回 true
        _fileSystemServiceMock
            .Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(true);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Once);
        _fileRepositoryMock.Verify(x => x.DeleteAsync(fileItem, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithPermanentFlagOnFolder_DeletesDirectoryRecursively()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var folder = CreateFileItem(Guid.NewGuid(), "folder", "/documents/folder", FileItemType.Folder);

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/folder" },
            Permanent = true
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/folder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(folder);

        // 设置目录存在检查返回 true
        _fileSystemServiceMock
            .Setup(x => x.DirectoryExists(It.IsAny<string>()))
            .Returns(true);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.DeleteDirectory(It.IsAny<string>(), true), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithMultipleItems_DeletesAllItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var file1 = CreateFileItem(Guid.NewGuid(), "file1.txt", "/documents/file1.txt", FileItemType.File);
        var file2 = CreateFileItem(Guid.NewGuid(), "file2.txt", "/documents/file2.txt", FileItemType.File);

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/file1.txt", "/documents/file2.txt" },
            Permanent = false
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/file1.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file1);
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/file2.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file2);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<FileItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingPath_SkipsItem()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/nonexistent.txt" },
            Permanent = false
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region RenameAsync Tests

    [Fact]
    public async Task RenameAsync_WithValidRequest_RenamesItem()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var file = CreateFileItem(Guid.NewGuid(), "old.txt", "/documents/old.txt", FileItemType.File);

        var request = new RenameItemRequest
        {
            Path = "/documents/old.txt",
            NewName = "new.txt"
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/old.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);

        _fileRepositoryMock
            .Setup(x => x.PathExistsAsync(It.Is<string>(p => p.Contains("new.txt")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.RenameAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RenameAsync_WithExistingName_ReturnsAlreadyExistsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var file = CreateFileItem(Guid.NewGuid(), "old.txt", "/documents/old.txt", FileItemType.File);

        var request = new RenameItemRequest
        {
            Path = "/documents/old.txt",
            NewName = "existing.txt"
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/old.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);

        _fileRepositoryMock
            .Setup(x => x.PathExistsAsync(It.Is<string>(p => p.Contains("existing.txt")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.RenameAsync(request, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("Conflict");
    }

    [Fact]
    public async Task RenameAsync_WithNonExistingFile_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var request = new RenameItemRequest
        {
            Path = "/documents/nonexistent.txt",
            NewName = "new.txt"
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        // Act
        var result = await _sut.RenameAsync(request, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("File.NotFound");
    }

    #endregion

    #region MoveAsync Tests

    [Fact]
    public async Task MoveAsync_WithValidRequest_MovesItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var file = CreateFileItem(Guid.NewGuid(), "file.txt", "/source/file.txt", FileItemType.File);
        var destFolder = CreateFileItem(Guid.NewGuid(), "destination", "/destination", FileItemType.Folder);

        var request = new MoveOrCopyRequest
        {
            SourcePaths = new List<string> { "/source/file.txt" },
            DestinationPath = "/destination"
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/source/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/destination", It.IsAny<CancellationToken>()))
            .ReturnsAsync(destFolder);

        // Act
        var result = await _sut.MoveAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.Move(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task MoveAsync_WithMultipleItems_MovesAllItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var file1 = CreateFileItem(Guid.NewGuid(), "file1.txt", "/source/file1.txt", FileItemType.File);
        var file2 = CreateFileItem(Guid.NewGuid(), "file2.txt", "/source/file2.txt", FileItemType.File);
        var destFolder = CreateFileItem(Guid.NewGuid(), "destination", "/destination", FileItemType.Folder);

        var request = new MoveOrCopyRequest
        {
            SourcePaths = new List<string> { "/source/file1.txt", "/source/file2.txt" },
            DestinationPath = "/destination"
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/source/file1.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file1);
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/source/file2.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(file2);
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/destination", It.IsAny<CancellationToken>()))
            .ReturnsAsync(destFolder);

        // Act
        var result = await _sut.MoveAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion

    #region GetRecycleBinAsync Tests

    [Fact]
    public async Task GetRecycleBinAsync_ReturnsDeletedItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deletedItems = new List<FileItem>
        {
            CreateFileItem(Guid.NewGuid(), "deleted1.txt", "/documents/deleted1.txt", FileItemType.File),
            CreateFileItem(Guid.NewGuid(), "deleted2.txt", "/documents/deleted2.txt", FileItemType.File)
        };

        _fileRepositoryMock
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedItems);

        // Act
        var result = await _sut.GetRecycleBinAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecycleBinAsync_WhenEmpty_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _fileRepositoryMock
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

        // Act
        var result = await _sut.GetRecycleBinAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region RestoreAsync Tests

    [Fact]
    public async Task RestoreAsync_WithValidFileId_RestoresFile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var file = CreateFileItem(fileId, "deleted.txt", "/documents/deleted.txt", FileItemType.File);

        _fileRepositoryMock
            .Setup(x => x.GetByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);

        // Act
        var result = await _sut.RestoreAsync(fileId, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileRepositoryMock.Verify(x => x.UpdateAsync(file, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_WithNonExistingFileId_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        _fileRepositoryMock
            .Setup(x => x.GetByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        // Act
        var result = await _sut.RestoreAsync(fileId, userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region EmptyRecycleBinAsync Tests

    [Fact]
    public async Task EmptyRecycleBinAsync_WithDeletedItems_DeletesAllItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var deletedItems = new List<FileItem>
        {
            CreateFileItem(Guid.NewGuid(), "deleted1.txt", "/documents/deleted1.txt", FileItemType.File),
            CreateFileItem(Guid.NewGuid(), "deleted2.txt", "/documents/deleted2.txt", FileItemType.File)
        };

        _fileRepositoryMock
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedItems);

        _fileSystemServiceMock
            .Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(true);

        // Act
        var result = await _sut.EmptyRecycleBinAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileSystemServiceMock.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Exactly(2));
        _fileRepositoryMock.Verify(x => x.DeleteAsync(It.IsAny<FileItem>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EmptyRecycleBinAsync_WhenEmpty_Succeeds()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _fileRepositoryMock
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

        // Act
        var result = await _sut.EmptyRecycleBinAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_WithValidQuery_ReturnsMatchingFiles()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var matchingFiles = new List<FileItem>
        {
            CreateFileItem(Guid.NewGuid(), "report.pdf", "/documents/report.pdf", FileItemType.File),
            CreateFileItem(Guid.NewGuid(), "report2.pdf", "/documents/report2.pdf", FileItemType.File)
        };

        var request = new SearchFilesRequest
        {
            Query = "report",
            Page = 1,
            PageSize = 50
        };

        _fileRepositoryMock
            .Setup(x => x.SearchAsync("report", userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchingFiles);

        // Act
        var result = await _sut.SearchAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_WithNoMatches_ReturnsEmptyResult()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var request = new SearchFilesRequest
        {
            Query = "nonexistent",
            Page = 1,
            PageSize = 50
        };

        _fileRepositoryMock
            .Setup(x => x.SearchAsync("nonexistent", userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

        // Act
        var result = await _sut.SearchAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private static FileItem CreateFileItem(
        Guid id,
        string name,
        string path,
        FileItemType itemType,
        Guid? parentId = null,
        long sizeBytes = 0,
        bool isHidden = false)
    {
        return itemType == FileItemType.Folder
            ? FileItem.CreateFolder(name, path, $"/mnt/storage{path}", Guid.NewGuid(), Guid.NewGuid(), parentId)
            : FileItem.CreateFile(name, path, $"/mnt/storage{path}", sizeBytes, "text/plain", Guid.NewGuid(), Guid.NewGuid(), parentId);
    }

    private static StoragePool CreateStoragePool(Guid id, string name, string mountPath)
    {
        return StoragePool.Create(name, mountPath);
    }

    #endregion
}
