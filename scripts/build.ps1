# Builds Break App: runs the tests, then publishes a single BreakApp.exe to dist\.
# Needs the .NET 10 SDK (uses %LOCALAPPDATA%\dotnet10 if present, else dotnet on PATH).
# The exe is framework-dependent: the .NET 10 Desktop Runtime must be installed on the PC that runs it.
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $env:LOCALAPPDATA 'dotnet10\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

Write-Host "== tests" -ForegroundColor Cyan
& $dotnet test (Join-Path $root 'tests\BreakApp.Core.Tests') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

Write-Host "== publish" -ForegroundColor Cyan
$out = Join-Path $root 'dist'
$sc = if ($SelfContained) { 'true' } else { 'false' }
& $dotnet publish (Join-Path $root 'src\BreakApp') -c Release -r win-x64 --self-contained $sc `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = Join-Path $out 'BreakApp.exe'
Write-Host ("== done: {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)) -ForegroundColor Green
