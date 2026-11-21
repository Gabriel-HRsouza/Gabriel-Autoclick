@echo off
setlocal enabledelayedexpansion

echo ========================================
echo   GABRIEL AUTOCLICK - BUILD RELEASE
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -NoPause
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha durante a geracao do release!
    pause
    exit /b 1
)

exit /b 0
