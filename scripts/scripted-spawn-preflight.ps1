#requires -Version 5.1
<#
  Read-only preflight check for the DS scripted world-spawn feature (monsters / NPCs / events /
  trigger zones). It never starts, stops or kills anything, and never writes outside the drive
  it reads from. Run it in the SAME shell you will use to launch Silo, Gateway and DsHost.

  Why this exists: the feature is wired across three processes that must share one switch and one
  HMAC key, and the Flax client resolves script assemblies from Binaries\Tools (elevated copy).
  A missing item shows up as "all commands rejected" or "signature-mismatch for everything",
  which is expensive to diagnose from behaviour alone but trivial to see here.

  Usage:
    powershell -ExecutionPolicy Bypass -File scripts\scripted-spawn-preflight.ps1
    powershell -ExecutionPolicy Bypass -File scripts\scripted-spawn-preflight.ps1 -ProbeSql
#>
param(
    [switch]$ProbeSql,
    [switch]$ClientCompile,
    [string]$SqlServer = '.',
    [string]$SqlDatabase = 'Game',
    [int]$SiloPort = 11111,
    [int]$GatewayTcpPort = 7789,
    [int]$DsUplinkPort = 19871
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

# Same value the server logs print as "key fingerprint" (SHA-256 of the raw key, first 4 bytes,
# upper-case hex). Compare the three process logs against this to prove they share one key.
function Get-KeyFingerprint {
    param([string]$Base64)
    if ([string]::IsNullOrWhiteSpace($Base64)) { return '(missing)' }
    $bytes = $null
    try { $bytes = [Convert]::FromBase64String($Base64) } catch { return '(not-base64)' }
    if ($null -eq $bytes -or $bytes.Length -eq 0) { return '(empty)' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash($bytes)
    return ([BitConverter]::ToString($hash, 0, 4) -replace '-', '').ToUpperInvariant()
}

$script:Problems = New-Object System.Collections.Generic.List[string]

function Say {
    param([string]$Level, [string]$Text)
    Write-Host ("[{0}] {1}" -f $Level, $Text)
}

function Warn {
    param([string]$Text)
    $script:Problems.Add($Text)
    Say 'WARN' $Text
}

Write-Host ''
Write-Host '=== Scripted world spawn: preflight ==='
Write-Host ("repo: {0}" -f $RepoRoot)
Write-Host ''

# ---------------------------------------------------------------- 1. shared switch + key ----
$envEnabled = [Environment]::GetEnvironmentVariable('ScriptedSpawn__Enabled')
$envKey = [Environment]::GetEnvironmentVariable('ScriptedSpawn__SigningKeyBase64')
$legacyEnabled = [Environment]::GetEnvironmentVariable('HZN_SCRIPTED_SPAWN')
$legacyKey = [Environment]::GetEnvironmentVariable('HZN_SCRIPT_SIGNING_KEY_B64')

Say 'INFO' ("ScriptedSpawn__Enabled = {0}" -f $(if ([string]::IsNullOrWhiteSpace($envEnabled)) { '(unset)' } else { $envEnabled }))
Say 'INFO' ("ScriptedSpawn__SigningKeyBase64 = {0}" -f $(if ([string]::IsNullOrWhiteSpace($envKey)) { '(unset)' } else { '(set)' }))
if (-not [string]::IsNullOrWhiteSpace($legacyEnabled) -or -not [string]::IsNullOrWhiteSpace($legacyKey)) {
    Say 'INFO' ("legacy HZN_* names are also present (fallback only; ScriptedSpawn__* wins): Enabled={0} Key={1}" -f `
        $(if ([string]::IsNullOrWhiteSpace($legacyEnabled)) { '(unset)' } else { $legacyEnabled }), `
        $(if ([string]::IsNullOrWhiteSpace($legacyKey)) { '(unset)' } else { '(set)' }))
}

$effectiveEnabled = if ([string]::IsNullOrWhiteSpace($envEnabled)) { $legacyEnabled } else { $envEnabled }
$effectiveKey = if ([string]::IsNullOrWhiteSpace($envKey)) { $legacyKey } else { $envKey }

if ([string]::IsNullOrWhiteSpace($effectiveEnabled) -or $effectiveEnabled.Trim() -notin @('1', 'true', 'True', 'TRUE', 'on', 'yes')) {
    Warn 'switch is off: Grain will reject every command with "scripted feature disabled". Set ScriptedSpawn__Enabled=1 in all three launch shells.'
}

if ([string]::IsNullOrWhiteSpace($effectiveKey)) {
    Warn 'no signing key in this shell: the process started from it will fail closed (Grain cannot sign / DS cannot verify).'
} else {
    $fingerprint = Get-KeyFingerprint $effectiveKey
    Say 'INFO' ("key fingerprint = {0}  (must match the [ScriptedSpawn] line in Silo, Gateway and DS logs)" -f $fingerprint)
    if ($fingerprint -eq '(not-base64)') {
        Warn 'signing key is not valid Base64; the process treats it as "no key".'
    } else {
        try {
            $keyLength = ([Convert]::FromBase64String($effectiveKey)).Length
            if ($keyLength -lt 32) { Warn ("signing key is only {0} bytes; use >= 32 (e.g. openssl rand -base64 32)." -f $keyLength) }
        } catch { }
    }
}

# --------------------------------------------------------- 2. listeners (restart detection) ----
function Test-PortListening {
    param([int]$Port)
    try {
        $conn = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $conn) { return $null }
        return $conn.OwningProcess
    } catch { return $null }
}

Write-Host ''
foreach ($probe in @(
        @{ Name = 'Orleans Silo'; Port = $SiloPort },
        @{ Name = 'Game Gateway (client TCP)'; Port = $GatewayTcpPort },
        @{ Name = 'Game Gateway (DS uplink)'; Port = $DsUplinkPort }
    )) {
    $pid0 = Test-PortListening -Port $probe.Port
    if ($null -eq $pid0) {
        Say 'INFO' ("{0} : port {1} has no listener." -f $probe.Name, $probe.Port)
    } else {
        $procName = try { (Get-Process -Id $pid0 -ErrorAction Stop).ProcessName } catch { 'unknown' }
        Warn ("{0} is already running (PID {1}, {2}) started BEFORE these changes: it holds the old assemblies. Restart it after deploying; nothing here was touched." -f $probe.Name, $pid0, $procName)
    }
}

# ------------------------------------------------------------------- 3. client assemblies ----
Write-Host ''
Say 'INFO' 'shared assemblies the Flax client actually references:'

function Get-DigestHead {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $digest = [System.Security.Cryptography.SHA256]::Create().ComputeHash($stream)
    } finally {
        $stream.Dispose()
    }
    return ([BitConverter]::ToString($digest, 0, 8) -replace '-', '').Substring(0, 16).ToUpperInvariant()
}

