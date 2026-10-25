<#
  CaféCostos · Script de publicación y empaquetado
  ------------------------------------------------
  1. Ejecuta las pruebas del motor de costeo.
  2. Publica CafeCostos.exe autocontenido (win-x64, un solo archivo, ReadyToRun).
  3. Compila el instalador con Inno Setup → publish\CafeCostos_Instalador_X.Y.Z.exe

  Uso (PowerShell, en la carpeta de la solución):
      .\publicar.ps1
      .\publicar.ps1 -SinPruebas
#>
param([switch]$SinPruebas)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "== CaféCostos: publicación ==" -ForegroundColor Cyan

# --- Requisitos ---
# (Los componentes de Microsoft ya NO se empaquetan: el instalador los descarga si faltan.)
# Busca Inno Setup 6 o 7 (o posterior) en las carpetas habituales
$iscc = @("${env:ProgramFiles(x86)}", "$env:ProgramFiles", "$env:LOCALAPPDATA\Programs") |
    Where-Object { $_ -and (Test-Path $_) } |
    ForEach-Object { Get-ChildItem $_ -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue } |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "No se encontró Inno Setup. Instálelo desde https://jrsoftware.org/isdl.php" }

# --- 1. Pruebas ---
if (-not $SinPruebas) {
    Write-Host "`n[1/3] Ejecutando pruebas del motor de costeo..." -ForegroundColor Yellow
    dotnet test tests\CafeCostos.Tests\CafeCostos.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Las pruebas fallaron. No se publica." }
}

# --- 2. Publicación ---
Write-Host "`n[2/3] Publicando CafeCostos.exe autocontenido..." -ForegroundColor Yellow
if (Test-Path publish\app) { Remove-Item publish\app -Recurse -Force }
dotnet publish src\CafeCostos.App\CafeCostos.App.csproj -p:PublishProfile=Win64 --nologo
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }
$exe = Get-Item publish\app\CafeCostos.exe
Write-Host ("    CafeCostos.exe: {0:N1} MB" -f ($exe.Length / 1MB))

# --- 3. Instalador ---
Write-Host "`n[3/3] Compilando el instalador..." -ForegroundColor Yellow
& $iscc /Q installer\CafeCostos.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup reportó errores." }

$inst = Get-ChildItem publish\CafeCostos_Instalador_*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`nListo: $($inst.FullName) ($([math]::Round($inst.Length/1MB,1)) MB)" -ForegroundColor Green
