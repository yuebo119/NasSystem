# 存储池与文件分享简化设计方案

## 1. 概述

### 1.1 背景

当前系统的存储池和文件分享功能存在过度设计的问题：
- 存储池实现了 RAID、磁盘管理等复杂功能，但这些应该由操作系统/硬件层面处理
- 文件分享基于协议（SMB/NFS），但实际上 Web 应用难以实现这些系统级协议
- 权限系统过于复杂，包含用户权限、组权限、拒绝权限等

### 1.2 设计目标

1. **简化存储池**：仅作为存储目录配置，移除硬件管理功能
2. **重新设计分享**：从"协议分享"转向"链接分享"，聚焦 Web 访问场景
3. **简化权限**：采用"所有者 + 直接授权 + 继承"的简单模型

### 1.3 适用场景

- 家庭/小型办公室 NAS
- 通过 Web 浏览器访问文件
- 通过分享链接与外部用户共享文件

---

## 2. 存储池简化设计

### 2.1 设计原则

- 存储池仅作为"存储根目录"的配置
- 移除 RAID、磁盘管理等硬件相关功能（交给操作系统）
- 支持多存储池（如 SSD 快速存储 + HDD 归档存储）
- 保留容量统计功能（从文件系统读取）

### 2.2 实体设计

```csharp
public class StoragePool : Entity
{
    public string Name { get; private set; }           // 存储池名称
    public string MountPath { get; private set; }      // 挂载路径（存储根目录）
    public string? Description { get; private set; }   // 描述
    public bool IsDefault { get; private set; }        // 是否默认存储池
    public bool IsEnabled { get; private set; }        // 是否启用
    
    // 容量信息（从文件系统动态读取）
    public long TotalBytes { get; private set; }       // 总容量
    public long UsedBytes { get; private set; }        // 已使用
    public long AvailableBytes => TotalBytes - UsedBytes;
    public double UsagePercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
}
```

### 2.3 移除的功能

| 移除项 | 原因 |
|--------|------|
| `RaidLevel` | RAID 应在操作系统/硬件层面配置 |
| `StoragePoolDisk` | 磁盘管理应由操作系统处理 |
| `EnableDeduplication` | 去重功能需要文件系统支持（如 ZFS） |
| `EnableCompression` | 压缩功能需要文件系统支持 |
| `Status` (Degraded/Rebuilding) | 这些状态与 RAID 相关 |

### 2.4 API 设计

```
GET    /api/v1/storage/pools              # 获取所有存储池
GET    /api/v1/storage/pools/{id}         # 获取存储池详情
POST   /api/v1/storage/pools              # 创建存储池
PUT    /api/v1/storage/pools/{id}         # 更新存储池
DELETE /api/v1/storage/pools/{id}         # 删除存储池
POST   /api/v1/storage/pools/{id}/default # 设为默认
POST   /api/v1/storage/refresh            # 刷新容量信息
```

---

## 3. 文件分享重新设计

### 3.1 设计原则

- 聚焦"链接分享"场景，通过短链接访问文件
- 支持公开访问、密码保护、仅内部用户三种模式
- 支持过期时间和下载次数限制
- 支持分享文件和文件夹

### 3.2 实体设计

```csharp
public class FileShare : Entity
{
    public string ShareCode { get; private set; }           // 短链接码 (如 "abc123")
    public Guid FileItemId { get; private set; }            // 关联的文件/文件夹
    public ShareAccessType AccessType { get; private set; } // 访问类型
    public string? PasswordHash { get; private set; }       // 密码哈希
    public PermissionLevel Permission { get; private set; } // 权限级别
    public DateTime? ExpiresAt { get; private set; }        // 过期时间
    public int? MaxDownloads { get; private set; }          // 最大下载次数
    public int DownloadCount { get; private set; }          // 已下载次数
    public Guid CreatedByUserId { get; private set; }       // 创建者
    public bool IsEnabled { get; private set; }             // 是否启用
    
    // 导航属性
    public FileItem? FileItem { get; private set; }
    public User? CreatedByUser { get; private set; }
}

public enum ShareAccessType
{
    Public = 0,      // 公开访问（任何人可访问）
    Password = 1,    // 密码保护
    Internal = 2     // 仅内部用户（需登录）
}
```

