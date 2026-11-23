<#
.SYNOPSIS
    HanVGamepad Windows 构建脚本

.DESCRIPTION
    web-client 构建 → dotnet 发布（目录展开形态）→ 绿色包（目录 + zip）→ Inno Setup 安装包。
    发布形态固定为「目录内散装 dll + exe」，不使用 PublishSingleFile / IncludeNativeLibrariesForSelfExtract：
    单文件 exe 每次启动都要把自己解压到临时目录，冷启动慢、杀软与便携场景都不友好。

    无参数运行时进入交互式问答；带参数时直接执行（适合 CI / 自动化）。

.PARAMETER Version
    覆盖版本号。默认读取 src/desktop/HanVGamepad.Desktop.csproj 的 <Version>。
    只接受 x.y.z 或 x.y.z.w，会同步写入发布程序集的文件版本。

.PARAMETER Clean
    构建前清理 artifacts\win-x64 与 artifacts\packages，并结束占用这些目录的进程。

.PARAMETER SkipWeb
    跳过 web-client 构建，复用 src/desktop/wwwroot 现有产物。

.PARAMETER SkipPublish
    跳过 dotnet publish，直接用 artifacts\win-x64\HanVGamepad 里已有的发布产物重新组包。

.PARAMETER NoPortable
    不生成绿色包 zip（目录仍会生成）。

.PARAMETER NoInstaller
    不生成安装包（缺 Inno Setup 时也用它跳过）。

.PARAMETER IncludeSymbols
    发布时保留 .pdb（默认不发布符号文件）。

.EXAMPLE
    .\windows-build.ps1
    .\windows-build.ps1 -Clean
    .\windows-build.ps1 -Version 1.1.0 -SkipWeb
    .\windows-build.ps1 -NoInstaller
#>

