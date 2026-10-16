# NAS System API 接口文档

## 概述

- **基础路径**: `/api/v1`
- **认证方式**: JWT Bearer Token
- **内容类型**: `application/json`（除文件上传外）

---

## 1. 用户管理 API

### 1.1 用户登录

**POST** `/api/v1/users/login`

无需认证

**请求体：**
```json
{
  "username": "admin",
  "password": "password123",
  "rememberMe": false
}
```

**响应：**
```json
{
  "success": true,
  "data": {
    "token": "eyJhbGciOiJIUzI1NiIs...",
    "refreshToken": "...",
    "expiresAt": "2026-01-29T12:00:00Z",
    "user": {
      "id": "guid",
      "username": "admin",
      "displayName": "管理员",
      "email": "admin@example.com",
      "role": "Admin",
      "isEnabled": true,
      "quotaBytes": null,
      "usedBytes": 0,
      "createdAt": "2026-01-01T00:00:00Z"
    }
  }
}
```

### 1.2 用户登出

**POST** `/api/v1/users/logout`

需要认证

**响应：**
```json
{
  "success": true,
  "data": null
}
```

### 1.3 获取当前用户信息

**GET** `/api/v1/users/me`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "id": "guid",
    "username": "admin",
    "displayName": "管理员",
    "email": "admin@example.com",
    "role": "Admin",
    "isEnabled": true,
    "lastLoginAt": "2026-01-29T10:00:00Z",
    "quotaBytes": 10737418240,
    "usedBytes": 1073741824,
    "quotaDisplay": "10 GB",
    "usedDisplay": "1 GB",
    "usagePercentage": 10.0,
    "createdAt": "2026-01-01T00:00:00Z"
  }
}
```

### 1.4 获取用户列表

**GET** `/api/v1/users`

需要管理员权限

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| page | int | 否 | 页码，默认 1 |
| pageSize | int | 否 | 每页数量，默认 20 |
| searchTerm | string | 否 | 搜索关键词 |

**响应：**
```json
{
  "success": true,
  "data": {
    "items": [...],
    "page": 1,
    "pageSize": 20,
    "totalCount": 100,
    "totalPages": 5
  }
}
```

### 1.5 获取用户详情

**GET** `/api/v1/users/{id}`

需要管理员权限

### 1.6 创建用户

**POST** `/api/v1/users`

需要管理员权限

**请求体：**
```json
{
  "username": "newuser",
  "password": "password123",
  "displayName": "新用户",
  "email": "user@example.com",
  "role": "User",
  "quotaBytes": 10737418240
}
```

### 1.7 更新用户

**PUT** `/api/v1/users/{id}`

需要管理员权限

**请求体：**
```json
{
  "displayName": "更新后的名称",
  "email": "newemail@example.com",
  "role": "PowerUser",
  "isEnabled": true,
  "quotaBytes": 21474836480
}
```

### 1.8 删除用户

**DELETE** `/api/v1/users/{id}`

需要管理员权限

### 1.9 修改密码

**POST** `/api/v1/users/{id}/password`

需要认证（只能修改自己的密码，管理员可修改任何人）

**请求体：**
```json
{
  "currentPassword": "oldPassword",
  "newPassword": "newPassword123"
}
```

### 1.10 重置密码

**POST** `/api/v1/users/{id}/reset-password`

需要管理员权限

**请求体：**
```json
{
  "newPassword": "temporaryPassword"
}
```

---

## 2. 文件管理 API

### 2.1 获取目录内容

**GET** `/api/v1/files/browse/{*path}`

需要认证

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| includeHidden | bool | 否 | 是否包含隐藏文件，默认 false |

`includeHidden` 为 false 时隐藏文件由 SQL 的 `IsHidden = 0` 过滤，不在应用层筛选。
服务端不排序：条目顺序由前端按用户选择重排，服务端排序的产物会被完整丢弃。

**条件请求**：响应带 `ETag`，值为该目录的内容版本号。任何写操作提交成功后
（建文件夹、上传、重命名、移动、复制、删除、恢复、清空回收站、扫描同步）
版本号递增，ETag 随之变化。带 `If-None-Match` 且版本未变时返回 `304` 空响应体，
此时服务端不做数据库查询、不做 DTO 映射、不做 JSON 序列化。

**响应：**
```json
{
  "success": true,
  "data": {
    "path": "/documents",
    "name": "documents",
    "parentId": "guid",
    "parentPath": "/",
    "items": [
      {
        "id": "guid",
        "name": "report.pdf",
        "path": "/documents/report.pdf",
        "itemType": "File",
        "sizeBytes": 1024000,
        "sizeDisplay": "1 MB",
        "mimeType": "application/pdf",
        "extension": ".pdf",
        "hash": "sha256...",
        "parentId": "guid",
        "ownerId": "guid",
        "ownerName": "admin",
        "isHidden": false,
        "isReadOnly": false,
        "createdAt": "2026-01-01T10:00:00Z",
        "updatedAt": "2026-01-15T12:00:00Z"
      }
    ],
    "folderCount": 5,
    "fileCount": 10,
    "totalSizeBytes": 10485760,
    "totalSizeDisplay": "10 MB",
    "breadcrumbs": [
      { "name": "根目录", "path": "/" },
      { "name": "documents", "path": "/documents" }
    ]
  }
}
```

### 2.2 获取文件信息

**GET** `/api/v1/files/info/{*path}`

需要认证

### 2.3 上传文件

**POST** `/api/v1/files/upload`

需要认证

**Content-Type**: `multipart/form-data`

**表单字段：**
| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| file | File | 是 | 要上传的文件 |
| path | string | 否 | 目标路径，默认 "/" |
| overwrite | bool | 否 | 是否覆盖，默认 false |

### 2.4 下载文件

**GET** `/api/v1/files/download/{*path}`

需要认证

**响应**: 文件二进制流

### 2.5 创建文件夹

**POST** `/api/v1/files/folder`

需要认证

**请求体：**
```json
{
  "parentPath": "/documents",
  "name": "NewFolder"
}
```

### 2.6 重命名

**PUT** `/api/v1/files/rename`

需要认证

**请求体：**
```json
{
  "path": "/documents/old-name.txt",
  "newName": "new-name.txt"
}
```

### 2.7 删除

**POST** `/api/v1/files/delete`

需要认证

**请求体：**
```json
{
  "paths": ["/documents/file1.txt", "/documents/file2.txt"],
  "permanent": false
}
```

### 2.8 移动

**POST** `/api/v1/files/move`

需要认证

**请求体：**
```json
{
  "sourcePaths": ["/documents/file.txt"],
  "destinationPath": "/archive"
}
```

### 2.9 复制

**POST** `/api/v1/files/copy`

需要认证

**请求体：**
```json
{
  "sourcePaths": ["/documents/file.txt"],
  "destinationPath": "/backup"
}
```

### 2.10 搜索文件

**GET** `/api/v1/files/search`

需要认证

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| query | string | 是 | 搜索关键词 |
| path | string | 否 | 搜索路径 |
| extension | string | 否 | 文件扩展名 |
| itemType | string | 否 | 类型：file/folder |
| page | int | 否 | 页码，默认 1 |
| pageSize | int | 否 | 每页数量，默认 50，上限 200 |

分页与计数都在服务端完成（`LIMIT/OFFSET` 加同 `WHERE` 的 `COUNT`），
`totalCount` 是匹配总数而非当前页条数。`pageSize` 超过 200 会被夹到 200。
排序固定为文件夹优先再按名称，翻页依赖这个全序保证不重不漏。

### 2.11 获取回收站

**GET** `/api/v1/files/recycle-bin`

需要认证

### 2.12 恢复文件

**POST** `/api/v1/files/restore/{id}`

需要认证

### 2.13 清空回收站

**DELETE** `/api/v1/files/recycle-bin`

需要认证

### 2.14 获取文件统计

**GET** `/api/v1/files/statistics`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "totalFiles": 1000,
    "totalFolders": 100,
    "totalSizeBytes": 10737418240,
    "totalSizeDisplay": "10 GB",
    "imageCount": 500,
    "videoCount": 100,
    "audioCount": 50,
    "documentCount": 200,
    "otherCount": 150,
    "recentFilesCount": 50
  }
}
```

