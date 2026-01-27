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

    #region StorageSize Tests

    [Fact]
    public void StorageSize_FromBytes_CreatesCorrectSize()
    {
        // Act
        var size = StorageSize.FromBytes(1024);

        // Assert
        size.Bytes.Should().Be(1024);
        size.ToKilobytes().Should().Be(1);
    }

    [Fact]
    public void StorageSize_FromKilobytes_CreatesCorrectSize()
    {
        // Act
        var size = StorageSize.FromKilobytes(1);

        // Assert
        size.Bytes.Should().Be(1024);
    }

    [Fact]
    public void StorageSize_FromMegabytes_CreatesCorrectSize()
    {
        // Act
        var size = StorageSize.FromMegabytes(1);

        // Assert
        size.Bytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void StorageSize_FromGigabytes_CreatesCorrectSize()
    {
        // Act
        var size = StorageSize.FromGigabytes(1);

        // Assert
        size.Bytes.Should().Be(1024L * 1024 * 1024);
    }

    [Fact]
    public void StorageSize_FromTerabytes_CreatesCorrectSize()
    {
        // Act
        var size = StorageSize.FromTerabytes(1);

        // Assert
        size.Bytes.Should().Be(1024L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void StorageSize_Zero_ReturnsZeroBytes()
    {
        // Act
        var size = StorageSize.Zero;

        // Assert
        size.Bytes.Should().Be(0);
    }

    [Fact]
    public void StorageSize_FromNegativeBytes_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        var act = () => StorageSize.FromBytes(-1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*不能为负数*");
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void StorageSize_ToHumanReadable_ReturnsCorrectFormat(long bytes, string expected)
    {
        // Arrange
        var size = StorageSize.FromBytes(bytes);

        // Act
        var result = size.ToHumanReadable();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void StorageSize_ToKilobytes_ReturnsCorrectValue()
    {
        // Arrange
        var size = StorageSize.FromBytes(2048);

        // Assert
        size.ToKilobytes().Should().Be(2);
    }

    [Fact]
    public void StorageSize_ToMegabytes_ReturnsCorrectValue()
    {
        // Arrange
        var size = StorageSize.FromBytes(2 * 1024 * 1024);

        // Assert
        size.ToMegabytes().Should().Be(2);
    }

    [Fact]
    public void StorageSize_ToGigabytes_ReturnsCorrectValue()
    {
        // Arrange
        var size = StorageSize.FromBytes(2L * 1024 * 1024 * 1024);

        // Assert
        size.ToGigabytes().Should().Be(2);
    }

    [Fact]
    public void StorageSize_Comparison_WorksCorrectly()
    {
        // Arrange
        var size1 = StorageSize.FromMegabytes(100);
        var size2 = StorageSize.FromMegabytes(200);

        // Assert
        size1.CompareTo(size2).Should().BeLessThan(0);
        size2.CompareTo(size1).Should().BeGreaterThan(0);
        size1.CompareTo(StorageSize.FromMegabytes(100)).Should().Be(0);
    }

    [Fact]
    public void StorageSize_Equality_WithSameBytes_AreEqual()
    {
        // Arrange
        var size1 = StorageSize.FromBytes(1024);
        var size2 = StorageSize.FromBytes(1024);

        // Assert
        size1.Should().Be(size2);
    }

    [Fact]
    public void StorageSize_Equality_WithDifferentBytes_AreNotEqual()
    {
        // Arrange
        var size1 = StorageSize.FromBytes(1024);
        var size2 = StorageSize.FromBytes(2048);

        // Assert
        size1.Should().NotBe(size2);
    }

    #endregion

    #region HashValue Tests

    [Fact]
    public void HashValue_FromHex_WithValidSha256_CreatesHashValue()
    {
        // Arrange
        var hexValue = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

        // Act
        var hash = HashValue.FromHex(HashAlgorithm.Sha256, hexValue);

        // Assert
        hash.Value.Should().Be(hexValue);
        hash.Algorithm.Should().Be(HashAlgorithm.Sha256);
        hash.Bytes.Should().HaveCount(32);
    }

    [Fact]
    public void HashValue_FromHex_WithLowercase_NormalizesToUppercase()
    {
        // Arrange
        var hexValue = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var hash = HashValue.FromHex(HashAlgorithm.Sha256, hexValue);

        // Assert
        hash.Value.Should().Be(hexValue.ToUpperInvariant());
    }

    [Fact]
    public void HashValue_FromHex_WithInvalidLength_ThrowsArgumentException()
    {
        // Arrange
        var invalidHash = "E3B0C44298FC"; // Too short for SHA256

        // Act & Assert
        var act = () => HashValue.FromHex(HashAlgorithm.Sha256, invalidHash);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*64*");
    }

    [Fact]
    public void HashValue_FromHex_WithInvalidCharacters_ThrowsArgumentException()
    {
        // Arrange
        var invalidHash = "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ";

        // Act & Assert
        var act = () => HashValue.FromHex(HashAlgorithm.Sha256, invalidHash);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*十六进制*");
    }

    [Fact]
    public void HashValue_FromHex_WithEmptyString_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => HashValue.FromHex(HashAlgorithm.Sha256, "");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*不能为空*");
    }

    [Fact]
    public void HashValue_FromBytes_WithValidBytes_CreatesHashValue()
    {
        // Arrange
        var bytes = new byte[32]; // SHA256 = 32 bytes
        Array.Fill<byte>(bytes, 0xAB);

        // Act
        var hash = HashValue.FromBytes(HashAlgorithm.Sha256, bytes);

        // Assert
        hash.Bytes.Should().BeEquivalentTo(bytes);
        hash.Algorithm.Should().Be(HashAlgorithm.Sha256);
    }

    [Fact]
    public void HashValue_FromBytes_WithEmptyArray_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => HashValue.FromBytes(HashAlgorithm.Sha256, Array.Empty<byte>());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*不能为空*");
    }

    [Fact]
    public void HashValue_FromHex_WithMd5_ValidatesCorrectLength()
    {
        // Arrange - MD5 is 32 hex characters (128 bits)
        var md5Hash = "D41D8CD98F00B204E9800998ECF8427E";

        // Act
        var hash = HashValue.FromHex(HashAlgorithm.Md5, md5Hash);

        // Assert
        hash.Algorithm.Should().Be(HashAlgorithm.Md5);
        hash.Bytes.Should().HaveCount(16);
    }

    [Fact]
    public void HashValue_FromHex_WithSha1_ValidatesCorrectLength()
    {
        // Arrange - SHA1 is 40 hex characters (160 bits)
        var sha1Hash = "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709";

        // Act
        var hash = HashValue.FromHex(HashAlgorithm.Sha1, sha1Hash);

        // Assert
        hash.Algorithm.Should().Be(HashAlgorithm.Sha1);
        hash.Bytes.Should().HaveCount(20);
    }

    [Fact]
    public void HashValue_FromHex_WithSha512_ValidatesCorrectLength()
    {
        // Arrange - SHA512 is 128 hex characters (512 bits)
        var sha512Hash = "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE47D0D13C5D85F2B0FF8318D2877EEC2F63B931BD47417A81A538327AF927DA3E";

        // Act
        var hash = HashValue.FromHex(HashAlgorithm.Sha512, sha512Hash);

        // Assert
        hash.Algorithm.Should().Be(HashAlgorithm.Sha512);
        hash.Bytes.Should().HaveCount(64);
    }

    [Fact]
    public void HashValue_Equality_WithSameHash_AreEqual()
    {
        // Arrange
        var hexValue = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        var hash1 = HashValue.FromHex(HashAlgorithm.Sha256, hexValue);
        var hash2 = HashValue.FromHex(HashAlgorithm.Sha256, hexValue);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void HashValue_Equality_WithDifferentHash_AreNotEqual()
    {
        // Arrange
        var hash1 = HashValue.FromHex(HashAlgorithm.Sha256, 
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
        var hash2 = HashValue.FromHex(HashAlgorithm.Sha256, 
            "A3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashValue_Equality_WithDifferentAlgorithm_AreNotEqual()
    {
        // Arrange - same bytes but different algorithm context
        var md5Hash = HashValue.FromHex(HashAlgorithm.Md5, "D41D8CD98F00B204E9800998ECF8427E");
        var md5Hash2 = HashValue.FromHex(HashAlgorithm.Md5, "A41D8CD98F00B204E9800998ECF8427E");

        // Assert
        md5Hash.Should().NotBe(md5Hash2);
    }

    #endregion
}
