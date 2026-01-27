# NAS System

一个现代化的网络附加存储 (NAS) 系统，基于 .NET 10 和 Vue 3 构建。

## 📋 功能特性

### 文件管理
- ✅ 文件浏览和导航
- ✅ 文件上传/下载
- ✅ 创建文件夹
- ✅ 重命名/移动/复制
- ✅ 软删除和回收站
- ✅ 文件搜索
- ✅ 缩略图生成
- ✅ 文件版本控制

### 存储管理
- ✅ 存储池管理
- ✅ RAID 支持 (0, 1, 5, 6, 10)
- ✅ 磁盘健康监控
- ✅ 容量统计

### 共享管理
- ✅ SMB/CIFS 共享
- ✅ NFS 共享
- ✅ WebDAV 支持
- ✅ 权限控制

### 用户管理
- ✅ 用户认证 (JWT)
- ✅ 角色权限管理
- ✅ 配额管理
- ✅ 多用户支持

## 🛠 技术栈

### 后端
- .NET 10 (ASP.NET Core)
- Entity Framework Core + SQLite
- Clean Architecture
- SignalR (实时通信)

### 前端
- Vue 3 + TypeScript
- Element Plus UI
- Pinia 状态管理
- Vite 构建工具

### 基础设施
- Docker 容器化
- Nginx 反向代理

## 📁 项目结构

```
NAS/
├── src/
│   ├── NasSystem.Api/           # Web API 入口
│   ├── NasSystem.Application/   # 应用服务层
│   ├── NasSystem.Domain/        # 领域模型
│   ├── NasSystem.Infrastructure/# 基础设施层
│   └── NasSystem.Shared/        # 共享组件
├── web/                         # Vue 前端
├── tests/
│   ├── NasSystem.UnitTests/     # 单元测试
│   └── NasSystem.IntegrationTests/ # 集成测试
├── docs/                        # 文档
├── Dockerfile
└── docker-compose.yml
```

## 🚀 快速开始

### 使用 Docker (推荐)

```bash
# 克隆项目
git clone https://github.com/your-repo/nas-system.git
cd nas-system

# 启动服务
docker-compose up -d

# 访问
open http://localhost:6050
```

### 本地开发

**后端:**
```bash
cd src/NasSystem.Api
dotnet run
# API 运行在 http://localhost:6050
```

**前端:**
```bash
cd web
npm install
npm run dev
# 前端运行在 http://localhost:6060
```

## 🧪 测试

```bash
# 运行所有测试
dotnet test

# 运行单元测试
dotnet test tests/NasSystem.UnitTests

# 运行集成测试
dotnet test tests/NasSystem.IntegrationTests
```

## 📊 测试覆盖

| 类型 | 数量 | 状态 |
|------|------|------|
| 单元测试 | 189 | ✅ 全部通过 |
| 集成测试 | 59 | ✅ 全部通过 |
| **总计** | **248** | ✅ **100%** |


## 🔧 配置

主要配置项 (`appsettings.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=nas.db"
  },
  "NasSystem": {
    "StoragePath": "/mnt/storage"
  },
  "Jwt": {
    "Secret": "your-secret-key",
    "Issuer": "NasSystem",
    "ExpirationMinutes": 60
  }
}
```

## 📄 API 文档

启动服务后访问: `http://localhost:6050/scalar/v1`

### 主要端点

| 端点 | 描述 |
|------|------|
| `POST /api/users/login` | 用户登录 |
| `GET /api/files/directory` | 获取目录内容 |
| `POST /api/files/upload` | 上传文件 |
| `GET /api/storage/pools` | 获取存储池 |
| `GET /api/shares` | 获取共享列表 |

## 📜 许可证

MIT License

## 🤝 贡献

欢迎提交 Pull Request 和 Issue!