param(
    [string]$Version = "",
    [switch]$Clean,
    [switch]$SkipWeb,
    [switch]$SkipPublish,
    [switch]$NoPortable,
    [switch]$NoInstaller,
    [switch]$IncludeSymbols
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ------------------------------------------------------------
#  路径
# ------------------------------------------------------------

$root       = $PSScriptRoot
$desktopProj = Join-Path $root 'src/desktop/HanVGamepad.Desktop.csproj'
$cliProj     = Join-Path $root 'src/cli/HanVGamepad.Cli.csproj'
$webDir      = Join-Path $root 'web-client'
$wwwroot     = Join-Path $root 'src/desktop/wwwroot'

$artifacts  = Join-Path $root 'artifacts'
$ridDir     = Join-Path $artifacts 'win-x64'
$stageDir   = Join-Path $ridDir 'HanVGamepad'      # 绿色包内容，同时是安装包的 SourceDir
$cliDir     = Join-Path $stageDir 'cli'
$packages   = Join-Path $artifacts 'packages'

$issPath    = Join-Path $root 'packaging/windows/HanVGamepad.iss'
$rid        = 'win-x64'

# ------------------------------------------------------------
#  输出工具
# ------------------------------------------------------------

function Write-Step {
    param([string]$Step, [string]$Msg)
    Write-Host ""
    Write-Host "  [$Step] $Msg" -ForegroundColor Cyan
    Write-Host "  $('-' * 50)" -ForegroundColor DarkGray
}

function Write-OK   { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Err  { param([string]$Msg) Write-Host "  [ERROR] $Msg" -ForegroundColor Red }
function Write-Warn { param([string]$Msg) Write-Host "  [WARN] $Msg" -ForegroundColor Yellow }
function Write-Note { param([string]$Msg) Write-Host "        $Msg" -ForegroundColor DarkGray }

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Format-Duration {
    param([TimeSpan]$Duration)
    if ($Duration.TotalMinutes -ge 1) {
        return "{0}m {1:N0}s" -f [math]::Floor($Duration.TotalMinutes), $Duration.Seconds
    }
    return "{0:N1}s" -f $Duration.TotalSeconds
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N1} MB" -f ($Bytes / 1MB) }
    return "{0:N0} KB" -f ($Bytes / 1KB)
}

function Ask-YesNo {
    param([string]$Prompt, [bool]$Default = $false)
    $suffix = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $answer = Read-Host "  $Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer -match '^[yY]'
}

function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    return [long]((Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum)
}

# 发布目录里可能还有上一次构建进程驻留，删除前先收尸。
function Stop-ProductProcesses {
    foreach ($name in @('HanVGamepad.Desktop', 'hanvgamepad')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Note "结束进程: $($_.ProcessName) (PID $($_.Id))"
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 500
}

# 读 csproj 里的 <Version>
function Get-ProjectVersion {
    param([string]$ProjectPath)
    $match = Select-String -LiteralPath $ProjectPath -Pattern '<Version>([^<]+)</Version>' |
        Select-Object -First 1
    if (-not $match) { return $null }
    return $match.Matches[0].Groups[1].Value.Trim()
}

# 1.2.3 → 1.2.3.0；预发布后缀（-beta）在这里被丢弃，只用于文件版本。
function ConvertTo-FourPartVersion {
    param([string]$PsVersion)
    $clean = ($PsVersion -replace '[-+].*$', '')
    $parts = $clean.Split('.')
    $nums = @('0', '0', '0', '0')
    for ($i = 0; $i -lt [math]::Min(4, $parts.Count); $i++) { $nums[$i] = $parts[$i] }
    return ($nums -join '.')
}

# ------------------------------------------------------------
#  横幅 + 目录校验
# ------------------------------------------------------------

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Yellow
Write-Host "   HanVGamepad 构建脚本 (Windows)"          -ForegroundColor Yellow
Write-Host "  ========================================" -ForegroundColor Yellow
Write-Host ""

if (-not (Test-Path $desktopProj) -or -not (Test-Path $cliProj)) {
    Write-Err "请在项目根目录下运行此脚本（找不到 src/desktop 或 src/cli）"
    exit 1
}

# ------------------------------------------------------------
#  Step 1: 前置依赖
# ------------------------------------------------------------

Write-Step '1' '检查前置依赖...'

$missing = @()
if (-not (Test-Command 'dotnet')) { $missing += 'dotnet' }
if (-not (Test-Command 'node'))   { $missing += 'node' }
if (-not (Test-Command 'npm'))    { $missing += 'npm' }

if ($missing.Count -gt 0) {
    Write-Err "缺少以下依赖: $($missing -join ', ')"
    exit 1
}

$dotnetVer = (& dotnet --version) 2>&1 | Select-Object -First 1
Write-OK "dotnet $dotnetVer"
Write-OK "node $(node --version) / npm $(npm --version)"

# ------------------------------------------------------------
#  交互式选项（无参数时）
# ------------------------------------------------------------

$anyParam = $Clean -or $SkipWeb -or $SkipPublish -or $NoPortable -or $NoInstaller -or $IncludeSymbols -or ($Version -ne '')

$currentVersion = Get-ProjectVersion $desktopProj
if (-not $currentVersion) {
    Write-Err "无法从 $desktopProj 读取 <Version>"
    exit 1
}

if (-not $anyParam) {
    Write-Host ""
    Write-Host "  请选择构建选项:" -ForegroundColor Cyan
    Write-Host ""

    $Clean       = Ask-YesNo '清理旧产物（artifacts）？'
    $SkipWeb     = -not (Ask-YesNo '重新构建前端（web-client → wwwroot）？' $true)
    $NoPortable  = -not (Ask-YesNo '生成绿色包 zip？' $true)
    $NoInstaller = -not (Ask-YesNo '生成安装包（Inno Setup）？' $true)

    Write-Host ""
    Write-Host "  当前版本: $currentVersion" -ForegroundColor Cyan
    $versionInput = "$(Read-Host '  输入新版本号回车修改，直接回车保持不变')".Trim()
    if ($versionInput) {
        if ($versionInput -notmatch '^\d+\.\d+(\.\d+){0,2}') {
            Write-Err "版本号格式不合法: $versionInput"
            exit 1
        }
        $Version = $versionInput
    }

    Write-Host ""
    Write-Host "  ┌─────────────────────────────────────┐" -ForegroundColor DarkGray
    Write-Host "  │  构建配置                            │" -ForegroundColor DarkGray
    Write-Host "  ├─────────────────────────────────────┤" -ForegroundColor DarkGray

    function Format-YN { param([bool]$Value) if ($Value) { return '✓ 是' } else { return '✗ 否' } }

    Write-Host ("  │  清理旧产物:  {0}              │" -f (Format-YN $Clean))       -ForegroundColor $(if ($Clean) { 'Yellow' } else { 'DarkGray' })
    Write-Host ("  │  构建前端:    {0}              │" -f (Format-YN (-not $SkipWeb))) -ForegroundColor $(if (-not $SkipWeb) { 'Yellow' } else { 'DarkGray' })
    Write-Host ("  │  绿色包:      {0}              │" -f (Format-YN (-not $NoPortable)))  -ForegroundColor $(if (-not $NoPortable) { 'Yellow' } else { 'DarkGray' })
    Write-Host ("  │  安装包:      {0}              │" -f (Format-YN (-not $NoInstaller))) -ForegroundColor $(if (-not $NoInstaller) { 'Yellow' } else { 'DarkGray' })
    Write-Host "  └─────────────────────────────────────┘" -ForegroundColor DarkGray
    Write-Host ""

    if (-not (Ask-YesNo '确认开始构建？' $true)) {
        Write-Host "  已取消" -ForegroundColor Yellow
        exit 0
    }
}
else {
    if ($Clean)       { Write-Host "  模式: 清理旧产物" -ForegroundColor DarkYellow }
    if ($SkipWeb)     { Write-Host "  模式: 跳过前端构建" -ForegroundColor DarkYellow }
    if ($SkipPublish) { Write-Host "  模式: 跳过 dotnet 发布" -ForegroundColor DarkYellow }
    if ($NoPortable)  { Write-Host "  模式: 不生成绿色包 zip" -ForegroundColor DarkYellow }
    if ($NoInstaller) { Write-Host "  模式: 不生成安装包" -ForegroundColor DarkYellow }
    Write-Host ""
}

# ------------------------------------------------------------
#  Step 2: 版本号
# ------------------------------------------------------------

Write-Step '2' '确定版本号...'

if (-not $Version) { $Version = $currentVersion }
if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}([-+][0-9A-Za-z.\-]+)?$') {
    Write-Err "版本号格式不合法: $Version"
    exit 1
}

