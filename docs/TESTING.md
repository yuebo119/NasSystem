# NAS System 测试文档

## 1. 测试概述

### 1.1 测试策略

NAS System 采用多层次测试策略：

| 测试类型 | 目的 | 工具 |
|----------|------|------|
| 单元测试 | 验证单个组件的正确性 | xUnit |
| 集成测试 | 验证组件间的交互 | xUnit + WebApplicationFactory |
| API 测试 | 验证 API 端点的行为 | xUnit + HttpClient |

### 1.2 测试项目结构

```
tests/
├── NasSystem.UnitTests/           # 单元测试
│   ├── Domain/                    # 领域层测试
│   │   ├── FileItemTests.cs
│   │   ├── StoragePoolTests.cs
│   │   ├── UserTests.cs
│   │   └── ValueObjectTests.cs
│   └── Services/                  # 服务层测试
│       ├── FileServiceTests.cs
│       ├── StorageServiceTests.cs
│       └── UserServiceTests.cs
│
└── NasSystem.IntegrationTests/    # 集成测试
    ├── ApiTestBase.cs             # 测试基类
    ├── BasicIntegrationTests.cs   # 基础集成测试
    ├── FileEndpointsIntegrationTests.cs
    └── UserEndpointsIntegrationTests.cs
```

---

## 2. 单元测试

### 2.1 领域实体测试

#### User 实体测试

```csharp
public class UserTests
{
    [Fact]
    public void Create_WithValidData_ShouldCreateUser()
    {
        // Arrange
        var username = "testuser";
        var passwordHash = "hash";
        var passwordSalt = "salt";

        // Act
        var user = User.Create(username, passwordHash, passwordSalt);

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(username.ToLowerInvariant(), user.Username);
        Assert.Equal(UserRole.User, user.Role);
        Assert.True(user.IsEnabled);
    }

    [Fact]
    public void UpdatePassword_ShouldUpdateHashAndSalt()
    {
        // Arrange
        var user = User.Create("testuser", "oldhash", "oldsalt");
        var newHash = "newhash";
        var newSalt = "newsalt";

        // Act
        user.UpdatePassword(newHash, newSalt);

        // Assert
        Assert.Equal(newHash, user.PasswordHash);
        Assert.Equal(newSalt, user.PasswordSalt);
    }

    [Fact]
    public void SetEnabled_ShouldToggleEnabledState()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");

        // Act
        user.SetEnabled(false);

        // Assert
        Assert.False(user.IsEnabled);
    }
}
```

#### FileItem 实体测试

```csharp
public class FileItemTests
{
    [Fact]
    public void CreateFolder_ShouldCreateFolderWithCorrectType()
    {
        // Arrange & Act
        var folder = FileItem.CreateFolder(
            "TestFolder",
            "/test/TestFolder",
            "C:\\Storage\\test\\TestFolder",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        Assert.Equal(FileItemType.Folder, folder.ItemType);
        Assert.True(folder.IsFolder);
        Assert.Equal(0, folder.SizeBytes);
    }

    [Fact]
    public void CreateFile_ShouldCreateFileWithCorrectProperties()
    {
        // Arrange & Act
        var file = FileItem.CreateFile(
            "document.pdf",
            "/documents/document.pdf",
            "C:\\Storage\\documents\\document.pdf",
            1024,
            "application/pdf",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Assert
        Assert.Equal(FileItemType.File, file.ItemType);
        Assert.True(file.IsFile);
        Assert.Equal(1024, file.SizeBytes);
        Assert.Equal(".pdf", file.Extension);
    }

    [Fact]
    public void Rename_ShouldUpdateNameAndPath()
    {
        // Arrange
        var file = FileItem.CreateFile(
            "old.txt",
            "/documents/old.txt",
            "C:\\Storage\\documents\\old.txt",
            100,
            "text/plain",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Act
        file.Rename("new.txt");

        // Assert
        Assert.Equal("new.txt", file.Name);
        Assert.Contains("new.txt", file.Path);
    }

    [Fact]
    public void SoftDelete_ShouldMarkAsDeleted()
    {
        // Arrange
        var file = FileItem.CreateFile(
            "test.txt",
            "/test.txt",
            "C:\\Storage\\test.txt",
            100,
            "text/plain",
            Guid.NewGuid(),
            Guid.NewGuid());

        // Act
        file.SoftDelete();

        // Assert
        Assert.True(file.IsDeleted);
        Assert.NotNull(file.DeletedAt);
    }
}
```

