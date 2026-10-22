// =============================================================================
// NasSystem.UnitTests - Services/FileAccessResolverTests.cs
// 虚拟路径解析的属主边界（端点共享的单一访问规则）
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Moq;
using NasSystem.Application.Files;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Repositories;
using Xunit;

/// <summary>
/// 锁定"查库 + 软删过滤 + 属主判定"的单一入口：本人拿物理路径，
/// 非本人只拿 Forbidden（不携带路径），不存在拿 NotFound。
/// </summary>
public sealed class FileAccessResolverTests
{
    private readonly Mock<IFileItemRepository> _repository = new();
    private readonly FileAccessResolver _sut;

    public FileAccessResolverTests()
    {
        _sut = new FileAccessResolver(_repository.Object);
    }

    [Fact]
    public async Task ResolveAsync_Owner_ReturnsPhysicalPath()
    {
        var owner = Guid.NewGuid();
        var item = FileItem.CreateFile(
            "a.jpg", "/a.jpg", "/mnt/pool/a.jpg", 10, "image/jpeg",
            Guid.NewGuid(), owner, hash: "h");
        _repository
            .Setup(r => r.GetByPathAsync("/a.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.ResolveAsync("/a.jpg", owner);

        result.Status.Should().Be(FileAccessStatus.Ok);
        result.File.Should().NotBeNull();
        result.File!.PhysicalPath.Should().Be("/mnt/pool/a.jpg");
        result.File.MimeType.Should().Be("image/jpeg");
        result.File.IsFolder.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_NotOwner_ReturnsForbiddenWithoutPath()
    {
        var item = FileItem.CreateFile(
            "a.jpg", "/a.jpg", "/mnt/pool/a.jpg", 10, "image/jpeg",
            Guid.NewGuid(), Guid.NewGuid(), hash: "h");
        _repository
            .Setup(r => r.GetByPathAsync("/a.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var result = await _sut.ResolveAsync("/a.jpg", Guid.NewGuid());

        result.Status.Should().Be(FileAccessStatus.Forbidden);
        result.File.Should().BeNull("非本人不能拿到物理路径");
    }

    [Fact]
    public async Task ResolveAsync_Missing_ReturnsNotFound()
    {
        _repository
            .Setup(r => r.GetByPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileItem?)null);

        var result = await _sut.ResolveAsync("/missing.jpg", Guid.NewGuid());

        result.Status.Should().Be(FileAccessStatus.NotFound);
        result.File.Should().BeNull();
    }
}