$version4 = ConvertTo-FourPartVersion $Version
Write-OK "版本 $Version（文件版本 $version4）"

$buildStart = Get-Date

# ------------------------------------------------------------
#  Step 3: 清理（可选）
# ------------------------------------------------------------

if ($Clean) {
    Write-Step '3' '清理旧产物...'
    Stop-ProductProcesses
    foreach ($dir in @($stageDir, $packages)) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force
            Write-Note "已删除 $dir"
        }
    }
    Write-OK '清理完成'
}
else {
    Write-Step '3' '跳过清理'
}

# ------------------------------------------------------------
#  Step 4: 前端构建
# ------------------------------------------------------------

if ($SkipWeb) {
    Write-Step '4' '跳过前端构建'
    if (-not (Test-Path (Join-Path $wwwroot 'index.html'))) {
        Write-Err "src/desktop/wwwroot/index.html 不存在，无法跳过（Web 页面会 404，服务端自检会拒绝上线）"
        exit 1
    }
    Write-Note "复用现有 wwwroot: $(Format-Size (Get-DirSize $wwwroot))"
}
else {
    Write-Step '4' '构建前端（web-client → src/desktop/wwwroot）...'
    $webStart = Get-Date

    Push-Location $webDir
    try {
        if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
            Write-Note '首次构建，安装 npm 依赖...'
            npm install
            if ($LASTEXITCODE -ne 0) { throw 'npm install 失败' }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build 失败' }
    }
    catch {
        Write-Err "前端构建失败: $_"
        exit 1
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path (Join-Path $wwwroot 'index.html'))) {
        Write-Err "构建完成但 wwwroot/index.html 不存在，检查 vite.config.ts 的 outDir"
        exit 1
    }
    Write-OK "前端构建完成（$(Format-Duration ((Get-Date) - $webStart))）"
}