### 2.15 扫描同步

**POST** `/api/v1/files/scan`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "addedFiles": 10,
    "addedFolders": 2,
    "removedFiles": 5,
    "removedFolders": 1,
    "updatedFiles": 3,
    "totalSizeBytes": 1073741824,
    "totalSizeDisplay": "1 GB",
    "duration": "00:00:05.123",
    "message": "扫描完成"
  }
}
```

---

## 3. 缩略图 API

### 3.1 获取缩略图

**GET** `/api/v1/thumbnails/{*path}`

需要认证

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| width | int | 否 | 宽度，默认 200 |
| height | int | 否 | 高度，默认 200 |

`{*path}` 是虚拟路径，服务端据此查库取得物理路径后再解码。
请求的宽高会先吸附到服务端档位（64/128/256/512/1024），任意宽高组合不会各自生成缓存文件。

**条件请求**：响应带 `ETag`（由路径加请求尺寸决定，同 URL 恒定）与
`Cache-Control: private, max-age=86400`。带 `If-None-Match` 且值未变时返回 `304` 空响应体，
此时服务端不做任何数据库查询。前端经 fetch 拉 blob 再转 objectURL，
浏览器私有缓存可直接命中，重复进入同一目录时缩略图零请求。

### 3.2 检查缩略图支持

**GET** `/api/v1/thumbnails/supported/{*path}`

需要认证

**响应：**
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

### 3.3 清除缩略图缓存

**DELETE** `/api/v1/thumbnails/cache/{*path}`

需要认证

### 3.4 清除所有缓存

**DELETE** `/api/v1/thumbnails/cache`

需要管理员权限

---

## 4. 文件版本 API

### 4.1 获取版本列表

**GET** `/api/v1/file-versions/{fileItemId}`

需要认证

**响应：**
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
      "comment": "更新内容",
      "isCurrent": true,
      "createdByUserId": "guid",
      "createdByUsername": "admin",
      "createdAt": "2026-01-29T12:00:00Z"
    }
  ]
}
```

