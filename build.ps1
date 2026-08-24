#requires -Version 5.1

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$project  = Join-Path $root 'src\VECLauncher\VECLauncher.csproj'
$outDir   = Join-Path $root 'билды exe'
$tempPub  = Join-Path $env:TEMP 'veclauncher_publish'

Write-Host ''
Write-Host 'VEC Launcher :: Single-File EXE Build' -ForegroundColor Green
Write-Host ''

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

if (-not $dotnet) {
    $candidates = @(
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $dotnet = $c; break }
    }
}
else { $dotnet = $dotnet.Source }

if (-not $dotnet) {
    Write-Host '.NET SDK not found. Installing .NET 8 SDK...' -ForegroundColor Yellow

    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & $installer -Channel 8.0 -Quality GA -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"

    $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
}

Write-Host "dotnet: $dotnet" -ForegroundColor DarkGray
& $dotnet --version

if (Test-Path $tempPub) { Remove-Item $tempPub -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host ''
Write-Host 'Publishing (self-contained, single-file, win-x64)...' -ForegroundColor Cyan
Write-Host ''

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false `
    -o $tempPub

if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed.' -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $tempPub 'VECLauncher.exe'
if (-not (Test-Path $exe)) { throw "VECLauncher.exe not found in $tempPub" }

Copy-Item $exe -Destination (Join-Path $outDir 'VECLauncher.exe') -Force

$size = [math]::Round((Get-Item (Join-Path $outDir 'VECLauncher.exe')).Length / 1MB, 1)

Write-Host ''
Write-Host 'DONE' -ForegroundColor Green
Write-Host ("File:    " + (Join-Path $outDir 'VECLauncher.exe'))
Write-Host ("Size:    $size MB")
Write-Host 'Runs on any Windows x64 PC without .NET installed.' -ForegroundColor DarkGray
Write-Host ''