$gameProject = Join-Path $RepoRoot 'HundunWorld\Source\Game.csproj'
$projectDir = Split-Path -Parent $gameProject
$toolsDir = (Get-ChildItem 'C:\Program Files (x86)\Flax' -Directory -Filter 'Flax_*' -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Name -replace '^Flax_', '') } -Descending |
    Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName 'Binaries\Tools' })
if ([string]::IsNullOrWhiteSpace($toolsDir)) {
    throw 'No Flax_* engine found under C:\Program Files (x86)\Flax'
}

# Which copies to compare is NOT hardcoded: the HintPath entries in Game.csproj are the truth,
# and they have moved before (Output\Windows, and now some configurations also point straight at
# the engine's Binaries\Tools). One assembly can be referenced several times - once per build
# configuration - so collect EVERY match instead of keeping the last one.
$watched = @('Horizon.Game.Message.dll', 'Horizon.Game.Core.dll', 'Horizon.Orleans.Interface.dll')
$referenced = @{}
foreach ($leaf in $watched) { $referenced[$leaf] = New-Object System.Collections.Generic.List[string] }
if (Test-Path -LiteralPath $gameProject) {
    [xml]$proj = Get-Content -LiteralPath $gameProject -Raw
    foreach ($node in $proj.SelectNodes('//HintPath')) {
        $leaf = Split-Path -Leaf ($node.InnerText -replace '\\', '/')
        if ($watched -notcontains $leaf) { continue }
        $resolved = [System.IO.Path]::GetFullPath((Join-Path $projectDir $node.InnerText))
        if (-not $referenced[$leaf].Contains($resolved)) { $referenced[$leaf].Add($resolved) }
    }
} else {
    Warn ("Game.csproj not found at {0}: cannot determine what the client references." -f $gameProject)
}

