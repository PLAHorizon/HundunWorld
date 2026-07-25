# 将新编译的 Horizon.Game.ECS.Arch.dll 复制到 Flax Tools 目录
# 使用方法：右键此文件 -> 使用 PowerShell 运行（需要管理员权限）
# 或在管理员 PowerShell 中执行：powershell -ExecutionPolicy Bypass -File "c:\Works\GitHubProjects\HundunWorld\copy-ecs-arch-to-flax.ps1"

$ErrorActionPreference = 'Stop'

$src = "c:\Works\GitHubProjects\HundunWorld\Horizon.Game.ECS.Arch\bin\Debug\net10.0"
$dst = "C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools"

# 检查是否以管理员身份运行
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "错误：需要管理员权限才能写入 $dst" -ForegroundColor Red
    Write-Host "请右键此脚本 -> '使用 PowerShell 运行（管理员）'，或在管理员 PowerShell 中执行此脚本" -ForegroundColor Yellow
    exit 1
}

# 检查 FlaxEditor 是否在运行
$flaxProc = Get-Process FlaxEditor -ErrorAction SilentlyContinue
if ($flaxProc) {
    Write-Host "警告：检测到 FlaxEditor 正在运行 (PID: $($flaxProc.Id -join ','))，DLL 可能被锁定" -ForegroundColor Yellow
    $choice = Read-Host "是否停止 FlaxEditor？(Y/N)"
    if ($choice -eq 'Y' -or $choice -eq 'y') {
        $flaxProc | Stop-Process -Force
        Start-Sleep -Seconds 3
        Write-Host "FlaxEditor 已停止" -ForegroundColor Green
    } else {
        Write-Host "用户取消，退出脚本" -ForegroundColor Yellow
        exit 0
    }
}

# 检查源文件
if (-not (Test-Path "$src\Horizon.Game.ECS.Arch.dll")) {
    Write-Host "错误：源 DLL 不存在：$src\Horizon.Game.ECS.Arch.dll" -ForegroundColor Red
    Write-Host "请先执行：dotnet build `"c:\Works\GitHubProjects\HundunWorld\Horizon.Game.ECS.Arch\Horizon.Game.ECS.Arch.csproj`" -c Debug" -ForegroundColor Yellow
    exit 1
}

# 复制文件
Write-Host "`n正在复制 DLL 到 Flax Tools 目录..." -ForegroundColor Cyan
$filesToCopy = @(
    "Horizon.Game.ECS.Arch.dll",
    "Horizon.Game.ECS.Arch.pdb",
    "Horizon.Game.ECS.Arch.xml"
)

foreach ($file in $filesToCopy) {
    $srcPath = Join-Path $src $file
    $dstPath = Join-Path $dst $file
    if (Test-Path $srcPath) {
        Copy-Item -Path $srcPath -Destination $dstPath -Force
        Write-Host "  已复制：$file" -ForegroundColor Green
    } else {
        Write-Host "  跳过（源文件不存在）：$file" -ForegroundColor DarkGray
    }
}

# 验证结果
$newDll = Get-Item "$dst\Horizon.Game.ECS.Arch.dll"
Write-Host "`n复制完成！" -ForegroundColor Green
Write-Host "目标文件：$($newDll.FullName)" -ForegroundColor Cyan
Write-Host "最后修改时间：$($newDll.LastWriteTime)" -ForegroundColor Cyan
Write-Host "文件大小：$($newDll.Length) 字节" -ForegroundColor Cyan

Write-Host "`n现在可以重新启动 FlaxEditor，编译错误应该已被修复。" -ForegroundColor Green
