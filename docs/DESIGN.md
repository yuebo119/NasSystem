# NAS System 设计文档

## 1. 系统架构

### 1.1 整体架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        客户端层                                   │
├─────────────────────────────────────────────────────────────────┤
│     Web Browser (Vue 3)    │    API Client    │   分享访问页面    │
└──────────────┬─────────────┴────────┬─────────┴────────┬────────┘
               │                      │                  │
               ▼                      ▼                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                     接入层 (Nginx)                               │
│         反向代理 / SSL 终止 / 静态文件服务                         │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                   API 层 (NasSystem.Api)                         │
├─────────────────────────────────────────────────────────────────┤
│  Minimal API Endpoints  │  SignalR Hub  │  Middleware  │ Scalar  │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                应用层 (NasSystem.Application)                     │
├─────────────────────────────────────────────────────────────────┤
│ UserService │ FileService │ FileShareService │ StorageService    │
│ FileVersionService │ ThumbnailService                            │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                  领域层 (NasSystem.Domain)                        │
├─────────────────────────────────────────────────────────────────┤
│  Entities  │  Value Objects  │  Domain Events  │  Repositories   │
└──────────────────────────────┬──────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│               基础设施层 (NasSystem.Infrastructure)               │
├─────────────────────────────────────────────────────────────────┤
│  PalORM + SQLite  │  File System  │  JWT Service  │  Hasher     │
└──────────────────────────────┬──────────────────────────────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
        ┌──────────┐    ┌──────────┐     ┌──────────┐
        │ SQLite   │    │ 文件系统  │     │ 内存缓存  │
        │ Database │    │ Storage  │     │ Cache    │
        └──────────┘    └──────────┘     └──────────┘
```

### 1.2 分层架构

采用清洁架构 (Clean Architecture) 设计：

| 层级 | 项目 | 职责 |
|------|------|------|
| API 层 | NasSystem.Api | HTTP 端点、中间件、SignalR Hub |
| 应用层 | NasSystem.Application | 业务逻辑、服务接口、DTO |
| 领域层 | NasSystem.Domain | 实体、值对象、领域事件、仓储接口 |
| 基础设施层 | NasSystem.Infrastructure | 数据访问、外部服务实现 |
| 共享层 | NasSystem.Shared | 通用类型、Result 模式 |

**依赖规则**：外层依赖内层，内层不依赖外层。

---

## 2. 技术选型

### 2.1 后端技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 11.0 | 运行时框架 |
| ASP.NET Core | 11.0 | Web API 框架 |
| PalORM | 6.1.0 | ORM 框架（编译期生成，SQLite 方言） |
| SQLite | 3.x | 嵌入式数据库 |
| Serilog | Latest | 结构化日志 |
| FluentValidation | 12.x | 输入验证 |
| SignalR | 11.0 | 实时通信 |

### 2.2 前端技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| Vue | 3.5 | 前端框架 |
| TypeScript | 5.x | 类型安全 |
| Vite | 6.x | 构建工具 |
| Element Plus | 2.9 | UI 组件库 |
| Pinia | 2.x | 状态管理 |
| Vue Router | 4.x | 路由管理 |
| Axios | 1.x | HTTP 客户端 |

---

## 3. 数据模型设计

### 3.1 实体关系图

```
┌─────────────────┐       ┌─────────────────┐       ┌─────────────────┐
│      User       │       │    FileItem     │       │   StoragePool   │
├─────────────────┤       ├─────────────────┤       ├─────────────────┤
│ Id (PK)         │◄──┐   │ Id (PK)         │   ┌──►│ Id (PK)         │
│ Username        │   │   │ Name            │   │   │ Name            │
│ PasswordHash    │   │   │ Path            │   │   │ MountPath       │
│ PasswordSalt    │   │   │ PhysicalPath    │   │   │ Description     │
│ DisplayName     │   │   │ ItemType        │   │   │ TotalBytes      │
│ Email           │   │   │ SizeBytes       │   │   │ UsedBytes       │
│ Role            │   │   │ MimeType        │   │   │ MaxCapacityBytes│
│ IsEnabled       │   │   │ Extension       │   │   │ IsDefault       │
│ QuotaBytes      │   │   │ Hash            │   │   │ IsEnabled       │
│ UsedBytes       │   │   │ OwnerId (FK)────┘   │   │ CreatedAt       │
│ HomeDirectory   │   │   │ ParentId (FK)───────│   │ UpdatedAt       │
│ LastLoginAt     │   │   │ StoragePoolId (FK)──┘   └─────────────────┘
│ CreatedAt       │   │   │ IsDeleted       │
│ UpdatedAt       │   │   │ DeletedAt       │
└─────────────────┘   │   │ IsHidden        │
                      │   │ IsReadOnly      │
                      │   │ CreatedAt       │
                      │   │ UpdatedAt       │
                      │   └─────────────────┘
                      │           │
                      │           │ 1:N
                      │           ▼
                      │   ┌─────────────────┐
                      │   │   FileVersion   │
                      │   ├─────────────────┤
                      │   │ Id (PK)         │
                      │   │ FileItemId (FK) │
                      │   │ VersionNumber   │
                      │   │ StoragePath     │
                      │   │ FileSize        │
                      │   │ FileHash        │
                      │   │ Comment         │
                      │   │ IsCurrent       │
                      │   │ CreatedByUserId │
                      │   │ CreatedAt       │
                      │   └─────────────────┘
                      │
                      │   ┌─────────────────┐
                      │   │   FileShare     │
                      │   ├─────────────────┤
                      │   │ Id (PK)         │
                      │   │ ShareCode       │
                      │   │ FileItemId (FK) │
                      │   │ AccessType      │
                      │   │ PasswordHash    │
                      │   │ Permission      │
                      │   │ ExpiresAt       │
                      │   │ MaxDownloads    │
                      │   │ DownloadCount   │
                      └───┤ CreatedByUserId │
                          │ IsEnabled       │
                          │ CreatedAt       │
                          │ UpdatedAt       │
                          └─────────────────┘
