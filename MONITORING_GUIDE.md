# 混沌世界(HundunWorld) 监控系统部署指南

**文档版本**: 1.0  
**更新日期**: 2026年2月8日  
**适用范围**: Orleans Silo, 游戏网关

---

## 📋 概述

混沌世界项目集成了基于OpenTelemetry的现代化可观测性（Observability）体系，包括：

- **APM (应用性能监控)**: OpenTelemetry SDK + 分布式追踪
- **指标导出**: Prometheus HTTP Listener 端点
- **可视化**: Grafana 仪表板
- **告警**: Prometheus Alertmanager 规则

### 架构图

```
┌──────────────┐    ┌──────────────┐
│ Orleans Silo │    │ Game Gateway │
│  :9464/metrics│    │  :9465/metrics│
└──────┬───────┘    └──────┬───────┘
       │                   │
       └───────┬───────────┘
               │
       ┌───────▼───────┐
       │  Prometheus    │
       │    :9090       │
       └───────┬───────┘
               │
       ┌───────▼───────┐     ┌─────────────────┐
       │   Grafana      │     │  Alertmanager    │
       │    :3000       │     │     :9093        │
       └───────────────┘     └─────────────────┘
```

---

## 🚀 快速开始

### 1. 服务端口分配

| 服务 | 端口 | 说明 |
|------|------|------|
| Orleans Silo Prometheus | 9464 | Silo指标导出端点 |
| Game Gateway Prometheus | 9465 | 网关指标导出端点 |
| Prometheus Server | 9090 | Prometheus Web UI |
| Grafana | 3000 | Grafana 仪表板 |
| Alertmanager | 9093 | 告警管理器 |

### 2. 配置Prometheus端口

通过 `appsettings.json` 配置自定义端口：

```json
{
  "Monitoring": {
    "PrometheusPort": 9464
  }
}
```

或通过环境变量：
```bash
export Monitoring__PrometheusPort=9464
```

### 3. 部署Prometheus

```bash
# 将配置文件复制到Prometheus配置目录
cp monitoring/prometheus/prometheus.yml /etc/prometheus/prometheus.yml
cp monitoring/prometheus/alert_rules.yml /etc/prometheus/alert_rules.yml

# 重启Prometheus
systemctl restart prometheus
```

或使用Docker：
```bash
docker run -d \
  --name prometheus \
  -p 9090:9090 \
  -v $(pwd)/monitoring/prometheus:/etc/prometheus \
  prom/prometheus:latest
```

### 4. 部署Grafana

```bash
docker run -d \
  --name grafana \
  -p 3000:3000 \
  grafana/grafana:latest
```

导入仪表板：
1. 打开 Grafana（http://localhost:3000）
2. 添加 Prometheus 数据源（http://prometheus:9090）
3. 导入仪表板：Dashboards → Import → 上传 `monitoring/grafana/hundunworld-dashboard.json`

---

## 📊 自定义指标

### Orleans Silo 指标 (`HundunWorld.Silo`)

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `hundunworld.silo.grain.calls.total` | Counter | Grain方法调用总数 |
| `hundunworld.silo.grain.call_errors.total` | Counter | Grain方法调用失败总数 |
| `hundunworld.silo.grain.call_duration.ms` | Histogram | Grain方法执行时长 |
| `hundunworld.silo.auth.login_attempts.total` | Counter | 登录尝试总数 |
| `hundunworld.silo.auth.login_success.total` | Counter | 登录成功总数 |
| `hundunworld.silo.auth.login_failures.total` | Counter | 登录失败总数 |
| `hundunworld.silo.auth.registrations.total` | Counter | 用户注册总数 |
| `hundunworld.silo.auth.password_changes.total` | Counter | 密码变更总数 |
| `hundunworld.silo.sessions.active` | UpDownCounter | 当前活跃会话数 |
| `hundunworld.silo.sessions.created.total` | Counter | 会话创建总数 |
| `hundunworld.silo.sessions.terminated.total` | Counter | 会话销毁总数 |
| `hundunworld.silo.tasks.running` | UpDownCounter | 运行中的任务数 |
| `hundunworld.silo.tasks.failed.total` | Counter | 失败的任务总数 |
| `hundunworld.silo.db.queries.total` | Counter | 数据库查询总数 |
| `hundunworld.silo.db.query_duration.ms` | Histogram | 数据库查询耗时 |

### 游戏网关指标 (`HundunWorld.Gateway`)

| 指标名 | 类型 | 说明 |
|--------|------|------|
| `hundunworld.gateway.connections.active` | UpDownCounter | 当前活跃连接数 |
| `hundunworld.gateway.connections.established.total` | Counter | 连接建立总数 |
| `hundunworld.gateway.connections.closed.total` | Counter | 连接断开总数 |
| `hundunworld.gateway.connections.errors.total` | Counter | 连接错误总数 |
| `hundunworld.gateway.messages.received.total` | Counter | 接收消息总数 |
| `hundunworld.gateway.messages.sent.total` | Counter | 发送消息总数 |
| `hundunworld.gateway.messages.processing_duration.ms` | Histogram | 消息处理时长 |
| `hundunworld.gateway.messages.errors.total` | Counter | 消息处理错误总数 |
| `hundunworld.gateway.network.bytes_received.total` | Counter | 接收字节总数 |
| `hundunworld.gateway.network.bytes_sent.total` | Counter | 发送字节总数 |
| `hundunworld.gateway.network.latency.ms` | Histogram | 网络延迟 |
| `hundunworld.gateway.orleans.calls.total` | Counter | Orleans调用总数 |
| `hundunworld.gateway.orleans.call_errors.total` | Counter | Orleans调用失败总数 |

