// =============================================================================
// NasSystem.UnitTests - UserTests.cs
// 用户实体单元测试
// =============================================================================

namespace NasSystem.UnitTests.Domain;

using FluentAssertions;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Events;
using Xunit;

/// <summary>
/// User 实体单元测试
/// </summary>
public class UserTests
{
    #region Create Tests

    [Fact]
    public void Create_WithValidParameters_CreatesUser()
    {
        // Act
        var user = User.Create(
            username: "testuser",
            passwordHash: "hash123",
            passwordSalt: "salt456");

        // Assert
        user.Should().NotBeNull();
        user.Username.Should().Be("testuser");
        user.PasswordHash.Should().Be("hash123");
        user.PasswordSalt.Should().Be("salt456");
        user.Role.Should().Be(UserRole.User);
        user.IsEnabled.Should().BeTrue();
        user.DisplayName.Should().Be("testuser");
        user.HomeDirectory.Should().Be("/home/testuser");
    }

    [Fact]
    public void Create_WithUppercaseUsername_NormalizesToLowercase()
    {
        // Act
        var user = User.Create(
            username: "TestUser",
            passwordHash: "hash",
            passwordSalt: "salt");

        // Assert
        user.Username.Should().Be("testuser");
        user.HomeDirectory.Should().Be("/home/testuser");
    }

    [Fact]
    public void Create_WithAdminRole_SetsAdminRole()
    {
        // Act
        var user = User.Create(
            username: "admin",
            passwordHash: "hash",
            passwordSalt: "salt",
            role: UserRole.Admin);

        // Assert
        user.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void Create_WithDisplayName_SetsDisplayName()
    {
        // Act
        var user = User.Create(
            username: "testuser",
            passwordHash: "hash",
            passwordSalt: "salt",
            displayName: "Test User");

        // Assert
        user.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public void Create_AddsUserCreatedEvent()
    {
        // Act
        var user = User.Create(
            username: "testuser",
            passwordHash: "hash",
            passwordSalt: "salt");

        // Assert
        user.DomainEvents.Should().ContainSingle();
        user.DomainEvents.First().Should().BeOfType<UserCreatedEvent>();
    }

    #endregion

    #region UpdatePassword Tests

    [Fact]
    public void UpdatePassword_UpdatesHashAndSalt()
    {
        // Arrange
        var user = User.Create("testuser", "oldhash", "oldsalt");
        user.ClearDomainEvents();

        // Act
        user.UpdatePassword("newhash", "newsalt");

        // Assert
        user.PasswordHash.Should().Be("newhash");
        user.PasswordSalt.Should().Be("newsalt");
    }

    [Fact]
    public void UpdatePassword_AddsPasswordChangedEvent()
    {
        // Arrange
        var user = User.Create("testuser", "oldhash", "oldsalt");
        user.ClearDomainEvents();

        // Act
        user.UpdatePassword("newhash", "newsalt");

        // Assert
        user.DomainEvents.Should().ContainSingle();
        user.DomainEvents.First().Should().BeOfType<UserPasswordChangedEvent>();
    }

    #endregion

    #region UpdateProfile Tests

    [Fact]
    public void UpdateProfile_UpdatesDisplayNameAndEmail()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");

        // Act
        user.UpdateProfile("New Display Name", "test@example.com");

        // Assert
        user.DisplayName.Should().Be("New Display Name");
        user.Email.Should().Be("test@example.com");
    }

    #endregion

    #region UpdateRole Tests

    [Fact]
    public void UpdateRole_ChangesRole()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.ClearDomainEvents();

        // Act
        user.UpdateRole(UserRole.Admin);

        // Assert
        user.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void UpdateRole_AddsRoleChangedEvent()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.ClearDomainEvents();

        // Act
        user.UpdateRole(UserRole.Admin);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var evt = user.DomainEvents.First() as UserRoleChangedEvent;
        evt.Should().NotBeNull();
        evt!.OldRole.Should().Be(UserRole.User);
        evt.NewRole.Should().Be(UserRole.Admin);
    }

    #endregion

    #region SetEnabled Tests

    [Fact]
    public void SetEnabled_ToFalse_DisablesUser()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");

        // Act
        user.SetEnabled(false);

        // Assert
        user.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_ToTrue_EnablesUser()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.SetEnabled(false);

        // Act
        user.SetEnabled(true);

        // Assert
        user.IsEnabled.Should().BeTrue();
    }

    #endregion

    #region RecordLogin Tests

    [Fact]
    public void RecordLogin_SetsLastLoginAt()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.LastLoginAt.Should().BeNull();

        // Act
        user.RecordLogin();

        // Assert
        user.LastLoginAt.Should().NotBeNull();
        user.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Quota Tests

    [Fact]
    public void SetQuota_SetsQuotaBytes()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        var quotaBytes = 1024L * 1024 * 1024 * 10; // 10GB

        // Act
        user.SetQuota(quotaBytes);

        // Assert
        user.QuotaBytes.Should().Be(quotaBytes);
    }

    [Fact]
    public void UpdateUsedSpace_SetsUsedBytes()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");

        // Act
        user.UpdateUsedSpace(1024 * 1024 * 500); // 500MB

        // Assert
        user.UsedBytes.Should().Be(1024 * 1024 * 500);
    }

    [Fact]
    public void IsOverQuota_WhenUnderQuota_ReturnsFalse()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.SetQuota(1024L * 1024 * 1024); // 1GB quota
        user.UpdateUsedSpace(512 * 1024 * 1024); // 512MB used

        // Assert
        user.IsOverQuota.Should().BeFalse();
    }

    [Fact]
    public void IsOverQuota_WhenOverQuota_ReturnsTrue()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.SetQuota(1024L * 1024 * 1024); // 1GB quota
        user.UpdateUsedSpace(1024L * 1024 * 1024 * 2); // 2GB used

        // Assert
        user.IsOverQuota.Should().BeTrue();
    }

    [Fact]
    public void IsOverQuota_WhenNoQuota_ReturnsFalse()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.UpdateUsedSpace(1024L * 1024 * 1024 * 100); // 100GB used

        // Assert
        user.IsOverQuota.Should().BeFalse();
    }

    [Fact]
    public void RemainingQuota_CalculatesCorrectly()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        user.SetQuota(1024L * 1024 * 1024); // 1GB quota
        user.UpdateUsedSpace(256 * 1024 * 1024); // 256MB used

        // Assert
        user.RemainingQuota.Should().Be(1024L * 1024 * 1024 - 256 * 1024 * 1024);
    }

    [Fact]
    public void RemainingQuota_WhenNoQuota_ReturnsNull()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");

        // Assert
        user.RemainingQuota.Should().BeNull();
    }

    #endregion
}