#### StoragePool 实体测试

```csharp
public class StoragePoolTests
{
    [Fact]
    public void Create_ShouldCreatePoolWithCorrectDefaults()
    {
        // Arrange & Act
        var pool = StoragePool.Create("TestPool", "D:\\Storage");

        // Assert
        Assert.Equal("TestPool", pool.Name);
        Assert.Equal("D:\\Storage", pool.MountPath);
        Assert.False(pool.IsDefault);
        Assert.True(pool.IsEnabled);
    }

    [Fact]
    public void UpdateCapacity_ShouldUpdateBytesCorrectly()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "D:\\Storage");
        var total = 1000000000L;
        var used = 500000000L;

        // Act
        pool.UpdateCapacity(total, used);

        // Assert
        Assert.Equal(total, pool.TotalBytes);
        Assert.Equal(used, pool.UsedBytes);
        Assert.Equal(500000000L, pool.AvailableBytes);
    }

    [Fact]
    public void UsagePercentage_ShouldCalculateCorrectly()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "D:\\Storage");
        pool.UpdateCapacity(1000, 500);

        // Act
        var percentage = pool.UsagePercentage;

        // Assert
        Assert.Equal(50.0, percentage);
    }

    [Fact]
    public void HasEnoughSpace_ShouldReturnCorrectResult()
    {
        // Arrange
        var pool = StoragePool.Create("TestPool", "D:\\Storage");
        pool.UpdateCapacity(1000, 500);

        // Act & Assert
        Assert.True(pool.HasEnoughSpace(400));
        Assert.False(pool.HasEnoughSpace(600));
    }
}
```

### 2.2 值对象测试

```csharp
public class ValueObjectTests
{
    [Fact]
    public void FilePath_ShouldNormalizePath()
    {
        // Arrange & Act
        var path1 = FilePath.Create("/documents/file.txt");
        var path2 = FilePath.Create("documents\\file.txt");

        // Assert
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void StorageSize_ShouldFormatCorrectly()
    {
        // Arrange
        var size = StorageSize.FromBytes(1073741824);

        // Act
        var display = size.ToDisplayString();

        // Assert
        Assert.Equal("1 GB", display);
    }
}
```

### 2.3 服务层测试

```csharp
public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _jwtServiceMock = new Mock<IJwtService>();
        
        _userService = new UserService(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _jwtServiceMock.Object);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        _userRepositoryMock.Setup(r => r.GetByUsernameAsync("testuser"))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.Verify("password", "hash", "salt"))
            .Returns(true);
        _jwtServiceMock.Setup(j => j.GenerateToken(user))
            .Returns("jwt-token");

        var request = new LoginDto { Username = "testuser", Password = "password" };

        // Act
        var result = await _userService.LoginAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Token);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldReturnError()
    {
        // Arrange
        var user = User.Create("testuser", "hash", "salt");
        _userRepositoryMock.Setup(r => r.GetByUsernameAsync("testuser"))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.Verify("wrongpassword", "hash", "salt"))
            .Returns(false);

        var request = new LoginDto { Username = "testuser", Password = "wrongpassword" };

        // Act
        var result = await _userService.LoginAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.InvalidCredentials", result.Error.Code);
    }
}
```

---

## 3. 集成测试

### 3.1 测试基类

```csharp
public abstract class ApiTestBase : IClassFixture<WebApplicationFactory<Program>>
{
    protected readonly HttpClient Client;
    protected readonly WebApplicationFactory<Program> Factory;

    protected ApiTestBase(NasSystemWebApplicationFactory factory)
    {
        // 工厂在 ConfigureWebHost 里摘掉按生产连接串注册的 PalormSessionHolder，
        // 换成指向本工厂临时库文件的实例；库名每工厂唯一，测试间天然隔离
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected async Task<string> GetAuthTokenAsync()
    {
        var loginRequest = new { username = "admin", password = "admin123" };
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", loginRequest);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>();
        return result?.Data?.Token ?? string.Empty;
    }

    protected void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
    }
}
```

