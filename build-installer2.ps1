<#
.SYNOPSIS
    耕地游戏中心 安装包构建脚�?

.DESCRIPTION
    1. 发布 GengDi 主应用（win-x64，framework-dependent�?
    2. 编译自定义安装程�?UI（Horizon.Game.GengDi.Installer�?
    3. 可选：若本机已安装 Inno Setup，则自动调用 iscc 生成最�?
       分发安装�?dist\GengDi-Setup-<版本>.exe

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version "1.2.0" -SkipInnoSetup
#>
param(
    [string]$Version       = "1.0.0",
    [switch]$SkipInnoSetup = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── 路径常量 ─────────────────────────────────────────────────────────
$RepoRoot     = $PSScriptRoot
$GengDiPCProj = Join-Path $RepoRoot "Horizon.Game.GengDi.PC\Horizon.Game.GengDi.PC.csproj"
$InstallerProj= Join-Path $RepoRoot "Horizon.Game.GengDi.Installer\Horizon.Game.GengDi.Installer.csproj"
$IssScript    = Join-Path $RepoRoot "GengDi.Setup.iss"

$PublishGengDi    = Join-Path $RepoRoot "publish\GengDi"
$PublishInstaller = Join-Path $RepoRoot "publish\Installer"
$DistDir          = Join-Path $RepoRoot "dist"

# ── 辅助函数 ─────────────────────────────────────────────────────────
function Step([string]$msg) {
    Write-Host "`n==> $msg" -ForegroundColor Cyan
}
function OK([string]$msg)   { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "    [FAIL] $msg" -ForegroundColor Red; exit 1 }

# ── 步骤 1：发�?GengDi 主应�?───────────────────────────────────────
Step "发布 Horizon.Game.GengDi.PC (win-x64, framework-dependent)"

if (-not (Test-Path $GengDiPCProj)) {
    Fail "找不到项目文件：$GengDiPCProj"
}

if (Test-Path $PublishGengDi) { Remove-Item -Recurse -Force $PublishGengDi }

dotnet publish $GengDiPCProj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $PublishGengDi `
    /p:Version=$Version `
    /p:PublishSingleFile=false

if ($LASTEXITCODE -ne 0) { Fail "dotnet publish 失败" }
OK "GengDi 发布完成 �?$PublishGengDi"

# ── 步骤 2：编译安装程�?UI ───────────────────────────────────────────
Step "编译安装程序 UI (Horizon.Game.GengDi.Installer)"

if (Test-Path $PublishInstaller) { Remove-Item -Recurse -Force $PublishInstaller }

dotnet publish $InstallerProj `
    --configuration Release `
    --output $PublishInstaller

if ($LASTEXITCODE -ne 0) { Fail "安装程序编译失败" }
OK "安装程序编译完成 �?$PublishInstaller"

# 将安装程�?EXE 复制�?payload 同级目录，方�?Inno Setup 引用
$InstallerExeSrc = Join-Path $PublishInstaller "GengDi.Setup.exe"
$InstallerExeDst = Join-Path $PublishGengDi    "GengDi.Setup.exe"
Copy-Item $InstallerExeSrc $InstallerExeDst -Force
OK "安装程序 EXE 已复制到 payload 目录"

# ── 步骤 3：Inno Setup 打包（可选） ─────────────────────────────────
if (-not $SkipInnoSetup) {
    Step "使用 Inno Setup 打包最终安装包"

    # 查找 Inno Setup 编译器（常见安装路径�?
    $InnoPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    $IsccExe = $InnoPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $IsccExe) {
        Write-Host "    [SKIP] 未找�?Inno Setup，跳过打包步骤�? -ForegroundColor Yellow
        Write-Host "           可从 https://jrsoftware.org/isinfo.php 下载安装后重试�?
    } else {
        New-Item -ItemType Directory -Force $DistDir | Out-Null

        & $IsccExe `
            "/DPayloadDir=$PublishGengDi" `
            "/DInstallerUIExe=$InstallerExeSrc" `
            "/DMyAppVersion=$Version" `
            $IssScript

        if ($LASTEXITCODE -ne 0) { Fail "Inno Setup 编译失败" }
        OK "安装包生成完�?�?$DistDir\GengDi-Setup-$Version.exe"
    }
} else {
    Write-Host "`n    [SKIP] 已跳�?Inno Setup 打包�?SkipInnoSetup�? -ForegroundColor Yellow
}

# ── 完成 ─────────────────────────────────────────────────────────────
Write-Host "`n构建完成�? -ForegroundColor Green
Write-Host "  主应用发布目�? : $PublishGengDi"
Write-Host "  安装程序 UI     : $InstallerExeSrc"
if (-not $SkipInnoSetup -and (Test-Path (Join-Path $DistDir "GengDi-Setup-$Version.exe"))) {
    Write-Host "  最终安装包      : $DistDir\GengDi-Setup-$Version.exe" -ForegroundColor Cyan
}