# ------------------------------------------------------------
#  Step 5: dotnet 发布（目录展开，非单文件）
# ------------------------------------------------------------

function Invoke-Publish {
    param(
        [string]$Project,
        [string]$OutDir,
        [string]$Label
    )

    if (Test-Path $OutDir) {
        Stop-ProductProcesses
        Remove-Item $OutDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

    $debugArgs = if ($IncludeSymbols) { @() } else { @('-p:DebugType=none', '-p:DebugSymbols=false') }

    $publishArgs = @(
        'publish', $Project,
        '-c', 'Release',
        '-r', $rid,
        '--self-contained', 'true',
        # 关键：目录展开发布。单文件会把自己解压到 %TEMP%，正是要避免的形态。
        '-p:PublishSingleFile=false',
        '-p:IncludeNativeLibrariesForSelfExtract=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$version4",
        "-p:FileVersion=$version4",
        '--nologo',
        '-v', 'minimal'
    ) + $debugArgs + @('-o', $OutDir)

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "$Label 发布失败（dotnet publish 退出码 $LASTEXITCODE）" }

    # 防回归：单文件发布的目录里几乎没有 dll。
    $dllCount = (Get-ChildItem $OutDir -Filter *.dll -File -ErrorAction SilentlyContinue).Count
    if ($dllCount -lt 5) {
        throw "$Label 发布产物只有 $dllCount 个 dll，疑似退化成单文件/自解压形态"
    }
    $exeCount = (Get-ChildItem $OutDir -Filter *.exe -File -ErrorAction SilentlyContinue).Count
    if ($exeCount -lt 1) {
        throw "$Label 发布产物里没有 exe"
    }
    Write-Note "${Label}: $dllCount 个 dll，$(Format-Size (Get-DirSize $OutDir))"
}

if ($SkipPublish) {
    Write-Step '5' '跳过 dotnet 发布'
    if (-not (Test-Path $stageDir) -or -not (Test-Path $cliDir)) {
        Write-Err "artifacts\win-x64\HanVGamepad 不完整，无法跳过发布"
        exit 1
    }
    Write-Note "复用 $stageDir"
}
else {
    Write-Step '5' '发布 .NET 程序（win-x64，目录展开）...'
    $pubStart = Get-Date

    try {
        Invoke-Publish -Project $desktopProj -OutDir $stageDir -Label '桌面端'
        Invoke-Publish -Project $cliProj     -OutDir $cliDir   -Label 'CLI'
    }
    catch {
        Write-Err "$_"
        exit 1
    }

    $guiExe = Join-Path $stageDir 'HanVGamepad.Desktop.exe'
    if (-not (Test-Path $guiExe)) {
        Write-Err "桌面端 exe 未生成: $guiExe"
        exit 1
    }
    if (-not (Test-Path (Join-Path $cliDir 'hanvgamepad.exe'))) {
        Write-Err "CLI exe 未生成: $cliDir\hanvgamepad.exe"
        exit 1
    }

    # 许可证随包（MIT 义务：LICENSE-ORIGINAL 必须随所有分发包）。
    foreach ($file in @('LICENSE', 'LICENSE-ORIGINAL', 'README.md')) {
        $src = Join-Path $root $file
        if (Test-Path $src) { Copy-Item $src -Destination $stageDir -Force }
    }

    Write-OK "发布完成（$(Format-Duration ((Get-Date) - $pubStart))）"
}

# 有 wwwroot 才算能跑的包；这里再兜一次底，避免打包出个 404 空壳。
if (-not (Test-Path (Join-Path $stageDir 'wwwroot/index.html'))) {
    Write-Err "绿色包目录缺少 wwwroot\index.html，Web 页面会 404"
    exit 1
}

# ------------------------------------------------------------
#  Step 6: 绿色包
# ------------------------------------------------------------

