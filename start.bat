@echo off
chcp 65001 >nul
title NAS System 一键启动

echo ========================================
echo        NAS System 一键启动脚本
echo ========================================
echo.

:: 检查 .NET SDK
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未找到 .NET SDK，请先安装 .NET 11 SDK
    pause
    exit /b 1
)

:: 检查 Node.js
where node >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 未找到 Node.js，请先安装 Node.js
    pause
    exit /b 1
)

echo [1/4] 正在还原 .NET 依赖...
dotnet restore --verbosity quiet
if %errorlevel% neq 0 (
    echo [错误] .NET 依赖还原失败
    pause
    exit /b 1
)

echo [2/4] 正在安装前端依赖...
cd web
if not exist node_modules (
    call npm install
    if %errorlevel% neq 0 (
        echo [错误] 前端依赖安装失败
        cd ..
        pause
        exit /b 1
    )
)
cd ..

echo [3/4] 正在启动后端服务 (端口 6050)...
start "NAS API Server" cmd /c "cd src\NasSystem.Api && dotnet run --no-restore"

:: 等待后端启动
echo 等待后端服务启动...
timeout /t 5 /nobreak >nul

echo [4/4] 正在启动前端服务 (端口 6060)...
start "NAS Web Server" cmd /c "cd web && npm run dev"

:: 等待前端启动
timeout /t 3 /nobreak >nul

echo.
echo ========================================
echo           启动完成！
echo ========================================
echo.
echo   开发模式 (推荐): http://localhost:6060
echo     - Vite 开发服务器，支持热更新
echo     - 自动代理 API 请求到后端
echo.
echo   生产模式: http://localhost:6050
echo     - 后端直接托管前端静态文件
echo     - API 接口: /api/v1/*
echo     - API 文档: /scalar/v1
echo.
echo   默认账号: admin
echo   默认密码: Admin@123
echo.
echo   按任意键打开浏览器 (开发模式)...
pause >nul

start http://localhost:6060

echo.
echo 提示: 关闭此窗口不会停止服务
echo       如需停止，请关闭 "NAS API Server" 和 "NAS Web Server" 窗口