```

### 3.2 核心实体说明

#### User（用户）
```csharp
public sealed class User : Entity
{
    public string Username { get; }           // 用户名（唯一）
    public string PasswordHash { get; }       // 密码哈希
    public string PasswordSalt { get; }       // 密码盐值
    public string? DisplayName { get; }       // 显示名称
    public string? Email { get; }             // 邮箱
    public UserRole Role { get; }             // 角色
    public bool IsEnabled { get; }            // 是否启用
    public long? QuotaBytes { get; }          // 配额（字节）
    public long UsedBytes { get; }            // 已使用空间
    public string HomeDirectory { get; }      // 主目录
    public DateTime? LastLoginAt { get; }     // 最后登录时间
}
```

#### FileItem（文件项）
```csharp
public sealed class FileItem : Entity
{
    public string Name { get; }               // 文件名
    public string Path { get; }               // 虚拟路径
    public string PhysicalPath { get; }       // 物理路径
    public FileItemType ItemType { get; }     // 类型（文件/文件夹）
    public long SizeBytes { get; }            // 大小
    public string? MimeType { get; }          // MIME 类型
    public string? Extension { get; }         // 扩展名
    public string? Hash { get; }              // SHA256 哈希
    public Guid StoragePoolId { get; }        // 存储池 ID
    public Guid OwnerId { get; }              // 所有者 ID
    public Guid? ParentId { get; }            // 父文件夹 ID
    public bool IsDeleted { get; }            // 是否已删除
    public DateTime? DeletedAt { get; }       // 删除时间
    public bool IsHidden { get; }             // 是否隐藏
    public bool IsReadOnly { get; }           // 是否只读
}
```

#### StoragePool（存储池）
```csharp
public sealed class StoragePool : Entity
{
    public string Name { get; }               // 名称
    public string MountPath { get; }          // 挂载路径
    public string? Description { get; }       // 描述
    public bool IsDefault { get; }            // 是否默认
    public bool IsEnabled { get; }            // 是否启用
    public long TotalBytes { get; }           // 总容量
    public long UsedBytes { get; }            // 已使用
    public long? MaxCapacityBytes { get; }    // 最大容量限制
}
```

#### FileShare（文件分享）
```csharp
public sealed class FileShare : Entity
{
    public string ShareCode { get; }          // 分享码
    public Guid FileItemId { get; }           // 文件 ID
    public ShareAccessType AccessType { get; }// 访问类型
    public string? PasswordHash { get; }      // 密码哈希
    public PermissionLevel Permission { get; }// 权限级别
    public DateTime? ExpiresAt { get; }       // 过期时间
    public int? MaxDownloads { get; }         // 最大下载次数
    public int DownloadCount { get; }         // 已下载次数
    public Guid CreatedByUserId { get; }      // 创建者 ID
    public bool IsEnabled { get; }            // 是否启用
}
```

### 3.3 枚举定义

```csharp
// 用户角色
public enum UserRole
{
    User = 0,        // 普通用户
    PowerUser = 1,   // 高级用户
    Admin = 2,       // 管理员
    SuperAdmin = 3   // 超级管理员
}

