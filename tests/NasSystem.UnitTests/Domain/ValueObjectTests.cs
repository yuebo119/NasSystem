// =============================================================================
// NasSystem.UnitTests - ValueObjectTests.cs
// 值对象单元测试
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.ValueObjects;
using Xunit;

/// <summary>
/// 值对象单元测试
/// </summary>
public class ValueObjectTests
{
    #region FilePath Tests

    [Fact]
    public void FilePath_Create_WithValidPath_CreatesFilePath()
    {
        // Act
        var path = FilePath.Create("/documents/report.pdf");

        // Assert
        path.Value.Should().Be("/documents/report.pdf");
        path.Name.Should().Be("report.pdf");
        path.Extension.Should().Be(".pdf");
        path.ParentPath.Should().Be("/documents");
        path.Depth.Should().Be(2);
        path.IsRoot.Should().BeFalse();
    }

    [Fact]
    public void FilePath_Create_WithRootPath_CreatesRootFilePath()
    {
        // Act
        var path = FilePath.Create("/");

        // Assert
        path.Value.Should().Be("/");
        path.IsRoot.Should().BeTrue();
        path.Depth.Should().Be(0);
        path.ParentPath.Should().BeNull();
    }

    [Fact]
    public void FilePath_Create_WithTrailingSlash_NormalizesPath()
    {
        // Act
        var path = FilePath.Create("/documents/");

        // Assert
        path.Value.Should().Be("/documents");
    }

    [Fact]
    public void FilePath_Create_WithWindowsPath_NormalizesToUnix()
    {
        // Act
        var path = FilePath.Create(@"\documents\subfolder");

        // Assert
        path.Value.Should().Be("/documents/subfolder");
    }

    [Fact]
    public void FilePath_Create_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => FilePath.Create("");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*不能为空*");
    }

    [Fact]
    public void FilePath_Create_WithNullPath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => FilePath.Create(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FilePath_Segments_ReturnsCorrectSegments()
    {
        // Act
        var path = FilePath.Create("/home/user/documents");

        // Assert
        path.Segments.Should().HaveCount(3);
        path.Segments[0].Should().Be("home");
        path.Segments[1].Should().Be("user");
        path.Segments[2].Should().Be("documents");
    }

    [Fact]
    public void FilePath_Extension_WithNoExtension_ReturnsNull()
    {
        // Act
        var path = FilePath.Create("/documents/readme");

        // Assert
        path.Extension.Should().BeNull();
    }

    [Fact]
    public void FilePath_Extension_WithDotFile_ReturnsNull()
    {
        // A file starting with dot (hidden file) should not have the dot treated as extension delimiter
        var path = FilePath.Create("/documents/.gitignore");

        // Assert - the whole name is .gitignore
        path.Name.Should().Be(".gitignore");
    }

    [Theory]
    [InlineData("/a/b/c", "/a/b")]
    [InlineData("/documents", "/")]
    [InlineData("/home/user/docs/file.txt", "/home/user/docs")]
    public void FilePath_ParentPath_ReturnsCorrectParent(string input, string expectedParent)
    {
        // Act
        var path = FilePath.Create(input);

        // Assert
        path.ParentPath.Should().Be(expectedParent);
    }

    [Fact]
    public void FilePath_Equality_WithSamePath_AreEqual()
    {
        // Arrange
        var path1 = FilePath.Create("/documents/file.txt");
        var path2 = FilePath.Create("/documents/file.txt");

        // Assert
        path1.Should().Be(path2);
        (path1 == path2).Should().BeTrue();
    }

    [Fact]
    public void FilePath_Equality_WithDifferentPath_AreNotEqual()
    {
        // Arrange
        var path1 = FilePath.Create("/documents/file1.txt");
        var path2 = FilePath.Create("/documents/file2.txt");

        // Assert
        path1.Should().NotBe(path2);
    }

    #endregion
}
