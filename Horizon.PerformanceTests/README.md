# 混沌世界项目 - 性能测试指南

**文档日期**: 2026年2月11日  
**测试框架**: NBomber 6.0.4  
**测试环境**: .NET 10.0

---

## 📋 概述

本文档介绍混沌世界项目的性能测试框架和测试方法。性能测试使用NBomber框架，针对Orleans Grains进行负载测试和性能基准测试。

---

## 🎯 性能目标

### Grain性能目标

| Grain类型 | 操作 | 目标RPS | 目标P95延迟 | 目标P99延迟 |
|----------|------|---------|------------|------------|
| PassportGrain | 登录 | >40 RPS | <100ms | <200ms |
| PassportGrain | 注册 | >15 RPS | <150ms | <300ms |
| CharacterGrain | 查询 | >100 RPS | <50ms | <100ms |
| CombatGrain | 攻击 | >80 RPS | <30ms | <60ms |
| CombatGrain | 技能释放 | >60 RPS | <40ms | <80ms |

### 系统性能目标

| 指标 | 目标值 |
|------|-------|
| 单Silo承载用户数 | >1000 |
| Gateway最大连接数 | >5000 |
| 数据库查询P95延迟 | <100ms |
| Redis缓存命中率 | >90% |
| 内存使用率 | <80% |
| CPU使用率（负载下） | <70% |

---

## 🚀 快速开始

### 1. 环境准备

```bash
# 安装依赖
cd Horizon.PerformanceTests
dotnet restore

# 配置测试环境
export TEST_DATABASE_CONNECTION="..."
export TEST_REDIS_CONNECTION="..."
```

### 2. 运行性能测试

```bash
# 运行所有性能测试
dotnet test

# 运行特定测试类
dotnet test --filter "FullyQualifiedName~PassportGrainPerformanceTests"

# 运行特定测试方法
dotnet test --filter "FullyQualifiedName~PassportGrain_LoginPerformanceTest"
```

### 3. 查看测试报告

测试完成后，NBomber会自动生成HTML报告：

```bash
# 报告位置
./Horizon.PerformanceTests/bin/Debug/net10.0/reports/

# 打开报告
# Windows: start reports/index.html
# Linux: xdg-open reports/index.html
# macOS: open reports/index.html
```

---

## 📊 测试场景

### 1. PassportGrain性能测试

#### 登录性能测试

- **测试目标**: 验证认证系统在高并发下的性能
- **负载模式**: 50 RPS，持续30秒
- **预热时间**: 10秒
- **性能指标**:
  - RPS > 40
  - P95延迟 < 100ms
  - 错误率 < 1%

#### 注册性能测试

- **测试目标**: 验证新用户注册的性能
- **负载模式**: 20 RPS，持续20秒
- **预热时间**: 5秒
- **性能指标**:
  - RPS > 15
  - P95延迟 < 150ms
  - 错误率 < 1%

### 2. CombatGrain性能测试

#### 攻击性能测试

- **测试目标**: 验证战斗系统攻击操作性能
- **负载模式**: 100 RPS，持续30秒
- **预热时间**: 10秒
- **性能指标**:
  - RPS > 80
  - P95延迟 < 30ms
  - 错误率 < 0.5%

#### 技能释放性能测试

- **测试目标**: 验证技能系统的性能
- **负载模式**: 80 RPS，持续30秒
- **预热时间**: 10秒
- **性能指标**:
  - RPS > 60
  - P95延迟 < 40ms
  - 错误率 < 0.5%

---

## 🔧 测试配置

### NBomber配置

```csharp
var scenario = Scenario.Create("test_name", async context =>
{
    // 测试逻辑
    return Response.Ok();
})
.WithWarmUpDuration(TimeSpan.FromSeconds(10))  // 预热时间
.WithLoadSimulations(
    Simulation.Inject(
        rate: 50,                               // 每秒请求数
        interval: TimeSpan.FromSeconds(1),      // 注入间隔
        during: TimeSpan.FromSeconds(30)        // 持续时间
    )
);
```

### Orleans TestCluster配置

```csharp
var builder = new TestClusterBuilder();
builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
_cluster = builder.Build();
await _cluster.DeployAsync();

public class TestSiloConfigurations : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .UseInMemoryReminderService()
            .AddMemoryGrainStorage("Default");
    }
}
```

