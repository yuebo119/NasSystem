// =============================================================================
// NasSystem.UnitTests - Services/ProtectedDirectoryEnforcementTests.cs
// 只读保护在执行层（FileService 写路径）的拦截测试（ROADMAP Phase 2）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.ProtectedDirectories;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using Xunit;

/// <summary>
/// 锁定只读保护在三条写路径的拦截：上传覆盖、Move（源与目标）、Delete。
/// </summary>
/// <remarks>
/// 这些测试证明的是"保护真的拦住了写操作"，而不是仓储层的前缀判定
/// （那由 SnapshotServiceTests/ProtectedDirectory 集成测试锁定）。
/// WebDAV 的 PUT/DELETE/MOVE 与网页端共用 FileService，此处通过即
/// 两条通道同时生效。
/// </remarks>
public class ProtectedDirectoryEnforcementTests
{
    /// <summary>模拟的受保护根：/Docs 及其全部子孙</summary>
    private const string ProtectedRoot = "/Docs";

    private readonly Mock<IFileItemRepository> _fileRepositoryMock;
    private readonly Mock<IStoragePoolRepository> _storagePoolRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<NasSystem.Application.Notifications.IHubNotificationService> _notifierMock;
    private readonly Mock<IProtectedDirectoryService> _protectedDirectoryServiceMock;
    private readonly FileService _sut;

    public ProtectedDirectoryEnforcementTests()
    {
        _fileRepositoryMock = new Mock<IFileItemRepository>();
        _storagePoolRepositoryMock = new Mock<IStoragePoolRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _configurationMock = new Mock<IConfiguration>();
        _notifierMock = new Mock<NasSystem.Application.Notifications.IHubNotificationService>();
        _protectedDirectoryServiceMock = new Mock<IProtectedDirectoryService>();

        _configurationMock.Setup(x => x["NasSystem:StoragePath"]).Returns("/mnt/storage");

        // 保护判定：/Docs 自身与子孙（段边界）为受保护，其余放行
        _protectedDirectoryServiceMock
            .Setup(x => x.IsProtectedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) =>
                Task.FromResult(
                    path == ProtectedRoot
                    || path.StartsWith(ProtectedRoot + "/", StringComparison.Ordinal)));

