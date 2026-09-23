# ============================================================================
#  Flax 无头 DS 程序集暂存脚本
# ----------------------------------------------------------------------------
#  把 DS 链路需要的托管程序集从 Horizon.DsHost 的构建输出，
#  复制到 Flax 构建能搜索到的目录（清单见 ServerGame.Build.cs 的 searchDirs）。
#
#  为什么需要本脚本：
#    ServerGame.Build.cs 刻意采用「找不到就抛异常」策略（而非 Game.Build.cs 的
#    静默跳过）。因此 DLL 不到位时构建会直接失败——这是设计意图，不是 bug。
#    本脚本负责把 DLL 放到位。
#
#  何时运行：
#    每次 dotnet build 完 DS 侧程序集、且准备编译 Flax 工程前。
#    典型顺序：
#      dotnet build Horizon.DsHost/Horizon.DsHost.csproj
#      powershell -ExecutionPolicy Bypass -File stage-ds-to-flax.ps1
#      FlaxEngine.exe -build -project HundunWorld.flaxproj -target ServerTarget ...
#
#  用法：
#    powershell -ExecutionPolicy Bypass -File stage-ds-to-flax.ps1
#    powershell -ExecutionPolicy Bypass -File stage-ds-to-flax.ps1 -WhatIf   # 只看要复制什么
#
#  注意：
#    - 目标目录 HundunWorld\Output\Windows 是 Flax 的工程输出目录，**不需要管理员权限**。
#    - 若同时想更新引擎 Tools 目录（GameEditorTarget 编译期引用），加 -AlsoEngineTools
#      （那一份需要管理员权限，因为位于 Program Files）。
#
#  ⚠️ 编码约束（踩坑记录）：
#    本文件含中文注释，**必须以 UTF-8 with BOM 保存**。
#    Windows PowerShell 5.1 在没有 BOM 时会按系统 ANSI 码页（简中为 GBK）解析脚本文件，
#    中文注释会变成乱码并吃掉引号/括号，报出一堆"意外的标记 }"、"字符串缺少终止符"——
#    症状看起来像语法错误，实际是编码问题。改本文件时请保持 BOM。
# ============================================================================

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # 跳过 FlaxEditor 占用检查（CI / 无人值守场景）
    [switch]$Force,

    # 同时把程序集复制到 Flax 引擎的 Binaries\Tools（需要管理员权限）
    [switch]$AlsoEngineTools,

    # DS 侧程序集的构建输出目录（相对仓库根）
    [string]$DsHostOutput = "Horizon.DsHost\bin\Debug\net10.0"
)

$ErrorActionPreference = 'Stop'

# ── 路径解析 ───────────────────────────────────────────────────────────────
$repoRoot   = $PSScriptRoot
$srcDir     = Join-Path $repoRoot $DsHostOutput
$flaxProject = Join-Path $repoRoot "HundunWorld"
$outputDir  = Join-Path $flaxProject "Output\Windows"
$engineTools = "C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools"

Write-Host ""
Write-Host "=== Flax 无头 DS 程序集暂存 ===" -ForegroundColor Cyan
Write-Host "源目录  : $srcDir"
Write-Host "目标目录: $outputDir"
Write-Host ""

# ── 前置检查 ───────────────────────────────────────────────────────────────
if (-not (Test-Path $srcDir)) {
    Write-Host "错误：DS 构建输出目录不存在：$srcDir" -ForegroundColor Red
    Write-Host "请先执行：" -ForegroundColor Yellow
    Write-Host "  dotnet build Horizon.DsHost\Horizon.DsHost.csproj" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $outputDir)) {
    Write-Host "错误：Flax 输出目录不存在：$outputDir" -ForegroundColor Red
    Write-Host "请先用 FlaxEditor 打开一次工程以生成目录结构。" -ForegroundColor Yellow
    exit 1
}

# 核心三项缺失时直接失败——它们是本次改造的产物，缺了说明 DS 侧压根没编译过。
$required = @("Horizon.DsHost.dll", "Horizon.Game.Sim.dll", "Horizon.Game.World.dll")
$missingRequired = @()
foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $srcDir $r))) { $missingRequired += $r }
}
if ($missingRequired.Count -gt 0) {
    Write-Host "错误：DS 侧核心程序集缺失，本脚本不会复制半成品：" -ForegroundColor Red
    foreach ($m in $missingRequired) { Write-Host "  - $m" -ForegroundColor Red }
    Write-Host "请先执行：dotnet build Horizon.DsHost\Horizon.DsHost.csproj" -ForegroundColor Yellow
    exit 1
}

# FlaxEditor 运行时会锁住已加载的托管程序集（复制报"文件被占用"）。
if (-not $Force) {
    $flaxProc = Get-Process FlaxEditor -ErrorAction SilentlyContinue
    if ($flaxProc) {
        Write-Host "警告：检测到 FlaxEditor 正在运行 (PID: $($flaxProc.Id -join ','))，目标 DLL 可能被锁定。" -ForegroundColor Yellow
        Write-Host "      请在编辑器中关闭工程后重跑本脚本，或加 -Force 跳过此检查。" -ForegroundColor Yellow
        exit 1
    }
}