---

## 📈 负载模式

### 1. 恒定负载 (Constant Load)

```csharp
Simulation.InjectPerSec(
    rate: 50,                               // 每秒50个请求
    during: TimeSpan.FromMinutes(1)         // 持续1分钟
)
```

### 2. 斜坡负载 (Ramp-up Load)

```csharp
Simulation.RampPerSec(
    rate: 100,                              // 从0逐渐增加到100 RPS
    during: TimeSpan.FromMinutes(1)         // 在1分钟内完成
)
```

### 3. 注入负载 (Inject Load)

```csharp
Simulation.Inject(
    rate: 50,                               // 每秒注入50个请求
    interval: TimeSpan.FromSeconds(1),      // 每秒注入一次
    during: TimeSpan.FromSeconds(30)        // 持续30秒
)
```

### 4. 保持负载 (Keep Constant)

```csharp
Simulation.KeepConstant(
    copies: 100,                            // 保持100个并发用户
    during: TimeSpan.FromMinutes(5)         // 持续5分钟
)
```

---

## 🔍 性能分析

### 查看关键指标

```csharp
var stats = NBomberRunner.RegisterScenarios(scenario).Run();

// 获取统计数据
var scenarioStats = stats.ScenarioStats[0];

// RPS（每秒请求数）
Console.WriteLine($"RPS: {scenarioStats.Ok.Request.RPS}");

// 延迟百分位
Console.WriteLine($"P50: {scenarioStats.Ok.Latency.Percent50}ms");
Console.WriteLine($"P75: {scenarioStats.Ok.Latency.Percent75}ms");
Console.WriteLine($"P95: {scenarioStats.Ok.Latency.Percent95}ms");
Console.WriteLine($"P99: {scenarioStats.Ok.Latency.Percent99}ms");

// 错误率
Console.WriteLine($"Success: {scenarioStats.Ok.Request.Count}");
Console.WriteLine($"Failed: {scenarioStats.Fail.Request.Count}");
```

### 性能瓶颈识别

1. **高延迟**: P95 > 目标值
   - 检查数据库查询
   - 检查Grain激活时间
   - 检查网络延迟

2. **低RPS**: 实际RPS < 目标RPS
   - 检查CPU使用率
   - 检查线程池饱和度
   - 检查Grain锁竞争

3. **高错误率**: 错误率 > 1%
   - 检查超时配置
   - 检查资源耗尽
   - 检查并发限制

---

## 📝 测试报告

### 报告内容

NBomber会生成包含以下内容的HTML报告：

1. **测试概览**
   - 测试场景名称
   - 测试持续时间
   - 总请求数

2. **性能指标**
   - RPS（每秒请求数）
   - 延迟分布（P50/P75/P95/P99）
   - 成功率和错误率

3. **时间序列图表**
   - RPS随时间变化
   - 延迟随时间变化
   - 错误率随时间变化

4. **详细统计**
   - 最小/最大/平均延迟
   - 数据吞吐量
   - 并发连接数

### 报告示例

```
Scenario: passport_login_test
Duration: 00:00:30
Total Requests: 1500

OK Stats:
  RPS: 48.5
  P50: 45ms
  P95: 89ms
  P99: 142ms
  Success: 1485

Fail Stats:
  Failed: 15
  Error Rate: 1%
```

---

## 🎯 性能优化建议

### 1. Orleans Grain优化

- 减少不必要的WriteStateAsync调用
- 使用Grain状态压缩（MemoryPack）
- 调整Grain激活超时配置
- 实施Grain分片策略

### 2. 数据库优化

- 添加缺失索引
- 修复N+1查询问题
- 使用读写分离
- 优化连接池配置

### 3. Redis缓存优化

- 实施热点数据预加载
- 配置缓存穿透保护
- 优化缓存过期策略
- 实施缓存一致性

### 4. 网络优化

- 启用消息合批
- 启用协议压缩（GZip）
- 优化TCP参数
- 实施连接池

---

## 📞 支持与联系

- **技术支持**: [GitHub Issues](https://github.com/PLAHorizon/HundunWorld/issues)
- **文档维护**: GitHub Copilot AI Agent
- **NBomber文档**: https://nbomber.com/docs/

---

**文档结束**

*本文档描述了混沌世界项目的性能测试框架和最佳实践。*
