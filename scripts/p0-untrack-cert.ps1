#requires -Version 5.1
<#
  取消跟踪证书文件，**保留磁盘上的文件**（等价于 git rm --cached，不做删除）。

  用途（P0-2，见 docs/P0执行任务_2026-11-07.md）：
    仓库里被跟踪的私钥/证书副本应当出库。本脚本只做"取消跟踪"这一件事，
    不改写历史、不删除文件、不提交、不轮换证书。

  安全设计：
    1) 默认 DryRun：只打印将要执行的命令，不改索引。要真正执行必须显式加 -Execute。
    2) 磁盘文件不存在时**拒绝处理**：那种情况下索引里是唯一副本，取消跟踪等于丢证。
    3) 只接受 .pfx/.p12/.jks/.keystore 后缀，其余路径直接拒绝，避免误伤。
    4) 执行后只打印 git status 片段，不调用 git commit / git push。

  用法：
    powershell -ExecutionPolicy Bypass -File scripts\p0-untrack-cert.ps1
    powershell -ExecutionPolicy Bypass -File scripts\p0-untrack-cert.ps1 -Execute

  人工必须完成的部分（脚本不做，也不应做）：
    · 在部署侧签发/轮换新证书，并把新证书分发到使用它的服务；
    · 确认旧证书失效时间；
    · 是否清理 git 历史（git filter-repo 等不可逆操作）需单独评审后由人执行。
#>

[CmdletBinding()]
param(
    # 目标路径（相对仓库根）
    [string[]] $Paths = @('cert.pfx', 'Horizon.WebApi/cert.pfx'),

    # 真正执行 git rm --cached；不加则为 DryRun
    [switch] $Execute,

    # 仓库根；默认取本脚本上一级目录
    [string] $RepoRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$AllowedExtensions = @('.pfx', '.p12', '.jks', '.keystore')

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Args)
    # git 在"未跟踪/不匹配"这类正常探测场景也会写 stderr；
    # 这里临时把 ErrorActionPreference 放宽，避免 stderr 被当成终止性错误抛出。
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $script:Root @Args 2>&1
    }
    finally {
        $ErrorActionPreference = $prev
    }
    return [pscustomobject]@{ Output = @($out | ForEach-Object { "$_" }); Code = $LASTEXITCODE }
}

# ---------- 0 定位仓库 ----------
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "仓库根不存在：$RepoRoot"
}
$script:Root = (Resolve-Path -LiteralPath $RepoRoot).Path

$probe = Invoke-Git rev-parse --is-inside-work-tree
if ($probe.Code -ne 0 -or ($probe.Output -join "`n").Trim() -ne 'true') {
    throw "不是 git 工作区：$script:Root"
}

$mode = if ($Execute) { 'EXECUTE' } else { 'DRYRUN' }
Write-Host "[p0-untrack-cert] 模式=$mode  仓库=$script:Root"
Write-Host ''

# ---------- 1 逐个校验 ----------
$plan = @()
$blocked = @()

foreach ($rel in $Paths) {
    $relNorm = $rel -replace '\\', '/'
    $full = Join-Path $script:Root $rel

    $ext = [System.IO.Path]::GetExtension($relNorm).ToLowerInvariant()
    if ($AllowedExtensions -notcontains $ext) {
        $blocked += "$relNorm（后缀不在白名单 $($AllowedExtensions -join ',')，拒绝处理）"
        continue
    }

    # 磁盘上必须存在：不存在时索引里是唯一副本，取消跟踪等于丢证
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        $blocked += "$relNorm（磁盘文件不存在，拒绝处理：此时索引里是唯一副本）"
        continue
    }

    $tracked = Invoke-Git ls-files --error-unmatch -- $relNorm
    if ($tracked.Code -ne 0) {
        Write-Host "skip  $relNorm（未被跟踪，无需处理）"
        continue
    }

    $size = (Get-Item -LiteralPath $full).Length
    $plan += [pscustomobject]@{ Path = $relNorm; Size = $size }
}

# ---------- 2 打印计划 ----------
if ($plan.Count -eq 0 -and $blocked.Count -eq 0) {
    Write-Host '没有需要取消跟踪的文件。'
}

foreach ($item in $plan) {
    Write-Host ("plan  git rm --cached -- {0}    (磁盘文件 {1:N0} B，保留不动)" -f $item.Path, $item.Size)
}

foreach ($b in $blocked) {
    Write-Warning "阻止  $b"
}

if ($blocked.Count -gt 0) {
    Write-Host ''
    Write-Host '存在被阻止的路径，本次不执行任何改动（请先人工确认）。'
    exit 1
}

if ($plan.Count -eq 0) { exit 0 }

# ---------- 3 执行 ----------
if (-not $Execute) {
    Write-Host ''
    Write-Host 'DryRun 结束：未改动索引。确认无误后加 -Execute 再跑一次。'
    exit 0
}

$failed = 0
foreach ($item in $plan) {
    $r = Invoke-Git rm --cached --quiet -- $item.Path
    if ($r.Code -ne 0) {
        Write-Warning "失败  $($item.Path)：$($r.Output -join ' ')"
        $failed++
    }
    else {
        Write-Host "done  $($item.Path)（仅取消跟踪，磁盘文件仍在）"
    }
}

# ---------- 4 结果与后续提示 ----------
Write-Host ''
Write-Host '当前索引状态（这几条应为 staged deletion）：'
foreach ($item in $plan) {
    $st = Invoke-Git status --short -- $item.Path
    Write-Host ("  {0}" -f (($st.Output -join "`n") -replace "`n", "`n  "))
    $still = Invoke-Git ls-files --error-unmatch -- $item.Path
    if ($still.Code -eq 0) { Write-Warning "  $($item.Path) 仍在索引里" ; $failed++ }
    elseif (Test-Path -LiteralPath (Join-Path $script:Root $item.Path)) { Write-Host "  $($item.Path) 磁盘文件保留 ✓" }
}

Write-Host ''
Write-Host '后续（人工）：'
Write-Host '  1) 先在部署侧签发/轮换新证书并分发，旧证明确失效时间；'
Write-Host '  2) 再提交这次"取消跟踪"的改动（本脚本不提交：git commit 由人执行）；'
Write-Host '  3) 历史里的旧密钥仍需单独决策是否用 git filter-repo 清理（不可逆，需评审）。'

if ($failed -gt 0) { exit 2 }
exit 0
