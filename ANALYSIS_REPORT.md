# HundunWorld / Horizon 源代码分析报告

> **分析日期**: 2026-02-07  
> **仓库**: PLAHorizon/HundunWorld  
> **分析范围**: 全部 13 个后端项目 + 1 个 Flax 客户端项目

---

## 目录

1. [项目概述](#1-项目概述)
2. [系统架构](#2-系统架构)
3. [技术栈分析](#3-技术栈分析)
4. [项目结构与依赖关系](#4-项目结构与依赖关系)
5. [核心模块分析](#5-核心模块分析)
6. [代码质量评估](#6-代码质量评估)
7. [安全审计](#7-安全审计)
8. [性能分析](#8-性能分析)
9. [未来开发建议](#9-未来开发建议)
10. [最佳实践路线图](#10-最佳实践路线图)

---

## 1. 项目概述

**HundunWorld（混沌世界）** 是一个大规模多人在线角色扮演游戏（MMORPG）的分布式服务端 + 客户端项目。后端基于 **Microsoft Orleans 虚拟演员框架**，客户端使用 **Flax Engine**，旨在支持百万级并发在线玩家。

### 核心目标
- 构建高可扩展性的分布式游戏服务器集群
- 实现完整的 MMORPG 游戏系统（角色、战斗、社交、经济等）
- 提供低延迟的网络通信网关
- 支持多平台游戏客户端

---

## 2. 系统架构

### 2.1 整体架构图

```
┌──────────────────────┐
│   Flax Game Client   │  ← HundunWorld 客户端 (Flax Engine + ECS)
│   (Arch ECS + C#)    │
└──────────┬───────────┘
           │ TCP/WebSocket/UDP
           ▼
┌──────────────────────┐
│    Game Gateway       │  ← 网络网关 (TouchSocket)
│  TCP:7789 / WS:8890  │     连接管理 / 消息路由 / 负载均衡
│  UDP:8889             │
└──────────┬───────────┘
           │ Orleans RPC
           ▼
┌──────────────────────┐
│   Orleans Silo       │  ← 业务逻辑集群 (Virtual Actor Model)
│  Cluster (Grains)    │     PassportGrain / CharacterGrain / GameGrain
│  Dashboard:1199      │
└──────────┬───────────┘
           │ ADO.NET / Redis
           ▼
┌──────────────────────┐     ┌──────────────────┐
│   SQL Server (EF)    │     │   Redis Cluster   │
│ 5 Database Contexts  │     │ Master/Slave/     │
│                      │     │ Sentinel          │
└──────────────────────┘     └──────────────────┘

┌──────────────────────┐
│    Web API           │  ← 管理后台 REST API
│  (ASP.NET Core)      │     Swagger / IdentityServer4
└──────────────────────┘
```

### 2.2 架构模式

| 模式 | 说明 |
|------|------|
| **Virtual Actor Model** | 基于 Orleans Grain 的虚拟演员模型，每个角色/账号为独立 Grain |
| **Gateway Pattern** | 游戏网关负责连接管理、协议转换和消息路由 |
| **ECS (Entity Component System)** | 客户端采用 Arch ECS 框架实现游戏逻辑解耦 |
| **Repository Pattern** | 数据访问层通过 `IDataContext` 抽象 |
| **CQRS 雏形** | 读写分离通过 Redis 缓存 + 数据库持久化初步实现 |

---

## 3. 技术栈分析

### 3.1 后端技术栈

| 组件 | 技术 | 版本 | 评估 |
|------|------|------|------|
| 运行时 | .NET | 10.0 | ✅ 前沿版本 |
| 分布式框架 | Microsoft Orleans | 9.2.1 | ✅ 成熟稳定 |
| 序列化 | MemoryPack | 1.21.4 | ✅ 高性能零分配 |
| 网络库 | TouchSocket | 4.0.2 | ✅ 国产高性能网络库 |
| ORM | Entity Framework Core | 9.0.10 | ✅ 标准选择 |
| 缓存 | StackExchange.Redis | 9.0.10 | ✅ 行业标准 |
| 对象映射 | AutoMapper | 15.1.0 | ⚠️ 建议评估 Mapperly |
| 压缩 | K4os.Compression.LZ4 | 1.3.8 | ✅ 高性能压缩 |
| 文档数据库 | MongoDB.Driver | 2.24.0 | ✅ 灵活数据存储 |
| API 文档 | Swagger/Swashbuckle | 9.0.4 | ✅ 标准 API 文档 |

### 3.2 客户端技术栈

| 组件 | 技术 | 版本 |
|------|------|------|
| 游戏引擎 | Flax Engine | 1.10 |
| ECS 框架 | Arch ECS | - |
| 网络通信 | SmartGatewayConnector | 自研 |
| 序列化 | MemoryPack | 与服务端一致 |

### 3.3 技术栈评估

**优势：**
- 全 C# 技术栈，前后端语言统一，减少上下文切换
- Orleans 框架天然支持水平扩展
- MemoryPack 零分配序列化性能优异
- ECS 架构在客户端保证高性能游戏逻辑

**风险：**
- .NET 10.0 为预览版本，生产稳定性需验证
- Flax Engine 社区相对较小，遇到问题时资源有限
- 多数据库上下文（5 个）增加了运维复杂度

---

## 4. 项目结构与依赖关系

### 4.1 解决方案结构

```
Horizon.sln (后端)
├── 基础设施层
│   ├── Horizon.Core.Abstract     ← 接口 & 抽象定义
│   ├── Horizon.Core              ← 核心工具、配置、助手类
│   ├── Horizon.Entities          ← EF Core 实体模型 (5 个数据库上下文)
│   └── Horizon.Model             ← 数据模型 (GameModel, AI, Article 等)
│
├── Orleans 分布式层
│   ├── Horizon.Orleans.Interface ← Grain 接口契约
│   ├── Horizon.Orleans.Grains    ← Grain 实现（核心业务逻辑）
│   └── Horizon.Orleans.Silo      ← Silo 宿主（服务端入口）
│
├── 游戏服务层
│   ├── Horizon.Game.Gateway      ← TCP/UDP 游戏网关（网关入口）
│   ├── Horizon.Game.Core         ← 核心游戏逻辑、消息处理
│   ├── Horizon.Game.Message      ← 网络消息定义
│   └── Horizon.Game.ECS          ← Arch ECS 框架集成
│
├── 支撑层
│   ├── Horizon.Share             ← 共享 DTO & 工具
│   ├── Horizon.Mapper            ← AutoMapper 配置
│   ├── Horizon.Strategy.Storage.Redis ← Redis 缓存策略
│   └── Horizon.WebApi            ← Web API 管理接口
│
HundunWorld.sln (客户端)
└── Game.csproj                   ← Flax Engine 游戏客户端
```

### 4.2 依赖关系图

```
Horizon.Core.Abstract (基础接口层)
    │
    ├──→ Horizon.Core
    ├──→ Horizon.Entities
    ├──→ Horizon.Model
    ├──→ Horizon.Share
    └──→ 所有上层项目
    
Horizon.Game.Message (消息协议层)
    │
    ├──→ Horizon.Game.Core
    ├──→ Horizon.Orleans.Interface
    ├──→ Horizon.Orleans.Grains
    └──→ Horizon.Game.Gateway

Horizon.Orleans.Interface (Grain 接口层)
    │
    └──→ Horizon.Orleans.Grains
          │
          └──→ Horizon.Orleans.Silo (入口)
                ├──→ Horizon.Game.Gateway (入口)
                └──→ Horizon.WebApi (入口)
```

---

## 5. 核心模块分析

### 5.1 Orleans Grain 系统

#### PassportGrain（账号系统）
- **状态**: 无状态 Grain (`IGrainWithGuidKey`)
- **功能**: 用户注册、登录、登出、密码修改
- **安全机制**: 登录尝试限流（15 分钟内 5 次）
- **会话管理**: Redis Token 存储

#### CharacterGrain（角色系统）
- **状态**: 有状态 Grain (`[PersistentState("character", "GameStore")]`)
- **功能覆盖**:
  - 🗡️ 战斗系统（攻击、技能、防御、死亡、复活）
  - 🎒 背包系统（物品、装备、制作）
  - 👥 社交系统（门派、帮会、组队、师徒、决斗）
  - 📜 任务系统（主线、支线、日常）
  - ⭐ 声望 & 侠义值系统

#### GameGrain（游戏信息）
- **状态**: 无状态 Grain
- **功能**: 服务器列表查询、游戏元数据

### 5.2 消息协议系统

```csharp
// 消息类型分段设计
1-99:     认证消息 (登录/注册/登出)
100-299:  战斗消息 (攻击/技能/防御/死亡)
300-499:  社交消息 (聊天/组队/帮会/师徒)
500-699:  物品消息 (背包/装备/交易/制作)
700-899:  任务消息 (接取/完成/奖励)
900-999:  系统消息 (公告/维护/更新)
```

- 使用 `MessageUnion` 基类 + MemoryPack 多态序列化
- 消息数量超过 1000+ 类型
- 各消息按功能域分类，结构清晰

### 5.3 游戏网关 (Game Gateway)

- **连接管理**: 支持 TCP (7789)、WebSocket (8890)、UDP (8889)
- **负载均衡**: `ILoadBalancer` 接口，支持网关选择
- **会话管理**: `ISessionManager` 管理玩家会话
- **性能监控**: 内建 CPU、内存、网络、消息速率统计
- **状态机**: 线程安全的网关状态转换 (Starting → Running → Stopping → Stopped)

### 5.4 客户端 ECS 架构

```
Components (数据):
├── PositionComponent    ← 位置数据
├── VelocityComponent    ← 速度数据
├── HealthComponent      ← 生命值
├── CameraComponent      ← 相机参数
├── CharacterController  ← 角色控制器
└── InputComponent       ← 输入状态

Systems (逻辑):
├── MovementSystem        ← 移动计算
├── RenderingSystem       ← 渲染更新
├── HealthSystem          ← 生命值管理
├── CameraSystem          ← 第三人称相机
├── CharacterControlSystem← 角色控制
└── InputSystem           ← 输入处理
```

### 5.5 相机系统

第三人称相机系统设计参考了剑网三和魔兽世界：
- 右键旋转视角
- 鼠标滚轮缩放
- 默认 45° 俯视视角
- 碰撞检测与震屏效果

---

## 6. 代码质量评估

### 6.1 代码规范

| 维度 | 评分 | 说明 |
|------|------|------|
| 命名规范 | ⭐⭐⭐⭐ | Handler、Service、Grain、I* 接口命名一致 |
| 代码结构 | ⭐⭐⭐⭐ | 分层清晰，职责分离合理 |
| 异步模式 | ⭐⭐⭐ | 大部分正确，存在 fire-and-forget 问题 |
| 错误处理 | ⭐⭐ | 存在空 catch 块和异常吞没 |
| 测试覆盖 | ⭐ | 几乎无测试代码 |
| 文档注释 | ⭐⭐ | 少量 XML 注释，缺少架构文档 |
| 日志记录 | ⭐⭐⭐⭐ | 结构化日志使用良好 (ILogger\<T\>) |

### 6.2 代码问题清单

#### 🔴 严重问题

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| 1 | 空异常捕获块 | PassportGrain:288, 402 | 错误被静默吞没，难以排查 |
| 2 | `goto` 语句使用 | PassportGrain:235, 236, 250, 295 | 降低代码可读性和可维护性 |
| 3 | `TimeSpan.Milliseconds` 误用 | PassportGrain:487 | 应使用 `TotalMilliseconds` |
| 4 | Fire-and-forget Task | SiloStartupExtension:70 | 异步初始化无法保证完成 |

#### 🟡 中等问题

| # | 问题 | 位置 | 影响 |
|---|------|------|------|
| 5 | 空 AutoMapper Profile | BasicProfile.cs | 未定义映射规则，死代码 |
| 6 | CPU 使用率计算缺陷 | GatewayService:263 | 监控数据可能不准确 |
| 7 | Console.WriteLine 代替日志 | PassportGrain:318 | 绕过结构化日志系统 |
| 8 | 注释掉的代码未清理 | PassportGrain:107, CharacterHandler:56-57 | 影响代码整洁度 |
| 9 | 反射注册影响启动性能 | SiloStartupExtension:33-59 | 大量反射调用拖慢启动 |

#### 🟢 建议改进

| # | 问题 | 建议 |
|---|------|------|
| 10 | 无 API 版本控制 | 统一使用 Asp.Versioning |
| 11 | 无熔断器模式 | 引入 Polly 熔断器 |
| 12 | 无输入验证框架 | 引入 FluentValidation |
| 13 | 配置类无验证 | 使用 IOptionsMonitor + 验证 |

### 6.3 代码示例修复建议

#### 修复空 catch 块

```csharp
// ❌ 当前代码 (PassportGrain)
catch (Exception e) { }

// ✅ 建议修改
catch (Exception e)
{
    _logger.LogError(e, "注册过程中发生异常，用户: {UserId}", userId);
    throw; // 或返回错误响应
}
```

#### 修复 goto 语句

```csharp
// ❌ 当前代码
goto RetryLogin;

// ✅ 建议修改 - 使用循环 + 状态机
while (retryCount < maxRetries)
{
    var result = await TryLoginAsync(credentials);
    if (result.Success) return result;
    retryCount++;
}
```

#### 修复 TimeSpan 误用

```csharp
// ❌ 当前代码
TimeSpan.FromMinutes(lockoutMinutes).Milliseconds  // 返回 0-999 毫秒部分

// ✅ 建议修改
TimeSpan.FromMinutes(lockoutMinutes).TotalMilliseconds  // 返回总毫秒数
```

---

## 7. 安全审计

### 7.1 🔴 高危问题

#### 7.1.1 明文凭据暴露

**位置**: `appsettings.json` 多处  
**问题**: Redis 密码、数据库连接字符串、第三方 API 密钥直接写在配置文件中

```json
// ❌ 当前配置 (示例)
"RedisOption": {
    "Masters": [{ "Password": "xxx" }],
    "Slaves": [{ "Password": "xxx" }]
}
```

**建议**: 
- 开发环境: 使用 `dotnet user-secrets`
- 生产环境: 使用 Azure Key Vault / HashiCorp Vault
- CI/CD: 使用环境变量注入

#### 7.1.2 密码安全问题

**位置**: PassportGrain  
**问题**: 
- 密码哈希验证代码被注释 (line 107)
- 存在明文密码比较逻辑
- 密码作为 salt 存储 (lines 278-279)

**建议**:
- 使用 PBKDF2 / bcrypt / Argon2 进行密码哈希
- 生成独立随机 salt
- 永远不存储明文密码

#### 7.1.3 硬编码 IP 地址

**位置**: appsettings.json  
**问题**: 内网 IP `192.168.1.78` 硬编码在配置中

**建议**: 使用 DNS 名称或环境变量替代

### 7.2 🟡 中等风险

| # | 问题 | 影响 | 建议 |
|---|------|------|------|
| 1 | Orleans Dashboard 凭据暴露 | 管理面板未加强保护 | 使用独立认证 + IP 白名单 |
| 2 | 无请求速率限制 (Web API) | DDoS 风险 | 引入 ASP.NET Rate Limiting |
| 3 | 无 CORS 策略 | 跨域请求风险 | 配置严格 CORS 策略 |
| 4 | 缺少输入验证 | 注入攻击风险 | 全面输入验证 |

### 7.3 🟢 低风险

| # | 问题 | 建议 |
|---|------|------|
| 1 | 日志中可能包含敏感数据 | 实现日志脱敏过滤器 |
| 2 | 无审计日志 | 添加关键操作审计跟踪 |
| 3 | 无 HTTPS 强制 | 生产环境强制 HTTPS |

---

## 8. 性能分析

### 8.1 优势

- ✅ MemoryPack 零分配序列化，网络传输高效
- ✅ Orleans Grain 生命周期管理自动释放资源
- ✅ Redis 主从 + 哨兵模式保证缓存高可用
- ✅ LZ4 压缩减少网络带宽
- ✅ 数据库连接池 (Max Pool Size: 200)

### 8.2 潜在瓶颈

| 瓶颈 | 位置 | 影响 | 建议 |
|------|------|------|------|
| CharacterGrain 加载全量状态 | CharacterGrain | 大对象 GC 压力 | 实现延迟加载 + 分片存储 |
| 反射注册启动开销 | SiloStartupExtension | 延长启动时间 | 编译时代码生成 |
| 多数据库上下文并发 | Entities 层 | 连接池竞争 | 读写分离 + 连接池隔离 |
| 无 Grain 去激活策略 | Orleans 配置 | 内存持续增长 | 配置 `CollectionAge` |
| 网关 while + Task.Delay(10) | Gateway Program.cs | CPU 空转 | 使用信号量 / 事件驱动 |

### 8.3 扩展能力评估

```
当前架构扩展路径:

单 Silo → 多 Silo 集群 (水平扩展)
         ├── 同一 ClusterId + 同一数据库
         ├── 自动 Grain 迁移与负载均衡
         └── 新增节点零停机

单网关 → 多网关 (水平扩展)
         ├── 前置负载均衡器 (Nginx/HAProxy)
         ├── 无状态设计支持任意扩展
         └── 客户端延迟感知选择

数据库 → 读写分离 → 分库分表
         ├── Redis 缓存层减少读压力
         ├── EF Core 支持读写分离
         └── 按角色 ID 分片
```

---

## 9. 未来开发建议

### 9.1 短期优先事项（1-3 个月）

#### P0 - 立即修复

- [ ] **安全加固**: 移除所有硬编码凭据，迁移到密钥管理系统
- [ ] **密码安全**: 实现 PBKDF2/bcrypt 密码哈希，替换明文存储
- [ ] **异常处理**: 修复所有空 catch 块，添加适当的日志和错误传播
- [ ] **代码清理**: 移除 `goto` 语句，重构为循环/状态机模式
- [ ] **Bug 修复**: 修正 `TimeSpan.Milliseconds` → `TotalMilliseconds`

#### P1 - 基础建设

- [ ] **单元测试**: 为 Grain 逻辑建立 xUnit 测试项目，目标覆盖率 60%+
- [ ] **集成测试**: 使用 Orleans TestCluster 进行 Grain 集成测试
- [ ] **CI/CD 流水线**: 配置 GitHub Actions (构建 → 测试 → 代码分析 → 部署)
- [ ] **代码分析**: 集成 SonarQube / Roslyn 分析器，强制代码质量门禁
- [ ] **日志增强**: 引入 Serilog 结构化日志 + Seq/ELK 日志聚合

### 9.2 中期目标（3-6 个月）

#### P2 - 架构优化

- [ ] **CQRS 实现**: 读写分离，查询走 Redis/只读库，写入走主库
- [ ] **事件溯源**: 关键业务（交易、战斗结算）实现事件溯源
- [ ] **熔断器模式**: 引入 Polly 实现熔断、重试、超时策略
- [ ] **API 网关**: 引入 YARP/Ocelot 统一 API 路由
- [ ] **输入验证**: 引入 FluentValidation 进行全面参数验证
- [ ] **性能监控**: 引入 OpenTelemetry 分布式追踪
- [ ] **Grain 状态分片**: CharacterGrain 状态按功能域分离（背包、技能、任务）

#### P3 - 功能增强

- [ ] **热更新系统**: 实现配置和游戏数据的热更新机制
- [ ] **GM 工具**: 完善 Web API 管理后台，支持在线运维
- [ ] **公告系统**: 实现游戏内公告推送
- [ ] **邮件系统**: 游戏内信件 & 物品附件
- [ ] **拍卖行系统**: 玩家间物品交易

### 9.3 长期规划（6-12 个月）

#### P4 - 高级特性

- [ ] **微服务化**: 将独立功能域拆分为独立部署的微服务
- [ ] **跨服系统**: 跨服战场、跨服组队
- [ ] **AI 系统增强**: 集成 NPC 行为树 + LLM 对话
- [ ] **数据分析**: 构建游戏数据仓库 + 数据分析平台
- [ ] **多平台支持**: 移动端客户端适配
- [ ] **国际化**: i18n 多语言支持

---

## 10. 最佳实践路线图

### 10.1 推荐项目结构调整

```
Horizon/
├── src/
│   ├── Core/                    ← 核心层 (不依赖任何外部框架)
│   │   ├── Horizon.Domain          ← 领域模型 & 业务规则
│   │   ├── Horizon.Application     ← 应用服务 & 用例
│   │   └── Horizon.SharedKernel    ← 共享内核 (基类、接口)
│   │
│   ├── Infrastructure/          ← 基础设施层
│   │   ├── Horizon.Persistence     ← EF Core 数据访问
│   │   ├── Horizon.Caching         ← Redis 缓存
│   │   ├── Horizon.Messaging       ← 消息队列集成
│   │   └── Horizon.Identity        ← 认证授权
│   │
│   ├── Orleans/                 ← Orleans 分布式层
│   │   ├── Horizon.Orleans.Abstractions ← Grain 接口
│   │   ├── Horizon.Orleans.Grains       ← Grain 实现
│   │   └── Horizon.Orleans.Silo         ← Silo 宿主
│   │
│   ├── Gateway/                 ← 网关层
│   │   ├── Horizon.Gateway.Core    ← 网关核心逻辑
│   │   └── Horizon.Gateway.Host    ← 网关宿主
│   │
│   └── Presentation/           ← 展示层
│       ├── Horizon.WebApi          ← REST API
│       └── Horizon.AdminUI         ← 管理后台
│
├── tests/
│   ├── Horizon.UnitTests           ← 单元测试
│   ├── Horizon.IntegrationTests    ← 集成测试
│   ├── Horizon.Orleans.Tests       ← Grain 测试
│   └── Horizon.LoadTests           ← 压力测试
│
├── client/
│   └── HundunWorld/                ← 游戏客户端
│
├── docs/                        ← 文档
│   ├── architecture/
│   ├── api/
│   └── deployment/
│
└── tools/                       ← 工具脚本
    ├── scripts/
    └── generators/
```

### 10.2 开发规范建议

#### 代码规范
```
1. 使用 .editorconfig 统一代码格式
2. 启用 nullable reference types (#nullable enable)
3. 使用 primary constructors 简化 DI 注入
4. 异步方法必须以 Async 结尾
5. 使用 record 类型定义不可变消息
6. 禁止使用 goto 语句
7. 异常处理必须记录日志
8. 配置类必须实现验证
```

#### Git 工作流
```
main ← 稳定发布分支
  └── develop ← 开发主分支
        ├── feature/* ← 功能分支
        ├── fix/* ← 修复分支
        └── release/* ← 发布准备分支
```

#### CI/CD 流水线建议

```yaml
# .github/workflows/ci.yml
stages:
  - build:        dotnet build
  - test:         dotnet test (单元 + 集成)
  - analyze:      SonarQube 代码分析
  - security:     依赖漏洞扫描
  - package:      Docker 镜像构建
  - deploy-dev:   开发环境自动部署
  - deploy-stage: 预发布环境 (手动审批)
  - deploy-prod:  生产环境 (手动审批 + 灰度)
```

### 10.3 监控与运维建议

```
┌─────────────┐    ┌──────────────┐    ┌──────────────┐
│ Application │───→│ OpenTelemetry│───→│  Grafana     │
│   Metrics   │    │   Collector  │    │  Dashboard   │
└─────────────┘    └──────────────┘    └──────────────┘
                          │
                   ┌──────┴──────┐
                   │             │
              ┌────▼────┐  ┌────▼────┐
              │Prometheus│  │  Jaeger  │
              │ (指标)   │  │ (追踪)  │
              └─────────┘  └─────────┘

推荐监控指标:
├── 游戏指标: 在线人数、消息吞吐量、延迟分位数
├── 系统指标: CPU/内存/磁盘/网络
├── Orleans 指标: Grain 激活数、消息队列长度、Silo 健康
├── 数据库指标: 查询延迟、连接池使用率、慢查询
└── 业务指标: 注册量、DAU、付费转化率
```

### 10.4 数据库优化建议

```
短期:
├── 添加必要索引 (角色名、账号名唯一索引)
├── 配置 EF Core 查询过滤器
├── 启用查询日志监控慢查询
└── 配置连接池超时与重试

中期:
├── 实现读写分离 (主库写、从库读)
├── 引入缓存失效策略 (Cache-Aside Pattern)
├── 配置数据库监控报警
└── 定期备份与恢复演练

长期:
├── 按服务器 ID 分库
├── 历史数据归档策略
├── 冷热数据分离
└── 引入时序数据库存储游戏事件
```

---

## 附录：核心文件索引

| 文件 | 路径 | 说明 |
|------|------|------|
| 解决方案 | `Horizon.sln` | 后端解决方案入口 |
| Silo 入口 | `Horizon.Orleans.Silo/Program.cs` | 服务端启动入口 |
| 网关入口 | `Horizon.Game.Gateway/Program.cs` | 游戏网关启动入口 |
| Web API 入口 | `Horizon.WebApi/Program.cs` | API 服务启动入口 |
| 角色 Grain | `Horizon.Orleans.Grains/CharacterGrain.cs` | 角色业务核心 |
| 账号 Grain | `Horizon.Orleans.Grains/PassportGrain.cs` | 账号认证核心 |
| 消息定义 | `Horizon.Game.Message/` | 网络消息协议 |
| 实体模型 | `Horizon.Entities/` | 数据库实体定义 |
| 客户端入口 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 游戏客户端入口 |
| 相机系统 | `HundunWorld/Source/Game/ThirdPersonCamera.cs` | 第三人称相机 |

---

> **总结**: HundunWorld 项目架构设计合理，技术选型前沿，具备支撑大规模 MMORPG 的技术基础。当前处于早期开发阶段，需优先解决安全问题和代码质量问题，建立完善的测试和 CI/CD 体系，为后续功能开发奠定坚实基础。

