#requires -Version 5.1
<#
  Launches the three existing backend processes with the scripted-world-spawn feature enabled,
  in the order they depend on, using ONE pair of environment variables for all three.

  It does not install, build, start or stop anything else: no Redis, no SQL Server, no new service,
  no additional process. It only sets ScriptedSpawn__Enabled / ScriptedSpawn__SigningKeyBase64 in the
  child process environment and starts Silo -> Game Gateway -> DsHost.

  Usage:
    # see exactly what it would do, start nothing:
    powershell -ExecutionPolicy Bypass -File scripts\start-scripted-local.ps1 -DryRun

    # a key you already have (recommended: reuse one so a restart keeps the same fingerprint):
    powershell -ExecutionPolicy Bypass -File scripts\start-scripted-local.ps1 -Key "<base64>"

    # generate a key for this run and print the command to reuse it:
    powershell -ExecutionPolicy Bypass -File scripts\start-scripted-local.ps1

  Why the key must be shared: Grain signs the verdict token, the Gateway self-checks it, the DS
  verifies it. Three different values = every command dies as "signature-mismatch" (by design,
  fail closed). After startup each process prints one [ScriptedSpawn] line with a key fingerprint;
  the three fingerprints must be identical - scripts\scripted-spawn-preflight.ps1 prints the same
  value for the current shell so you can compare before launching.
#>
param(
    [string]$Key = '',
    [switch]$DryRun,
    [string]$RepoRoot = '',
    [string]$LogDirectory = '',
    [int]$SiloPort = 11111,
    [int]$GatewayTcpPort = 7789,
    [int]$DsUplinkPort = 19871,
    [int]$DsListenPort = 19880,
    [long[]]$BootstrapAdminIds = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    # Logs go to TEMP by default: never into the repository, so a launch can't dirty the working tree.
    $LogDirectory = Join-Path $env:TEMP 'hzn-scripted-launch'
}

function Get-KeyFingerprint {
    param([string]$Base64)
    if ([string]::IsNullOrWhiteSpace($Base64)) { return '(missing)' }
    $bytes = $null
    try { $bytes = [Convert]::FromBase64String($Base64) } catch { return '(not-base64)' }
    if ($bytes.Length -lt 1) { return '(empty)' }
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return ([BitConverter]::ToString($hash, 0, 4) -replace '-', '').ToUpperInvariant()
}

function Test-PortListening([int]$Port) {
    try {
        $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $conn) { return $null }
        return $conn.OwningProcess
    } catch { return $null }
}

# ------------------------------------------------------------------ 1. the key ----
if ([string]::IsNullOrWhiteSpace($Key)) {
    # Reuse an explicitly exported value first; otherwise mint a 32-byte key for this run.
    $Key = [Environment]::GetEnvironmentVariable('ScriptedSpawn__SigningKeyBase64')
}
if ([string]::IsNullOrWhiteSpace($Key)) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $Key = [Convert]::ToBase64String($bytes)
    Write-Host ''
    Write-Host 'No -Key given: generated one for this run.' -ForegroundColor Yellow
    Write-Host ("Reuse it on the next launch with:  -Key ""{0}""" -f $Key)
    Write-Host 'A different key per restart is fine functionally, but old in-flight tokens stop verifying (fail closed).'
}
try { $keyBytes = [Convert]::FromBase64String($Key) } catch { throw "Key is not valid Base64: $($_.Exception.Message)" }
if ($keyBytes.Length -lt 32) { throw "Key must be at least 32 bytes of Base64 (got $($keyBytes.Length)). Example: openssl rand -base64 32" }

$fingerprint = Get-KeyFingerprint $Key
Write-Host ''
Write-Host ("fingerprint {0}  (the [ScriptedSpawn] line of Silo, Gateway and DS must all print this same value)" -f $fingerprint)

# ------------------------------------------------------------------ 2. the plan ----
$commonEnv = @{
    'ScriptedSpawn__Enabled'           = '1'
    'ScriptedSpawn__SigningKeyBase64'  = $Key
}
if ($BootstrapAdminIds.Count -gt 0) {
    # .NET's configuration binder reads arrays as Section__Key__0 / __1.
    for ($i = 0; $i -lt $BootstrapAdminIds.Count; $i++) {
        $commonEnv["ScriptedSpawn__BootstrapAdminIds__$i"] = [string]$BootstrapAdminIds[$i]
    }
    Write-Host ("bootstrap admins: {0}  (use them to grant the first GM, then clear them)" -f ($BootstrapAdminIds -join ','))
}

