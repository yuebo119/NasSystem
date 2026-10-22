// =============================================================================
// NasSystem.UnitTests - UserServiceTests.cs
// 用户服务单元测试
// =============================================================================

namespace NasSystem.UnitTests.Services;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NasSystem.Application.Common;
using NasSystem.Application.Users;
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Common;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Domain.Repositories;
using NasSystem.Domain.Services;
using Xunit;

/// <summary>
/// UserService 单元测试
/// </summary>
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IFileItemRepository> _fileItemRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _fileItemRepositoryMock = new Mock<IFileItemRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _jwtServiceMock = new Mock<IJwtService>();

        // 已用空间默认 0：个别用例要特定值时在用例内再 Setup 覆盖
        _fileItemRepositoryMock
            .Setup(x => x.CalculateUserUsedSpaceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        _fileItemRepositoryMock
            .Setup(x => x.CalculateUsersUsedSpaceAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long>());

        _sut = new UserService(
            _userRepositoryMock.Object,
            _fileItemRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _passwordHasherMock.Object,
            _jwtServiceMock.Object,
            Options.Create(new JwtSettings()),
            // 时间源（CONVENTIONS §5.1 / 审计 F-028）
            new NasSystem.Infrastructure.Services.SystemDateTimeProvider());
    }

    #region GetByIdAsync Tests

    [Fact]
    public async Task GetByIdAsync_WithExistingUser_ReturnsUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.GetByIdAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetByIdAsync_UsedBytes_ComesFromRealtimeAggregateNotEntityField()
    {
        // 锁定修复：User.UsedBytes 字段没有生产维护者，读路径必须取实时聚合。
        // 修复前返回实体字段值（恒 0），本测试红；修复后返回聚合值，转绿
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _fileItemRepositoryMock
            .Setup(x => x.CalculateUserUsedSpaceAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4_096L);

        var result = await _sut.GetByIdAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.UsedBytes.Should().Be(4_096L);
        result.Value.UsedDisplay.Should().Be("4 KB");
    }

    [Fact]
    public async Task GetPagedAsync_UsedBytes_ComesFromBatchAggregate()
    {
        // 分页路径批量聚合：有文件的用户返回聚合值，无文件的用户返回 0
        var withFiles = User.Create("withfiles", "hash", "salt");
        SetUserId(withFiles, Guid.NewGuid());
        var withoutFiles = User.Create("nofiles", "hash", "salt");
        SetUserId(withoutFiles, Guid.NewGuid());

        _userRepositoryMock
            .Setup(x => x.GetPagedAsync(1, 20, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<User> { withFiles, withoutFiles }, 2));
        _fileItemRepositoryMock
            .Setup(x => x.CalculateUsersUsedSpaceAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, long> { [withFiles.Id] = 2_048L });

        var result = await _sut.GetPagedAsync(1, 20);

        result.IsSuccess.Should().BeTrue();
        var dtos = result.Value.Items.ToList();
        dtos.Should().HaveCount(2);
        dtos.Single(u => u.Username == "withfiles").UsedBytes.Should().Be(2_048L);
        dtos.Single(u => u.Username == "nofiles").UsedBytes.Should().Be(0L);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _sut.GetByIdAsync(userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesUser()
    {
        // Arrange
        var dto = new CreateUserDto(
            Username: "newuser",
            Password: "password123",
            DisplayName: "New User",
            Email: "newuser@example.com");

        _userRepositoryMock
            .Setup(x => x.UsernameExistsAsync("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _userRepositoryMock
            .Setup(x => x.EmailExistsAsync("newuser@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _passwordHasherMock
            .Setup(x => x.HashPassword("password123"))
            .Returns(("hashedpwd", "salt123"));

        _userRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, CancellationToken _) => u);

        _unitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("newuser");
        result.Value.DisplayName.Should().Be("New User");
        result.Value.Email.Should().Be("newuser@example.com");

        _userRepositoryMock.Verify(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithExistingUsername_ReturnsConflictError()
    {
        // Arrange
        var dto = new CreateUserDto(Username: "existinguser", Password: "password123");

        _userRepositoryMock
            .Setup(x => x.UsernameExistsAsync("existinguser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("existinguser");
        result.Error.Message.Should().Contain("已存在");

        _userRepositoryMock.Verify(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithExistingEmail_ReturnsConflictError()
    {
        // Arrange
        var dto = new CreateUserDto(
            Username: "newuser",
            Password: "password123",
            Email: "existing@example.com");

        _userRepositoryMock
            .Setup(x => x.UsernameExistsAsync("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _userRepositoryMock
            .Setup(x => x.EmailExistsAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("existing@example.com");
    }

    [Fact]
    public async Task CreateAsync_WithAdminRole_CreatesAdminUser()
    {
        // Arrange
        var dto = new CreateUserDto(
            Username: "adminuser",
            Password: "password123",
            Role: "Admin");

        _userRepositoryMock
            .Setup(x => x.UsernameExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _passwordHasherMock
            .Setup(x => x.HashPassword(It.IsAny<string>()))
            .Returns(("hash", "salt"));

        // Act
        var result = await _sut.CreateAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Admin");
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WithValidData_UpdatesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        var dto = new UpdateUserDto(
            DisplayName: "Updated Name",
            Email: "updated@example.com");

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _userRepositoryMock
            .Setup(x => x.EmailExistsAsync("updated@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.UpdateAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("Updated Name");
        result.Value.Email.Should().Be("updated@example.com");
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new UpdateUserDto(DisplayName: "New Name");

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _sut.UpdateAsync(userId, dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task UpdateAsync_WithRoleChange_UpdatesRole()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        var dto = new UpdateUserDto(Role: "Admin");

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.UpdateAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task UpdateAsync_WithDisableUser_DisablesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        var dto = new UpdateUserDto(IsEnabled: false);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.UpdateAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingUser_DeletesUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.DeleteAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _userRepositoryMock.Verify(x => x.DeleteAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _sut.DeleteAsync(userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task DeleteAsync_WithSuperAdmin_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("superadmin", "hash", "salt", role: UserRole.SuperAdmin);
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.DeleteAsync(userId);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("CannotDeleteSuperAdmin");
    }

    #endregion

    #region LoginAsync Tests

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsLoginResult()
    {
        // Arrange
        var user = User.Create("testuser", "hashedpwd", "salt123");
        var dto = new LoginDto { Username = "testuser", Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword("password123", "hashedpwd", "salt123"))
            .Returns(true);

        _jwtServiceMock
            .Setup(x => x.GenerateAccessToken(user, It.IsAny<TimeSpan?>()))
            .Returns("access_token_123");

        _jwtServiceMock
            .Setup(x => x.GenerateRefreshToken())
            .Returns("refresh_token_456");

        _jwtServiceMock
            .Setup(x => x.GetTokenExpiration("access_token_123"))
            .Returns(DateTime.UtcNow.AddHours(1));

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("access_token_123");
        result.Value.RefreshToken.Should().Be("refresh_token_456");
        result.Value.User.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task LoginAsync_WithRememberMe_PassesExtendedLifetime()
    {
        // 2026-10-21 审计 P1 回归：LoginDto.RememberMe 曾经全链路无人读，
        // 前端"记住我"是死控件。这里锁勾选时必须把延长后的有效期交给
        // JWT 服务，令牌过期时间随之变长
        var user = User.Create("rememberuser", "hashedpwd", "salt123");
        var dto = new LoginDto { Username = "rememberuser", Password = "password123", RememberMe = true };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("rememberuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword("password123", "hashedpwd", "salt123"))
            .Returns(true);

        TimeSpan? capturedLifetime = null;
        _jwtServiceMock
            .Setup(x => x.GenerateAccessToken(user, It.IsAny<TimeSpan?>()))
            .Callback((User _, TimeSpan? lifetime) => capturedLifetime = lifetime)
            .Returns("access_token_123");

        _jwtServiceMock
            .Setup(x => x.GenerateRefreshToken())
            .Returns("refresh_token_456");

        _jwtServiceMock
            .Setup(x => x.GetTokenExpiration("access_token_123"))
            .Returns(DateTime.UtcNow.AddDays(7));

        var result = await _sut.LoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        capturedLifetime.Should().Be(TimeSpan.FromDays(7), "勾选记住我后令牌应按配置的天数签发");
    }

    [Fact]
    public async Task LoginAsync_WithoutRememberMe_UsesDefaultLifetime()
    {
        // 不勾选时不得传覆盖值：回落配置的 AccessTokenExpirationMinutes
        var user = User.Create("plainuser", "hashedpwd", "salt123");
        var dto = new LoginDto { Username = "plainuser", Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("plainuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword("password123", "hashedpwd", "salt123"))
            .Returns(true);

        TimeSpan? capturedLifetime = TimeSpan.FromMinutes(1);
        _jwtServiceMock
            .Setup(x => x.GenerateAccessToken(user, It.IsAny<TimeSpan?>()))
            .Callback((User _, TimeSpan? lifetime) => capturedLifetime = lifetime)
            .Returns("access_token_123");

        _jwtServiceMock
            .Setup(x => x.GenerateRefreshToken())
            .Returns("refresh_token_456");

        _jwtServiceMock
            .Setup(x => x.GetTokenExpiration("access_token_123"))
            .Returns(DateTime.UtcNow.AddHours(1));

        var result = await _sut.LoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        capturedLifetime.Should().BeNull("不勾选记住我时不应覆盖有效期");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidUsername_ReturnsInvalidCredentialsError()
    {
        // Arrange
        var dto = new LoginDto { Username = "nonexistent", Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.InvalidCredentials");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ReturnsInvalidCredentialsError()
    {
        // Arrange
        var user = User.Create("testuser", "hashedpwd", "salt123");
        var dto = new LoginDto { Username = "testuser", Password = "wrongpassword" };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword("wrongpassword", "hashedpwd", "salt123"))
            .Returns(false);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.InvalidCredentials");
    }

    [Fact]
    public async Task LoginAsync_WithDisabledUser_ReturnsAccountDisabledError()
    {
        // Arrange
        var user = User.Create("testuser", "hashedpwd", "salt123");
        user.SetEnabled(false);
        var dto = new LoginDto { Username = "testuser", Password = "password123" };

        _userRepositoryMock
            .Setup(x => x.GetByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _sut.LoginAsync(dto);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.Disabled");
    }

    #endregion

    #region ChangePasswordAsync Tests

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_ChangesPassword()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "oldhash", "oldsalt");
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword("currentpwd", "oldhash", "oldsalt"))
            .Returns(true);

        _passwordHasherMock
            .Setup(x => x.HashPassword("newpwd"))
            .Returns(("newhash", "newsalt"));

        // Act
        var result = await _sut.ChangePasswordAsync(userId, "currentpwd", "newpwd");

        // Assert
        result.IsSuccess.Should().BeTrue();
        _userRepositoryMock.Verify(x => x.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithInvalidCurrentPassword_ReturnsPasswordMismatchError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create("testuser", "hash", "salt");
        SetUserId(user, userId);

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(x => x.VerifyPassword(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _sut.ChangePasswordAsync(userId, "wrongpwd", "newpwd");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.PasswordMismatch");
    }

    [Fact]
    public async Task ChangePasswordAsync_WithNonExistingUser_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _sut.ChangePasswordAsync(userId, "current", "new");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    #endregion

    #region Helper Methods

    private static void SetUserId(User user, Guid id)
    {
        // Use reflection to set the Id property for testing purposes
        var idProperty = typeof(Entity).GetProperty("Id");
        idProperty?.SetValue(user, id);
    }

    #endregion
}
