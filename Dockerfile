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
# RID 特定的 assets 文件，否则 --no-restore 会拿到不匹配的还原结果
RUN dotnet restore src/NasSystem.Api/NasSystem.Api.csproj -r linux-x64

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

# 安装必要工具
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# 创建非 root 用户
RUN groupadd -r nasuser && useradd -r -g nasuser nasuser

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
