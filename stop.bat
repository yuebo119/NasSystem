@echo off
chcp 65001 >nul
title NAS System 停止脚本

echo ========================================
echo        NAS System 停止脚本
echo ========================================
echo.

echo 正在停止服务...

:: 停止 dotnet 进程
taskkill /f /im dotnet.exe >nul 2>&1

:: 停止 node 进程 (前端开发服务器)
taskkill /f /im node.exe >nul 2>&1

:: 停止 API 宿主进程：start.bat 用 dotnet run 拉起后，API 以独立的
:: NasSystem.Api.exe apphost 运行，只杀 dotnet.exe 会把它留成孤儿进程
:: 继续占用 6050，下次启动报"地址已被占用"
taskkill /f /im NasSystem.Api.exe >nul 2>&1

echo.
echo 所有服务已停止
echo.
pause
