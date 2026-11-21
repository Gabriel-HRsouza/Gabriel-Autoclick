param(
    [switch]$NoPause = $false
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GABRIEL AUTOCLICK - BUILD RELEASE (PS1)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Discover dotnet.exe across all standard locations
$dotnetCandidates = @(
    "$env:USERPROFILE\.dotnet\dotnet.exe",
    "C:\Program Files\dotnet\dotnet.exe",
    "C:\Program Files (x86)\dotnet\dotnet.exe"
)

$dotnetExe = ""
foreach ($candidate in $dotnetCandidates) {
    if (Test-Path $candidate) {
        $dotnetExe = $candidate
        break
    }
}

if ([string]::IsNullOrWhiteSpace($dotnetExe)) {
    $cmd = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($cmd) {
        $dotnetExe = $cmd.Source
    }
}

if ([string]::IsNullOrWhiteSpace($dotnetExe) -or -not (Test-Path $dotnetExe)) {
    Write-Host "[ERRO CRITICO] O .NET SDK nao foi encontrado em nenhum local padrao!" -ForegroundColor Red
    Write-Host "Por favor, instale o .NET 8.0 SDK em https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

Write-Host "[INFO] Usando .NET SDK em: $dotnetExe" -ForegroundColor Green
$versionOutput = & $dotnetExe --version
Write-Host "[INFO] Versao do .NET SDK: $versionOutput" -ForegroundColor Green
Write-Host ""

# 2. Terminate any running instances
Write-Host "[1/6] Encerrando processos anteriores do Gabriel Autoclick..." -ForegroundColor Yellow
Stop-Process -Name "GabrielAutoclick" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 3. Clean bin, obj and release output folder
Write-Host "[2/6] Limpando pastas temporarias (bin, obj, release)..." -ForegroundColor Yellow
$projectDir = "$PSScriptRoot\GabrielAutoclick"
$releaseDir = "$PSScriptRoot\release"

if (Test-Path "$projectDir\bin") { Remove-Item -Path "$projectDir\bin" -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$projectDir\obj") { Remove-Item -Path "$projectDir\obj" -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $releaseDir) { Remove-Item -Path $releaseDir -Recurse -Force -ErrorAction SilentlyContinue }

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# 4. Restore dependencies
Write-Host "[3/6] Restaurando dependencias..." -ForegroundColor Yellow
& $dotnetExe restore "$projectDir\GabrielAutoclick.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERRO] Falha ao restaurar pacotes!" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

# 5. Compile & Publish Self-Contained Single-File (win-x64 Release, no PDB)
Write-Host "[4/6] Compilando e Publicando Executavel Single-File (win-x64 Release)..." -ForegroundColor Yellow
& $dotnetExe publish "$projectDir\GabrielAutoclick.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $releaseDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERRO] Falha na publicacao do executavel!" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

# 6. Clean any non-exe residuals
Write-Host "[5/6] Limpando arquivos residuais e gerando pacote ZIP para Windows..." -ForegroundColor Yellow
Get-ChildItem -Path $releaseDir -Exclude "GabrielAutoclick.exe" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$finalExe = "$releaseDir\GabrielAutoclick.exe"
if (-not (Test-Path $finalExe)) {
    Write-Host "[ERRO] O arquivo $finalExe nao foi encontrado apos o publish!" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

Unblock-File -Path $finalExe -ErrorAction SilentlyContinue

# Copy Installer Helper & Quick Guide into release folder for ZIP packaging
$installerBat = "$PSScriptRoot\Instalar-GabrielAutoclick.bat"
if (Test-Path $installerBat) {
    Copy-Item -Path $installerBat -Destination "$releaseDir\Instalar-GabrielAutoclick.bat" -Force
}

$leiaMeContent = @"
============================================================
  GABRIEL AUTOCLICK - GUIA RAPIDO PARA WINDOWS 10 / 11 (x64)
============================================================

COMO USAR (MODO PORTATIL - NAO PRECISA INSTALAR):
  1. Dê dois cliques diretamente em "GabrielAutoclick.exe".
  2. O aplicativo já inclui o runtime .NET 8 embutido (Single-File).

COMO CRIAR ATALHO NA AREA DE TRABALHO E MENU INICIAR:
  1. Dê dois cliques em "Instalar-GabrielAutoclick.bat".
  2. Ele copiará o app para sua pasta de programas do usuário e criará
     os atalhos automaticamente na Área de Trabalho e no Menu Iniciar.

DICA IMPORTANTE (JOGOS COMO ADMINISTRADOR):
  - Se o jogo alvo rodar como Administrador, clique com o botão direito
    em "GabrielAutoclick.exe" e selecione "Executar como Administrador".

ATALHOS GLOBAIS PADRAO:
  - F6        : Iniciar / Parar Autoclicker
  - F7        : Abrir Mira para Selecionar Posição
  - F8 / F9   : Iniciar / Parar Macro
  - F12 / ESC : PANIC BUTTON (Para tudo imediatamente)

Repositório Oficial e Atualizações:
https://github.com/Gabriel-HRsouza/Gabriel-Autoclick/releases
"@
Set-Content -Path "$releaseDir\LEIA-ME.txt" -Value $leiaMeContent -Encoding UTF8

# Create portable ZIP archive
$zipPath = "$releaseDir\GabrielAutoclick-win-x64.zip"
Compress-Archive -Path "$releaseDir\GabrielAutoclick.exe", "$releaseDir\Instalar-GabrielAutoclick.bat", "$releaseDir\LEIA-ME.txt" -DestinationPath $zipPath -Force

# 7. Generate SHA256SUMS.txt
Write-Host "[6/6] Gerando hashes SHA-256..." -ForegroundColor Yellow
$exeHash = (Get-FileHash -Path $finalExe -Algorithm SHA256).Hash.ToLower()
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
$shaContent = "$exeHash  GabrielAutoclick.exe`r`n$zipHash  GabrielAutoclick-win-x64.zip"
Set-Content -Path "$releaseDir\SHA256SUMS.txt" -Value $shaContent -Encoding ASCII

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  BUILD CONCLUIDO COM SUCESSO!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "Arquivos gerados em '$releaseDir':" -ForegroundColor Cyan
Get-ChildItem -Path $releaseDir | ForEach-Object {
    Write-Host ("  - {0,-32} ({1:N2} MB)" -f $_.Name, ($_.Length / 1MB)) -ForegroundColor White
}
Write-Host ""
