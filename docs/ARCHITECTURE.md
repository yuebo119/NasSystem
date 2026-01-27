# NAS System 架构设计文档

## 目录

1. [项目概述](#项目概述)
2. [架构设计](#架构设计)
3. [技术栈](#技术栈)
4. [项目结构](#项目结构)
5. [核心模块](#核心模块)
6. [数据模型](#数据模型)
7. [API 设计](#api-设计)
8. [安全设计](#安全设计)
9. [部署指南](#部署指南)
10. [开发指南](#开发指南)

---

## 项目概述

NAS System 是一个基于 Windows 平台的网络附属存储系统，提供文件管理、共享服务、用户权限控制等功能。

### 核心功能

| 功能模块 | 描述 |
|---------|------|
| **文件管理** | 上传、下载、浏览、搜索、回收站 |
| **共享管理** | SMB/NFS/WebDAV 共享协议支持 |
| **存储管理** | 存储池管理、RAID 配置、磁盘健康监控 |
| **用户管理** | 多用户、角色权限、配额管理 |
| **系统设置** | 系统配置、日志查看、备份恢复 |

### 设计目标

- **高性能**: 支持大文件传输和高并发访问
- **可扩展**: 模块化设计，便于功能扩展
- **安全性**: 完善的认证授权和数据加密
- **易用性**: 直观的 Web 管理界面
- **可靠性**: 数据完整性保护和故障恢复

---

## 架构设计

### 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                     客户端层                                  │
├─────────────────────────────────────────────────────────────┤
│  Web Browser   │  SMB Client   │  Mobile App   │  API Client │
└────────┬───────┴───────┬───────┴───────┬───────┴──────┬──────┘
         │               │               │              │
         ▼               ▼               ▼              ▼
┌─────────────────────────────────────────────────────────────┐
│                     接入层 (Nginx)                           │
│  - 反向代理  - SSL 终止  - 负载均衡  - 静态文件服务           │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    API 层 (NasSystem.Api)                    │
├─────────────────────────────────────────────────────────────┤
│  Minimal API  │  SignalR Hub  │  Auth Middleware  │  Swagger │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                应用层 (NasSystem.Application)                │
├─────────────────────────────────────────────────────────────┤
│  UserService  │  FileService  │  ShareService  │ StorageService│
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  领域层 (NasSystem.Domain)                   │
├─────────────────────────────────────────────────────────────┤
│  Entities  │  Value Objects  │  Domain Events  │  Repositories│
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│               基础设施层 (NasSystem.Infrastructure)          │
├─────────────────────────────────────────────────────────────┤
│  EF Core  │  File System  │  Password Hasher  │  Event Dispatcher│
└────────────────────────────┬────────────────────────────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐   ┌──────────┐
        │ SQLite   │  │ 文件系统  │   │ 缓存服务  │
        │ Database │  │ Storage  │   │ Memory   │
        └──────────┘  └──────────┘   └──────────┘
```

### 分层架构

采用清洁架构 (Clean Architecture) 设计原则：

```
              ┌─────────────────────────────────┐
              │        NasSystem.Api            │  ← 最外层
              │     (Controllers, Endpoints)    │
              └────────────────┬────────────────┘
                               │
              ┌────────────────▼────────────────┐
              │     NasSystem.Application       │
              │  (Services, DTOs, Validators)   │
              └────────────────┬────────────────┘
                               │
              ┌────────────────▼────────────────┐
              │       NasSystem.Domain          │  ← 核心层
              │ (Entities, Interfaces, Events)  │
              └────────────────┬────────────────┘
                               │
              ┌────────────────▼────────────────┐
              │    NasSystem.Infrastructure     │
              │ (DB, FileSystem, External APIs) │
              └─────────────────────────────────┘
```

**依赖规则**: 内层不依赖外层，所有依赖指向核心层。

---

## 技术栈

### 后端

| 技术 | 版本 | 用途 |
|-----|------|-----|
| .NET | 10.0 | 运行时框架 |
| ASP.NET Core | 10.0 | Web API 框架 |
| Entity Framework Core | 10.0 | ORM |
| SQLite | 3.x | 数据库 |
| Serilog | 10.0 | 结构化日志 |
| FluentValidation | 12.x | 验证框架 |
| Mapster | 7.x | 对象映射 |
| SignalR | 10.0 | 实时通信 |

### 前端

| 技术 | 版本 | 用途 |
|-----|------|-----|
| Vue | 3.5 | 前端框架 |
| TypeScript | 5.7 | 类型安全 |
| Vite | 6.0 | 构建工具 |
| Element Plus | 2.9 | UI 组件库 |
| Pinia | 2.3 | 状态管理 |
| Vue Router | 4.5 | 路由管理 |
| Axios | 1.7 | HTTP 客户端 |

---

## 项目结构

```
NasSystem/
├── NasSystem.sln                    # 解决方案文件
├── Directory.Build.props            # 全局构建配置
├── global.json                      # SDK 版本配置
│
├── src/                             # 源代码
│   ├── NasSystem.Api/               # API 层
│   │   ├── Endpoints/               # Minimal API 端点
│   │   ├── Extensions/              # 服务扩展
│   │   ├── Hubs/                    # SignalR Hubs
│   │   ├── Middleware/              # 中间件
│   │   └── Program.cs               # 入口点
│   │
│   ├── NasSystem.Application/       # 应用层
│   │   ├── Common/                  # 公共定义
│   │   ├── Users/                   # 用户模块
│   │   ├── Files/                   # 文件模块
│   │   ├── Shares/                  # 共享模块
│   │   └── Storage/                 # 存储模块
│   │
│   ├── NasSystem.Domain/            # 领域层
│   │   ├── Common/                  # 基础类
│   │   ├── Entities/                # 实体
│   │   ├── Enums/                   # 枚举
│   │   ├── Events/                  # 领域事件
│   │   ├── Repositories/            # 仓储接口
│   │   ├── Services/                # 领域服务接口
│   │   └── ValueObjects/            # 值对象
│   │
│   ├── NasSystem.Infrastructure/    # 基础设施层
│   │   ├── Data/                    # 数据访问
│   │   │   ├── Configurations/      # EF Core 配置
│   │   │   └── Repositories/        # 仓储实现
│   │   └── Services/                # 服务实现
│   │
│   └── NasSystem.Shared/            # 共享库
│       ├── Result.cs                # Result 模式
│       ├── ApiResponse.cs           # API 响应格式
│       └── PagedResult.cs           # 分页结果
│
├── tests/                           # 测试项目
│   ├── NasSystem.UnitTests/
│   └── NasSystem.IntegrationTests/
│
├── web/                             # Vue 前端
│   ├── src/
│   │   ├── api/                     # API 模块
│   │   ├── components/              # 组件
│   │   ├── layouts/                 # 布局
│   │   ├── router/                  # 路由
│   │   ├── stores/                  # Pinia stores
│   │   ├── styles/                  # 样式
│   │   ├── types/                   # 类型定义
│   │   └── views/                   # 页面
│   ├── package.json
│   └── vite.config.ts
│
└── docs/                            # 文档
    └── ARCHITECTURE.md              # 本文档
```

---

## 核心模块

### 用户模块 (Users)

```csharp
// 实体
User {
    Id, Username, PasswordHash, PasswordSalt,
    DisplayName, Email, Role, IsEnabled,
    HomeDirectory, QuotaBytes, UsedBytes
}

// 服务接口
IUserService {
    GetUsersAsync(), GetByIdAsync(), CreateAsync(),
    UpdateAsync(), DeleteAsync(), ChangePasswordAsync(),
    LoginAsync(), LogoutAsync(), ValidateCredentialsAsync()
}
```

### 文件模块 (Files)

```csharp
// 实体
FileItem {
    Id, Name, Path, PhysicalPath, ItemType,
    SizeBytes, MimeType, Extension, Hash,
    ParentId, OwnerId, StoragePoolId,
    IsHidden, IsReadOnly, IsDeleted
}

// 服务接口
IFileService {
    GetDirectoryContentsAsync(), GetFileInfoAsync(),
    CreateFolderAsync(), UploadFileAsync(), DownloadFileAsync(),
    RenameAsync(), MoveAsync(), CopyAsync(), DeleteAsync(),
    SearchAsync()
}
```

### 共享模块 (Shares)

```csharp
// 实体
Share {
    Id, Name, Description, Path, PhysicalPath,
    ShareType, IsEnabled, IsPublic, IsReadOnly,
    StoragePoolId, QuotaBytes, EnableRecycleBin
}

SharePermission {
    Id, ShareId, UserId, GroupName,
    Permission, IsDeny
}

// 服务接口
IShareService {
    GetSharesAsync(), GetAccessibleSharesAsync(),
    CreateAsync(), UpdateAsync(), DeleteAsync(),
    SetPermissionsAsync(), CheckPermissionAsync()
}
```

### 存储模块 (Storage)

```csharp
// 实体
StoragePool {
    Id, Name, Description, MountPath,
    Status, RaidLevel, TotalBytes, UsedBytes,
    IsDefault, IsEnabled, EnableDeduplication
}

StoragePoolDisk {
    Id, StoragePoolId, SerialNumber, Model,
    DevicePath, SizeBytes, DiskType, Status,
    HealthPercentage, Temperature
}

// 服务接口
IStorageService {
    GetOverviewAsync(), GetPoolsAsync(),
    CreatePoolAsync(), UpdatePoolAsync(), DeletePoolAsync(),
    GetSystemDisksAsync(), ScanDiskHealthAsync()
}
```

---

## 数据模型

### ER 图

```
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│    User     │       │   FileItem  │       │    Share    │
├─────────────┤       ├─────────────┤       ├─────────────┤
│ Id (PK)     │◄──┐   │ Id (PK)     │   ┌──►│ Id (PK)     │
│ Username    │   │   │ Name        │   │   │ Name        │
│ PasswordHash│   │   │ Path        │   │   │ Path        │
│ Role        │   │   │ ItemType    │   │   │ ShareType   │
│ QuotaBytes  │   │   │ SizeBytes   │   │   │ IsPublic    │
└─────────────┘   │   │ OwnerId (FK)├───┘   │ StoragePoolId│
                  │   │ ParentId (FK)───────►└─────────────┘
                  │   │ StoragePoolId       ▲
                  │   └─────────────┘       │
                  │                         │
                  │   ┌─────────────┐       │
                  │   │SharePermission      │
                  │   ├─────────────┤       │
                  │   │ Id (PK)     │       │
                  └───┤ UserId (FK) │       │
                      │ ShareId (FK)├───────┘
                      │ Permission  │
                      └─────────────┘

┌─────────────┐       ┌─────────────┐
│ StoragePool │       │StoragePoolDisk│
├─────────────┤       ├─────────────┤
│ Id (PK)     │◄──────┤ StoragePoolId│
│ Name        │       │ Id (PK)     │
│ MountPath   │       │ SerialNumber│
│ RaidLevel   │       │ Model       │
│ TotalBytes  │       │ DiskType    │
│ UsedBytes   │       │ Status      │
└─────────────┘       └─────────────┘
```

---

## API 设计

### RESTful 端点

| 模块 | 方法 | 路径 | 描述 |
|------|------|------|------|
| **用户** | POST | `/api/v1/users/login` | 登录 |
| | POST | `/api/v1/users/logout` | 登出 |
| | GET | `/api/v1/users` | 获取用户列表 |
| | GET | `/api/v1/users/{id}` | 获取用户详情 |
| | POST | `/api/v1/users` | 创建用户 |
| | PUT | `/api/v1/users/{id}` | 更新用户 |
| | DELETE | `/api/v1/users/{id}` | 删除用户 |
| **文件** | GET | `/api/v1/files/directory` | 获取目录内容 |
| | GET | `/api/v1/files/info` | 获取文件信息 |
| | POST | `/api/v1/files/folder` | 创建文件夹 |
| | POST | `/api/v1/files/upload` | 上传文件 |
| | GET | `/api/v1/files/download` | 下载文件 |
| | POST | `/api/v1/files/rename` | 重命名 |
| | POST | `/api/v1/files/move` | 移动 |
| | POST | `/api/v1/files/copy` | 复制 |
| | POST | `/api/v1/files/delete` | 删除 |
| **共享** | GET | `/api/v1/shares` | 获取共享列表 |
| | GET | `/api/v1/shares/{id}` | 获取共享详情 |
| | POST | `/api/v1/shares` | 创建共享 |
| | PUT | `/api/v1/shares/{id}` | 更新共享 |
| | DELETE | `/api/v1/shares/{id}` | 删除共享 |
| **存储** | GET | `/api/v1/storage/overview` | 存储概览 |
| | GET | `/api/v1/storage/pools` | 获取存储池 |
| | POST | `/api/v1/storage/pools` | 创建存储池 |
| | GET | `/api/v1/storage/disks` | 获取磁盘列表 |

### 统一响应格式

```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "timestamp": "2025-01-01T00:00:00Z"
}
```

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "Error.NotFound",
    "message": "资源未找到"
  },
  "timestamp": "2025-01-01T00:00:00Z"
}
```

### SignalR Hub

```typescript
// 连接端点
/hubs/nas

// 客户端 → 服务端
SubscribeToPath(path: string)    // 订阅路径变更
UnsubscribeFromPath(path: string)
SubscribeToTask(taskId: string)  // 订阅任务进度

// 服务端 → 客户端
FileChanged(data)          // 文件变更通知
UploadProgress(data)       // 上传进度
TaskCompleted(data)        // 任务完成
SystemMessage(data)        // 系统消息
StorageChanged(data)       // 存储变更
```

---

## 安全设计

### 认证方式

- **JWT Token**: Bearer token 认证
- **刷新令牌**: 支持令牌刷新机制
- **密码加密**: PBKDF2-SHA256 + 随机盐值

### 授权模型

```
角色层级:
SuperAdmin > Admin > User > Guest

权限级别:
Full > Write > Read > None
```

### 安全措施

1. **传输安全**: HTTPS/TLS 1.3
2. **密码安全**: PBKDF2 + 100000 迭代
3. **防暴力破解**: 登录失败锁定
4. **CORS 配置**: 限制跨域请求
5. **输入验证**: FluentValidation 验证所有输入
6. **SQL 注入防护**: EF Core 参数化查询
7. **XSS 防护**: 输出编码

---

## 部署指南

### 系统要求

- **操作系统**: Windows 10/11, Windows Server 2019+
- **运行时**: .NET 9.0 Runtime
- **内存**: 最低 2GB, 推荐 4GB+
- **存储**: 系统盘 10GB+, 数据盘按需

### 开发环境启动

```bash
# 后端
cd src/NasSystem.Api
dotnet run

# 前端
cd web
npm install
npm run dev
```

### 生产部署

```bash
# 构建后端
dotnet publish -c Release -o ./publish

# 构建前端
cd web
npm run build

# 运行
./publish/NasSystem.Api.exe
```

### Windows 服务部署

```powershell
# 注册为 Windows 服务
New-Service -Name "NasSystem" -BinaryPathName "C:\NasSystem\NasSystem.Api.exe" -StartupType Automatic

# 启动服务
Start-Service NasSystem
```

---

## 开发指南

### 开发规范

1. **命名规范**
   - 类名: PascalCase (如 `UserService`)
   - 方法名: PascalCase (如 `GetUserAsync`)
   - 变量名: camelCase (如 `userName`)
   - 常量: UPPER_SNAKE_CASE (如 `MAX_FILE_SIZE`)

2. **代码风格**
   - 使用 C# 13.0 特性
   - 启用 Nullable 引用类型
   - 使用 record 定义 DTO
   - 使用 Result 模式处理错误

3. **提交规范**
   ```
   feat: 新功能
   fix: 修复 Bug
   docs: 文档更新
   refactor: 代码重构
   test: 测试相关
   chore: 构建/工具相关
   ```

### 添加新功能示例

```bash
# 1. 在 Domain 层添加实体
src/NasSystem.Domain/Entities/NewEntity.cs

# 2. 在 Domain 层添加仓储接口
src/NasSystem.Domain/Repositories/INewEntityRepository.cs

# 3. 在 Application 层添加服务
src/NasSystem.Application/NewModule/INewService.cs
src/NasSystem.Application/NewModule/Dtos/NewEntityDtos.cs

# 4. 在 Infrastructure 层实现仓储
src/NasSystem.Infrastructure/Data/Repositories/NewEntityRepository.cs

# 5. 在 Api 层添加端点
src/NasSystem.Api/Endpoints/NewEndpoints.cs

# 6. 注册依赖注入
DependencyInjection.cs
```

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2025-01 | 初始版本，完成核心架构设计 |

---

*文档最后更新: 2025年1月*
