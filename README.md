# NAS System

一个现代化的网络附加存储 (NAS) 系统，基于 .NET 11 和 Vue 3 构建。

## 📋 功能特性

### 文件管理
- ✅ 文件浏览和目录导航
- ✅ 文件上传/下载
- ✅ 创建文件夹
- ✅ 重命名/移动/复制
- ✅ 软删除和回收站
- ✅ 文件搜索（支持类型、扩展名过滤）
- ✅ 缩略图生成
- ✅ 文件版本控制
- ✅ 文件扫描同步

### 文件分享
- ✅ 创建分享链接
- ✅ 公开/密码保护分享
- ✅ 设置过期时间
- ✅ 设置下载次数限制
- ✅ 文件夹分享浏览

### 存储管理
- ✅ 存储池管理
- ✅ 容量监控
- ✅ 容量限制设置
- ✅ 默认存储池设置

### 用户管理
- ✅ 用户认证 (JWT)
- ✅ 角色权限管理（SuperAdmin/Admin/PowerUser/User）
- ✅ 用户配额管理
- ✅ 多用户支持

### 系统管理
- ✅ 系统状态监控
- ✅ 健康检查
- ✅ 目录浏览
- ✅ 驱动器信息

## 🛠 技术栈

### 后端
- .NET 11 (ASP.NET Core Minimal API)
- PalORM 6.4.1 + SQLite（编译期生成的微 ORM）
- Clean Architecture
- SignalR (实时通信)
- JWT 认证

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
NasSystem/
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
│   ├── REQUIREMENTS.md          # 需求文档
│   ├── DESIGN.md                # 设计文档
│   ├── API_REFERENCE.md         # API 接口文档
│   ├── TESTING.md               # 测试文档
│   └── USER_MANUAL.md           # 使用手册
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

### 默认账户

| 用户名 | 密码 | 角色 |
|--------|------|------|
| admin | admin123 | SuperAdmin |

> ⚠️ 首次登录后请立即修改默认密码

## 🧪 测试

```bash
# 运行所有测试
dotnet test --solution NasSystem.sln

# 运行单元测试
dotnet test --project tests/NasSystem.UnitTests

# 运行集成测试
dotnet test --project tests/NasSystem.IntegrationTests
```

## 🔧 配置

主要配置项 (`appsettings.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/nas.db"
  },
  "NasSystem": {
    "StoragePath": "/app/storage"
  },
  "Jwt": {
    "Secret": "your-secret-key-at-least-32-characters",
    "Issuer": "NasSystem",
    "Audience": "NasSystemUsers",
    "ExpirationMinutes": 60
  }
}
```

## 📄 API 文档

启动服务后访问: `http://localhost:6050/scalar/v1`

### 主要端点

| 模块 | 端点 | 描述 |
|------|------|------|
| 用户 | `POST /api/v1/users/login` | 用户登录 |
| 用户 | `GET /api/v1/users/me` | 获取当前用户 |
| 文件 | `GET /api/v1/files/browse/{path}` | 浏览目录 |
| 文件 | `POST /api/v1/files/upload` | 上传文件 |
| 文件 | `GET /api/v1/files/download/{path}` | 下载文件 |
| 分享 | `POST /api/v1/file-shares` | 创建分享 |
| 分享 | `GET /api/v1/public-shares/{code}` | 访问分享 |
| 存储 | `GET /api/v1/storage/overview` | 存储概览 |
| 系统 | `GET /api/v1/system/health` | 健康检查 |

详细 API 文档请参阅 [API_REFERENCE.md](docs/API_REFERENCE.md)

## 📚 文档

| 文档 | 描述 |
|------|------|
| [需求文档](docs/REQUIREMENTS.md) | 系统功能需求说明 |
| [设计文档](docs/DESIGN.md) | 系统架构和设计说明 |
| [API 文档](docs/API_REFERENCE.md) | 完整的 API 接口参考 |
| [测试文档](docs/TESTING.md) | 测试策略和用例 |
| [使用手册](docs/USER_MANUAL.md) | 用户操作指南 |

## 🐳 Docker 部署

### 环境变量

| 变量 | 描述 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | Production |
| `JWT_SECRET` | JWT 密钥 | - |
| `ConnectionStrings__DefaultConnection` | 数据库连接 | - |
| `NasSystem__StoragePath` | 存储路径 | /app/storage |

### 数据卷

| 卷 | 描述 |
|------|------|
| `nas-data` | SQLite 数据库 |
| `nas-storage` | 文件存储 |
| `nas-logs` | 应用日志 |

## 🔒 安全说明

- 密码使用 PBKDF2-SHA256 加密存储
- API 使用 JWT Bearer Token 认证
- 支持 HTTPS 传输加密
- 输入验证防止注入攻击

## 📜 许可证

MIT License

## 🤝 贡献

欢迎提交 Pull Request 和 Issue!

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

---

## 版本历史

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.0.0 | 2026-01-29 | 初始版本 |