$plan = @(
    [pscustomobject]@{
        Name = 'Horizon.Orleans.Silo'
        Exe  = Join-Path $RepoRoot 'Horizon.Orleans.Silo\bin\Debug\net10.0\Horizon.Orleans.Silo.exe'
        Port = $SiloPort
        Note = 'authority: GmCommandGrain signs the verdict tokens'
    },
    [pscustomobject]@{
        Name = 'Horizon.Game.Gateway'
        Exe  = Join-Path $RepoRoot 'Horizon.Game.Gateway\bin\Debug\net10.0\Horizon.Game.Gateway.exe'
        Port = $GatewayTcpPort
        Note = 'entry for /spawn //event //trigger (GameGateway, not WebApi)'
    },
    [pscustomobject]@{
        Name = 'Horizon.DsHost'
        Exe  = Join-Path $RepoRoot 'Horizon.DsHost\bin\Debug\net10.0\Horizon.DsHost.exe'
        Port = $DsUplinkPort
        Note = 'world simulation; verifies tokens, never decides permissions'
    }
)

Write-Host ''
foreach ($item in $plan) {
    $owner = Test-PortListening $item.Port
    $state = if ($null -eq $owner) { 'free' } else { "occupied by PID $owner" }
    $built = if (Test-Path -LiteralPath $item.Exe) { 'built' } else { 'MISSING - build it (plain dotnet build, no -p:OutputPath)' }
    Write-Host ("{0,-24} exe={1,-7} port {2} {3}  <- {4}" -f $item.Name, $built, $item.Port, $state, $item.Note)
}

if ($DryRun) {
    Write-Host ''
    Write-Host '-DryRun: nothing was started.' -ForegroundColor Cyan
    exit 0
}

# Refuse rather than fight an existing instance: never kill a process this script did not start.
foreach ($item in $plan) {
    $owner = Test-PortListening $item.Port
    if ($null -ne $owner) {
        throw ("{0} already listens on port {1} (PID {2}). Stop it yourself if that is what you want; this script never kills processes." -f $item.Name, $item.Port, $owner)
    }
    if (-not (Test-Path -LiteralPath $item.Exe)) { throw ("{0} not built: {1}" -f $item.Name, $item.Exe) }
}

New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null

# ------------------------------------------------------------------ 3. start ----
# Set the pair on THIS process first: Start-Process children inherit the parent environment,
# and there is no way to inject per-child variables on PowerShell 5.1. Setting it after the launch
# (as this script originally did) would start all three processes with the feature OFF.
foreach ($pair in $commonEnv.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($pair.Key, $pair.Value, 'Process')
}

foreach ($item in $plan) {
    $argList = @()
    if ($item.Name -eq 'Horizon.DsHost') {
        # DsHost does not read appsettings for this feature; it takes the same env pair plus its own
        # addressing. --orleans-silo stays unset so a local DS does not join a cluster being booted.
        $argList = @("--ds-id=ds-local", "--shard-ids=0", "--listen-port=$DsListenPort",
                     '--advertise-host=127.0.0.1', '--gateway-host=127.0.0.1', "--gateway-port=$DsUplinkPort",
                     '--tick-rate=60')
    }

    $log = Join-Path $LogDirectory ("{0}.log" -f $item.Name)
    Write-Host ''
    Write-Host ("starting {0} -> {1}" -f $item.Name, $log)
    $proc = Start-Process -FilePath $item.Exe -WorkingDirectory (Split-Path -Parent $item.Exe) `
        -RedirectStandardOutput $log -RedirectStandardError ($log + '.err') -PassThru -WindowStyle Hidden -ArgumentList $argList

    Start-Sleep -Seconds 3
    if ($proc.HasExited) { throw ("{0} exited immediately (exit code {1}); see {2}" -f $item.Name, $proc.ExitCode, $log) }
    Write-Host ("{0} running as PID {1}" -f $item.Name, $proc.Id)

    if ($item.Name -ne 'Horizon.DsHost') { Start-Sleep -Seconds 4 }   # Silo before Gateway, Gateway before DS
}

Write-Host ''
Write-Host 'All three launched. Check the fingerprints agree before touching the game client:' -ForegroundColor Green
foreach ($item in $plan) {
    $log = Join-Path $LogDirectory ("{0}.log" -f $item.Name)
    $line = Select-String -Path $log -Pattern '\[ScriptedSpawn\]' -SimpleMatch -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $line) { Write-Host ("  {0}: no [ScriptedSpawn] line yet" -f $item.Name) -ForegroundColor Yellow }
    else { Write-Host ("  {0}: {1}" -f $item.Name, $line.Line.Trim()) }
}
Write-Host ''
Write-Host 'Next: apply scripts\sql\gm_script_grant.sql, then in game run'
Write-Host '  /grant <characterId> 4095      (4095 = GameMaster; 2047 = WorldDirector only)'
Write-Host 'and /audit to check the trail. Silo and Gateway must always be deployed together (IGmCommandGrain is Version(2)).'
