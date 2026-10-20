# =============================================================================
# NAS System - Dockerfile
# 多阶段构建: 前端 + 后端
# =============================================================================

# ============== 阶段 1: 构建前端 ==============
FROM node:20-alpine AS frontend-build
WORKDIR /app/web

# 复制前端依赖文件
COPY web/package*.json ./

# 安装依赖
RUN npm ci --silent

# 复制前端源码
COPY web/ ./

# 构建前端
RUN npm run build

# ============== 阶段 2: 构建后端 ==============
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS backend-build
WORKDIR /src

# 复制解决方案和项目文件
COPY NasSystem.sln ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/NasSystem.Api/NasSystem.Api.csproj src/NasSystem.Api/
COPY src/NasSystem.Application/NasSystem.Application.csproj src/NasSystem.Application/
COPY src/NasSystem.Domain/NasSystem.Domain.csproj src/NasSystem.Domain/
COPY src/NasSystem.Infrastructure/NasSystem.Infrastructure.csproj src/NasSystem.Infrastructure/
COPY src/NasSystem.Shared/NasSystem.Shared.csproj src/NasSystem.Shared/

# 还原依赖。必须显式指定工程而非裸 `dotnet restore`：
# .dockerignore 排除了 tests 与 tools，而 NasSystem.sln 引用了这两个目录下的工程，
# 裸 restore 会以 MSB3202 "未找到项目文件" 失败（2026-10-12 实测复现）。
# 指定 API 工程会连带还原它的全部项目依赖链，行为与还原整个 src 子集等价。
# 必须带 -r linux-x64：下面的 publish 用了 ReadyToRun，RID 特定的发布需要
# RID 特定的 assets 文件，否则 --no-restore 会拿到不匹配的还原结果。
# 还必须带 -p:PublishReadyToRun=true：ReadyToRun 优化要运行时包
# （Microsoft.NETCore.App.Runtime.linux-x64），restore 不带该属性时
# assets 里没有运行时包，publish 阶段报 NETSDK1094（2026-10-20 wslc
# 容器构建实证：本机从无 Docker，该镜像在修掉此前从未构建成功过）
RUN dotnet restore src/NasSystem.Api/NasSystem.Api.csproj -r linux-x64 -p:PublishReadyToRun=true

# 复制所有源码
COPY src/ src/

# 构建发布版本。
# PublishReadyToRun：把热点方法预编译为本地码，省掉启动后 tiered 编译的爬坡。
# 这个项目的启动路径不短（MigrateAsync 建表 + 补两个部分索引 + 5 个实体 schema
# 校验 + EF 残留检查 + 种子判定），冷启动时间是用户和 HEALTHCHECK 都能感知的。
# 代价：产物变大（约 +10~20MB），且必须与运行时同 RID 同架构，故只在 Docker
# 这一条 linux-x64 链路上开，Windows 开发机构保持框架依赖的 JIT 形态。
# --self-contained false 保持框架依赖：带了 -r 会让 publish 默认变成自包含，
# 那会把整个运行时打进镜像，与 aspnet 运行时镜像重复
RUN dotnet publish src/NasSystem.Api/NasSystem.Api.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish \
    --no-restore \
    -p:PublishReadyToRun=true

# ============== 阶段 3: 运行时镜像 ==============
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS runtime
WORKDIR /app

# 安装必要工具。ffmpeg（ROADMAP Phase 3 视频转码）：Debian 仓库版约
# 100MB 级（含 libx264 等编解码器链）。镜像体积代价换取 HLS 按需转码
# 能力；转码输出落临时目录由应用管理，镜像本身不含媒体数据。
# 装在哪：aspnet 镜像的 apt 源即 Debian bookworm/trixie 官方源，
# ffmpeg 是 main 组件，无需第三方源
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# 创建非 root 用户。UID/GID 固定 1000：SMB sidecar（docker/smb）以
# force user = nasuser 映射共享卷上的全部写入，两侧 UID 不一致时
# sidecar 落盘的文件应用层无权读写。固定值是两侧对齐的唯一真源。
# 幂等四种情况都覆盖：已是 nasuser / 1000 被别的名字占（2026-10-20
# wslc 构建实证：新版 aspnet 基础镜像自带 ubuntu 用户占着 1000，
# 裸 groupadd -g 1000 直接 GID already exists 失败） / 1000 空闲 /
# 老基础镜像上从未有 1000。占用者统一改名为 nasuser（UID/GID 不变）
RUN set -eux; \
    if getent group 1000 | cut -d: -f1 | grep -qx nasuser; then :; \
    elif getent group 1000 >/dev/null 2>&1; then \
        groupmod -n nasuser "$(getent group 1000 | cut -d: -f1)"; \
    else \
        groupadd -r -g 1000 nasuser; \
    fi; \
    if getent passwd 1000 | cut -d: -f1 | grep -qx nasuser; then :; \
    elif getent passwd 1000 >/dev/null 2>&1; then \
        usermod -l nasuser -d /home/nasuser -g nasuser "$(getent passwd 1000 | cut -d: -f1)"; \
    else \
        useradd -r -u 1000 -g nasuser nasuser; \
    fi

# 创建数据目录
RUN mkdir -p /app/data /app/storage /app/logs \
    && chown -R nasuser:nasuser /app

# 复制后端发布文件
COPY --from=backend-build /app/publish .

# 复制前端构建文件到 wwwroot
COPY --from=frontend-build /app/web/../src/NasSystem.Api/wwwroot ./wwwroot

# 设置环境变量
ENV ASPNETCORE_URLS=http://+:6050
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/nas.db"
ENV NasSystem__StoragePath=/app/storage

# 切换到非 root 用户
USER nasuser

# 暴露端口
EXPOSE 6050

# 健康检查
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:6050/health || exit 1

# 启动应用
ENTRYPOINT ["dotnet", "NasSystem.Api.dll"]
