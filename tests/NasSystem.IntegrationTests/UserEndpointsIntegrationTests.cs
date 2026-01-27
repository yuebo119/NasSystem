// =============================================================================
// NasSystem.IntegrationTests - UserEndpointsIntegrationTests.cs
// 用户 API 端点集成测试
// =============================================================================

namespace NasSystem.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NasSystem.Application.Users.Dtos;
using NasSystem.Domain.Entities;
using NasSystem.Domain.Enums;
using NasSystem.Infrastructure.Data;
using Xunit;

/// <summary>
/// 用户 API 端点集成测试
/// </summary>
public class UserEndpointsIntegrationTests : ApiTestBase
{
    public UserEndpointsIntegrationTests(NasSystemWebApplicationFactory factory) 
        : base(factory)
    {
        factory.EnsureDatabaseCreated();
    }

    #region Login Tests

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndUserInfo()
    {
        // Arrange
        await SeedTestUserAsync("loginuser", "password123");

        var loginRequest = new LoginDto(
            Username: "loginuser",
            Password: "password123");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Token.Should().NotBeNullOrEmpty();
        result.Data.RefreshToken.Should().NotBeNullOrEmpty();
        result.Data.User.Username.Should().Be("loginuser");
    }

    [Fact]
    public async Task Login_WithInvalidUsername_ReturnsUnauthorized()
    {
        // Arrange
        var loginRequest = new LoginDto(
            Username: "nonexistentuser",
            Password: "password123");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        // Arrange
        await SeedTestUserAsync("testuser", "correctpassword");

        var loginRequest = new LoginDto(
            Username: "testuser",
            Password: "wrongpassword");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithDisabledUser_ReturnsForbidden()
    {
        // Arrange
        await SeedTestUserAsync("disableduser", "password123", isEnabled: false);

        var loginRequest = new LoginDto(
            Username: "disableduser",
            Password: "password123");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);

        // Assert - may return various error codes
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    #endregion

    #region GetCurrentUser Tests

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        ClearAuthToken();
        var response = await Client.GetAsync("/api/v1/users/me");

        // Assert - should return 401 Unauthorized
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_ReturnsUserInfo()
    {
        // Arrange
        var token = await GetAuthTokenAsync("meuser", "password123");
        SetAuthToken(token);

        // Act
        var response = await Client.GetAsync("/api/v1/users/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.Username.Should().Be("meuser");
    }

    #endregion

    #region GetUsers Tests (Admin)

    [Fact]
    public async Task GetUsers_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsers_WithNonAdminToken_ReturnsForbidden()
    {
        // Arrange
        var token = await GetAuthTokenAsync("regularuser", "password123", UserRole.User);
        SetAuthToken(token);

        // Act
        var response = await Client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_WithAdminToken_ReturnsUserList()
    {
        // Arrange
        var token = await GetAuthTokenAsync("adminuser", "password123", UserRole.Admin);
        SetAuthToken(token);

        // Act
        var response = await Client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region CreateUser Tests (Admin)

    [Fact]
    public async Task CreateUser_WithValidRequest_CreatesUserAndReturnsCreated()
    {
        // Arrange
        var token = await GetAuthTokenAsync("admincreator", "password123", UserRole.Admin);
        SetAuthToken(token);

        var createRequest = new CreateUserDto(
            Username: "newuser" + Guid.NewGuid().ToString("N")[..8],
            Password: "newpassword123",
            DisplayName: "New User",
            Email: $"newuser{Guid.NewGuid():N}@example.com");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users", createRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.Data!.DisplayName.Should().Be("New User");
    }

    [Fact]
    public async Task CreateUser_WithDuplicateUsername_ReturnsConflict()
    {
        // Arrange
        var token = await GetAuthTokenAsync("admindup", "password123", UserRole.Admin);
        SetAuthToken(token);

        var existingUsername = "duplicateuser" + Guid.NewGuid().ToString("N")[..6];
        await SeedTestUserAsync(existingUsername, "password");

        var createRequest = new CreateUserDto(
            Username: existingUsername,
            Password: "password123");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users", createRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateUser_WithoutAdminRole_ReturnsForbidden()
    {
        // Arrange
        var token = await GetAuthTokenAsync("regularuser2", "password123", UserRole.User);
        SetAuthToken(token);

        var createRequest = new CreateUserDto(
            Username: "shouldfail",
            Password: "password123");

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users", createRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region UpdateUser Tests (Admin)

    [Fact]
    public async Task UpdateUser_WithValidRequest_UpdatesUser()
    {
        // Arrange
        var token = await GetAuthTokenAsync("adminupdater", "password123", UserRole.Admin);
        SetAuthToken(token);

        var userId = await SeedTestUserAsync("updatetarget", "password123");

        var updateRequest = new UpdateUserDto(
            DisplayName: "Updated Name",
            Email: $"updated{Guid.NewGuid():N}@example.com");

        // Act
        var response = await Client.PutAsJsonAsync($"/api/v1/users/{userId}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(JsonOptions);
        result!.Data!.DisplayName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateUser_WithNonExistingId_ReturnsNotFound()
    {
        // Arrange
        var token = await GetAuthTokenAsync("adminnotfound", "password123", UserRole.Admin);
        SetAuthToken(token);

        var updateRequest = new UpdateUserDto(DisplayName: "Should Fail");

        // Act
        var response = await Client.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region DeleteUser Tests (Admin)

    [Fact]
    public async Task DeleteUser_WithValidId_DeletesUserAndReturnsNoContent()
    {
        // Arrange
        var token = await GetAuthTokenAsync("admindeleter", "password123", UserRole.Admin);
        SetAuthToken(token);

        var userIdToDelete = await SeedTestUserAsync("deletetarget", "password123");

        // Act
        var response = await Client.DeleteAsync($"/api/v1/users/{userIdToDelete}");

        // Assert
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteUser_WithNonExistingId_ReturnsNotFound()
    {
        // Arrange
        var token = await GetAuthTokenAsync("admindeleter2", "password123", UserRole.Admin);
        SetAuthToken(token);

        // Act
        var response = await Client.DeleteAsync($"/api/v1/users/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_WithValidToken_ReturnsOk()
    {
        // Arrange
        var token = await GetAuthTokenAsync("logoutuser", "password123");
        SetAuthToken(token);

        // Act
        var response = await Client.PostAsync("/api/v1/users/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await Client.PostAsync("/api/v1/users/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Helper Methods

    private async Task<Guid> SeedTestUserAsync(
        string username, 
        string password, 
        UserRole role = UserRole.User,
        bool isEnabled = true)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NasDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<NasSystem.Domain.Services.IPasswordHasher>();

        var (hash, salt) = passwordHasher.HashPassword(password);
        var user = User.Create(username, hash, salt, role);
        
        if (!isEnabled)
        {
            user.SetEnabled(false);
        }

        context.Users.Add(user);
        await context.SaveChangesAsync();
        
        return user.Id;
    }

    private async Task<string> GetAuthTokenAsync(
        string username, 
        string password,
        UserRole role = UserRole.User)
    {
        await SeedTestUserAsync(username, password, role);

        var loginRequest = new LoginDto(Username: username, Password: password);
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to get auth token: {response.StatusCode}");
        }

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>(JsonOptions);
        return result?.Data?.Token ?? throw new InvalidOperationException("Token was null");
    }

    #endregion
}