### 3.2 用户端点测试

```csharp
public class UserEndpointsIntegrationTests : ApiTestBase
{
    public UserEndpointsIntegrationTests(WebApplicationFactory<Program> factory) 
        : base(factory) { }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnToken()
    {
        // Arrange
        var request = new { username = "admin", password = "admin123" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResultDto>>();
        Assert.True(result?.Success);
        Assert.NotEmpty(result?.Data?.Token ?? "");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturn401()
    {
        // Arrange
        var request = new { username = "admin", password = "wrongpassword" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/users/login", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_ShouldReturnUser()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync("/api/v1/users/me");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        Assert.True(result?.Success);
        Assert.Equal("admin", result?.Data?.Username);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ShouldReturn401()
    {
        // Act
        var response = await Client.GetAsync("/api/v1/users/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

### 3.3 文件端点测试

```csharp
public class FileEndpointsIntegrationTests : ApiTestBase
{
    public FileEndpointsIntegrationTests(WebApplicationFactory<Program> factory) 
        : base(factory) { }

    [Fact]
    public async Task GetDirectoryContents_ShouldReturnItems()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync("/api/v1/files/browse/");

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<DirectoryContentDto>>();
        Assert.True(result?.Success);
        Assert.NotNull(result?.Data?.Items);
    }

    [Fact]
    public async Task CreateFolder_ShouldCreateNewFolder()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        SetAuthHeader(token);
        var request = new { parentPath = "/", name = "TestFolder" };

        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/files/folder", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<FileItemDto>>();
        Assert.True(result?.Success);
        Assert.Equal("TestFolder", result?.Data?.Name);
    }

    [Fact]
    public async Task UploadFile_ShouldUploadSuccessfully()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        SetAuthHeader(token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("test content"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.txt");
        content.Add(new StringContent("/"), "path");

        // Act
        var response = await Client.PostAsync("/api/v1/files/upload", content);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SearchFiles_ShouldReturnMatchingFiles()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync("/api/v1/files/search?query=test");

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

---

## 4. 运行测试

### 4.1 命令行运行

```bash
# 运行所有测试
dotnet test --solution NasSystem.sln

# 运行单元测试
dotnet test --project tests/NasSystem.UnitTests

# 运行集成测试
dotnet test --project tests/NasSystem.IntegrationTests

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~UserTests"

# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~Login_WithValidCredentials"

# 生成测试覆盖率报告（MTP 模式由 coverlet.MTP 扩展提供）
dotnet test --coverlet --coverlet-output-format cobertura
```

### 4.2 Visual Studio / Rider

1. 打开测试资源管理器
2. 选择要运行的测试
3. 点击运行或调试

---

## 5. 测试覆盖率

### 5.1 当前覆盖率

| 模块 | 覆盖率 |
|------|--------|
| NasSystem.Domain | 85% |
| NasSystem.Application | 75% |
| NasSystem.Infrastructure | 60% |
| NasSystem.Api | 70% |

### 5.2 覆盖率目标

- 领域层：> 90%
- 应用层：> 80%
- 基础设施层：> 60%
- API 层：> 70%

---

## 6. 测试最佳实践

### 6.1 命名规范

```
[测试方法名]_[测试场景]_[预期结果]

例如：
- Login_WithValidCredentials_ShouldReturnToken
- CreateFolder_WithExistingName_ShouldReturnError
- Delete_NonExistentFile_ShouldReturn404
```

### 6.2 AAA 模式

```csharp
[Fact]
public void TestMethod()
{
    // Arrange - 准备测试数据
    var input = "test";

    // Act - 执行被测方法
    var result = _service.Process(input);

    // Assert - 验证结果
    Assert.NotNull(result);
}
```

### 6.3 测试隔离

- 每个测试应该独立运行
- 使用每测试类独立的临时 SQLite 文件库避免测试间干扰（PalORM 无内存 provider，真库还顺带覆盖唯一索引与外键）
- 清理测试产生的临时文件

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2026-01-29 | 初始版本 |