// 文件项类型
public enum FileItemType
{
    File = 0,        // 文件
    Folder = 1,      // 文件夹
    SymbolicLink = 2 // 符号链接
}

// 分享访问类型
public enum ShareAccessType
{
    Public = 0,      // 公开访问
    Password = 1,    // 密码保护
    Internal = 2     // 仅内部用户
}

// 权限级别
public enum PermissionLevel
{
    None = 0,        // 无权限
    Read = 1,        // 只读
    ReadWrite = 2,   // 读写
    FullControl = 3  // 完全控制
}
```

---

## 4. 模块设计

### 4.1 用户模块

```
NasSystem.Application/Users/
├── IUserService.cs          # 服务接口
├── UserService.cs           # 服务实现
├── Dtos/
│   └── UserDtos.cs          # 数据传输对象
└── Validators/
    └── UserValidators.cs    # 输入验证
```

**核心接口：**
```csharp
public interface IUserService
{
    Task<Result<LoginResultDto>> LoginAsync(LoginDto request);
    Task<Result<UserDto>> GetByIdAsync(Guid id);
    Task<Result<PagedResult<UserDto>>> GetPagedAsync(int page, int pageSize, string? search);
    Task<Result<UserDto>> CreateAsync(CreateUserDto request);
    Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserDto request);
    Task<Result> DeleteAsync(Guid id);
    Task<Result> ChangePasswordAsync(Guid id, string currentPassword, string newPassword);
    Task<Result> ResetPasswordAsync(Guid id, string newPassword);
}
```

### 4.2 文件模块

```
NasSystem.Application/Files/
├── IFileService.cs          # 服务接口
├── FileService.cs           # 服务实现
├── Dtos/
│   └── FileDtos.cs          # 数据传输对象
└── Validators/
    └── FileValidators.cs    # 输入验证
```

**核心接口：**
```csharp
public interface IFileService
{
    Task<Result<DirectoryContentDto>> GetDirectoryContentsAsync(string path, Guid userId, bool includeHidden);
    Task<Result<FileItemDto>> GetFileInfoAsync(string path, Guid userId);
    Task<Result<FileItemDto>> CreateFolderAsync(CreateFolderRequest request, Guid userId);
    Task<Result<FileItemDto>> UploadFileAsync(UploadFileMetadata metadata, Stream stream, Guid userId);
    Task<Result<(Stream, string, string)>> DownloadFileAsync(string path, Guid userId);
    Task<Result<FileItemDto>> RenameAsync(RenameItemRequest request, Guid userId);
    Task<Result> MoveAsync(MoveOrCopyRequest request, Guid userId);
    Task<Result> CopyAsync(MoveOrCopyRequest request, Guid userId);
    Task<Result> DeleteAsync(DeleteItemsRequest request, Guid userId);
    Task<Result<PagedResult<FileItemDto>>> SearchAsync(SearchFilesRequest request, Guid userId);
    Task<Result<IReadOnlyList<FileItemDto>>> GetRecycleBinAsync(Guid userId);
    Task<Result> RestoreAsync(Guid id, Guid userId);
    Task<Result> EmptyRecycleBinAsync(Guid userId);
    Task<Result<FileStatisticsDto>> GetStatisticsAsync(Guid userId);
    Task<Result<ScanResultDto>> ScanAndSyncAsync(Guid userId);
}
```

### 4.3 文件分享模块

```
NasSystem.Application/FileShares/
├── IFileShareService.cs     # 服务接口
├── FileShareService.cs      # 服务实现
└── Dtos/
    └── FileShareDtos.cs     # 数据传输对象
```

**核心接口：**
```csharp
public interface IFileShareService
{
    // 管理接口
    Task<Result<FileShareDto>> CreateAsync(CreateFileShareRequest request, Guid userId, string baseUrl);
    Task<Result<IReadOnlyList<FileShareDto>>> GetMySharesAsync(Guid userId, string baseUrl);
    Task<Result<FileShareDto>> GetByIdAsync(Guid id, Guid userId, string baseUrl);
    Task<Result<FileShareDto>> UpdateAsync(Guid id, UpdateFileShareRequest request, Guid userId, string baseUrl);
    Task<Result> DeleteAsync(Guid id, Guid userId);
    Task<Result<FileShareDto>> RegenerateShareCodeAsync(Guid id, Guid userId, string baseUrl);
    