### 4.2 获取指定版本

**GET** `/api/v1/file-versions/{fileItemId}/{versionNumber}`

需要认证

### 4.3 下载指定版本

**GET** `/api/v1/file-versions/{fileItemId}/{versionNumber}/download`

需要认证

**响应**: 文件二进制流

### 4.4 恢复到指定版本

**POST** `/api/v1/file-versions/{fileItemId}/{versionNumber}/restore`

需要认证

### 4.5 比较版本

**GET** `/api/v1/file-versions/{fileItemId}/compare`

需要认证

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| version1 | int | 是 | 版本号 1 |
| version2 | int | 是 | 版本号 2 |

**响应：**
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

### 4.6 删除版本

**DELETE** `/api/v1/file-versions/{fileItemId}/{versionNumber}`

需要认证

---

## 5. 文件分享 API

### 5.1 创建分享

**POST** `/api/v1/file-shares`

需要认证

**请求体：**
```json
{
  "fileItemId": "guid",
  "accessType": "Public",
  "password": null,
  "permission": "Read",
  "expiresAt": "2026-02-28T23:59:59Z",
  "maxDownloads": 100
}
```

**accessType 可选值：**
- `Public` - 公开访问
- `Password` - 密码保护
- `Internal` - 仅内部用户

**响应：**
```json
{
  "success": true,
  "data": {
    "id": "guid",
    "shareCode": "abc123",
    "shareUrl": "http://localhost:6050/share/abc123",
    "fileItemId": "guid",
    "fileName": "document.pdf",
    "filePath": "/documents/document.pdf",
    "isFolder": false,
    "sizeBytes": 1024000,
    "sizeDisplay": "1 MB",
    "accessType": "Public",
    "permission": "Read",
    "expiresAt": "2026-02-28T23:59:59Z",
    "maxDownloads": 100,
    "downloadCount": 0,
    "isEnabled": true,
    "isExpired": false,
    "isDownloadLimitReached": false,
    "createdByUserId": "guid",
    "createdByUserName": "admin",
    "createdAt": "2026-01-29T10:00:00Z"
  }
}
```

