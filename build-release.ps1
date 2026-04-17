# Build a self-contained release package for deployment to a client PC.
# Run from the project root: .\build-release.ps1
# Output: dist\GymSyncZkt\ — copy this entire folder to a USB/share and
#         run install.ps1 as Administrator on the client PC.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = "$root\dist\GymSyncZkt"

Write-Host ""
Write-Host "Building GymSync ZKTeco Middleware..." -ForegroundColor Cyan
Write-Host ""

# Clean
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
New-Item -ItemType Directory -Force -Path "$dist\app" | Out-Null

# Publish self-contained
Write-Host "[1/4] Publishing self-contained build..." -ForegroundColor Yellow
dotnet publish "$root\src\GymSync.Zkt.WebUI" `
    -c Release `
    -o "$dist\app" `
    --self-contained true `
    -r win-x64 `
    -p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Copy SDK
Write-Host "[2/4] Copying ZKTeco SDK..." -ForegroundColor Yellow
Copy-Item -Path "$root\sdk" -Destination "$dist\sdk" -Recurse

# Copy install scripts
Write-Host "[3/4] Copying install scripts..." -ForegroundColor Yellow
Copy-Item "$root\install\install.ps1" $dist
Copy-Item "$root\install\uninstall.ps1" $dist
Copy-Item "$root\config.example.json" "$dist\config.json"

# Create a simple batch launcher for the install script
Write-Host "[4/4] Creating launcher..." -ForegroundColor Yellow
@"
@echo off
echo ============================================
echo   GymSync ZKTeco Middleware — Installer
echo ============================================
echo.
echo This will install the GymSync ZKTeco middleware
echo as a Windows service on this PC.
echo.
echo Press any key to continue or close this window to cancel...
pause >nul
powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
"@ | Set-Content "$dist\INSTALL.bat" -Encoding ASCII

@"
@echo off
echo Uninstalling GymSync ZKTeco Middleware...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
pause
"@ | Set-Content "$dist\UNINSTALL.bat" -Encoding ASCII

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Output: $dist"
Write-Host ""
Write-Host "  To deploy:"
Write-Host "    1. Copy the dist\GymSyncZkt\ folder to the client PC"
Write-Host "    2. Edit config.json with the client's device IPs"
Write-Host "    3. Right-click INSTALL.bat -> Run as Administrator"
Write-Host ""