    // 公开访问接口
    Task<Result<PublicShareInfoDto>> GetPublicShareInfoAsync(string shareCode);
    Task<Result<string>> VerifyPasswordAsync(string shareCode, string password);
    Task<Result<(Stream, string, string)>> GetDownloadStreamAsync(string shareCode, string? token, string? subPath);
    Task<Result<ShareFolderContentDto>> GetFolderContentAsync(string shareCode, string? token, string? subPath);
}
```

### 4.4 存储模块

```
NasSystem.Application/Storage/
├── IStorageService.cs       # 服务接口
├── StorageService.cs        # 服务实现
└── Dtos/
    └── StorageDtos.cs       # 数据传输对象
```

**核心接口：**
```csharp
public interface IStorageService
{
    Task<Result<StorageOverviewDto>> GetOverviewAsync();
    Task<Result<IReadOnlyList<StoragePoolDto>>> GetPoolsAsync();
    Task<Result<StoragePoolDto>> GetPoolByIdAsync(Guid id);
    Task<Result<StoragePoolDto>> CreatePoolAsync(CreateStoragePoolRequest request);
    Task<Result<StoragePoolDto>> UpdatePoolAsync(Guid id, UpdateStoragePoolRequest request);
    Task<Result> DeletePoolAsync(Guid id);
    Task<Result> SetDefaultPoolAsync(Guid id);
    Task<Result> RefreshCapacityAsync();
    Task<Result<CheckStorageSpaceResponse>> CheckStorageSpaceAsync(CheckStorageSpaceRequest request);
}
```

### 4.5 文件版本模块

```
NasSystem.Application/FileVersions/
├── IFileVersionService.cs   # 服务接口
└── FileVersionService.cs    # 服务实现
```

**核心接口：**
```csharp
public interface IFileVersionService
{
    Task<Result<IReadOnlyList<FileVersionDto>>> GetVersionsAsync(Guid fileItemId, Guid userId, CancellationToken ct);
    Task<Result<FileVersionDto>> GetVersionAsync(Guid fileItemId, int versionNumber, Guid userId, CancellationToken ct);
    Task<Result<(Stream, string, string)>> DownloadVersionAsync(Guid fileItemId, int versionNumber, Guid userId, CancellationToken ct);
    Task<Result<FileVersionDto>> RestoreVersionAsync(Guid fileItemId, int versionNumber, Guid userId, CancellationToken ct);
    Task<Result<VersionComparisonDto>> CompareVersionsAsync(Guid fileItemId, int v1, int v2, Guid userId, CancellationToken ct);
    Task<Result> DeleteVersionAsync(Guid fileItemId, int versionNumber, Guid userId, CancellationToken ct);
}
```

---

## 5. API 设计

### 5.1 API 端点概览

| 模块 | 基础路径 | 说明 |
|------|----------|------|
| 用户管理 | `/api/v1/users` | 用户认证和管理 |
| 文件管理 | `/api/v1/files` | 文件操作 |
| 缩略图 | `/api/v1/thumbnails` | 缩略图服务 |
| 文件版本 | `/api/v1/file-versions` | 版本管理 |
| 文件分享 | `/api/v1/file-shares` | 分享管理 |
| 公开分享 | `/api/v1/public-shares` | 公开访问 |
| 存储管理 | `/api/v1/storage` | 存储池管理 |
| 系统管理 | `/api/v1/system` | 系统信息 |

### 5.2 统一响应格式

**成功响应：**
```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

**错误响应：**
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "Error.Code",
    "message": "错误描述"
  }
}
```

### 5.3 认证机制

- 使用 JWT Bearer Token 认证
- Token 通过登录接口获取
- 在请求头中携带：`Authorization: Bearer <token>`
- 部分接口需要管理员权限（RequireAdmin 策略）

---

## 6. 安全设计

### 6.1 密码安全
- 使用 PBKDF2-SHA256 算法
- 100,000 次迭代
- 随机盐值

### 6.2 认证授权
- JWT Token 认证
- 基于角色的访问控制 (RBAC)
- 管理员权限策略

### 6.3 数据安全
- 参数化查询防止 SQL 注入
- 输入验证防止 XSS
- HTTPS 传输加密

---

## 7. 部署架构

### 7.1 Docker 部署

```yaml
services:
  nas-system:
    build: .
    ports:
      - "6050:6050"
    volumes:
      - nas-data:/app/data      # 数据库
      - nas-storage:/app/storage # 文件存储
      - nas-logs:/app/logs      # 日志
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
```

### 7.2 目录结构

```
/app/
├── data/           # SQLite 数据库
│   └── nas.db
├── storage/        # 文件存储根目录
│   └── {pool}/     # 各存储池目录
├── logs/           # 应用日志
└── wwwroot/        # 前端静态文件
```

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2026-01-29 | 初始版本 |
