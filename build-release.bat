@echo off
setlocal enabledelayedexpansion

echo ========================================
echo   GABRIEL AUTOCLICK - BUILD RELEASE
echo ========================================
echo.

:: 1. Search for dotnet.exe
set "DOTNET_CMD="

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_CMD=%USERPROFILE%\.dotnet\dotnet.exe"
) else if exist "C:\Program Files\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Program Files\dotnet\dotnet.exe"
) else if exist "C:\Program Files (x86)\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Program Files (x86)\dotnet\dotnet.exe"
) else (
    where dotnet >nul 2>nul
    if %ERRORLEVEL% EQU 0 (
        set "DOTNET_CMD=dotnet"
    )
)

if "%DOTNET_CMD%"=="" (
    echo [ERRO CRITICO] O .NET SDK nao foi encontrado no sistema!
    echo Verifique se o .NET 8.0 SDK esta instalado.
    pause
    exit /b 1
)

echo [INFO] Utilizando .NET em: "%DOTNET_CMD%"
"%DOTNET_CMD%" --version
echo.

:: 2. Terminate old processes
echo [1/5] Encerrando processos anteriores...
taskkill /f /im GabrielAutoclick.exe >nul 2>nul
timeout /t 1 /nobreak >nul 2>nul

:: 3. Clean temporary folders
echo [2/5] Limpando pastas temporarias...
if exist "GabrielAutoclick\bin" rmdir /s /q "GabrielAutoclick\bin"
if exist "GabrielAutoclick\obj" rmdir /s /q "GabrielAutoclick\obj"
if exist "release" rmdir /s /q "release"
mkdir "release"

:: 4. Restore
echo [3/5] Restaurando dependencias...
"%DOTNET_CMD%" restore "GabrielAutoclick\GabrielAutoclick.csproj"
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha ao restaurar dependencias!
    pause
    exit /b 1
)

:: 5. Publish Clean Single-File
echo [4/5] Publicando executavel Single-File (win-x64 Release)...
"%DOTNET_CMD%" publish "GabrielAutoclick\GabrielAutoclick.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "release"
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Falha ao publicar executavel!
    pause
    exit /b 1
)

:: 6. Clean any non-exe residuals
echo [5/5] Limpando arquivos residuais...
for %%F in ("release\*") do (
    if /i not "%%~nxF"=="GabrielAutoclick.exe" del /f /q "%%F"
)
for /d %%D in ("release\*") do (
    rmdir /s /q "%%D"
)

echo.
echo ========================================
echo   BUILD CONCLUIDO COM SUCESSO!
echo   Executavel standalone: release\GabrielAutoclick.exe
echo ========================================
echo.
exit /b 0