foreach ($leaf in $watched) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($leaf)
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($r in $referenced[$leaf]) { $paths.Add($r) }
    $paths.Add((Join-Path $toolsDir $leaf))
    $paths.Add((Join-Path $RepoRoot ("{0}\bin\Debug\net10.0\{1}" -f $name, $leaf)))

    Say 'INFO' ("  {0}  referenced by {1} config(s)" -f $leaf, $referenced[$leaf].Count)
    $heads = @{}
    foreach ($path in ($paths | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $path)) {
            Say 'INFO' ("      (absent) {0}" -f $path)
            continue
        }
        $item = Get-Item -LiteralPath $path
        $head = Get-DigestHead -Path $item.FullName
        $heads[$path] = @{ Head = $head; Modified = $item.LastWriteTime }
        $marker = if ($referenced[$leaf] -contains $path) { ' <- referenced by Game.csproj' } else { '' }
        Say 'INFO' ("      sha256={0} mtime={1:o} {2}{3}" -f $head, $item.LastWriteTime, $path, $marker)
    }

    if ($referenced[$leaf].Count -eq 0) {
        Say 'INFO' '      (no HintPath entry in Game.csproj - the client does not reference it directly.)'
        continue
    }

    $distinct = @($heads.Values | ForEach-Object { $_.Head } | Sort-Object -Unique)
    if ($heads.Count -gt 1 -and $distinct.Count -gt 1) {
        $newest = $heads.GetEnumerator() | Sort-Object { $_.Value.Modified } -Descending | Select-Object -First 1
        $stale = New-Object System.Collections.Generic.List[string]
        foreach ($r in $referenced[$leaf]) {
            if (-not $heads.ContainsKey($r)) {
                Warn ("{0}: Game.csproj references {1} but that file is absent." -f $leaf, $r)
                continue
            }
            if ($heads[$r].Head -ne $newest.Value.Head -and -not $stale.Contains($heads[$r].Head)) {
                $stale.Add($heads[$r].Head)
                Warn ("{0}: a referenced copy ({1}) is not the newest build (referenced {2:o} vs newest {3:o})." -f $leaf, $heads[$r].Head, $heads[$r].Modified, $newest.Value.Modified)
            }
        }
        $toolsPath = Join-Path $toolsDir $leaf
        if ($heads.ContainsKey($toolsPath)) {
            $referencedHeads = @($referenced[$leaf] | ForEach-Object { if ($heads.ContainsKey($_)) { $heads[$_].Head } } | Sort-Object -Unique)
            if ($referencedHeads -notcontains $heads[$toolsPath].Head) {
                Warn ("{0}: the Flax Binaries\Tools copy differs from every referenced copy. Flax.Build resolves script assemblies there first, so run scripts\copy-shared-dlls-to-flax-tools.ps1 elevated." -f $leaf)
            }
        }
    }
}


# ------------------------------------------------------------------------- 4. grant table ----
Write-Host ''
if ($ProbeSql) {
    $sqlcmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($null -eq $sqlcmd) {
        Warn 'sqlcmd.exe not on PATH: the gm_script_grant table was NOT verified. Manual check: SELECT OBJECT_ID(''gm_script_grant'').'
    } else {
        $query = "SET NOCOUNT ON; IF OBJECT_ID('gm_script_grant') IS NULL PRINT 'MISSING' ELSE PRINT 'PRESENT';"
        $output = & sqlcmd -S $SqlServer -d $SqlDatabase -E -Q $query 2>&1
        $text = ($output | Out-String).Trim()
        if ($text -match 'PRESENT') {
            Say 'INFO' "grant table present ($SqlServer/$SqlDatabase)."
        } elseif ($text -match 'MISSING') {
            Warn ("grant table missing in {0}/{1}: grants will fail until scripts\sql\gm_script_grant.sql is applied." -f $SqlServer, $SqlDatabase)
        } else {
            Warn ("could not read the grant table state; sqlcmd said: {0}" -f $text)
        }
    }
} else {
    Say 'INFO' 'grant table not probed (pass -ProbeSql to check it; it is read-only either way).'
}

