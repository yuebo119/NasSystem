# NAS System API 文档

## 概述

NAS System API 是一个 RESTful API，用于管理网络附加存储系统。所有 API 端点都位于 `/api/v1` 前缀下。

## 认证

除登录接口外，所有 API 都需要 JWT Bearer Token 认证。

### 获取 Token

```http
POST /api/v1/users/login
Content-Type: application/json

{
  "username": "admin",
  "password": "password123"
}
```

**响应:**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "expiresAt": "2024-01-01T12:00:00Z",
    "user": {
      "id": "guid",
      "username": "admin",
      "role": "Admin"
    }
  }
}
```

### 使用 Token

在后续请求的 Header 中添加:
```
Authorization: Bearer <your-token>
```

---

## 用户管理 `/api/v1/users`

### 登录
```http
POST /api/v1/users/login
```

### 登出
```http
POST /api/v1/users/logout
Authorization: Bearer <token>
```

### 获取当前用户
```http
GET /api/v1/users/me
Authorization: Bearer <token>
```

### 获取用户列表 (管理员)
```http
GET /api/v1/users?page=1&pageSize=20&search=keyword
Authorization: Bearer <admin-token>
```

### 获取用户详情 (管理员)
```http
GET /api/v1/users/{id}
Authorization: Bearer <admin-token>
```

### 创建用户 (管理员)
```http
POST /api/v1/users
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "username": "newuser",
  "password": "password123",
  "email": "user@example.com",
  "role": "User",
  "quotaBytes": 10737418240
}
```

### 更新用户 (管理员)
```http
PUT /api/v1/users/{id}
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "email": "newemail@example.com",
  "role": "Admin",
  "isEnabled": true
}
```

### 删除用户 (管理员)
```http
DELETE /api/v1/users/{id}
Authorization: Bearer <admin-token>
```

### 修改密码
```http
POST /api/v1/users/{id}/password
Authorization: Bearer <token>
Content-Type: application/json

{
  "currentPassword": "oldPassword",
  "newPassword": "newPassword123"
}
```

### 重置密码 (管理员)
```http
POST /api/v1/users/{id}/reset-password
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "newPassword": "temporaryPassword"
}
```

---

## 文件管理 `/api/v1/files`

### 浏览目录
```http
GET /api/v1/files/browse/{path}?includeHidden=false
Authorization: Bearer <token>
```

**响应:**
```json
{
  "success": true,
  "data": {
    "currentPath": "/documents",
    "items": [
      {
        "id": "guid",
        "name": "report.pdf",
        "path": "/documents/report.pdf",
        "type": "File",
        "size": 1024000,
        "mimeType": "application/pdf",
        "createdAt": "2024-01-01T10:00:00Z",
        "modifiedAt": "2024-01-01T12:00:00Z"
      }
    ],
    "totalItems": 10,
    "totalSize": 5242880
  }
}
```

### 获取文件信息
```http
GET /api/v1/files/info/{path}
Authorization: Bearer <token>
```

### 上传文件
```http
POST /api/v1/files/upload
Authorization: Bearer <token>
Content-Type: multipart/form-data

file: <binary>
path: /documents
overwrite: false
```

### 下载文件
```http
GET /api/v1/files/download/{path}
Authorization: Bearer <token>
```

### 创建文件夹
```http
POST /api/v1/files/folder
Authorization: Bearer <token>
Content-Type: application/json

{
  "parentPath": "/documents",
  "name": "NewFolder"
}
```

### 重命名
```http
PUT /api/v1/files/rename
Authorization: Bearer <token>
Content-Type: application/json

{
  "path": "/documents/old-name.txt",
  "newName": "new-name.txt"
}
```

### 删除
```http
POST /api/v1/files/delete
Authorization: Bearer <token>
Content-Type: application/json

{
  "paths": ["/documents/file1.txt", "/documents/file2.txt"],
  "permanent": false
}
```

### 移动
```http
POST /api/v1/files/move
Authorization: Bearer <token>
Content-Type: application/json

