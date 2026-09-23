# 独立编译校验 HundunWorld 的 Game 目标（Flax Development profile）。
#
# 为什么需要它：
#   1) 正常 `dotnet build` / Flax 编辑器构建会被 devenv.exe 或运行中的游戏进程锁住
#      输出 DLL（MSB3027），报错与代码无关；
#   2) Flax 写入的 Game.CSharp.response 是**上次成功构建**的快照——新增的 .cs
#      文件不在其中，直接复用会得到“幽灵错误”（比如新页面引用的类型找不到）。
#
# 本脚本复用该 response，但：
#   - 把 /out、/doc、/generatedfilesout 改写到本目录，不碰被锁的产物；
#   - 自动补上 Source 下所有尚未出现在 response 里的 .cs（排除其它目标的
#     *.build.cs、ServerGame.gen.cs 与 Source/ServerGame/*）。
#
# 用法： powershell -ExecutionPolicy Bypass -File .compile-temp\verify-game-compile.ps1

$ErrorActionPreference = 'Stop'

$repoDir = 'C:\Works\GitHubProjects\HundunWorld'
$gameDir = Join-Path $repoDir 'HundunWorld'
$respSrc = Join-Path $gameDir 'Cache\Intermediate\GameTarget\Windows\x64\Development\Game.CSharp.response'
$workDir = Join-Path $repoDir '.compile-temp\verify-game'
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

if (-not (Test-Path $respSrc)) {
    throw "找不到 response 文件：$respSrc（请先在 Flax 里成功构建一次 Game）"
}

function Normalize-Path([string]$p) {
    return $p.Trim().Trim('"').Replace('/', '\').ToLowerInvariant()
}

$lines = Get-Content -Path $respSrc
$listed = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($line in $lines) {
    if (-not $line.StartsWith('/')) { [void]$listed.Add((Normalize-Path $line)) }
}

$newLines = New-Object 'System.Collections.Generic.List[string]'
foreach ($line in $lines) {
    if ($line -like '/out:*') { $newLines.Add('/out:"' + (Join-Path $workDir 'Game.CSharp.verify.dll') + '"'); continue }
    if ($line -like '/doc:*') { $newLines.Add('/doc:"' + (Join-Path $workDir 'Game.CSharp.verify.xml') + '"'); continue }
    if ($line -like '/generatedfilesout:*') { $newLines.Add('/generatedfilesout:"' + (Join-Path $workDir 'gen') + '"'); continue }
    $newLines.Add($line)
}

# 补上 response 快照之后新增的源文件
$added = 0
Get-ChildItem -Path (Join-Path $gameDir 'Source') -Recurse -Filter *.cs -File | ForEach-Object {
    $full = $_.FullName
    if ($full -like '*\Source\ServerGame\*') { return }
    if ($full -like '*.build.cs' -or $_.Name -eq 'ServerGame.gen.cs') { return }
    if (-not $listed.Contains((Normalize-Path $full))) {
        $newLines.Add('"' + $full + '"')
        $added++
    }
}

$respNew = Join-Path $workDir 'verify.response'
Set-Content -Path $respNew -Value $newLines -Encoding UTF8
Write-Output "response: $($newLines.Count) 行（补入 $added 个新增源文件）"

# 取版本号最大的 SDK（字符串降序会把 9.0.x 排在 10.0.x 前面，必须按 [version] 比较）
$csc = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' |
    Sort-Object { [version]$_.Directory.Parent.Parent.Name } -Descending | Select-Object -First 1
Write-Output "csc: $($csc.FullName)"
Write-Output '=== compile ==='

$output = & 'dotnet' exec $csc.FullName ('@' + $respNew) 2>&1
$errors = $output | Where-Object { $_ -match 'error CS' }
$warnings = $output | Where-Object { $_ -match 'warning CS' }

if ($errors) {
    Write-Output "--- $($errors.Count) 个编译错误 ---"
    $errors | Select-Object -First 40 | ForEach-Object { Write-Output $_ }
} else {
    Write-Output '--- 0 个编译错误 ---'
}
Write-Output "warnings: $($warnings.Count)"
Write-Output "csc exit code: $LASTEXITCODE"
