#requires -Version 5.1
<#
  Copies the shared managed assemblies the Flax client compiles against into the ENGINE's
  Binaries\Tools folder. Run this from an ELEVATED PowerShell: Program Files (x86) is protected,
  and a non-elevated run fails with MSB3021 / Access denied (which is exactly why the client
  keeps building against an old copy and reports CS0246 / CS0117 for types that do exist).

  Why the Tools folder matters: Flax.Build resolves script assemblies from
  <engine>\Binaries\Tools first, NOT from HundunWorld\Output\Windows. Refreshing only the
  Output copy does not change what the editor compiles.

  Usage (elevated):
    powershell -ExecutionPolicy Bypass -File scripts\copy-shared-dlls-to-flax-tools.ps1
    powershell -ExecutionPolicy Bypass -File scripts\copy-shared-dlls-to-flax-tools.ps1 -SourceConfiguration Debug

  The script prints, for every file, the first 16 hex chars of its SHA-256 before and after the
  copy - the same value scripts\scripted-spawn-preflight.ps1 reports, so the two are comparable.
  Nothing is stopped, started or killed.
#>
param(
    [string]$RepoRoot = '',
    [string]$EngineToolsDir = '',
    [string]$SourceConfiguration = 'Debug',
    [string[]]$Assemblies = @(
        'Horizon.Game.Message',
        'Horizon.Orleans.Interface',
        # Mirrors the reference list in HundunWorld/Source/Game/Game.Build.cs (the client script
        # assembly). Flax.Build resolves script assemblies from Binaries\Tools FIRST, so any
        # Horizon assembly the client compiles against must be refreshed here too - otherwise the
        # editor silently compiles against an old copy while repo-side gates use the fresh one.
        # Measured 2026-10-06: Tools copies of Sim / ECS.Arch / World were already stale while
        # Message and Core matched, and those three are exactly what QuestObjectiveCodec and the
        # ECS sync systems compile against.
        # 2026-10-07：客户端改指 Contracts（Sim/World 已拆出权威算法），且 Horizon.Game.Core
        # 不再进客户端清单——这里必须同步，否则编辑器会继续吃旧的 Sim/World 副本。
        'Horizon.Game.ECS',
        'Horizon.Game.ECS.Arch',
        'Horizon.Game.Sim',
        'Horizon.Game.Sim.Contracts',
        'Horizon.Game.World',
        'Horizon.Game.World.Contracts',
        'Horizon.Core.Abstract',
        # 客户端只用到 AppType / PassportType → 改由 Share.Client 提供；DS 侧仍需要 Core.Abstract
        'Horizon.Share.Client'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

if ([string]::IsNullOrWhiteSpace($EngineToolsDir)) {
    # 引擎换版本（1.12 → 1.13 …）后不要再改脚本：自动挑已安装的最高版本。
    $engine = Get-ChildItem 'C:\Program Files (x86)\Flax' -Directory -Filter 'Flax_*' -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Name -replace '^Flax_', '') } -Descending |
        Select-Object -First 1
    if ($null -eq $engine) {
        throw 'No Flax_* engine found under C:\Program Files (x86)\Flax'
    }
    $EngineToolsDir = Join-Path $engine.FullName 'Binaries\Tools'
    Write-Host "EngineToolsDir (auto) = $EngineToolsDir"
}

function Get-DigestHead {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)
    } finally {
        $stream.Dispose()
    }
    return ([BitConverter]::ToString($hash, 0, 8) -replace '-', '').ToUpperInvariant()
}

Write-Host ''
Write-Host ("repo:   {0}" -f $RepoRoot)
Write-Host ("engine: {0}" -f $EngineToolsDir)
Write-Host ("source configuration: {0}" -f $SourceConfiguration)

if (-not (Test-Path -LiteralPath $EngineToolsDir)) {
    throw "engine Tools directory not found: $EngineToolsDir (pass -EngineToolsDir)"
}

# Fail fast on a non-elevated shell instead of half-copying and dying on the second file.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'this script writes into Program Files (x86) and must run in an ELEVATED PowerShell. Re-run it from "Run as administrator".'
}

$copied = 0
$missing = New-Object System.Collections.Generic.List[string]
$mismatched = New-Object System.Collections.Generic.List[string]

foreach ($name in $Assemblies) {
    $srcDir = Join-Path $RepoRoot ("{0}\bin\{1}\net10.0" -f $name, $SourceConfiguration)
    if (-not (Test-Path -LiteralPath $srcDir)) {
        $missing.Add($srcDir)
        continue
    }

    foreach ($ext in @('.dll', '.pdb', '.xml')) {
        $src = Join-Path $srcDir ($name + $ext)
        if (-not (Test-Path -LiteralPath $src)) { continue }

        $dst = Join-Path $EngineToolsDir ($name + $ext)
        $srcHead = Get-DigestHead -Path $src

        Copy-Item -LiteralPath $src -Destination $dst -Force
        $dstHead = Get-DigestHead -Path $dst

        $flag = if ($srcHead -eq $dstHead) { 'OK  ' } else { 'DIFF' }
        Write-Host ("  [{0}] {1}{2}  src={3} dst={4}" -f $flag, $name, $ext, $srcHead, $dstHead)
        if ($flag -ne 'OK  ') { $mismatched.Add($name + $ext) }
        $copied++
    }
}

Write-Host ''
if ($copied -eq 0) {
    Write-Host "nothing copied - no build output found under $RepoRoot (build the projects first)." -ForegroundColor Red
    foreach ($d in $missing) { Write-Host ("  missing: {0}" -f $d) }
    exit 1
}

if ($mismatched.Count -gt 0) {
    Write-Host ("copied {0} file(s), but these did not match after the copy: {1}" -f $copied, ($mismatched -join ', ')) -ForegroundColor Red
    exit 1
}

Write-Host ("copied {0} file(s); every digest matches the source build." -f $copied) -ForegroundColor Green
Write-Host 'Next: rebuild the client (Flax editor or dotnet build Game.csproj -c Editor.Windows.Development -p:Platform=x64),'
Write-Host 'then re-run scripts\scripted-spawn-preflight.ps1 to confirm the three copies are now the same build.'
exit 0
