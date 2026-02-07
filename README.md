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
- **安全认证**: PBKDF2密码哈希，JWT令牌，会话管理
- **渐进式世界观**: 武侠(1-50级) → 仙侠(51-150级) → 玄幻(151-300级)
- **五行战斗系统**: 木火土金水相生相克的策略战斗

### 系统组件

```
混沌世界
├── Horizon.Orleans.Silo      # Orleans分布式服务器
├── Horizon.Game.Gateway       # 游戏网关服务
├── Horizon.WebApi             # RESTful API服务
├── Horizon.Orleans.Grains     # 业务逻辑Grains
├── Horizon.Core               # 核心工具库
├── Horizon.Entities           # 数据实体和EF Core
├── Horizon.Model              # 数据传输对象
└── HundunWorld                # Flax Engine客户端
```

## 🏗️ 技术架构

### 后端架构

```
┌─────────────┐    ┌─────────────┐
│ Web Client  │    │ Game Client │
└──────┬──────┘    └──────┬──────┘
       │                  │
       ▼                  ▼
┌─────────────┐    ┌─────────────┐
│   WebApi    │    │   Gateway   │
└──────┬──────┘    └──────┬──────┘
       │                  │
       └────────┬─────────┘
                ▼
        ┌──────────────┐
        │Orleans Cluster│
        │   (Grains)    │
        └───────┬───────┘
                │
      ┌─────────┴─────────┐
      ▼                   ▼
┌───────────┐      ┌──────────┐
│ SQL Server│      │  Redis   │
└───────────┘      └──────────┘
```

### 客户端架构

```
HundunWorld (Flax Engine)
├── ECS System (Arch)
│   ├── Components
│   ├── Systems
│   └── Entities
├── Network Module
│   ├── Gateway Selector
│   ├── Message Handler
│   └── Reconnect Logic
├── Game Systems
│   ├── Camera System
│   ├── Combat System
│   ├── Skill System
│   └── Movement System
└── UI System
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
│   ├── Program.cs                 # 入口点
│   ├── Services/                  # 后台服务
│   └── appsettings.json           # 配置文件
├── Horizon.Game.Gateway/          # 游戏网关
│   ├── Program.cs
│   ├── MessageHandlers/           # 消息处理器
│   └── appsettings.json
├── Horizon.WebApi/                # Web API
│   ├── Controllers/               # API控制器
│   └── Startup.cs
├── Horizon.Orleans.Grains/        # Orleans Grains
│   ├── PassportGrain.cs           # 身份认证
│   ├── CharacterGrain.cs          # 角色管理
│   └── ...
├── Horizon.Orleans.Interface/     # Grain接口
├── Horizon.Core/                  # 核心工具
│   ├── SecurePasswordHasher.cs    # 密码哈希
│   └── PassportHelper.cs
├── Horizon.Entities/              # 数据实体
│   ├── Contexts/                  # DbContext
│   └── Migrations/                # EF迁移
├── Horizon.Model/                 # 数据模型
├── HundunWorld/                   # Flax客户端
│   ├── Source/                    # C#源码
│   │   ├── Game/                  # 游戏逻辑
│   │   ├── Network/               # 网络模块
│   │   └── UI/                    # UI系统
│   └── Content/                   # 游戏资源
├── ANALYSIS_REPORT.md             # 项目分析报告
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

# 运行特定项目测试
dotnet test Horizon.Game.Gateway.Tests/
```

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

1. ✅ **密码安全存储**: 使用PBKDF2 + HMACSHA512哈希
2. ✅ **配置文件安全**: 模板文件替代硬编码凭证
3. ✅ **会话管理**: JWT令牌和会话超时
4. ✅ **登录保护**: 频率限制（5次/15分钟）
5. ✅ **审计日志**: 记录所有认证操作

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

- [ANALYSIS_REPORT.md](ANALYSIS_REPORT.md) - 完整的项目分析报告
- [SECURITY_CONFIG_GUIDE.md](SECURITY_CONFIG_GUIDE.md) - 安全配置指南
- [PASSWORD_MIGRATION_GUIDE.md](PASSWORD_MIGRATION_GUIDE.md) - 密码系统迁移指南

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

### Orleans Dashboard

访问 `http://localhost:1199` 查看Orleans集群状态（需要配置用户名密码）。

### 指标监控

项目支持Prometheus指标导出，可配合Grafana使用：

```bash
# 启用Prometheus导出
# 在appsettings.json中配置
```

## 🤝 贡献指南

目前项目处于内部开发阶段，暂不接受外部贡献。

## 📝 版本历史

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

**最后更新**: 2026-02-07  
**项目状态**: 🚧 开发中  
**维护者**: PLAHorizon Team
