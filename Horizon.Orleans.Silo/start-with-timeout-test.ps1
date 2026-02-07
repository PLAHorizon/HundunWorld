# Horizon Orleans Silo 超时配置测试启动脚本
# 用于验证重构后的Orleans网关超时配置

Write-Host "🚀 启动 Horizon Orleans Silo 超时配置测试..." -ForegroundColor Green

# 设置环境变量以启用网关测试
$env:RUN_GATEWAY_TESTS = "true"

# 检查项目文件是否存在
$projectPath = "d:\Long\Flax\AIProjects\0528\Horizon.Orleans.Silo\Horizon.Orleans.Silo.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Host "❌ 项目文件不存在: $projectPath" -ForegroundColor Red
    exit 1
}

Write-Host "📋 重构后的超时配置特性:" -ForegroundColor Cyan
Write-Host "   ✅ 网关连接超时: 5秒" -ForegroundColor Green
Write-Host "   ✅ 网关消息传送超时: 15秒" -ForegroundColor Green
Write-Host "   ✅ 响应超时: 30秒 (修复了30分钟超时问题)" -ForegroundColor Green
Write-Host "   ✅ 网关重连超时: 30秒" -ForegroundColor Green
Write-Host "   ✅ 心跳检测超时: 8秒" -ForegroundColor Green
Write-Host "   ✅ 消息确认超时: 12秒" -ForegroundColor Green
Write-Host "   ✅ 最大转发次数: 2" -ForegroundColor Green
Write-Host ""

Write-Host "🔧 正在构建项目..." -ForegroundColor Yellow
try {
    Set-Location "d:\Long\Flax\AIProjects\0528\Horizon.Orleans.Silo"
    dotnet build --configuration Release --verbosity minimal
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ 项目构建失败" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✅ 项目构建成功" -ForegroundColor Green
} catch {
    Write-Host "❌ 构建过程中发生错误: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🌐 启动Orleans Silo (包含网关诊断和超时测试)..." -ForegroundColor Yellow
Write-Host "注意: 首次启动会运行完整的网关连接诊断和超时验证测试" -ForegroundColor Cyan
Write-Host "请观察日志输出中的诊断结果..." -ForegroundColor Cyan
Write-Host ""

try {
    # 启动Orleans Silo
    dotnet run --configuration Release
} catch {
    Write-Host "❌ Orleans Silo启动失败: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🏁 Horizon Orleans Silo 超时配置测试完成" -ForegroundColor Green
