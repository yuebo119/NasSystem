# NAS System

一个现代化的网络附加存储 (NAS) 系统，基于 .NET 11 和 Vue 3 构建。

## 📋 功能特性

### 文件管理
- ✅ 文件浏览和目录导航（列表/网格双视图、面包屑、排序、虚拟滚动）
- ✅ 文件上传/下载（流式上传、Range 断点续传）
- ✅ 创建文件夹
- ✅ 重命名/移动/复制（剪贴板式剪切粘贴）
- ✅ 软删除和回收站
- ✅ 文件搜索（文件名 + 图片内文字 OCR 双通道）
- ✅ 缩略图生成（SkiaSharp 缩放、磁盘缓存、ETag/304）
- ✅ 文件扫描同步
- ✅ 文件预览（图片/视频/音频/文本）
- ✅ 文件版本控制（覆盖上传与永久删除前自动产版本，默认保留 10 个/30 天，可下载/恢复）
- ✅ SignalR 按目录订阅实时刷新

### 移动备份
- ✅ 备份设备注册/枚举/吊销
- ✅ 增量同步计划（清单 diff：缺路径或体积不同才传）
- ✅ PWA 可安装 + 移动优先备份页（选文件或整夹，进度可见，失败条目下次自动重试）

### 协议层访问（WebDAV / SMB）
- ✅ WebDAV 挂载：Windows 资源管理器、macOS Finder、Linux davfs2、iOS 文件 App 直接挂载（`/dav`，Basic 认证）
- ✅ WebDAV 完整文件操作经应用层：配额检查、软删除、实时推送全部生效，大文件不受网页端 500MB 上限约束
- ✅ SMB 网络磁盘直通（可选 sidecar，`docker compose --profile smb up -d`），管理员直通模式
- ✅ 认证暴力破解防护：同客户端 5 分钟 10 次失败锁定
- 详见 [协议层访问指南](docs/PROTOCOL_ACCESS.md)

### 文件分享
- ✅ 创建分享链接
- ✅ 公开/密码保护/仅内部用户（Internal）分享
- ✅ 设置过期时间
- ✅ 设置下载次数限制
- ✅ 文件夹分享浏览（流式 ZIP 打包下载）
- ✅ 分享统计（总数/有效/过期/下载量）

### 存储管理
- ✅ 存储池管理
- ✅ 容量监控（库记账与物理磁盘双口径）
- ✅ 容量限制设置
- ✅ 默认存储池设置
- ✅ 上传前空间预检（配额剩余与磁盘物理剩余取小）

### 用户管理
- ✅ 用户认证 (JWT)
- ✅ 角色权限管理（SuperAdmin/Admin/PowerUser/User）
- ✅ 用户配额管理
- ✅ 多用户支持
- ✅ 登录限流防暴力破解

### 系统管理与监控
- ✅ 系统信息八组域（应用/.NET 运行时/操作系统/处理器/内存/数据库/网络/进程）
- ✅ Dashboard 仪表盘（存储账目、分享概览、最近文件、回收站面板、快捷操作、自动刷新）
- ✅ 健康检查
- ✅ 目录浏览、驱动器信息
- ✅ Serilog 结构化日志

### 系统配置（Web 可配，改完即生效）
- ✅ 系统配置页（管理员）：文件版本保留策略（数量/天数）、OCR 上传后自动识别开关、WebDAV 启用开关与裸 HTTP 放行、快照定时策略（开关/cron/保留数）、语义搜图开关与自动建索引
- ✅ 配置由 descriptor 驱动：后端注册表是前后端单一事实源，新增可配项前端零改动
- ✅ WebDAV / SMB 服务状态卡：挂载地址、共享名、一键复制
- ✅ 全量校验后整单保存；上传上限/JWT/CORS 等启动期参数明示为只读，不留死键
- ✅ 维护操作：清空缩略图/转码缓存（确认文案写明重建代价，清空后按需重建）

### 界面与体验
- ✅ 中英文双语切换
- ✅ 暗色主题（跟随系统 + 手动持久化）
- ✅ 响应式布局（窄屏侧边栏改抽屉）

### 媒体库
- ✅ 图片/视频/音频按类型聚合，时间线（按月分节）浏览
- ✅ 元数据按需提取：图片尺寸（SkiaSharp）、MP4/MKV 时长与分辨率、MP3 时长（托管解析，无需 FFmpeg）
- ✅ 缩略图网格 + 时长/尺寸徽章，点击跳转文件管理器
- ✅ 语义搜图：自然语言搜图（CLIP 图文向量 + sqlite-vec KNN，模型经设置页下载，后台自动或手动建索引）
- ✅ 视频 HLS 多码率自适应播放（硬件编码优先，转码状态与缓存清理在设置页）

### 规划中
- 📋 生态扩展（ROADMAP Phase 4，按需不排期）

## 🛠 技术栈

