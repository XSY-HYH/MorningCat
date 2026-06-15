# MorningCat 发布脚本
# 用法:
#   .\publish.ps1 ml        - 发布 MorningCatLaunch
#   .\publish.ps1 mlc       - 发布 MorningCatLaunchCore
#   .\publish.ps1 mctc      - 发布 MorningCat 完整核心包
#   .\publish.ps1 all       - 发布全部

param(
    [Parameter(Position=0)]
    [ValidateSet("ml", "mlc", "mctc", "all")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$OutputRoot = Join-Path $ProjectRoot "PublishOutput"
$Version = "5.0.3"

function Clean-BinObj {
    Write-Host "[Clean] 清理 bin/obj 目录..." -ForegroundColor Cyan
    Get-ChildItem -Path $ProjectRoot -Directory -Recurse -Include bin, obj |
        Where-Object { $_.FullName -notlike "*PublishOutput*" } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[Clean] 完成" -ForegroundColor Green
}

function Publish-ML {
    Write-Host "`n[ML] 发布 MorningCatLaunch..." -ForegroundColor Cyan
    $project = Join-Path $ProjectRoot "MorningCatLaunch\MorningCatLaunch.csproj"
    $outDir = Join-Path $OutputRoot "ML"

    # 先构建 MLC 以生成 mlc.zip
    Write-Host "[ML] 先构建 MorningCatLaunchCore..." -ForegroundColor Yellow
    $mlcProject = Join-Path $ProjectRoot "MorningCatLaunchCore\MorningCatLaunchCore.csproj"
    $mlcOut = Join-Path $ProjectRoot "MorningCatLaunchCore\bin\Release\net10.0"
    dotnet publish $mlcProject -c Release -o $mlcOut /p:Version=$Version

    # 打包 MLC 为 mlc.zip
    Write-Host "[ML] 打包 MorningCatLaunchCore 为 mlc.zip..." -ForegroundColor Yellow
    $mlcZipPath = Join-Path $ProjectRoot "MorningCatLaunch\mlc.zip"
    if (Test-Path $mlcZipPath) { Remove-Item $mlcZipPath -Force }
    Compress-Archive -Path "$mlcOut\*" -DestinationPath $mlcZipPath -Force

    # 发布 ML
    dotnet publish $project -c Release -o $outDir /p:Version=$Version

    # 清理临时 mlc.zip
    Remove-Item $mlcZipPath -Force -ErrorAction SilentlyContinue

    # 压缩输出
    $zipName = "MorningCatLaunch-$Version.zip"
    $zipPath = Join-Path $OutputRoot $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
    Write-Host "[ML] 完成: $zipPath" -ForegroundColor Green
}

function Publish-MLC {
    Write-Host "`n[MLC] 发布 MorningCatLaunchCore..." -ForegroundColor Cyan
    $project = Join-Path $ProjectRoot "MorningCatLaunchCore\MorningCatLaunchCore.csproj"
    $outDir = Join-Path $OutputRoot "MLC"

    dotnet publish $project -c Release -o $outDir /p:Version=$Version

    # 压缩输出
    $zipName = "MorningCatLaunchCore-$Version.zip"
    $zipPath = Join-Path $OutputRoot $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
    Write-Host "[MLC] 完成: $zipPath" -ForegroundColor Green
}

function Publish-MCTC {
    Write-Host "`n[MCTC] 发布 MorningCat 完整核心包..." -ForegroundColor Cyan
    $project = Join-Path $ProjectRoot "MorningCat\MorningCat.csproj"
    $outDir = Join-Path $OutputRoot "MCTC"

    dotnet publish $project -c Release -o $outDir /p:Version=$Version

    # 复制核心依赖
    $libDir = Join-Path $ProjectRoot "Lib"
    $coreFiles = @("logs.dll", "ModuleManagerLib.dll", "OneBotLib.dll", "MorningCat.PlatformAbstraction.dll")
    foreach ($file in $coreFiles) {
        $src = Join-Path $libDir $file
        if (Test-Path $src) {
            Copy-Item $src $outDir -Force
            Write-Host "[MCTC] 复制: $file"
        }
    }

    # 压缩输出
    $zipName = "MorningCat-Core-$Version.zip"
    $zipPath = Join-Path $OutputRoot $zipName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
    Write-Host "[MCTC] 完成: $zipPath" -ForegroundColor Green
}

# 主流程
Write-Host "MorningCat v$Version 发布工具" -ForegroundColor Magenta
Write-Host "=============================" -ForegroundColor Magenta

Clean-BinObj

if ($Target -eq "all") {
    Publish-ML
    Publish-MLC
    Publish-MCTC
} elseif ($Target -eq "ml") {
    Publish-ML
} elseif ($Target -eq "mlc") {
    Publish-MLC
} elseif ($Target -eq "mctc") {
    Publish-MCTC
}

Write-Host "`n发布完成! 输出目录: $OutputRoot" -ForegroundColor Magenta