### 3.3 分享码生成规则

- 长度：6-8 个字符
- 字符集：小写字母 + 数字（排除易混淆字符 0/o/l/1）
- 示例：`abc123`, `x7k9m2`

```csharp
private static string GenerateShareCode()
{
    const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
    var random = new Random();
    return new string(Enumerable.Range(0, 6)
        .Select(_ => chars[random.Next(chars.Length)])
        .ToArray());
}
```

### 3.4 访问流程

```
┌─────────────────────────────────────────────────────────────┐
│  用户访问: https://nas.example.com/s/{shareCode}            │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  1. 验证分享有效性                                           │
│     - 分享是否存在                                           │
│     - 是否启用                                               │
│     - 是否过期                                               │
│     - 下载次数是否超限                                       │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  2. 验证访问权限                                             │
│     - Public: 直接通过                                       │
│     - Password: 验证密码                                     │
│     - Internal: 验证用户登录状态                             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  3. 返回内容                                                 │
│     - 文件: 显示预览/下载页面                                │
│     - 文件夹: 显示目录列表                                   │
└─────────────────────────────────────────────────────────────┘
```

### 3.5 API 设计

#### 管理 API（需要登录）

```
POST   /api/v1/shares                     # 创建分享
GET    /api/v1/shares                     # 获取我的分享列表
GET    /api/v1/shares/{id}                # 获取分享详情
PUT    /api/v1/shares/{id}                # 更新分享设置
DELETE /api/v1/shares/{id}                # 删除分享
```

#### 公开访问 API（无需登录）

```
GET    /s/{shareCode}                     # 获取分享信息
POST   /s/{shareCode}/verify              # 验证密码
GET    /s/{shareCode}/download            # 下载文件
GET    /s/{shareCode}/download/{path}     # 下载文件夹中的文件
GET    /s/{shareCode}/files               # 获取文件夹内容
GET    /s/{shareCode}/files/{path}        # 获取子目录内容
```

### 3.6 DTO 设计

```csharp
// 创建分享请求
public record CreateFileShareRequest
{
    public Guid FileItemId { get; init; }
    public ShareAccessType AccessType { get; init; }
    public string? Password { get; init; }
    public PermissionLevel Permission { get; init; } = PermissionLevel.Read;
    public DateTime? ExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
}

// 分享信息响应
public record FileShareDto
{
    public Guid Id { get; init; }
    public string ShareCode { get; init; }
    public string ShareUrl { get; init; }           // 完整分享链接
    public Guid FileItemId { get; init; }
    public string FileName { get; init; }
    public string FilePath { get; init; }
    public bool IsFolder { get; init; }
    public ShareAccessType AccessType { get; init; }
    public PermissionLevel Permission { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
    public int DownloadCount { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
}

// 公开访问响应（不包含敏感信息）
public record PublicShareInfoDto
{
    public string ShareCode { get; init; }
    public string FileName { get; init; }
    public bool IsFolder { get; init; }
    public long? SizeBytes { get; init; }
    public string? SizeDisplay { get; init; }
    public ShareAccessType AccessType { get; init; }
    public PermissionLevel Permission { get; init; }
    public bool RequiresPassword { get; init; }
    public bool IsExpired { get; init; }
    public bool IsDownloadLimitReached { get; init; }
}
```

---

## 4. 文件权限简化设计

### 4.1 设计原则

- 简化权限模型：所有者 → 管理员 → 直接授权 → 继承
- 移除组权限和拒绝权限
- 支持权限继承（从父目录继承）

### 4.2 实体设计

```csharp
public class FilePermission : Entity
{
    public Guid FileItemId { get; private set; }        // 文件/文件夹
    public Guid UserId { get; private set; }            // 被授权用户
    public PermissionLevel Permission { get; private set; }  // 权限级别
    
    // 导航属性
    public FileItem? FileItem { get; private set; }
    public User? User { get; private set; }
}

public enum PermissionLevel
{
    None = 0,           // 无权限
    Read = 1,           // 只读
    ReadWrite = 2,      // 读写
    FullControl = 3     // 完全控制
}
```