        _sut = new FileService(
            _fileRepositoryMock.Object,
            _storagePoolRepositoryMock.Object,
            _userRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _fileSystemServiceMock.Object,
            _configurationMock.Object,
            _notifierMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<NasSystem.Application.Files.IThumbnailService>().Object,
            new Mock<NasSystem.Application.FileVersions.IFileVersionService>().Object,
            new Mock<NasSystem.Application.Ocr.IOcrService>().Object,
            new Mock<NasSystem.Application.Configuration.ISystemConfigService>().Object,
            _protectedDirectoryServiceMock.Object,
            // 磁盘物理剩余探测（F-027 收口后为注入依赖）
            new NasSystem.Application.Storage.DiskSpace(_fileSystemServiceMock.Object),
            NullLogger<FileService>.Instance);
    }

    private static FileItem CreateExistingFile(string path, Guid? ownerId = null) =>
        FileItem.CreateFile(
            name: System.IO.Path.GetFileName(path),
            path: path,
            physicalPath: $"/mnt/storage{path}",
            sizeBytes: 10,
            mimeType: "text/plain",
            storagePoolId: Guid.NewGuid(),
            ownerId: ownerId ?? Guid.NewGuid());

    [Fact]
    public async Task UploadFileAsync_OverwriteProtectedPath_IsRejected()
    {
        // Arrange：目标路径已存在（覆盖场景）且处于保护内
        var userId = Guid.NewGuid();
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/Docs/secret.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingFile("/Docs/secret.txt", userId));

        // 任何物理 IO 都不该发生（拦截在落盘之前）——OpenWrite 被调用即失败
        _fileSystemServiceMock
            .Setup(x => x.OpenWrite(It.IsAny<string>()))
            .Throws(new InvalidOperationException("拦截后不应发生物理写入"));

        var metadata = new UploadFileMetadata
        {
            FileName = "secret.txt", DestinationPath = "/Docs", FileSize = 10, Overwrite = true
        };

        // Act
        var result = await _sut.UploadFileAsync(
            metadata, new System.IO.MemoryStream([1, 2, 3]), userId);

        // Assert
        result.IsFailure.Should().BeTrue("覆盖受保护目录内的文件必须被拒绝");
        result.Error.Code.Should().Be("File.ProtectedForbidden",
            "错误码含 Forbidden 关键词，ResultExtensions 映射 403");
        result.Error.Message.Should().Contain("/Docs");
    }

    [Fact]
    public async Task UploadFileAsync_OverwriteUnprotectedPath_NotRejectedByProtection()
    {
        // Arrange：目标存在但不在保护内。放行后可能因其他前置（配额/池）失败，
        // 但错误码不得是保护拒绝——保护不该误伤未标记目录
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/Other/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingFile("/Other/file.txt"));
        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((NasSystem.Domain.Entities.StoragePool?)null);

        var metadata = new UploadFileMetadata
        {
            FileName = "file.txt", DestinationPath = "/Other", FileSize = 10, Overwrite = true
        };

        var result = await _sut.UploadFileAsync(
            metadata, new System.IO.MemoryStream([1, 2, 3]), Guid.NewGuid());

        result.IsFailure.Should().BeTrue("默认池缺失时按 PoolNotFound 失败（与保护无关）");
        result.Error.Code.Should().NotBe("File.ProtectedForbidden",
            "未标记目录不得被保护逻辑拒绝");
    }

    [Fact]
    public async Task MoveAsync_SourceInsideProtectedRoot_IsRejected()
    {
        var request = new MoveOrCopyRequest { SourcePaths = ["/Docs/a.txt"], DestinationPath = "/Other" };

        var result = await _sut.MoveAsync(request, Guid.NewGuid());

        result.IsFailure.Should().BeTrue("移出受保护目录（变相删除）必须被拒绝");
        result.Error.Code.Should().Be("File.ProtectedForbidden");
    }

    [Fact]
    public async Task MoveAsync_DestinationInsideProtectedRoot_IsRejected()
    {
        var request = new MoveOrCopyRequest { SourcePaths = ["/Other/a.txt"], DestinationPath = "/Docs" };

        var result = await _sut.MoveAsync(request, Guid.NewGuid());

        result.IsFailure.Should().BeTrue("写入受保护目录（变相覆盖）必须被拒绝");
        result.Error.Code.Should().Be("File.ProtectedForbidden");
    }

    [Fact]
    public async Task MoveAsync_NeitherSideProtected_PassesProtectionGate()
    {
        // 两端都未标记：保护放行。后续可能因池/目录准备失败，但错误码不得是保护拒绝
        _storagePoolRepositoryMock
            .Setup(x => x.GetDefaultAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((NasSystem.Domain.Entities.StoragePool?)null);

        var request = new MoveOrCopyRequest { SourcePaths = ["/Other/a.txt"], DestinationPath = "/Elsewhere" };

        var result = await _sut.MoveAsync(request, Guid.NewGuid());

        result.Error.Code.Should().NotBe("File.ProtectedForbidden",
            "两端都未标记的移动不被保护拦截");
    }

    [Fact]
    public async Task DeleteAsync_ProtectedPath_ReportsFailedItemInsteadOfDeleting()
    {
        // Arrange：库里有这条记录（路径存在），但处于保护内
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/Docs/secret.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingFile("/Docs/secret.txt"));

        var request = new DeleteItemsRequest { Paths = ["/Docs/secret.txt"], Permanent = false };

        var result = await _sut.DeleteAsync(request, Guid.NewGuid());

        // Delete 的既有语义是 per-item 结果汇总：保护拒绝进 FailedItems，
        // 不当成整个请求失败，其余项照常删除
        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedCount.Should().Be(0, "受保护项不计入删除成功数");
        result.Value.FailedItems.Should().ContainSingle()
            .Which.Should().Contain("只读保护");
    }

    [Fact]
    public async Task DeleteAsync_PermanentProtectedPath_IsAlsoRejected()
    {
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/Docs/secret.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateExistingFile("/Docs/secret.txt"));

        var request = new DeleteItemsRequest { Paths = ["/Docs/secret.txt"], Permanent = true };

        var result = await _sut.DeleteAsync(request, Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedCount.Should().Be(0, "永久删除同样被保护拦截");
        result.Value.FailedItems.Should().ContainSingle()
            .Which.Should().Contain("只读保护");
    }

    [Fact]
    public async Task DeleteAsync_UnprotectedPath_DeletesNormally()
    {
        // 对照：未标记路径的软删照常走通（防拦截逻辑误伤正常删除）
        var userId = Guid.NewGuid();
        var target = CreateExistingFile("/Other/file.txt", userId);
        _fileRepositoryMock
            .Setup(x => x.GetByPathAsync("/Other/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        _fileRepositoryMock
            .Setup(x => x.GetSubtreeEntriesAsync("/Other/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NasSystem.Domain.Repositories.FileItemEntry>
            {
                new("/Other/file.txt", FileItemType.File, 10)
            });
        _fileRepositoryMock
            .Setup(x => x.SoftDeleteSubtreeAsync(
                "/Other/file.txt", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new DeleteItemsRequest { Paths = ["/Other/file.txt"], Permanent = false };

        var result = await _sut.DeleteAsync(request, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeletedCount.Should().Be(1);
        result.Value.FailedItems.Should().BeEmpty();
    }
}
