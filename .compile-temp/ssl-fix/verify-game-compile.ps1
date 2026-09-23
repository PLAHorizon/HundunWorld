$ErrorActionPreference = 'Stop'

$gameDir = 'C:\Works\GitHubProjects\HundunWorld\HundunWorld'
$respSrc = Join-Path $gameDir 'Cache\Intermediate\GameTarget\Windows\x64\Development\Game.CSharp.response'
$tempDir = 'C:\Works\GitHubProjects\HundunWorld\.compile-temp\ssl-fix'
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

$outDll = Join-Path $tempDir 'Game.CSharp.verify.dll'
$outXml = Join-Path $tempDir 'Game.CSharp.verify.xml'

$lines = Get-Content $respSrc
$newLines = New-Object System.Collections.Generic.List[string]

foreach ($line in $lines) {
    if ($line -like '/out:*') {
        $newLines.Add('/out:"' + $outDll + '"')
        continue
    }
    if ($line -like '/doc:*') {
        $newLines.Add('/doc:"' + $outXml + '"')
        continue
    }
    if ($line -like '/generatedfilesout:*') {
        $newLines.Add('/generatedfilesout:"' + $tempDir + '"')
        continue
    }
    $newLines.Add($line)
}

# Flax 默认引用集不含 System.Net.Security（Game.Build.cs 已补充该引用）
$newLines.Add('/reference:"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.10\ref\net10.0\System.Net.Security.dll"')
# 新增文件（真正构建时由 Flax 自动纳入源文件列表）
$newLines.Add('"' + (Join-Path $gameDir 'Source\Game\Network\DevTlsTrustPolicy.cs') + '"')

$respNew = Join-Path $tempDir 'verify.response'
Set-Content -Path $respNew -Value $newLines -Encoding UTF8

$csc = 'C:\Program Files\dotnet\sdk\10.0.302\Roslyn\bincore\csc.dll'
Write-Output '=== compile start (Flax Development profile) ==='
& 'dotnet' exec $csc ('@' + $respNew) 2>&1 | Select-Object -Last 80
Write-Output "=== csc exit code: $LASTEXITCODE ==="
