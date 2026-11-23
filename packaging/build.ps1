# HanVGamepad 统一构建入口
# 用法:
#   pwsh packaging/build.ps1                   # 全平台产物
#   pwsh packaging/build.ps1 -Runtime win-x64  # 仅 Windows
#   pwsh packaging/build.ps1 -Runtime linux-x64
#   pwsh packaging/build.ps1 -SkipWeb          # 跳过 web-client 构建（已有产物时）
param(
    [string]$Runtime = "",
    [switch]$SkipWeb
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"

Write-Host "== HanVGamepad build ==" -ForegroundColor Cyan

# 1. web-client → src/desktop/wwwroot
if (-not $SkipWeb) {
    Write-Host "[1/3] building web-client..." -ForegroundColor Yellow
    Push-Location (Join-Path $root "web-client")
    try {
        npm install
        npm run build
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "[1/3] skipped web-client (-SkipWeb)" -ForegroundColor Yellow
}

# 2. dotnet publish
$runtimes = if ($Runtime) { @($Runtime) } else { @("win-x64", "linux-x64", "linux-arm64", "osx-arm64", "osx-x64") }

Write-Host "[2/3] publishing .NET hosts..." -ForegroundColor Yellow
foreach ($rid in $runtimes) {
    $out = Join-Path $artifacts $rid
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    if ($rid -like "win-*") {
        # Windows：目录展开发布（散装 dll + exe）。单文件 exe 每次启动都要自解压到临时目录，
        # Windows 产物明确不用这种形态；绿色包与安装包见根目录 windows-build.ps1。
        dotnet publish (Join-Path $root "src/desktop/HanVGamepad.Desktop.csproj") -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o (Join-Path $out "gui")
        dotnet publish (Join-Path $root "src/cli/HanVGamepad.Cli.csproj")     -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o (Join-Path $out "cli")
    }
    else {
        # Linux / macOS：CLI（headless）+ 可选 GUI。linux-arm64 仅 CLI（树莓派等无 GUI 场景）。
        # 与 Windows 一致：目录展开，不用单文件自解压。
        dotnet publish (Join-Path $root "src/cli/HanVGamepad.Cli.csproj") -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o (Join-Path $out "cli")
        if ($rid -ne "linux-arm64") {
            dotnet publish (Join-Path $root "src/desktop/HanVGamepad.Desktop.csproj") -c Release -r $rid --self-contained true -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false -o (Join-Path $out "gui")
        }
    }

    # 许可文件随包分发。
    Copy-Item (Join-Path $root "README.md") (Join-Path $out "README.md") -Force
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $out "LICENSE") -Force
    Copy-Item (Join-Path $root "LICENSE-ORIGINAL") (Join-Path $out "LICENSE-ORIGINAL") -Force

    Write-Host "  -> $out" -ForegroundColor Green
}

# 3. 汇总
Write-Host "[3/3] done. artifacts:" -ForegroundColor Cyan
Get-ChildItem $artifacts -Directory | ForEach-Object { Write-Host "  $($_.FullName)" }
