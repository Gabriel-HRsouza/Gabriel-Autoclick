# Instalador Rápido via PowerShell (1 Comando) - Gabriel Autoclick
# Uso: irm https://raw.githubusercontent.com/Gabriel-HRsouza/Gabriel-Autoclick/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "  GABRIEL AUTOCLICK - INSTALADOR AUTOMATICO (WINDOWS)   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\GabrielAutoclick"
$TargetExe  = Join-Path $InstallDir "GabrielAutoclick.exe"
$ReleaseUrl = "https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases/latest/download/GabrielAutoclick.exe"

Write-Host "[1/4] Verificando processos em execucao..." -ForegroundColor Yellow
Stop-Process -Name "GabrielAutoclick" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Write-Host "[2/4] Baixando a versao mais recente do Gabriel Autoclick..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $ReleaseUrl -OutFile $TargetExe -UseBasicParsing
Unblock-File -Path $TargetExe -ErrorAction SilentlyContinue

Write-Host "[3/4] Criando atalhos na Area de Trabalho e Menu Iniciar..." -ForegroundColor Yellow
$WshShell = New-Object -ComObject WScript.Shell
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Gabriel Autoclick.lnk"
$StartMenuShortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "Gabriel Autoclick.lnk"

foreach ($ShortcutPath in @($DesktopShortcut, $StartMenuShortcut)) {
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)
    $Shortcut.TargetPath = $TargetExe
    $Shortcut.WorkingDirectory = $InstallDir
    $Shortcut.Description = "Gabriel Autoclick - Advanced Mouse Automation"
    $Shortcut.IconLocation = "$TargetExe,0"
    $Shortcut.Save()
}

Write-Host "[4/4] Iniciando o Gabriel Autoclick..." -ForegroundColor Green
Start-Process -FilePath $TargetExe -WorkingDirectory $InstallDir

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host "  GABRIEL AUTOCLICK INSTALADO COM SUCESSO!              " -ForegroundColor Green
Write-Host "  Local: $TargetExe" -ForegroundColor White
Write-Host "  Atalhos criados na Area de Trabalho e Menu Iniciar.   " -ForegroundColor White
Write-Host "========================================================" -ForegroundColor Green
