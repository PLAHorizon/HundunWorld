<#
.SYNOPSIS
    耕地游戏中心 安装包构建脚本

.DESCRIPTION
    1. 发布 GengDi 主应用（win-x64，framework-dependent）
    2. 编译自定义安装程序 UI（Horizon.Game.GengDi.Installer）
    3. 可选：若本机已安装 Inno Setup，则自动调用 iscc 生成最终
       分发安装包 dist\GengDi-Setup-<版本>.exe

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

# Path constants
$RepoRoot     = $PSScriptRoot
$GengDiPCProj = Join-Path $RepoRoot "Horizon.Game.GengDi.PC\Horizon.Game.GengDi.PC.csproj"
$InstallerProj= Join-Path $RepoRoot "Horizon.Game.GengDi.Installer\Horizon.Game.GengDi.Installer.csproj"
$IssScript    = Join-Path $RepoRoot "GengDi.Setup.iss"

$PublishGengDi    = Join-Path $RepoRoot "publish\GengDi"
$PublishInstaller = Join-Path $RepoRoot "publish\Installer"
$DistDir          = Join-Path $RepoRoot "dist"

# Helper functions
function Step([string]$msg) {
    Write-Host "`n==> $msg" -ForegroundColor Cyan
}
function OK([string]$msg)   { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Fail([string]$msg) { Write-Host "    [FAIL] $msg" -ForegroundColor Red; exit 1 }

# Step 1: Publish GengDi main application
Step "Publish Horizon.Game.GengDi.PC (win-x64, framework-dependent)"

if (-not (Test-Path $GengDiPCProj)) {
    Fail "Cannot find project file: $GengDiPCProj"
}

# Clean old publish directory
if (Test-Path $PublishGengDi) { 
    Write-Host "    Cleaning old publish directory..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $PublishGengDi 
}

# Restore and clean to ensure latest code is used
Write-Host "    Restoring NuGet packages..." -ForegroundColor DarkGray
dotnet restore $GengDiPCProj --verbosity quiet

if ($LASTEXITCODE -ne 0) { Fail "dotnet restore failed" }

Write-Host "    Cleaning old build..." -ForegroundColor DarkGray
dotnet clean $GengDiPCProj --configuration Release --verbosity quiet

# Publish main application
Write-Host "    Publishing application..." -ForegroundColor DarkGray
dotnet publish $GengDiPCProj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    --output $PublishGengDi `
    /p:Version=$Version `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=false

if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed" }

# Verify publish result
if (-not (Test-Path (Join-Path $PublishGengDi "GengDi.exe"))) {
    Fail "GengDi.exe not found in publish directory"
}

OK "GengDi published to $PublishGengDi"
Write-Host "    File count: $((Get-ChildItem -Path $PublishGengDi -Recurse -File).Count)" -ForegroundColor DarkGray

# Step 2: Compile Installer UI
Step "Compile Installer UI (Horizon.Game.GengDi.Installer)"

if (-not (Test-Path $InstallerProj)) {
    Fail "Cannot find installer project file: $InstallerProj"
}

# Clean old installer publish directory
if (Test-Path $PublishInstaller) { 
    Write-Host "    Cleaning old installer publish directory..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $PublishInstaller 
}

# Restore and clean
Write-Host "    Restoring NuGet packages..." -ForegroundColor DarkGray
dotnet restore $InstallerProj --verbosity quiet

if ($LASTEXITCODE -ne 0) { Fail "dotnet restore failed" }

Write-Host "    Cleaning old build..." -ForegroundColor DarkGray
dotnet clean $InstallerProj --configuration Release --verbosity quiet

# Compile installer
Write-Host "    Compiling installer..." -ForegroundColor DarkGray
dotnet publish $InstallerProj `
    --configuration Release `
    --output $PublishInstaller `
    /p:Version=$Version

if ($LASTEXITCODE -ne 0) { Fail "Installer compilation failed" }

# Verify installer EXE
if (-not (Test-Path (Join-Path $PublishInstaller "GengDi.Setup.exe"))) {
    Fail "GengDi.Setup.exe not found in publish directory"
}

OK "Installer compiled to $PublishInstaller"
Write-Host "    File count: $((Get-ChildItem -Path $PublishInstaller -Recurse -File).Count)" -ForegroundColor DarkGray

# Step 3: Build standalone WPF installer distribution directory
Step "Build standalone WPF installer distribution (dist\GengDi-StandaloneInstaller)"

$StandaloneDir     = Join-Path $DistDir    "GengDi-StandaloneInstaller"
$StandalonePayload = Join-Path $StandaloneDir "payload"

if (Test-Path $StandaloneDir) { Remove-Item -Recurse -Force $StandaloneDir }
New-Item -ItemType Directory -Force $StandalonePayload | Out-Null

# Copy WPF installer main EXE
Copy-Item (Join-Path $PublishInstaller "GengDi.Setup.exe") `
          (Join-Path $StandaloneDir    "GengDi.Setup.exe") -Force

# Copy main application payload (exclude *.pdb and installer itself)
Get-ChildItem -Path $PublishGengDi -Recurse -File |
    Where-Object { $_.Extension -ne '.pdb' } |
    ForEach-Object {
        $rel  = $_.FullName.Substring($PublishGengDi.Length).TrimStart('\','/')
        $dest = Join-Path $StandalonePayload $rel
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Force $destDir | Out-Null
        }
        Copy-Item $_.FullName $dest -Force
    }

OK "Standalone WPF installer distribution generated: $StandaloneDir"

# Step 4: Inno Setup packaging (optional)
if (-not $SkipInnoSetup) {
    Step "Package final installer with Inno Setup"

    if (-not (Test-Path $IssScript)) {
        Fail "Cannot find Inno Setup script: $IssScript"
    }

    # Find Inno Setup compiler (common installation paths)
    $InnoPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    $IsccExe = $InnoPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $IsccExe) {
        Write-Host "    [SKIP] Inno Setup not found, skipping packaging step." -ForegroundColor Yellow
        Write-Host "           Download from https://jrsoftware.org/isinfo.php and try again."
    } else {
        New-Item -ItemType Directory -Force $DistDir | Out-Null

        Write-Host "    Invoking Inno Setup compiler..." -ForegroundColor DarkGray
        & $IsccExe `
            "/DPayloadDir=$PublishGengDi" `
            "/DMyAppVersion=$Version" `
            $IssScript | Out-Host

        if ($LASTEXITCODE -ne 0) { Fail "Inno Setup compilation failed" }
        if (-not (Test-Path (Join-Path $DistDir "GengDi-Setup-$Version.exe"))) {
            Fail "Inno Setup output file not found"
        }
        OK "Installer package generated: $DistDir\GengDi-Setup-$Version.exe"
    }
} else {
    Write-Host "`n    [SKIP] Inno Setup packaging skipped (-SkipInnoSetup)" -ForegroundColor Yellow
}

# Done
Write-Host "`nBuild completed successfully!" -ForegroundColor Green
Write-Host "  Main app publish dir : $PublishGengDi"
Write-Host "  Installer UI         : $(Join-Path $PublishInstaller 'GengDi.Setup.exe')"
Write-Host "  Standalone dist      : $StandaloneDir" -ForegroundColor Cyan
if (-not $SkipInnoSetup -and (Test-Path (Join-Path $DistDir "GengDi-Setup-$Version.exe"))) {
    Write-Host "  Inno Setup package   : $DistDir\GengDi-Setup-$Version.exe" -ForegroundColor Cyan
}
