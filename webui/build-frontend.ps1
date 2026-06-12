$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $PSScriptRoot
$frontendDir = Join-Path $projectDir "webui"
$cacheDir = "C:\Users\qwq\AppData\Local\Temp\MorningCat-WebUI-Build"
$distDir = Join-Path $cacheDir "dist"
$targetDir = Join-Path $projectDir "MorningCat.WebUI\wwwroot\webui"

Write-Host "=== MorningCat WebUI Frontend Build Script ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/5] Cleaning cache directory..." -ForegroundColor Yellow
if (Test-Path $cacheDir) {
    Remove-Item -Path $cacheDir -Recurse -Force
}
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null

Write-Host "[2/5] Copying frontend to cache..." -ForegroundColor Yellow
Get-ChildItem -Path $frontendDir | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $cacheDir -Recurse -Force
}

Write-Host "[3/5] Building frontend..." -ForegroundColor Yellow
Push-Location $cacheDir
try {
    Write-Host "Current directory: $(Get-Location)" -ForegroundColor Gray
    Write-Host "Files in directory:" -ForegroundColor Gray
    Get-ChildItem -Name
    
    npm install --legacy-peer-deps
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE"
    }
    
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "npm run build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "[4/5] Copying build output to wwwroot..." -ForegroundColor Yellow
if (Test-Path $targetDir) {
    Remove-Item -Path $targetDir -Recurse -Force
}
New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

if (Test-Path $distDir) {
    Copy-Item -Path "$distDir\*" -Destination $targetDir -Recurse -Force
    Write-Host "Build output copied to: $targetDir" -ForegroundColor Green
}
else {
    throw "Build output directory not found: $distDir"
}

Write-Host "[5/5] Cleaning cache directory..." -ForegroundColor Yellow
Remove-Item -Path $cacheDir -Recurse -Force

Write-Host ""
Write-Host "=== Build completed successfully! ===" -ForegroundColor Green
Write-Host "Output: $targetDir" -ForegroundColor Cyan