### .NET运行时指标（自动采集）

通过 `OpenTelemetry.Instrumentation.Runtime` 自动采集：

| 指标名 | 说明 |
|--------|------|
| `process.runtime.dotnet.gc.collections.count` | GC回收次数 |
| `process.runtime.dotnet.gc.heap.size` | GC堆大小 |
| `process.runtime.dotnet.thread_pool.threads.count` | 线程池线程数 |
| `process.runtime.dotnet.thread_pool.queue.length` | 线程池队列长度 |
| `process.runtime.dotnet.assemblies.count` | 加载的程序集数 |

---

## 🔔 告警规则

### 严重告警 (Critical)

| 规则 | 条件 | 说明 |
|------|------|------|
| SiloInstanceDown | 服务停止响应 > 1分钟 | Orleans Silo宕机 |
| GatewayInstanceDown | 服务停止响应 > 1分钟 | 游戏网关宕机 |
| GrainCallErrorRateCritical | 错误率 > 15% | Grain调用严重异常 |
| LoginFailureSpike | 失败 > 10次/分钟 | 可能遭受暴力破解 |
| NoRunningTasks | 任务数 = 0 | Silo后台任务全部停止 |

### 警告告警 (Warning)

| 规则 | 条件 | 说明 |
|------|------|------|
| GrainCallLatencyHigh | P95延迟 > 1秒 | Grain调用慢 |
| GrainCallErrorRateHigh | 错误率 > 5% | Grain调用异常 |
| LoginFailureRateHigh | 失败率 > 30% | 登录异常 |
| GatewayConnectionsHigh | 连接数 > 5000 | 连接数过多 |
| MessageProcessingLatencyHigh | P95延迟 > 500ms | 消息处理慢 |
| HighMemoryUsage | GC堆 > 1GB | 内存使用过高 |
| HighGCPressure | GC > 10次/分钟 | GC压力大 |
| ThreadPoolStarvation | 队列 > 100 | 线程池饥饿 |

---

## 🔧 在代码中记录指标

### Silo端指标记录示例

```csharp
using Horizon.Orleans.Silo.Monitoring;

// 记录登录尝试
HorizonMetrics.LoginAttemptsTotal.Add(1);

// 记录登录成功
HorizonMetrics.LoginSuccessTotal.Add(1);

// 记录会话创建
HorizonMetrics.ActiveSessions.Add(1);
HorizonMetrics.SessionsCreatedTotal.Add(1);

// 记录数据库查询
var stopwatch = Stopwatch.StartNew();
// ... 执行查询 ...
stopwatch.Stop();
HorizonMetrics.DbQueriesTotal.Add(1);
HorizonMetrics.DbQueryDuration.Record(stopwatch.ElapsedMilliseconds);
```

### 网关端指标记录示例

```csharp
using Horizon.Game.Gateway.Monitoring;

// 记录连接建立
GatewayMetrics.ActiveConnections.Add(1);
GatewayMetrics.ConnectionsEstablishedTotal.Add(1);

// 记录消息处理
using var activity = GatewayMetrics.StartMessageActivity("Login");
var stopwatch = Stopwatch.StartNew();
// ... 处理消息 ...
stopwatch.Stop();
GatewayMetrics.MessagesReceivedTotal.Add(1);
GatewayMetrics.MessageProcessingDuration.Record(stopwatch.ElapsedMilliseconds);
```

---

## 📈 Grafana仪表板面板

仪表板 `hundunworld-dashboard.json` 包含以下面板组：

1. **📊 系统概览** - Silo/网关状态、连接数、会话数、调用速率、任务数
2. **⚡ Grain性能** - 调用速率、延迟分位数(P50/P95/P99)、错误率
3. **🔐 认证与安全** - 登录速率、注册和密码变更
4. **🌐 网关连接** - 活跃连接数、建立/断开/错误速率
5. **📨 消息处理** - 消息吞吐量、处理延迟分位数
6. **🖥️ .NET运行时** - GC堆内存、GC回收、线程池、网络流量、延迟

---

## 🔒 安全注意事项

1. **指标端点访问控制**: Prometheus端点暴露的指标可能包含敏感信息。建议：
   - 在生产环境中限制端口访问（防火墙规则）
   - 使用内网IP绑定而非 `0.0.0.0`
   
2. **Grafana访问控制**: 
   - 修改默认管理员密码
   - 启用HTTPS
   - 配置LDAP/OAuth认证

3. **Alertmanager通知安全**:
   - 使用加密通道发送告警通知
   - 避免在告警消息中包含敏感数据

---

## 📝 故障排除

### 常见问题

**Q: Prometheus无法抓取指标？**
- 检查端口是否正确：Silo默认9464，Gateway默认9465
- 检查防火墙规则
- 访问 `http://host:9464/metrics` 确认端点可用

**Q: Grafana没有数据？**
- 确认Prometheus数据源配置正确
- 检查Prometheus是否正在抓取目标
- 在Prometheus UI中执行查询确认指标存在

**Q: 告警没有触发？**
- 检查 `alert_rules.yml` 语法
- 在Prometheus UI → Alerts 页面查看规则状态
- 确认Alertmanager已正确配置
