@echo off
if not exist "%~dp0releases\CodexReset.exe" (
    echo 请先运行 Build-Exe.ps1 构建程序。
    pause
    exit /b 1
)
start "" "%~dp0releases\CodexReset.exe"