{
  "sourcePaths": ["/documents/file.txt"],
  "destinationPath": "/archive"
}
```

### 复制
```http
POST /api/v1/files/copy
Authorization: Bearer <token>
Content-Type: application/json

{
  "sourcePaths": ["/documents/file.txt"],
  "destinationPath": "/backup"
}
```

### 搜索文件
```http
GET /api/v1/files/search?query=report&type=File&page=1&pageSize=20
Authorization: Bearer <token>
```

### 获取回收站
```http
GET /api/v1/files/recycle-bin
Authorization: Bearer <token>
```

### 恢复删除的文件
```http
POST /api/v1/files/restore/{id}
Authorization: Bearer <token>
```

### 清空回收站
```http
DELETE /api/v1/files/recycle-bin
Authorization: Bearer <token>
```

---

## 缩略图 `/api/v1/thumbnails`

### 获取缩略图
```http
GET /api/v1/thumbnails/{path}?width=200&height=200
Authorization: Bearer <token>
```

**响应:** 图片二进制数据 (image/jpeg)

### 检查缩略图支持
```http
GET /api/v1/thumbnails/supported/{path}
Authorization: Bearer <token>
```

**响应:**
```json
{
  "success": true,
  "data": {
    "path": "/images/photo.jpg",
    "isSupported": true,
    "extension": ".jpg"
  }
}
```

### 清除缩略图缓存
```http
DELETE /api/v1/thumbnails/cache/{path}
Authorization: Bearer <token>
```

### 清除所有缓存 (管理员)
```http
DELETE /api/v1/thumbnails/cache
Authorization: Bearer <admin-token>
```

---

## 文件版本 `/api/v1/file-versions`

### 获取文件版本列表
```http
GET /api/v1/file-versions/{fileItemId}
Authorization: Bearer <token>
```

**响应:**
```json
{
  "success": true,
  "data": [
    {
      "id": "guid",
      "fileItemId": "guid",
      "versionNumber": 3,
      "fileSize": 1024,
      "fileHash": "sha256...",
      "comment": "Updated content",
      "isCurrent": true,
      "createdByUserId": "guid",
      "createdByUsername": "admin",
      "createdAt": "2024-01-01T12:00:00Z"
    }
  ]
}
```

### 获取指定版本
```http
GET /api/v1/file-versions/{fileItemId}/{versionNumber}
Authorization: Bearer <token>
```

### 下载指定版本
```http
GET /api/v1/file-versions/{fileItemId}/{versionNumber}/download
Authorization: Bearer <token>
```

### 恢复到指定版本
```http
POST /api/v1/file-versions/{fileItemId}/{versionNumber}/restore
Authorization: Bearer <token>
```

### 比较版本
```http
GET /api/v1/file-versions/{fileItemId}/compare?version1=1&version2=3
Authorization: Bearer <token>
```

**响应:**
```json
{
  "success": true,
  "data": {
    "version1": { ... },
    "version2": { ... },
    "sizeDifference": 512,
    "hashDiffers": true,
    "timeDifference": "2.05:30:00"
  }
}
```

### 删除版本
```http
DELETE /api/v1/file-versions/{fileItemId}/{versionNumber}
Authorization: Bearer <token>
```

---

## 共享管理 `/api/v1/shares`

### 获取共享列表
```http
GET /api/v1/shares?page=1&pageSize=20
Authorization: Bearer <token>
```

### 获取用户可访问的共享
```http
GET /api/v1/shares/accessible
Authorization: Bearer <token>
```

### 获取共享详情
```http
GET /api/v1/shares/{id}
Authorization: Bearer <token>
```

### 创建共享 (管理员)
```http
POST /api/v1/shares
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "name": "PublicShare",
  "path": "/shared/public",
  "storagePoolId": "guid",
  "description": "公共共享文件夹",
  "isPublic": true,
  "isReadOnly": false
}
```

### 更新共享 (管理员)
```http
PUT /api/v1/shares/{id}
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "name": "RenamedShare",
  "description": "更新后的描述",
  "isEnabled": true
}
```

### 删除共享 (管理员)
```http
DELETE /api/v1/shares/{id}
Authorization: Bearer <admin-token>
```

### 获取共享权限
```http
GET /api/v1/shares/{id}/permissions
Authorization: Bearer <admin-token>
```

### 设置用户权限
```http
POST /api/v1/shares/{id}/permissions
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "userId": "guid",
  "canRead": true,
  "canWrite": true,
  "canDelete": false
}
```

---

## 存储管理 `/api/v1/storage`

### 获取存储池列表
```http
GET /api/v1/storage/pools
Authorization: Bearer <token>
```

### 获取存储池详情
```http
GET /api/v1/storage/pools/{id}
Authorization: Bearer <token>
```

### 创建存储池 (管理员)
```http
POST /api/v1/storage/pools
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "name": "MainStorage",
  "raidLevel": "RAID5",
  "diskIds": ["disk1", "disk2", "disk3"],
  "enableDeduplication": true,
  "enableCompression": true
}
```

### 删除存储池 (管理员)
```http
DELETE /api/v1/storage/pools/{id}
Authorization: Bearer <admin-token>
```

### 获取磁盘列表
```http
GET /api/v1/storage/disks
Authorization: Bearer <token>
```

---

## 系统 `/api/v1/system`

### 获取系统状态
```http
GET /api/v1/system/status
Authorization: Bearer <token>
```

**响应:**
```json
{
  "success": true,
  "data": {
    "cpuUsage": 25.5,
    "memoryUsage": 60.2,
    "totalMemoryMB": 16384,
    "availableMemoryMB": 6553,
    "uptime": "5.12:30:00",
    "version": "1.0.0"
  }
}
```

### 健康检查
```http
GET /health
```

### 版本信息
```http
GET /api/version
```

---

## 错误响应

所有 API 在发生错误时返回统一格式:

```json
{
  "success": false,
  "error": {
    "code": "User.NotFound",
    "message": "用户不存在"
  }
}
```

### HTTP 状态码

| 状态码 | 描述 |
|--------|------|
| 200 | 成功 |
| 201 | 创建成功 |
| 400 | 请求参数错误 |
| 401 | 未认证 |
| 403 | 权限不足 |
| 404 | 资源不存在 |
| 409 | 资源冲突 |
| 500 | 服务器内部错误 |

### 常见错误码

| 错误码 | 描述 |
|--------|------|
| `Auth.InvalidCredentials` | 用户名或密码错误 |
| `Auth.TokenExpired` | Token 已过期 |
| `User.NotFound` | 用户不存在 |
| `User.AlreadyExists` | 用户名已存在 |
| `File.NotFound` | 文件不存在 |
| `File.AlreadyExists` | 文件已存在 |
| `Permission.Denied` | 权限不足 |
| `Storage.InsufficientSpace` | 存储空间不足 |

---

## SignalR 实时通知

### 连接

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/nas", {
        accessTokenFactory: () => token
    })
    .build();

await connection.start();
```

### 事件

| 事件 | 数据 | 描述 |
|------|------|------|
| `FileCreated` | `{ path, type, size }` | 文件创建 |
| `FileUpdated` | `{ path, size }` | 文件更新 |
| `FileDeleted` | `{ path }` | 文件删除 |
| `FileMoved` | `{ oldPath, newPath }` | 文件移动 |
| `StorageUpdated` | `{ poolId, usedSpace }` | 存储更新 |
| `SystemAlert` | `{ level, message }` | 系统警告 |

### 订阅目录

```javascript
// 订阅目录变化
await connection.invoke("SubscribeToDirectory", "/documents");

// 取消订阅
await connection.invoke("UnsubscribeFromDirectory", "/documents");
```
