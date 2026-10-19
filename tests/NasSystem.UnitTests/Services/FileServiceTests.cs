// =============================================================================
// NasSystem.UnitTests - FileServiceTests.cs
// 文件服务单元测试
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<NasSystem.Application.Notifications.IHubNotificationService> _notifierMock;
    private readonly FileService _sut;

    public FileServiceTests()
    {
        _fileRepositoryMock = new Mock<IFileItemRepository>();
        _storagePoolRepositoryMock = new Mock<IStoragePoolRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _configurationMock = new Mock<IConfiguration>();
        _notifierMock = new Mock<NasSystem.Application.Notifications.IHubNotificationService>();
        _configurationMock.Setup(x => x["NasSystem:StoragePath"]).Returns("/mnt/storage");

        _sut = new FileService(
            _fileRepositoryMock.Object,
            _storagePoolRepositoryMock.Object,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _fileSystemServiceMock.Object,
            _configurationMock.Object,
            _notifierMock.Object,
            // 真实 MemoryCache：Moq 模拟 IMemoryCache 时扩展方法 Set 会踩 null
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            // S3：子树软删改批量 SQL 后缩略图缓存清理由服务显式调用，
            // 本测试类不覆盖删除路径，mock 即可
            new Mock<IThumbnailService>().Object,
            // ROADMAP Phase 1：版本服务注入（覆盖上传/永久删除前自动产版本）。
            // 本测试类不覆盖版本路径，mock 即可
            new Mock<NasSystem.Application.FileVersions.IFileVersionService>().Object,
            // ROADMAP Phase 2/配置页：上传后 OCR 自动识别与动态配置。
            // 本测试类不覆盖这两条路径，mock 即可（GetBool 默认回落 descriptor
            // 默认值 false，自动识别不触发）
            new Mock<NasSystem.Application.Ocr.IOcrService>().Object,
            new Mock<NasSystem.Application.Configuration.ISystemConfigService>().Object,
            // ROADMAP Phase 2 只读保护：写路径拦截依赖。本测试类不覆盖保护路径，

            // mock 的 IsProtectedAsync 默认返回 false，对既有用例零影响

            new Mock<NasSystem.Application.ProtectedDirectories.IProtectedDirectoryService>().Object,

            NullLogger<NasSystem.Application.Files.FileService>.Instance);
    }

    #region GetDirectoryContentsAsync Tests

    [Fact]
    public async Task GetDirectoryContentsAsync_WithValidPath_ReturnsDirectoryContent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents";
        
        var parentItem = CreateFileItem(Guid.NewGuid(), "documents", "/documents", FileItemType.Folder, ownerId: userId);
        var childFile = CreateFileItem(Guid.NewGuid(), "test.txt", "/documents/test.txt", FileItemType.File, null, 1024);
        var childFolder = CreateFileItem(Guid.NewGuid(), "subfolder", "/documents/subfolder", FileItemType.Folder, null);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentItem);

        _fileRepositoryMock
            .Setup(x => x.GetChildrenSummariesAsync(It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemSummary>
            {
                CreateSummary("test.txt", "/documents/test.txt", FileItemType.File, parentId: parentItem.Id, sizeBytes: 1024),
                CreateSummary("subfolder", "/documents/subfolder", FileItemType.Folder, parentId: parentItem.Id)
            });

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
            .Setup(x => x.GetChildrenSummariesAsync(null, It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemSummary>
            {
                CreateSummary("documents", "/documents", FileItemType.Folder)
            });

        // Act
        var result = await _sut.GetDirectoryContentsAsync(path, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/");
        result.Value.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetDirectoryContentsAsync_ForwardsIncludeHiddenFlagToRepository()
    {
        // 隐藏过滤已从服务层内存 Where 下推到仓储 SQL（GetChildrenSummariesAsync 的
        // IsHidden = 0 条件），服务层不再有可观测的过滤行为，只剩"把 includeHidden
        // 原样交给仓储"这一件事。真正的过滤行为由 PalormSemanticsTests 的仓储级
        // 集成测试覆盖。故这里只锁参数传递，避免留下一个自证空转的用例
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents";

        var parentItem = CreateFileItem(Guid.NewGuid(), "documents", "/documents", FileItemType.Folder, ownerId: userId);

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentItem);

        _fileRepositoryMock
            .Setup(x => x.GetChildrenSummariesAsync(parentItem.Id, userId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemSummary>
            {
                CreateSummary("visible.txt", "/documents/visible.txt", FileItemType.File, parentId: parentItem.Id)
            });

        // Act
        var result = await _sut.GetDirectoryContentsAsync(path, userId, includeHidden: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].Name.Should().Be("visible.txt");

        _fileRepositoryMock.Verify(
            x => x.GetChildrenSummariesAsync(parentItem.Id, userId, false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetFileInfoAsync Tests

    [Fact]
    public async Task GetFileInfoAsync_WithExistingFile_ReturnsFileInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var path = "/documents/test.txt";
        var fileItem = CreateFileItem(Guid.NewGuid(), "test.txt", path, FileItemType.File, ownerId: userId);

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

        var parentItem = CreateFileItem(parentId, "documents", parentPath, FileItemType.Folder, ownerId: userId);
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

    #region UploadFileAsync 预检 Tests

    [Fact]
    public async Task UploadFileAsync_WhenUserOverQuota_ReturnsQuotaExceededError()
    {
        // Arrange：已用 95 + 待传 10 > 配额 100
        var userId = Guid.NewGuid();
        var user = NasSystem.Domain.Entities.User.Create(
            username: "quotauser", passwordHash: "h", passwordSalt: "s");
        user.SetQuota(100);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _fileRepositoryMock
            .Setup(x => x.CalculateUserUsedSpaceAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(95);

        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoragePool(Guid.NewGuid(), "default", "C:/storage"));

        var metadata = new UploadFileMetadata
        {
            FileName = "big.bin",
            DestinationPath = "/",
            FileSize = 10
        };

        // Act
        var result = await _sut.UploadFileAsync(metadata, new MemoryStream(new byte[10]), userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.QuotaExceeded");

        // 预检必须先于任何物理写入
        _fileSystemServiceMock.Verify(x => x.CreateDirectory(It.IsAny<string>()), Times.Never);
        _fileSystemServiceMock.Verify(x => x.OpenWrite(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_WhenPoolHasNoSpace_ReturnsInsufficientSpaceError()
    {
        // Arrange：用户无配额（跳过配额检查）；池未刷新容量，AvailableBytes=0 < 声明大小
        var userId = Guid.NewGuid();
        var pool = CreateStoragePool(Guid.NewGuid(), "default", "C:/storage");

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NasSystem.Domain.Entities.User?)null);

        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        _storagePoolRepositoryMock
            .Setup(x => x.GetByIdAsync(pool.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);

        var metadata = new UploadFileMetadata
        {
            FileName = "big.bin",
            DestinationPath = "/",
            FileSize = 1024
        };

        // Act
        var result = await _sut.UploadFileAsync(metadata, new MemoryStream(new byte[1024]), userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Storage.InsufficientSpace");

        _fileSystemServiceMock.Verify(x => x.OpenWrite(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithValidItems_SoftDeletesItems()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileItem = CreateFileItem(fileId, "test.txt", "/documents/test.txt", FileItemType.File, ownerId: userId);

        var request = new DeleteItemsRequest 
        { 
            Paths = new List<string> { "/documents/test.txt" },
            Permanent = false
        };

        // S3：软删走单条 SQL（GetSubtreeEntriesAsync 取副作用清单 +
        // SoftDeleteSubtreeAsync 登记批量命令），不再逐条 UpdateAsync
        _fileRepositoryMock
            .Setup(x => x.GetSubtreeEntriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemEntry>
            {
                new("/documents/test.txt", FileItemType.File, 1024),
            });

        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/documents/test.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileItem);

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _fileRepositoryMock.Verify(
            x => x.SoftDeleteSubtreeAsync("/documents/test.txt", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithPermanentFlag_PermanentlyDeletesFile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var fileItem = CreateFileItem(Guid.NewGuid(), "delete-me.txt", "/documents/delete-me.txt", FileItemType.File, ownerId: userId);

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/delete-me.txt" },
            Permanent = true
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathIncludingDeletedAsync("/documents/delete-me.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileItem);

        // 设置文件存在检查返回 true
        _fileSystemServiceMock
            .Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(true);
        _fileRepositoryMock
            .Setup(x => x.GetDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());


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
        var folder = CreateFileItem(Guid.NewGuid(), "folder", "/documents/folder", FileItemType.Folder, ownerId: userId);

        var request = new DeleteItemsRequest
        {
            Paths = new List<string> { "/documents/folder" },
            Permanent = true
        };

        _fileRepositoryMock
            .Setup(x => x.GetByPathIncludingDeletedAsync("/documents/folder", It.IsAny<CancellationToken>()))
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
        var file1 = CreateFileItem(Guid.NewGuid(), "file1.txt", "/documents/file1.txt", FileItemType.File, ownerId: userId);
        var file2 = CreateFileItem(Guid.NewGuid(), "file2.txt", "/documents/file2.txt", FileItemType.File, ownerId: userId);

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

        // S3：软删走单条 SQL，逐条 UpdateAsync 的验证改为批量命令的验证
        _fileRepositoryMock
            .Setup(x => x.GetSubtreeEntriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemEntry>());

        // Act
        var result = await _sut.DeleteAsync(request, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.DeletedCount.Should().Be(2);
        // 软删除链路已按 CONVENTIONS §3.5 落地（ITM-N7/N4 验收）：
        // Permanent=false 走 SoftDeleteSubtreeAsync（单条 SQL），而非物理删除
        _fileRepositoryMock.Verify(
            x => x.SoftDeleteSubtreeAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
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
        var file = CreateFileItem(Guid.NewGuid(), "old.txt", "/documents/old.txt", FileItemType.File, ownerId: userId);

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
        _fileRepositoryMock
            .Setup(x => x.GetDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

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
        var file = CreateFileItem(Guid.NewGuid(), "old.txt", "/documents/old.txt", FileItemType.File, ownerId: userId);

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
        var file = CreateFileItem(Guid.NewGuid(), "file.txt", "/source/file.txt", FileItemType.File, ownerId: userId);
        var destFolder = CreateFileItem(Guid.NewGuid(), "destination", "/destination", FileItemType.Folder, ownerId: userId);

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

        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoragePool(Guid.NewGuid(), "default", "C:/storage"));

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
        var file1 = CreateFileItem(Guid.NewGuid(), "file1.txt", "/source/file1.txt", FileItemType.File, ownerId: userId);
        var file2 = CreateFileItem(Guid.NewGuid(), "file2.txt", "/source/file2.txt", FileItemType.File, ownerId: userId);
        var destFolder = CreateFileItem(Guid.NewGuid(), "destination", "/destination", FileItemType.Folder, ownerId: userId);

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

        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStoragePool(Guid.NewGuid(), "default", "C:/storage"));

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
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
        var file = CreateFileItem(fileId, "deleted.txt", "/documents/deleted.txt", FileItemType.File, ownerId: userId);

        // S3：恢复走单条 SQL（整棵子树），不再逐条 Restore + UpdateAsync
        _fileRepositoryMock
            .Setup(x => x.GetByIdAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(file);
        _fileRepositoryMock
            .Setup(x => x.GetSubtreeEntriesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItemEntry>
            {
                new("/documents/deleted.txt", FileItemType.File, 2048),
            });

        // Act
        var result = await _sut.RestoreAsync(fileId, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _fileRepositoryMock.Verify(
            x => x.RestoreSubtreeAsync("/documents/deleted.txt", It.IsAny<CancellationToken>()),
            Times.Once);
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
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.GetDeletedItemsAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

        // Act
        var result = await _sut.EmptyRecycleBinAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region GetRecentFilesAsync Tests

    [Fact]
    public async Task GetRecentFilesAsync_ReturnsItemsFromRepositoryMappedToDtos()
    {
        // Dashboard 最近文件面板的数据源：仓储按 CreatedAt 倒序取前 N 条，
        // 服务只做映射，顺序原样透出（面板依赖仓储的倒序）
        var userId = Guid.NewGuid();
        var items = new List<FileItem>
        {
            CreateFileItem(Guid.NewGuid(), "latest.txt", "/latest.txt", FileItemType.File, null, 2048),
            CreateFileItem(Guid.NewGuid(), "newfolder", "/newfolder", FileItemType.Folder)
        };

        _fileRepositoryMock
            .Setup(x => x.GetRecentAsync(userId, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var result = await _sut.GetRecentFilesAsync(userId, 6);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Name.Should().Be("latest.txt");
        result.Value[0].SizeBytes.Should().Be(2048);
        result.Value[0].IsFile.Should().BeTrue();
        result.Value[1].Name.Should().Be("newfolder");
        result.Value[1].IsFolder.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentFilesAsync_WithNoItems_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        _fileRepositoryMock
            .Setup(x => x.GetRecentAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileItem>());

        var result = await _sut.GetRecentFilesAsync(userId, 6);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
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
            .Setup(x => x.SearchAsync(
                "report",
                userId,
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool?>(),
                1,
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileSearchResult { Items = matchingFiles, TotalCount = 2 });

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
            .Setup(x => x.SearchAsync(
                "nonexistent",
                userId,
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool?>(),
                1,
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileSearchResult { Items = [], TotalCount = 0 });

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
        bool isHidden = false,
        Guid? ownerId = null)
    {
        var owner = ownerId ?? Guid.NewGuid();
        return itemType == FileItemType.Folder
            ? FileItem.CreateFolder(name, path, $"/mnt/storage{path}", Guid.NewGuid(), owner, parentId)
            : FileItem.CreateFile(name, path, $"/mnt/storage{path}", sizeBytes, "text/plain", Guid.NewGuid(), owner, parentId);
    }

    /// <summary>
    /// 浏览摘要的测试替身。服务走 GetChildrenSummariesAsync 而非 GetChildrenAsync，
    /// 摘要不含 PhysicalPath/StoragePoolId 等浏览 DTO 用不到的列
    /// </summary>
    private static FileItemSummary CreateSummary(
        string name,
        string path,
        FileItemType itemType,
        Guid? parentId = null,
        long sizeBytes = 0,
        bool isHidden = false) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = path,
            ItemType = itemType,
            SizeBytes = sizeBytes,
            MimeType = itemType == FileItemType.File ? "text/plain" : null,
            Extension = itemType == FileItemType.File ? ".txt" : null,
            OwnerId = Guid.NewGuid(),
            ParentId = parentId,
            IsHidden = isHidden,
            IsReadOnly = false,
            CreatedAt = DateTime.UtcNow
        };

    private static StoragePool CreateStoragePool(Guid id, string name, string mountPath)
    {
        return StoragePool.Create(name, mountPath);
    }

    #endregion
}
