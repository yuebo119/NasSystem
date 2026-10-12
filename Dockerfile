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

# 还原依赖
RUN dotnet restore

# 复制所有源码
COPY src/ src/

# 构建发布版本
RUN dotnet publish src/NasSystem.Api/NasSystem.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

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
