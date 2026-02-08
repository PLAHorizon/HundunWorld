# 混沌世界 (HundunWorld) - MMORPG游戏项目

[![.NET Version](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![Orleans](https://img.shields.io/badge/Orleans-9.2.1-purple.svg)](https://dotnet.github.io/orleans/)
[![Flax Engine](https://img.shields.io/badge/Flax%20Engine-1.10-green.svg)](https://flaxengine.com/)
[![License](https://img.shields.io/badge/license-Proprietary-red.svg)]()

混沌世界是一款基于Microsoft Orleans分布式框架和Flax Engine游戏引擎构建的大型多人在线角色扮演游戏（MMORPG）。项目融合了武侠、仙侠和玄幻元素，提供渐进式世界观和丰富的游戏玩法。

## 📋 目录

- [项目概述](#项目概述)
- [技术架构](#技术架构)
- [快速开始](#快速开始)
- [项目结构](#项目结构)
- [配置说明](#配置说明)
- [开发指南](#开发指南)
- [部署指南](#部署指南)
- [安全说明](#安全说明)
- [文档](#文档)

## 🎮 项目概述

### 核心特性

- **分布式架构**: 基于Orleans Virtual Actor模型，支持横向扩展和高并发
- **现代化技术栈**: .NET 10, Orleans 9.2.1, EF Core 9.0, Redis集群
- **高性能网络**: 使用MemoryPack序列化和TouchSocket通信框架
- **安全认证**: PBKDF2密码哈希，JWT令牌，Redis会话管理
- **可观测性**: OpenTelemetry指标, Prometheus告警, Grafana仪表板, Seq日志聚合, 分布式追踪
- **事件驱动**: Orleans Stream事件发布/消费, 22种游戏事件类型
- **CI/CD**: GitHub Actions自动构建/测试, CodeQL安全扫描, Dependabot依赖管理
- **测试覆盖**: 836个单元测试用例（xUnit + Moq）
- **渐进式世界观**: 武侠(1-50级) → 仙侠(51-150级) → 玄幻(151-300级)
- **五行战斗系统**: 木火土金水相生相克的策略战斗

### 系统组件

```
混沌世界
├── Horizon.Orleans.Silo          # Orleans分布式服务器（含Filters/Monitoring/Services）
├── Horizon.Game.Gateway          # 游戏网关服务（TouchSocket TCP通信）
├── Horizon.WebApi                # RESTful API服务
├── Horizon.Orleans.Grains        # 业务逻辑Grains（26个Grain实现）
├── Horizon.Orleans.Interface     # Grain接口定义（26个接口，13个文件）
├── Horizon.Game.Message          # 网络消息协议（MemoryPack序列化）
├── Horizon.Core                  # 核心工具库（密码哈希、缓存）
├── Horizon.Core.Abstract         # 核心抽象接口
├── Horizon.Entities              # 数据实体和EF Core（5个DbContext）
├── Horizon.Model                 # 数据传输对象
├── Horizon.Game.Core             # 游戏核心逻辑
├── Horizon.Game.ECS              # ECS框架（Arch引擎）
├── Horizon.Mapper                # AutoMapper映射配置
├── Horizon.Share                 # 共享工具库
├── Horizon.Strategy.Storage.Redis # Redis缓存策略
├── Horizon.Game.Gateway.Tests    # 单元测试（836个测试用例）
├── HundunWorld                   # Flax Engine客户端（231个C#源文件）
└── monitoring                    # 监控配置（Prometheus/Grafana/Alertmanager）
```

## 🏗️ 技术架构

### 后端架构

```
┌─────────────┐    ┌─────────────┐
│ Web Client  │    │ Game Client │
└──────┬──────┘    └──────┬──────┘
       │                  │
       ▼                  ▼
┌─────────────┐    ┌──────────────┐
│   WebApi    │    │   Gateway    │
└──────┬──────┘    │ (TouchSocket)│
       │           └──────┬───────┘
       └────────┬─────────┘
                ▼
        ┌──────────────┐     ┌────────────────┐
        │Orleans Cluster│────▶│ Orleans Stream │
        │  (26 Grains)  │     │  (事件驱动)    │
        └───────┬───────┘     └────────────────┘
                │
      ┌─────────┼─────────┐
      ▼         ▼         ▼
┌──────────┐ ┌──────┐ ┌────────────┐
│SQL Server│ │Redis │ │ Prometheus │
└──────────┘ └──────┘ │ + Grafana  │
                      └────────────┘
```

### 客户端架构

```
HundunWorld (Flax Engine, 231个C#源文件)
├── ECS System (Arch)
│   ├── 14 Components (Player, Combat, Health, Skill, NPC, ...)
│   └── 9 Systems (Movement, Combat, Camera, Input, ...)
├── Network Module
│   ├── Gateway Selector (智能网关选择)
│   ├── 16+ Message Handlers (登录/角色/战斗/技能...)
│   ├── Network Sync (AOI/NPC/技能同步)
│   └── Reconnect Logic (断线重连)
├── Game Systems
│   ├── Combat System (五行战斗/技能/特效)
│   ├── Character System (属性/轻功/攀爬)
│   ├── Camera System (第三人称/碰撞/震屏)
│   ├── Equipment & Crafting (装备/材料/合成)
│   └── World Management (分块场景/实体同步)
├── Rendering
│   ├── MetaHuman (角色外观编辑)
│   └── Material System (皮肤/头发/眼睛材质)
├── UI System (84+脚本)
│   ├── Authentication (登录/注册)
│   ├── Character Creation (角色创建/管理)
│   ├── Game Main (背包/技能栏/属性/合成)
│   ├── MetaHuman Editor (外观编辑面板)
│   └── Performance Monitor (性能监控)
└── Performance
    └── Memory/Network/System Optimizer
```

## 🚀 快速开始

### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [SQL Server 2019+](https://www.microsoft.com/sql-server) 或 [PostgreSQL 13+](https://www.postgresql.org/)
- [Redis 6+](https://redis.io/)
- [Flax Engine 1.10](https://flaxengine.com/) (仅客户端开发)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) 或 [Rider](https://www.jetbrains.com/rider/)

### 安装步骤

#### 1. 克隆仓库

```bash
git clone https://github.com/PLAHorizon/HundunWorld.git
cd HundunWorld
```

#### 2. 配置数据库

```sql
-- 创建数据库
CREATE DATABASE Orleans;
CREATE DATABASE Basic;
CREATE DATABASE Game;

-- 应用Orleans集群迁移
-- (参见 Horizon.Entities/Migrations 目录)
```

#### 3. 配置Redis

```bash
# 启动Redis服务器
redis-server

# 或使用Docker
docker run -d -p 6379:6379 redis:latest
```

#### 4. 配置应用

```bash
# 从模板创建配置文件
cp Horizon.Orleans.Silo/appsettings.template.json Horizon.Orleans.Silo/appsettings.json
cp Horizon.Game.Gateway/appsettings.template.json Horizon.Game.Gateway/appsettings.json
cp Horizon.WebApi/appsettings.template.json Horizon.WebApi/appsettings.json

# 编辑配置文件，填入实际的连接字符串和密钥
# 详见 SECURITY_CONFIG_GUIDE.md
```

#### 5. 构建项目

```bash
# 恢复NuGet包
dotnet restore

# 构建解决方案
dotnet build

# 或使用Visual Studio的构建功能
```

#### 6. 运行迁移

```bash
cd Horizon.Entities
dotnet ef database update --context BasicEntityContext
dotnet ef database update --context GameEntityContext
```

#### 7. 启动服务

```bash
# 启动Orleans Silo
cd Horizon.Orleans.Silo
dotnet run

# 启动Gateway（新终端）
cd Horizon.Game.Gateway
dotnet run

# 启动WebApi（新终端）
cd Horizon.WebApi
dotnet run
```

#### 8. 运行客户端

使用Flax Engine打开 `HundunWorld/Game.flaxproj`，然后点击Play运行游戏。

## 📁 项目结构

```
HundunWorld/
├── Horizon.Orleans.Silo/          # Orleans服务器主机
│   ├── Program.cs                 # 入口点和服务注册
│   ├── SiloStartupExtension.cs    # Silo启动扩展配置
│   ├── Filters/                   # Grain调用过滤器
│   │   ├── CorrelationIdFilter.cs # 分布式追踪关联ID
│   │   ├── GrainExceptionFilter.cs # 统一异常处理
│   │   ├── GrainCallValidationFilter.cs # 参数验证
│   │   └── ClientConnectionTrackingFilter.cs # 连接跟踪
│   ├── Monitoring/                # 监控与可观测性
│   │   ├── HorizonMetrics.cs      # 自定义Silo指标
│   │   ├── OpenTelemetryExtensions.cs # OpenTelemetry配置
│   │   └── SeqLoggingProvider.cs  # Seq日志集成
│   └── Services/                  # 后台服务
├── Horizon.Game.Gateway/          # 游戏网关
│   ├── Program.cs                 # 入口点
│   ├── Network/                   # TCP网络服务
│   ├── Monitoring/                # 网关指标和追踪
│   └── Services/                  # 网关服务
├── Horizon.WebApi/                # Web API
│   └── Controllers/               # API控制器
├── Horizon.Orleans.Grains/        # Orleans Grains（26个实现）
│   ├── PassportGrain.cs           # 身份认证
│   ├── CharacterGrain.cs          # 角色管理
│   ├── CombatGrain.cs             # 战斗系统
│   ├── CombatCalculator.cs        # 战斗纯计算逻辑
│   ├── TeamGrain.cs               # 组队系统
│   ├── GuildGrain.cs              # 公会系统
│   ├── SocialGrain.cs             # 社交系统
│   ├── InventoryGrain.cs          # 背包系统
│   ├── SkillGrain.cs              # 技能系统
│   ├── CraftingGrain.cs           # 合成系统
│   ├── WuxingAlchemyGrain.cs      # 五行炼制系统
│   ├── QuestGrain.cs              # 任务系统
│   ├── DungeonGrain.cs            # 副本系统
│   ├── TradeGrain.cs              # 交易系统
│   ├── MarketGrain.cs             # 市场系统
│   ├── GameEventPublisher.cs      # 事件发布器
│   ├── GameEventConsumerGrain.cs  # 事件消费者
│   ├── SessionManager.cs          # Redis会话管理
│   └── ...                        # 更多Grain实现
├── Horizon.Orleans.Interface/     # Grain接口（26个接口，13个文件）
├── Horizon.Game.Message/          # 网络消息协议
│   ├── Network/                   # 消息定义（20个文件）
│   └── Enums/                     # 枚举定义（13个文件）
├── Horizon.Core/                  # 核心工具
│   ├── SecurePasswordHasher.cs    # PBKDF2密码哈希
│   └── PassportHelper.cs          # 认证辅助工具
├── Horizon.Entities/              # 数据实体
│   ├── Contexts/                  # 5个DbContext
│   └── Migrations/                # EF迁移
├── Horizon.Game.Gateway.Tests/    # 单元测试（836个用例，20个文件）
├── monitoring/                    # 监控部署配置
│   ├── prometheus/                # Prometheus配置和告警规则
│   ├── grafana/                   # Grafana仪表板
│   └── alertmanager/              # Alertmanager配置
├── HundunWorld/                   # Flax客户端（231个C#文件）
│   └── Source/Game/               # 游戏源码
│       ├── Character/             # 角色系统
│       ├── Combat/                # 战斗系统（含五行技能）
│       ├── ClimbingSystem/        # 攀爬系统
│       ├── ECS/                   # ECS框架（14组件+9系统）
│       ├── Network/               # 网络通信（16+消息处理器）
│       ├── UI/                    # UI系统（84+脚本）
│       ├── Rendering/             # 渲染系统（MetaHuman）
│       ├── Worlds/                # 世界管理
│       ├── Performance/           # 性能优化
│       └── Scene/                 # 场景管理
├── .github/                       # CI/CD配置
│   ├── workflows/ci.yml           # 构建和测试工作流
│   ├── workflows/codeql.yml       # CodeQL安全扫描
│   └── dependabot.yml             # 依赖自动更新
├── DEVELOPMENT_ROADMAP.md         # 开发路线图
├── ANALYSIS_REPORT.md             # 项目分析报告
├── MONITORING_GUIDE.md            # 监控部署指南
├── SECURITY_CONFIG_GUIDE.md       # 安全配置指南
├── PASSWORD_MIGRATION_GUIDE.md    # 密码迁移指南
└── README.md                      # 本文件
```

## ⚙️ 配置说明

### 环境变量

建议通过环境变量管理敏感配置：

```bash
# Redis
export REDIS_PASSWORD="your_redis_password"

# 数据库
export DB_BASIC_CONNECTION_STRING="..."
export DB_GAME_CONNECTION_STRING="..."

# Orleans
export ORLEANS_SQLSERVER_CONNECTION_STRING="..."
export ORLEANS_DASHBOARD_USERNAME="admin"
export ORLEANS_DASHBOARD_PASSWORD="secure_password"

# 云服务
export ALI_OSS_ACCESS_KEY_ID="..."
export ALI_OSS_ACCESS_KEY_SECRET="..."
export BAIDU_API_KEY="..."
export BAIDU_SECRET_KEY="..."
```

详细配置说明请参见 [SECURITY_CONFIG_GUIDE.md](SECURITY_CONFIG_GUIDE.md)

### Orleans集群配置

```json
{
  "ClusterOptions": {
    "ClusterId": "dev",
    "ServiceId": "BaseService"
  },
  "ClusteringSiloOptions": {
    "SqlServer": {
      "ConnectionString": "...",
      "Invariant": "System.Data.SqlClient"
    }
  }
}
```

## 👨‍💻 开发指南

### 代码风格

- 使用C# 10特性
- 遵循Microsoft C#编码规范
- 使用异步/等待模式
- 为公共API添加XML文档注释

### 添加新Grain

```csharp
// 1. 定义接口
public interface IMyGrain : IGrainWithStringKey
{
    Task<string> DoSomethingAsync(string input);
}

// 2. 实现Grain
public class MyGrain : Grain, IMyGrain
{
    private readonly ILogger<MyGrain> _logger;

    public MyGrain(ILogger<MyGrain> logger)
    {
        _logger = logger;
    }

    public async Task<string> DoSomethingAsync(string input)
    {
        _logger.LogInformation("Processing: {Input}", input);
        return await Task.FromResult($"Processed: {input}");
    }
}
```

### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定项目测试（含代码覆盖率）
dotnet test Horizon.Game.Gateway.Tests/ --collect:"XPlat Code Coverage"

# 运行测试并生成trx报告
dotnet test Horizon.Game.Gateway.Tests/ --logger "trx;LogFileName=test-results.trx"
```

### 技术栈版本

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 10.0 | 运行时 |
| Orleans | 9.2.1 | 分布式框架 |
| xUnit | 2.9.3 | 测试框架 |
| Moq | 4.20.72 | Mock框架 |
| MemoryPack | 1.21.4 | 序列化 |
| OpenTelemetry | 1.15.0 | 可观测性 |
| TouchSocket | 4.0.2 | TCP通信 |
| AutoMapper | 16.0.0 | 对象映射 |
| Polly | 8.6.4 | 弹性策略 |

## 🚢 部署指南

### Docker部署

```bash
# 构建镜像
docker build -t hundunworld/silo:latest -f Horizon.Orleans.Silo/Dockerfile .
docker build -t hundunworld/gateway:latest -f Horizon.Game.Gateway/Dockerfile .

# 运行容器
docker-compose up -d
```

### Kubernetes部署

```bash
# 应用配置
kubectl apply -f k8s/

# 检查状态
kubectl get pods -n hundunworld
```

## 🔒 安全说明

### 重要安全提醒

⚠️ **本项目已实施以下安全措施**：

1. ✅ **密码安全存储**: 使用PBKDF2 + HMACSHA512哈希（210,000迭代，符合OWASP标准）
2. ✅ **配置文件安全**: 模板文件替代硬编码凭证，环境变量管理敏感配置
3. ✅ **会话管理**: Redis持久化会话，24小时TTL过期机制
4. ✅ **登录保护**: 频率限制（5次/15分钟）
5. ✅ **审计日志**: 结构化日志记录所有认证操作
6. ✅ **分布式追踪**: CorrelationId关联ID跨服务追踪
7. ✅ **请求验证**: GrainCallValidationFilter统一参数校验
8. ✅ **异常处理**: GrainExceptionFilter统一异常捕获和指标上报
9. ✅ **依赖安全**: Dependabot自动扫描NuGet包和GitHub Actions漏洞
10. ✅ **代码安全**: CodeQL静态安全扫描（每周定时+PR触发）

### 安全最佳实践

- 定期更新依赖包
- 使用HTTPS/TLS加密通信
- 定期轮换密钥和密码
- 启用数据库审计
- 配置防火墙规则
- 定期备份数据

详见 [ANALYSIS_REPORT.md](ANALYSIS_REPORT.md) 的安全章节。

## 📚 文档

### 核心文档

- [DEVELOPMENT_ROADMAP.md](DEVELOPMENT_ROADMAP.md) - 后续开发路线图
- [ANALYSIS_REPORT.md](ANALYSIS_REPORT.md) - 完整的项目分析报告
- [MONITORING_GUIDE.md](MONITORING_GUIDE.md) - 监控系统部署指南
- [SECURITY_CONFIG_GUIDE.md](SECURITY_CONFIG_GUIDE.md) - 安全配置指南
- [PASSWORD_MIGRATION_GUIDE.md](PASSWORD_MIGRATION_GUIDE.md) - 密码系统迁移指南
- [TASK_STATUS_MONITORING.md](TASK_STATUS_MONITORING.md) - 任务状态监控系统说明

### Wiki文档

项目维护了详细的Wiki文档（位于 `.qoder/repowiki/`）：

- [项目概述](.qoder/repowiki/zh/content/项目概述.md)
- [核心架构](.qoder/repowiki/zh/content/核心架构/核心架构.md)
- [数据模型](.qoder/repowiki/zh/content/数据模型与ORM映射/数据模型与ORM映射.md)
- [身份认证](.qoder/repowiki/zh/content/身份认证服务/身份认证服务.md)
- [网关服务](.qoder/repowiki/zh/content/网关服务/网关服务.md)

## 🛠️ 故障排除

### 常见问题

**Q: Orleans集群无法连接**

A: 检查数据库连接字符串和防火墙规则，确保所有节点可以访问集群数据库。

**Q: Redis连接失败**

A: 验证Redis密码和端口配置，确保Redis服务正在运行。

**Q: 密码验证失败**

A: 如果是从旧系统迁移，确保按照 [PASSWORD_MIGRATION_GUIDE.md](PASSWORD_MIGRATION_GUIDE.md) 执行迁移步骤。

**Q: 客户端无法连接网关**

A: 检查网关IP地址和端口配置，确保客户端和网关在同一网络或网关端口已正确暴露。

## 📈 性能监控

### 监控体系

项目集成了完整的可观测性体系，详见 [MONITORING_GUIDE.md](MONITORING_GUIDE.md)：

- **OpenTelemetry**: Silo端（端口9464）和Gateway端（端口9465）指标导出
- **Prometheus**: 指标收集和告警规则（配置文件位于 `monitoring/prometheus/`）
- **Grafana**: 可视化仪表板（配置文件位于 `monitoring/grafana/`）
- **Alertmanager**: 告警通知（邮件/Webhook，配置文件位于 `monitoring/alertmanager/`）
- **Seq**: 结构化日志聚合（CLEF格式）
- **CorrelationId**: 分布式请求追踪

### Orleans Dashboard

访问 `http://localhost:1199` 查看Orleans集群状态（需要配置用户名密码）。

## 🤝 贡献指南

目前项目处于内部开发阶段，暂不接受外部贡献。

## 📝 版本历史

### v0.2.0 (2026-02-08)

- ✨ 完成服务端核心功能（26个Grain实现：战斗/社交/交易/任务/副本等）
- 🏗️ 事件驱动架构（Orleans Stream + GameEventPublisher + 22种事件类型）
- 📊 完整监控体系（OpenTelemetry + Prometheus + Grafana + Alertmanager + Seq）
- 🔄 Grain接口版本管理（26个接口版本标记 + 滚动升级支持）
- 🧪 836个单元测试用例（20个测试文件）
- 🔒 CI/CD安全扫描（CodeQL + Dependabot）
- 📝 分布式追踪（CorrelationId + 统一异常处理 + 参数验证）

### v0.1.0 (2026-02-07)

- ✨ 实施安全密码哈希系统
- 🔒 移除硬编码凭证
- 🐛 修复PassportGrain代码质量问题
- 📚 添加完整的文档和迁移指南

## 📧 联系方式

- 项目负责人: PLAHorizon
- 技术支持: [GitHub Issues](https://github.com/PLAHorizon/HundunWorld/issues)

## 📄 许可证

本项目为专有软件，保留所有权利。未经授权不得使用、复制或分发。

---

**最后更新**: 2026-02-08  
**项目状态**: 🚧 开发中  
**维护者**: PLAHorizon Team