### 后端
- .NET 11 (ASP.NET Core Minimal API)
- PalORM 6.5.0 + SQLite（编译期生成的微 ORM）
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
│   ├── USER_MANUAL.md           # 使用手册
│   ├── PROTOCOL_ACCESS.md       # 协议层访问指南（WebDAV/SMB）
│   └── ROADMAP.md               # 改造路线图
├── docker/
│   └── smb/                     # SMB sidecar（可选 profile: smb）
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
| admin | admin123（仅开发环境） | SuperAdmin |

> ⚠️ 首次登录后请立即修改默认密码
>
> 生产环境（`ASPNETCORE_ENVIRONMENT=Production`）不再播种 admin123：初始密码取配置项 `NasSystem:AdminPassword`（Docker 部署对应环境变量 `NasSystem__AdminPassword`），未提供时启动时生成随机密码并**一次性打印到启动日志**，请及时登录修改

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

> StoragePath 经环境变量 NasSystem__StoragePath 注入，appsettings 不设该段。

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/nas.db"
  },
  "Jwt": {
    "SecretKey": "<SET-VIA-ENV>",
    "Issuer": "NasSystem",
    "Audience": "NasSystemUsers",
    "AccessTokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
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
| [协议层访问指南](docs/PROTOCOL_ACCESS.md) | WebDAV/SMB 挂载方法 |
| [改造路线图](docs/ROADMAP.md) | 功能演进规划与阶段方案 |

## 🐳 Docker 部署

### 无 Docker 环境：WSL3 wslc 原生容器替代

本机没有 Docker 时，可用 WSL3 自带的 `wslc` 容器 CLI 完成相同的构建与运行验证（2026-10-20 全链路实测：镜像构建、Production 启动、nginx 反代、卷持久化均通过）。Docker Hub 被墙而 MCR 与镜像站可达时的操作顺序：

```bash
# 1. 基础镜像：MCR 直拉；Docker Hub 走镜像站后改回原名
#    （wslc 无 tag 命令，用一行的 Dockerfile 重新命名）
wslc pull mcr.microsoft.com/dotnet/sdk:11.0-preview
wslc pull mcr.microsoft.com/dotnet/aspnet:11.0-preview
wslc pull docker.1ms.run/library/node:20-alpine
wslc pull docker.1ms.run/library/nginx:alpine
printf 'FROM docker.1ms.run/library/node:20-alpine\n'  > /tmp/tag-node/Dockerfile
printf 'FROM docker.1ms.run/library/nginx:alpine\n' > /tmp/tag-nginx/Dockerfile
wslc build -t node:20-alpine  -f /tmp/tag-node/Dockerfile  /tmp/tag-node
wslc build -t nginx:alpine   -f /tmp/tag-nginx/Dockerfile /tmp/tag-nginx

# 2. 构建与运行（compose 的等效手工展开；wslc 无 compose 子命令）
wslc build -t nas-system:test .
wslc network create nas-network   # 用户网络才能按容器名解析
wslc run -d --name nas-system --network nas-network --network-alias nas-system \
  -p 6050:6050 --volume nas-data:/app/data \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e NasSystem__StoragePath=/app/storage \
  -e "Jwt__SecretKey=$(python -c 'import secrets;print(secrets.token_urlsafe(48))')" \
  -e Jwt__Issuer=NasSystem -e Jwt__Audience=NasSystemUsers \
  -e NasSystem__AdminPassword=your-initial-password \
  nas-system:test
```

三个实测踩坑（已对应修进仓库）：Git Bash 的 MSYS 会把 `-e` 值里的 `/app/storage` 改写为主机路径，传环境变量前加 `export MSYS_NO_PATHCONV=1`；`wslc run` 不认 `-v` 短选项，用 `--volume`；卷挂载的 source 用 Windows 路径（`C:\...`）。

### 环境变量

| 变量 | 描述 | 默认值 |
|------|------|--------|
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | Production |
| `JWT_SECRET` | JWT 密钥 | - |
| `ConnectionStrings__DefaultConnection` | 数据库连接 | - |
| `NasSystem__StoragePath` | 存储路径 | /app/storage |
| `NasSystem__AdminPassword` | 初始管理员密码（仅首次播种生效；不提供则生成随机密码并打印到启动日志） | - |
| `NasSystem__ForwardedHeaders__KnownProxies` | 反代真实客户端 IP：可信代理 IP 列表（填 nginx 容器 IP）。填了才采纳 X-Forwarded-For，WebDAV 失败锁定与分享限流由此按真实客户端隔离；不填则全体共享一个桶（详见 docker-compose.yml 注释） | - |
| `NasSystem__RateLimiting__ShareGlobalPerMinute` | 分享码探测的全局速率闸（次/分钟），防分布式枚举 | 600 |

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
| 1.1.0 | 2026-10-18 | 协议层落地（WebDAV 挂载 + SMB sidecar），Dashboard、系统信息八组域、Internal 分享、SignalR 实时推送、暗色主题与 i18n |