### 5.2 获取我的分享列表

**GET** `/api/v1/file-shares`

需要认证

### 5.3 获取分享详情

**GET** `/api/v1/file-shares/{id}`

需要认证

### 5.4 更新分享

**PUT** `/api/v1/file-shares/{id}`

需要认证

**请求体：**
```json
{
  "accessType": "Password",
  "password": "newpassword",
  "clearPassword": false,
  "permission": "Read",
  "expiresAt": "2026-03-31T23:59:59Z",
  "clearExpiresAt": false,
  "maxDownloads": 200,
  "clearMaxDownloads": false,
  "isEnabled": true
}
```

### 5.5 删除分享

**DELETE** `/api/v1/file-shares/{id}`

需要认证

### 5.6 重新生成分享码

**POST** `/api/v1/file-shares/{id}/regenerate-code`

需要认证

---

## 6. 公开分享访问 API

### 6.1 获取分享信息

**GET** `/api/v1/public-shares/{shareCode}`

无需认证

**响应：**
```json
{
  "success": true,
  "data": {
    "shareCode": "abc123",
    "fileName": "document.pdf",
    "isFolder": false,
    "sizeBytes": 1024000,
    "sizeDisplay": "1 MB",
    "accessType": "Public",
    "permission": "Read",
    "requiresPassword": false,
    "isExpired": false,
    "isDownloadLimitReached": false,
    "isAccessible": true,
    "createdByUserName": "admin"
  }
}
```

### 6.2 验证分享密码

**POST** `/api/v1/public-shares/{shareCode}/verify`

无需认证

**请求体：**
```json
{
  "password": "sharepassword"
}
```

**响应：**
```json
{
  "success": true,
  "data": {
    "accessToken": "temporary-access-token"
  }
}
```

### 6.3 下载分享文件

**GET** `/api/v1/public-shares/{shareCode}/download`

无需认证（密码保护的分享需要 token 参数）

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| token | string | 条件 | 密码验证后获取的 token |

### 6.4 下载分享文件夹中的文件

**GET** `/api/v1/public-shares/{shareCode}/download/{*subPath}`

无需认证

### 6.5 获取分享文件夹内容

**GET** `/api/v1/public-shares/{shareCode}/files`

无需认证

**响应：**
```json
{
  "success": true,
  "data": {
    "shareCode": "abc123",
    "currentPath": "/",
    "items": [
      {
        "id": "guid",
        "name": "file.txt",
        "isFolder": false,
        "sizeBytes": 1024,
        "sizeDisplay": "1 KB",
        "mimeType": "text/plain",
        "createdAt": "2026-01-01T10:00:00Z"
      }
    ]
  }
}
```

### 6.6 获取分享子文件夹内容

**GET** `/api/v1/public-shares/{shareCode}/files/{*subPath}`

无需认证

---

## 7. 存储管理 API

### 7.1 获取存储概览