# ------------------------------------------------------- 5. offline client compile (opt-in) ----
if ($ClientCompile) {
    Write-Host ''
    Say 'INFO' 'client script compile (Flax own response file, references redirected at the repo builds):'

    $response = Join-Path $RepoRoot 'HundunWorld\Cache\Intermediate\GameEditorTarget\Windows\x64\Development\Game.CSharp.response'
    if (-not (Test-Path -LiteralPath $response)) {
        Warn ("no Flax response file at {0}: open the editor once (or run Flax.Build) so it gets generated." -f $response)
    } else {
        $csc = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($null -eq $csc) {
            Warn 'csc.dll not found under the dotnet SDK: cannot run the offline client compile.'
        } else {
            $raw = Get-Content -LiteralPath $response -Raw

            # Flax writes /langversion:14.0, which the SDK csc rejects (CS1617). 13.0 keeps the diagnostics identical.
            $patched = $raw -replace '/langversion:14.0', '/langversion:13.0'

            $tempOut = (Join-Path $env:TEMP 'hzn_client_compile_Game.CSharp.dll') -replace '\\', '/'
            $patched = [regex]::Replace($patched, '/out:"[^"]*"', ('/out:"{0}"' -f $tempOut))

            # Answer the question "does the client code match the CURRENT protocol?", not
            # "does it match whatever copy happens to sit in the engine folder" (section 3 reports that).
            $redirected = 0
            foreach ($leaf in @('Horizon.Game.Message.dll', 'Horizon.Game.Core.dll', 'Horizon.Game.Sim.dll', 'Horizon.Core.Abstract.dll')) {
                $name = [System.IO.Path]::GetFileNameWithoutExtension($leaf)
                # Try both layouts: the default is bin/Debug/net10.0/, but a build with
                # -p:OutputPath=bin/Debug/ leaves the assemblies directly in bin/Debug/.
                # (Keep this file ASCII-only: PowerShell 5.1 reads BOM-less files as the ANSI
                # codepage, and multi-byte UTF-8 in comments can desynchronise its parser.)
                $candidates = @(
                    (Join-Path $RepoRoot ("{0}\bin\Debug\net10.0\{1}" -f $name, $leaf)),
                    (Join-Path $RepoRoot ("{0}\bin\Debug\{1}" -f $name, $leaf))
                )
                $repoPath = $null
                foreach ($candidate in $candidates) {
                    if (Test-Path -LiteralPath $candidate) { $repoPath = $candidate; break }
                }
                if ($null -eq $repoPath) {
                    Warn ("{0}: no build output under bin\Debug - cannot point the client compile at the current protocol. Build that project first (plain dotnet build, no -p:OutputPath)." -f $leaf)
                    continue
                }
                $forward = $repoPath -replace '\\', '/'
                $before = $patched
                $patched = [regex]::Replace($patched, ('/reference:"[^"]*' + [regex]::Escape($leaf) + '"'), ('/reference:"{0}"' -f $forward))
                if ($patched -ne $before) { $redirected++ }
            }

            if ($redirected -eq 0) {
                Warn 'client compile could not be pointed at the repo builds; the verdict below reflects the ENGINE copy only (see section 3).'
            }

            $rsp = Join-Path $env:TEMP 'hzn_client_compile.rsp'
            [System.IO.File]::WriteAllText($rsp, $patched)

            $output = & dotnet exec $csc.FullName "@$rsp" 2>&1 | Out-String
            # Count DISTINCT diagnostics, not regex hits: the compiler emits each error twice
            # (once inline, once in its summary), so an occurrence count over-reports and the
            # number in the headline can never be matched against the lines printed below.
            $diagLines = @(($output -split "`r?`n") | ForEach-Object { $_.Trim() } |
                Where-Object { $_ -match 'error CS[0-9]+' } | Sort-Object -Unique)
            $totalErrors = $diagLines.Count

            if ($totalErrors -eq 0) {
                $entryCount = ($patched -split "`r?`n").Count
                Say 'INFO' ("client sources compile clean ({0} response entries; {1} protocol reference(s) pointed at the repo builds; output at {2})." -f $entryCount, $redirected, $tempOut)
            } else {
                Warn ("client compile reports {0} distinct error(s): {1}" -f $totalErrors, ((@($diagLines | ForEach-Object { if ($_ -match '(error CS[0-9]+)') { $matches[1] } }) | Sort-Object -Unique) -join ', '))
                # Attribution first: with several sessions editing this project at once, the useful
                # question is "are any of these in MY files", not the full listing.
                foreach ($g in ($diagLines | ForEach-Object {
                    if ($_ -match '^(.*?)\([0-9]+,') { Split-Path $matches[1] -Leaf } else { 'unknown' }
                } | Group-Object | Sort-Object Count -Descending)) {
                    Say 'INFO' ("      {0} in {1}" -f $g.Count, $g.Name)
                }
                foreach ($line in ($diagLines | Select-Object -First 20)) {
                    Say 'INFO' ("      {0}" -f $line)
                }
                Warn 'if the failures are CS0117/CS1061 on members that DO exist in the repo, the engine copy of the protocol assembly is stale - run scripts\copy-shared-dlls-to-flax-tools.ps1 elevated.'
            }
        }
    }
}

# ------------------------------------------------------------------------------ summary ----
Write-Host ''
if ($script:Problems.Count -eq 0) {
    Write-Host 'PREFLIGHT OK - switch on, key present, assemblies consistent, no stale listener reported.'
    Write-Host 'Next: start Silo -> Gateway -> DsHost from THIS shell, then check each prints [ScriptedSpawn] ... <same fingerprint>.'
    exit 0
}

Write-Host ("PREFLIGHT: {0} item(s) need attention (see WARN lines above)." -f $script:Problems.Count)
exit 1