# ── 程序集清单 ─────────────────────────────────────────────────────────────
# 必须与 HundunWorld\Source\ServerGame\ServerGame.Build.cs 的 dlls[] **保持一致**。
# 两处清单漂移会导致"构建期找不到 DLL"或"运行时类型加载失败"，且症状难定位。
$assemblies = @(
    # DS 宿主与权威模拟（本次改造核心三项）
    "Horizon.DsHost.dll"
    "Horizon.Game.Sim.dll"
    "Horizon.Game.World.dll"

    # 协议与消息契约
    "Horizon.Game.Core.dll"
    "Horizon.Game.Message.dll"

    # 基础库
    "Horizon.Core.Abstract.dll"

    # 序列化 / 压缩
    "MemoryPack.Core.dll"
    "K4os.Compression.LZ4.dll"
    "System.IO.Hashing.dll"

    # Microsoft.Extensions
    "Microsoft.Extensions.ObjectPool.dll"
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll"
    "Microsoft.Extensions.Options.dll"
    "Microsoft.Extensions.Primitives.dll"
    "Microsoft.Extensions.DependencyModel.dll"
    "Microsoft.Extensions.Logging.Abstractions.dll"

    # Orleans 序列化契约
    "Orleans.Serialization.dll"
    "Orleans.Serialization.Abstractions.dll"
)

# ── 目标目录集合 ───────────────────────────────────────────────────────────
$destinations = @($outputDir)
if ($AlsoEngineTools) {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Host "错误：-AlsoEngineTools 需要管理员权限（目标为 Program Files）。" -ForegroundColor Red
        Write-Host "请以管理员身份重开 PowerShell 后重跑。" -ForegroundColor Yellow
        exit 1
    }
    $destinations += $engineTools
}

# ── 复制 ───────────────────────────────────────────────────────────────────
$copied  = 0
$skipped = @()

foreach ($dst in $destinations) {
    Write-Host "→ $dst" -ForegroundColor Cyan

    foreach ($name in $assemblies) {
        $srcPath = Join-Path $srcDir $name

        if (-not (Test-Path $srcPath)) {
            $skipped += $name
            Write-Host "    跳过（源不存在）：$name" -ForegroundColor DarkGray
            continue
        }

        $dstPath = Join-Path $dst $name
        if ($PSCmdlet.ShouldProcess($dstPath, "复制 $name")) {
            Copy-Item -Path $srcPath -Destination $dstPath -Force
            $copied++
            Write-Host "    OK  $name" -ForegroundColor Green
        }

        # PDB 一并带上：DS 崩溃时栈帧才有行号。
        $pdbName = [System.IO.Path]::ChangeExtension($name, ".pdb")
        $pdbSrc = Join-Path $srcDir $pdbName
        if (Test-Path $pdbSrc) {
            $pdbDst = Join-Path $dst $pdbName
            if ($PSCmdlet.ShouldProcess($pdbDst, "复制 $pdbName")) {
                Copy-Item -Path $pdbSrc -Destination $pdbDst -Force
            }
        }
    }
    Write-Host ""
}

# ── 汇总 ───────────────────────────────────────────────────────────────────
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host "已复制 : $copied 个程序集（× $($destinations.Count) 个目标目录）" -ForegroundColor Green
if ($skipped.Count -gt 0) {
    Write-Host "未找到 : $($skipped -join ', ')" -ForegroundColor Yellow
    Write-Host "         这些不在 DS 依赖闭包内属正常；若是 Horizon.* 缺失则说明 DS 侧未编译完全。" -ForegroundColor DarkGray
}

# ── 校验目标目录最终状态 ───────────────────────────────────────────────────
Write-Host ""
Write-Host "目标目录校验：" -ForegroundColor Cyan
$stillMissing = @()
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $outputDir $name))) { $stillMissing += $name }
}
if ($stillMissing.Count -eq 0) {
    Write-Host "  核心三项已就位（Horizon.DsHost / Game.Sim / Game.World）。" -ForegroundColor Green
    Write-Host ""
    Write-Host "下一步（构建 DS 目标）：" -ForegroundColor Cyan
    Write-Host '  FlaxEngine.exe -build -project "HundunWorld\HundunWorld.flaxproj" -target ServerTarget -platform Windows -arch x64' -ForegroundColor Gray
    Write-Host '运行（无头）：' -ForegroundColor Cyan
    Write-Host '  HundunWorld\Output\Windows\HundunWorld.exe -headless -null -mute' -ForegroundColor Gray
} else {
    Write-Host "  仍缺失：$($stillMissing -join ', ')" -ForegroundColor Red
    exit 1
}
