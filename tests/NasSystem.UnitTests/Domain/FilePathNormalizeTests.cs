// =============================================================================
// NasSystem.UnitTests - FilePathNormalizeTests.cs
// 路径规范化真源（FilePath.Normalize）的边界守护测试
//
// 【为什么这组测试必须存在】ITM-N26 批次将四份私有 NormalizePath 拷贝收敛到
// FilePath.Normalize 强防护版（解析 .. 与重复斜杠）。收敛后所有层的路径语义
// 都由这一个方法决定——这里的每个断言都是全系统路径行为的契约锚点：
// 唯一索引兜底、物理映射一致性、防穿越校验均以其输出为前提。
// 修改 Normalize 实现前先读本文件；任何断言变化都是行为变更，需评审。
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using Moq;
using NasSystem.Domain.Entities;
using NasSystem.Application.Files;
using NasSystem.Application.Files.Dtos;
using NasSystem.Application.Notifications;
using NasSystem.Domain.Common;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using NasSystem.Domain.ValueObjects;
using Xunit;

/// <summary>
/// FilePath.Normalize 边界守护测试
/// </summary>
public class FilePathNormalizeTests
{
    [Theory]
    [InlineData("/a/../b", "/b")]
    [InlineData("/a/b/../../c", "/c")]
    [InlineData("../../x", "/x")]                       // 越界 .. 在根处被忽略（防逃逸）
    [InlineData("/a/./b", "/a/b")]                      // 当前段移除
    [InlineData("/a//b", "/a/b")]                       // 重复斜杠压缩
    [InlineData("///", "/")]
    [InlineData("\\a\\b", "/a/b")]                      // Windows 分隔符归一
    [InlineData("/a/b/", "/a/b")]                       // 尾斜杠移除
    [InlineData("a/b", "/a/b")]                         // 相对路径补根
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]                            // 空白归一为根（历史契约）
    public void Normalize_WithBoundaryInput_ProducesCanonicalPath(
        string input, string expected)
    {
        FilePath.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void CreateFolder_WithDotDotSegments_NormalizesToPhysicalSafePath()
    {
        // 收敛后的行为强化：含 .. 的创建请求被解析为规范物理安全路径，
        // 而非按字面量入库（旧行为会与唯一索引/物理映射产生歧义）
        var folder = FileItem.CreateFolder(
            name: "sub",
            path: "/docs/../sub",
            physicalPath: "/mnt/storage/sub",
            storagePoolId: Guid.NewGuid(),
            ownerId: Guid.NewGuid());

        folder.Path.Should().Be("/sub");
    }

    [Fact]
    public async Task Browse_WithDotDotPath_ResolvesCanonically()
    {
        // 应用层委托同样走强版："/docs/../" 与 "/" 归一为同一浏览目标
        var service = CreateService();
        var result = await service.GetDirectoryContentsAsync("/whatever/..", Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/");
    }

    private static FileService CreateService()
    {
        var repoMock = new Moq.Mock<IFileItemRepository>();
        // 归一化目标为根目录时的最小行为：父不存在、子列表为空
        repoMock.Setup(r => r.GetChildrenAsync(It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        repoMock.Setup(r => r.GetDeletedItemsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
        return new FileService(
            repoMock.Object,
            new Moq.Mock<IStoragePoolRepository>().Object,
            new Moq.Mock<IUserRepository>().Object,
            new Moq.Mock<IUnitOfWork>().Object,
            new Moq.Mock<NasSystem.Domain.Services.IFileSystemService>().Object,
            new Moq.Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new Moq.Mock<NasSystem.Application.Notifications.IHubNotificationService>().Object,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
    }
}