### 4.3 权限检查逻辑

```csharp
public PermissionLevel GetEffectivePermission(FileItem file, User user)
{
    // 1. 超级管理员 -> 完全控制
    if (user.Role == UserRole.SuperAdmin)
        return PermissionLevel.FullControl;
    
    // 2. 文件所有者 -> 完全控制
    if (file.OwnerId == user.Id)
        return PermissionLevel.FullControl;
    
    // 3. 管理员 -> 完全控制
    if (user.Role == UserRole.Admin)
        return PermissionLevel.FullControl;
    
    // 4. 检查直接权限
    var directPermission = GetDirectPermission(file.Id, user.Id);
    if (directPermission != PermissionLevel.None)
        return directPermission;
    
    // 5. 检查父目录继承权限
    if (file.ParentId.HasValue)
        return GetEffectivePermission(file.Parent, user);
    
    // 6. 默认无权限
    return PermissionLevel.None;
}
```

### 4.4 API 设计

```
GET    /api/v1/files/{id}/permissions           # 获取文件权限列表
POST   /api/v1/files/{id}/permissions           # 添加权限
PUT    /api/v1/files/{id}/permissions/{userId}  # 更新权限
DELETE /api/v1/files/{id}/permissions/{userId}  # 移除权限
```

---

## 5. 数据库设计

### 5.1 表结构

```sql
-- 存储池表（简化）
CREATE TABLE StoragePools (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    MountPath TEXT NOT NULL UNIQUE,
    Description TEXT,
    IsDefault INTEGER DEFAULT 0,
    IsEnabled INTEGER DEFAULT 1,
    TotalBytes INTEGER DEFAULT 0,
    UsedBytes INTEGER DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT
);

-- 文件分享表（新增）
CREATE TABLE FileShares (
    Id TEXT PRIMARY KEY,
    ShareCode TEXT NOT NULL UNIQUE,
    FileItemId TEXT NOT NULL,
    AccessType INTEGER NOT NULL DEFAULT 0,
    PasswordHash TEXT,
    Permission INTEGER NOT NULL DEFAULT 1,
    ExpiresAt TEXT,
    MaxDownloads INTEGER,
    DownloadCount INTEGER DEFAULT 0,
    CreatedByUserId TEXT NOT NULL,
    IsEnabled INTEGER DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    
    FOREIGN KEY (FileItemId) REFERENCES FileItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
);

CREATE INDEX IX_FileShares_ShareCode ON FileShares(ShareCode);
CREATE INDEX IX_FileShares_FileItemId ON FileShares(FileItemId);
CREATE INDEX IX_FileShares_CreatedByUserId ON FileShares(CreatedByUserId);

-- 文件权限表（简化）
CREATE TABLE FilePermissions (
    Id TEXT PRIMARY KEY,
    FileItemId TEXT NOT NULL,
    UserId TEXT NOT NULL,
    Permission INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    
    FOREIGN KEY (FileItemId) REFERENCES FileItems(Id) ON DELETE CASCADE,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    UNIQUE(FileItemId, UserId)
);

CREATE INDEX IX_FilePermissions_FileItemId ON FilePermissions(FileItemId);
CREATE INDEX IX_FilePermissions_UserId ON FilePermissions(UserId);
```

### 5.2 迁移计划

1. 创建新表 `FileShares` 和 `FilePermissions`
2. 简化 `StoragePools` 表（移除 RAID 相关字段）
3. 迁移现有 `Shares` 数据到 `FileShares`（如有）
4. 迁移现有 `SharePermissions` 数据到 `FilePermissions`（如有）
5. 删除旧表 `Shares`, `SharePermissions`, `StoragePoolDisks`

---

## 6. 前端界面设计

### 6.1 分享对话框