if ($NoPortable) {
    Write-Step '6' '跳过绿色包 zip'
}
else {
    Write-Step '6' '生成绿色包 zip...'
    New-Item -ItemType Directory -Force -Path $packages | Out-Null

    $zipPath = Join-Path $packages "HanVGamepad-$Version-win-x64-portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    $zipStart = Get-Date
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stageDir,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $true)
    Write-OK "绿色包: $zipPath（$(Format-Size (Get-Item $zipPath).Length)，$(Format-Duration ((Get-Date) - $zipStart))）"
}

# ------------------------------------------------------------
#  Step 7: 安装包（Inno Setup）
# ------------------------------------------------------------

function Find-Iscc {
    $bases = @()
    if (${env:ProgramFiles(x86)}) { $bases += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe') }
    if ($env:ProgramFiles)        { $bases += (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe') }
    if ($env:LOCALAPPDATA)        { $bases += (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
                                    $bases += (Join-Path $env:LOCALAPPDATA 'Inno Setup 6/ISCC.exe') }
    foreach ($candidate in $bases) {
        if (Test-Path $candidate) { return $candidate }
    }

    # 注册表兜底：装着但不在常规位置
    foreach ($hive in @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        if (Test-Path $hive) {
            $loc = (Get-ItemProperty $hive -ErrorAction SilentlyContinue).InstallLocation
            if ($loc) {
                $exe = Join-Path $loc 'ISCC.exe'
                if (Test-Path $exe) { return $exe }
            }
        }
    }

    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { return $cmd.Source }

    return $null
}

if ($NoInstaller) {
    Write-Step '7' '跳过安装包'
}
else {
    Write-Step '7' '生成安装包（Inno Setup）...'

    if (-not (Test-Path $issPath)) {
        Write-Err "找不到安装脚本: $issPath"
        exit 1
    }

    $iscc = Find-Iscc
    if (-not $iscc) {
        Write-Err '没找到 ISCC.exe（Inno Setup 6 未安装）'
        Write-Note '安装命令: winget install --id JRSoftware.InnoSetup --exact --source winget'
        Write-Note '或只想出绿色包: .\windows-build.ps1 -NoInstaller'
        exit 1
    }
    Write-Note "ISCC: $iscc"

    New-Item -ItemType Directory -Force -Path $packages | Out-Null

    $outputBase = "HanVGamepad-$Version-win-x64-setup"
    $isccStart = Get-Date

    & $iscc `
        "/DAppVersion=$Version" `
        "/DAppVersion4=$version4" `
        "/DSourceDir=$($stageDir.TrimEnd('\'))" `
        "/DOutputDir=$($packages.TrimEnd('\'))" `
        "/DOutputBase=$outputBase" `
        $issPath

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Inno Setup 编译失败（退出码 $LASTEXITCODE）"
        exit 1
    }

    $setupPath = Join-Path $packages "$outputBase.exe"
    if (-not (Test-Path $setupPath)) {
        Write-Err "安装包未生成: $setupPath"
        exit 1
    }
    Write-OK "安装包: $setupPath（$(Format-Size (Get-Item $setupPath).Length)，$(Format-Duration ((Get-Date) - $isccStart))）"
}

# ------------------------------------------------------------
#  完成
# ------------------------------------------------------------

$totalDuration = (Get-Date) - $buildStart

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Green
Write-Host "   构建完成！"                             -ForegroundColor Green
Write-Host "  ========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  耗时: $(Format-Duration $totalDuration)" -ForegroundColor Green
Write-Host ""
Write-Host "  输出:" -ForegroundColor Cyan
Write-Host "    绿色包目录:   artifacts\win-x64\HanVGamepad\"

$zipPath    = Join-Path $packages "HanVGamepad-$Version-win-x64-portable.zip"
$setupPath  = Join-Path $packages "HanVGamepad-$Version-win-x64-setup.exe"
if (Test-Path $zipPath)   { Write-Host "    绿色包 zip:   artifacts\packages\$([System.IO.Path]::GetFileName($zipPath))" }
if (Test-Path $setupPath) { Write-Host "    安装包:       artifacts\packages\$([System.IO.Path]::GetFileName($setupPath))" }

Write-Host ""
Write-Host "  版本 $Version    目录体积 $(Format-Size (Get-DirSize $stageDir))" -ForegroundColor DarkGray
Write-Host ""