**GET** `/api/v1/storage/overview`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "totalBytes": 1099511627776,
    "usedBytes": 549755813888,
    "availableBytes": 549755813888,
    "usagePercentage": 50.0,
    "totalDisplay": "1 TB",
    "usedDisplay": "512 GB",
    "availableDisplay": "512 GB",
    "poolCount": 2,
    "healthStatus": "Healthy"
  }
}
```

### 7.2 获取存储池列表

**GET** `/api/v1/storage/pools`

需要认证

### 7.3 获取存储池详情

**GET** `/api/v1/storage/pools/{id}`

需要认证

### 7.4 创建存储池

**POST** `/api/v1/storage/pools`

需要管理员权限

**请求体：**
```json
{
  "name": "MainStorage",
  "description": "主存储池",
  "mountPath": "D:\\NasStorage",
  "maxCapacityBytes": 1099511627776
}
```

### 7.5 更新存储池

**PUT** `/api/v1/storage/pools/{id}`

需要管理员权限

**请求体：**
```json
{
  "name": "UpdatedName",
  "description": "更新后的描述",
  "isEnabled": true,
  "maxCapacityBytes": 2199023255552
}
```

### 7.6 删除存储池

**DELETE** `/api/v1/storage/pools/{id}`

需要管理员权限

### 7.7 设置默认存储池

**POST** `/api/v1/storage/pools/{id}/default`

需要管理员权限

### 7.8 刷新容量信息

**POST** `/api/v1/storage/refresh`

需要管理员权限

### 7.9 检查存储空间

**POST** `/api/v1/storage/check-space`

需要认证

**请求体：**
```json
{
  "fileSize": 1073741824,
  "storagePoolId": "guid"
}
```

**响应：**
```json
{
  "success": true,
  "data": {
    "hasEnoughSpace": true,
    "availableBytes": 549755813888,
    "requiredBytes": 1073741824,
    "message": "存储空间充足"
  }
}
```

---

## 8. 系统管理 API

### 8.1 健康检查

**GET** `/api/v1/system/health`

无需认证

**响应：**
```json
{
  "success": true,
  "data": {
    "status": "Healthy",
    "timestamp": "2026-01-29T10:00:00Z"
  }
}
```

### 8.2 获取系统信息

**GET** `/api/v1/system/info`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "name": "NAS System",
    "version": "1.0.0",
    "os": "Microsoft Windows NT 10.0.22631.0",
    "machineName": "NAS-SERVER",
    "processorCount": 8,
    "dotNetVersion": "10.0.0",
    "startTime": "2026-01-29T08:00:00Z"
  }
}
```

### 8.3 获取系统状态

**GET** `/api/v1/system/status`

需要认证

**响应：**
```json
{
  "success": true,
  "data": {
    "status": "Running",
    "uptime": "1.02:30:00",
    "memoryUsageMB": 256,
    "cpuTime": "00:15:30",
    "threadCount": 25,
    "handleCount": 500
  }
}
```

### 8.4 获取系统驱动器

**GET** `/api/v1/system/drives`

需要管理员权限

**响应：**
```json
{
  "success": true,
  "data": [
    {
      "name": "C:",
      "label": "System",
      "driveType": "Fixed",
      "totalSize": 256060514304,
      "availableSpace": 128030257152,
      "totalDisplay": "238.47 GB",
      "availableDisplay": "119.24 GB"
    }
  ]
}
```

### 8.5 浏览系统目录

**GET** `/api/v1/system/browse-directories`

需要管理员权限

**查询参数：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| path | string | 否 | 目录路径，空则返回驱动器列表 |

---

## 错误码参考

| 错误码 | HTTP 状态码 | 说明 |
|--------|-------------|------|
| Auth.InvalidCredentials | 401 | 用户名或密码错误 |
| Auth.TokenExpired | 401 | Token 已过期 |
| Auth.Unauthorized | 401 | 未授权访问 |
| User.NotFound | 404 | 用户不存在 |
| User.AlreadyExists | 409 | 用户名已存在 |
| User.CannotDeleteSuperAdmin | 403 | 不能删除超级管理员 |
| File.NotFound | 404 | 文件不存在 |
| File.AlreadyExists | 409 | 文件已存在 |
| File.AccessDenied | 403 | 文件访问被拒绝 |
| Share.NotFound | 404 | 分享不存在 |
| Share.Expired | 410 | 分享已过期 |
| Share.DownloadLimitReached | 410 | 下载次数已达上限 |
| Share.InvalidPassword | 401 | 分享密码错误 |
| Storage.NotFound | 404 | 存储池不存在 |
| Storage.InsufficientSpace | 507 | 存储空间不足 |
| Validation.InvalidInput | 400 | 输入验证失败 |

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2026-01-29 | 初始版本 |
