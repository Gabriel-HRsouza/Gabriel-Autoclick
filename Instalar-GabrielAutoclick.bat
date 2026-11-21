@echo off
chcp 65001 >nul 2>nul
setlocal enabledelayedexpansion
title Instalador - Gabriel Autoclick (Windows x64)

echo ========================================================
echo   GABRIEL AUTOCLICK - INSTALADOR RAPIDO PARA WINDOWS
echo ========================================================
echo.

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\GabrielAutoclick"
set "TARGET_EXE=%INSTALL_DIR%\GabrielAutoclick.exe"
set "LOCAL_EXE=%~dp0GabrielAutoclick.exe"
set "DOWNLOAD_URL=https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest/download/GabrielAutoclick.exe"

:: 1. Encerrar instancia aberta caso exista
taskkill /f /im GabrielAutoclick.exe >nul 2>nul

:: 2. Criar pasta de instalacao do usuario (nao requer Admin)
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
)

:: 3. Copiar executavel local (se veio do ZIP) ou baixar do GitHub Releases
if exist "%LOCAL_EXE%" (
    echo [1/3] Copiando GabrielAutoclick.exe local para "%INSTALL_DIR%"...
    copy /y "%LOCAL_EXE%" "%TARGET_EXE%" >nul
) else (
    echo [1/3] Baixando a versao mais recente do GitHub Releases...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%DOWNLOAD_URL%' -OutFile '%TARGET_EXE%' -UseBasicParsing"
)

if not exist "%TARGET_EXE%" (
    echo.
    echo [ERRO] Nao foi possivel obter o GabrielAutoclick.exe!
    echo Verifique sua conexao com a internet ou baixe diretamente em:
    echo https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest
    pause
    exit /b 1
)

:: 4. Remover bloqueio do SmartScreen (Zone.Identifier) e criar atalhos
echo [2/3] Configurando atalhos na Area de Trabalho e no Menu Iniciar...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Unblock-File -Path '%TARGET_EXE%' -ErrorAction SilentlyContinue; " ^
    "$WshShell = New-Object -ComObject WScript.Shell; " ^
    "$DesktopPath = [Environment]::GetFolderPath('Desktop'); " ^
    "$StartMenuPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Gabriel Autoclick.lnk'; " ^
    "$DesktopLnk = $WshShell.CreateShortcut((Join-Path $DesktopPath 'Gabriel Autoclick.lnk')); " ^
    "$DesktopLnk.TargetPath = '%TARGET_EXE%'; " ^
    "$DesktopLnk.WorkingDirectory = '%INSTALL_DIR%'; " ^
    "$DesktopLnk.Description = 'Gabriel Autoclick - Advanced Mouse Automation'; " ^
    "$DesktopLnk.IconLocation = '%TARGET_EXE%,0'; " ^
    "$DesktopLnk.Save(); " ^
    "$StartLnk = $WshShell.CreateShortcut($StartMenuPath); " ^
    "$StartLnk.TargetPath = '%TARGET_EXE%'; " ^
    "$StartLnk.WorkingDirectory = '%INSTALL_DIR%'; " ^
    "$StartLnk.Description = 'Gabriel Autoclick - Advanced Mouse Automation'; " ^
    "$StartLnk.IconLocation = '%TARGET_EXE%,0'; " ^
    "$StartLnk.Save();"

echo [3/3] Iniciando Gabriel Autoclick...
start "" "%TARGET_EXE%"

echo.
echo ========================================================
echo   INSTALACAO CONCLUIDA COM SUCESSO!
echo   - Instalado em: %TARGET_EXE%
echo   - Atalho criado na Area de Trabalho e Menu Iniciar
echo ========================================================
echo.
timeout /t 4
exit /b 0