```
┌─────────────────────────────────────────────────────────────┐
│  分享 "report.pdf"                                     [×]  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  访问方式                                                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ○ 公开访问    任何人都可以访问                        │   │
│  │ ● 密码保护    需要输入密码才能访问                    │   │
│  │ ○ 仅内部用户  需要登录系统才能访问                    │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  访问密码                                                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ********                                    [生成]   │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  权限                                                        │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ ● 只读    ○ 可编辑                                   │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  有效期                                                      │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 永久有效                                        [▼]  │   │
│  └─────────────────────────────────────────────────────┘   │
│  选项: 永久有效 / 1天 / 7天 / 30天 / 自定义                  │
│                                                             │
│  下载限制                                                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 无限制                                          [▼]  │   │
│  └─────────────────────────────────────────────────────┘   │
│  选项: 无限制 / 1次 / 10次 / 100次 / 自定义                  │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  分享链接                                                    │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ https://nas.example.com/s/abc123            [复制]   │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│                              [取消]    [创建分享]            │
└─────────────────────────────────────────────────────────────┘
```

### 6.2 分享管理页面

```
┌─────────────────────────────────────────────────────────────┐
│  我的分享                                      [+ 新建分享]  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 📄 report.pdf                                        │   │
│  │ 链接: /s/abc123  |  公开  |  只读  |  永久有效       │   │
│  │ 下载: 15 次  |  创建于: 2024-01-15                   │   │
│  │                              [复制链接] [编辑] [删除] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ 📁 项目文档                                          │   │
│  │ 链接: /s/xyz789  |  密码保护  |  读写  |  7天后过期  │   │
│  │ 下载: 3 次  |  创建于: 2024-01-20                    │   │
│  │                              [复制链接] [编辑] [删除] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 6.3 公开分享访问页面

```
┌─────────────────────────────────────────────────────────────┐
│                         NAS System                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                    ┌─────────────────┐                      │
│                    │                 │                      │
│                    │    📄 PDF       │                      │
│                    │                 │                      │
│                    └─────────────────┘                      │
│                                                             │
│                      report.pdf                             │
│                      2.5 MB                                 │
│                                                             │
│              ┌─────────────────────────┐                    │
│              │       下载文件           │                    │
│              └─────────────────────────┘                    │
│                                                             │
│              ┌─────────────────────────┐                    │
│              │       在线预览           │                    │
│              └─────────────────────────┘                    │
│                                                             │
│                  分享者: admin                              │
│                  有效期: 永久                               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

---

## 7. 实现计划

### 7.1 阶段一：数据库迁移

1. 创建 `FileShare` 实体和配置
2. 创建 `FilePermission` 实体和配置
3. 简化 `StoragePool` 实体
4. 执行数据库迁移

### 7.2 阶段二：后端服务

1. 实现 `IFileShareService` 和 `FileShareService`
2. 实现 `IFilePermissionService` 和 `FilePermissionService`
3. 简化 `StorageService`
4. 实现分享相关的 API 端点

### 7.3 阶段三：前端界面

1. 实现分享对话框组件
2. 实现分享管理页面
3. 实现公开分享访问页面
4. 集成到文件管理页面

### 7.4 阶段四：测试与优化

1. 单元测试
2. 集成测试
3. 性能优化
4. 安全审计

---

## 8. 安全考虑

### 8.1 分享安全

- 分享码使用加密安全的随机数生成
- 密码使用 bcrypt 哈希存储
- 支持过期时间和下载次数限制
- 记录访问日志

### 8.2 权限安全

- 所有文件操作都需要权限检查
- 管理员操作需要二次确认
- 敏感操作记录审计日志

### 8.3 API 安全

- 公开 API 限流保护
- 密码验证失败次数限制
- HTTPS 强制

---

## 9. 附录

### 9.1 与现有系统的兼容性

| 现有功能 | 处理方式 |
|---------|---------|
| `Share` 实体 | 迁移数据后删除 |
| `SharePermission` 实体 | 迁移数据后删除 |
| `StoragePoolDisk` 实体 | 直接删除 |
| `ShareType` 枚举 | 删除（不再需要协议类型） |

### 9.2 未来扩展

- WebDAV 协议支持（可选）
- 分享统计分析
- 分享二维码生成
- 批量分享管理
