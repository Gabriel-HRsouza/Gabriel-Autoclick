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
    Write-Host "Verificados:" -ForegroundColor Yellow
    foreach ($candidate in $dotnetCandidates) {
        Write-Host "  - $candidate" -ForegroundColor Yellow
    }
    Write-Host "Por favor, instale o .NET 8.0 SDK em https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

Write-Host "[INFO] Usando .NET SDK em: $dotnetExe" -ForegroundColor Green
$versionOutput = & $dotnetExe --version
Write-Host "[INFO] Versao do .NET SDK: $versionOutput" -ForegroundColor Green
Write-Host ""

# 2. Terminate any running instances
Write-Host "[1/5] Encerrando processos anteriores do Gabriel Autoclick..." -ForegroundColor Yellow
Stop-Process -Name "GabrielAutoclick" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 3. Clean bin, obj and release output folder
Write-Host "[2/5] Limpando pastas temporarias (bin, obj, release)..." -ForegroundColor Yellow
$projectDir = "$PSScriptRoot\GabrielAutoclick"
$releaseDir = "$PSScriptRoot\release"

if (Test-Path "$projectDir\bin") { Remove-Item -Path "$projectDir\bin" -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path "$projectDir\obj") { Remove-Item -Path "$projectDir\obj" -Recurse -Force -ErrorAction SilentlyContinue }
if (Test-Path $releaseDir) { Remove-Item -Path $releaseDir -Recurse -Force -ErrorAction SilentlyContinue }

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# 4. Restore dependencies
Write-Host "[3/5] Restaurando dependencias..." -ForegroundColor Yellow
& $dotnetExe restore "$projectDir\GabrielAutoclick.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERRO] Falha ao restaurar pacotes!" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}

# 5. Compile & Publish Self-Contained Single-File (win-x64 Release, no PDB)
Write-Host "[4/5] Compilando e Publicando Executavel Single-File (win-x64 Release)..." -ForegroundColor Yellow
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

# 6. Clean any non-exe residuals to guarantee pure standalone folder
Write-Host "[5/5] Limpando arquivos residuais da pasta release..." -ForegroundColor Yellow
Get-ChildItem -Path $releaseDir -Exclude "GabrielAutoclick.exe" | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$finalExe = "$releaseDir\GabrielAutoclick.exe"
if (Test-Path $finalExe) {
    $exeSize = (Get-Item $finalExe).Length / 1MB
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  BUILD CONCLUIDO COM SUCESSO!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Executavel gerado em:" -ForegroundColor Cyan
    Write-Host "  $finalExe" -ForegroundColor White
    Write-Host ("Tamanho: {0:N2} MB" -f $exeSize) -ForegroundColor Gray
    Write-Host ""
    Write-Host "Conteudo da pasta release:" -ForegroundColor Yellow
    Get-ChildItem -Path $releaseDir | ForEach-Object { Write-Host "  - $($_.Name) ($([math]::Round($_.Length/1MB, 2)) MB)" -ForegroundColor White }
    Write-Host ""
} else {
    Write-Host "[ERRO] O arquivo $finalExe nao foi encontrado apos o publish!" -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Pressione ENTER para fechar..." }
    exit 1
}
