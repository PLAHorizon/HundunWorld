# 架构总览 · ARCHITECTURE

> **最后更新**：2026-06-15 · 配套文档：[NETCODE](./NETCODE.md) · [SERVER](./SERVER.md) · [CLIENT](./CLIENT.md)

---

## 1. 执行摘要

HundunWorld（产品名"混沌世界"）是武侠题材分布式 MMORPG，采用**双客户端 + Orleans 微服务集群**架构：

- **游戏客户端**：基于 **Flax Engine 1.12**（C# 引擎），叠加自研 **Arch ECS** 同步层
- **桌面启动器**：**Avalonia 11.3.12**（"耕地"游戏中心），负责启动与凭证注入
- **服务端**：**.NET 10 + Orleans 10.0.1** Actor 集群，拆为 Silo / Gateway / WebApi / IM Gateway 四类宿主
- **Netcode**：**客户端预测 + 服务端权威 + 快照插值**（CSP + Snapshot Interpolation 混合）
- **统一运行时**：全栈 `.NET 10`（仅安装器用 net48），统一序列化（MemoryPack），统一传输（TouchSocket TCP）

---

## 2. 顶层架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│  客户端层                                                                │
│  ┌────────────────────────────┐      ┌──────────────────────────────┐  │
│  │ Flax Engine 1.12           │      │ Avalonia 11.3 (耕地启动器)    │  │
│  │ ├─ 自研 ECSManager         │      │ ├─ LiteDB 本地存储            │  │
│  │ ├─ Arch ECS (网络同步层)   │      │ └─ 写入 HorizonGame.ini 凭证  │  │
│  │ ├─ TouchSocket TCP 客户端  │      └──────────────────────────────┘  │
│  │ ├─ Combat (五行技能)       │                                        │
│  │ ├─ Character (轻功)        │                                        │
│  │ └─ TraeBridge (编辑器AI桥) │                                        │
│  └────────────┬───────────────┘                                        │
└───────────────┼─────────────────────────────────────────────────────────┘
                │ TCP 7789 / UDP 8889 / WS 8890
                │ 帧格式: [8B 头][MemoryPack][LZ4>256B]
                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Horizon.Game.Gateway（游戏网关，可水平扩展）                           │
│  ├─ TouchSocket 监听 + IConnectionManager + ISessionRegistry           │
│  ├─ SyncPacketHandler（双通道入口：RPC + Sync 帧）                      │
│  ├─ SyncDispatcherHostedService（1/60s fanout 循环分发）                │
│  ├─ CharacterFingerprintService（Redis 分布式锁，防多开）               │
│  └─ OpenTelemetry 链路追踪                                             │
└────────────────┬────────────────────────────────────────────────────────┘
                 │ Orleans Client（AdoNet clustering via SQL Server）
                 ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Horizon.Orleans.Silo 集群（虚拟 Actor，可水平扩展）                    │
│  ├─ ZoneShardGrain    空间权威模拟（1/60s Tick + AOI + MovementValidator)│
│  ├─ PlayerSessionGrain 瞬态会话 / 输入去重 / 重连决策                   │
│  ├─ CharacterGrain    [PersistentState] RPG 玩法（937 行）              │
│  ├─ 100+ Grain：花卉电商 / IM / 支付 / 跨服 / 副本 / 排行 / 社交       │
│  └─ Orleans Dashboard：http://192.168.1.78:1199                         │
└────────────────┬────────────────────────────────────────────────────────┘
                 │
        ┌────────┴─────────┬──────────────┬──────────────┐
        ▼                  ▼              ▼              ▼
   SQL Server        Redis 哨兵      MongoDB       OSS/百度/讯飞
   Orleans+Game      会话/锁/缓存    文档存储      存储/AI/TTS
   Basic             9379/9679/      (Grains)      (Configs)
                      9779/9879
```

---

## 3. 技术栈链路矩阵

### 3.1 引擎与框架

| 层 | 技术 | 版本 | 验证来源 |
|----|------|------|---------|
| 游戏引擎 | **Flax Engine** | 1.12 | `Game.csproj:35` DefineConstants `FLAX_1_12_OR_NEWER`，HintPath 指向 `Flax\Flax_1.12\` |
| 桌面 UI | **Avalonia** | 11.3.12 | `Horizon.Game.GengDi.csproj` |
| Actor 框架 | **Microsoft Orleans** | **10.0.1** | `Horizon.Orleans.Grains.csproj:16-22`（全部 Orleans 包统一） |
| ECS | **Arch** | 2.0.0-beta | `Horizon.Game.ECS.Arch.csproj:15` |
| ECS 系统 | **Arch.System** | 1.1.0 | `Horizon.Game.ECS.Arch.csproj:16` |
| 网络传输 | **TouchSocket** | 4.1.1 | `Horizon.Game.Message.csproj:37-38` |

### 3.2 序列化与压缩

| 技术 | 版本 | 用途 |
|------|------|------|
| **MemoryPack.Core + Generator** | 1.21.4 | 主序列化（消息、SyncPacket、组件） |
| Orleans.Serialization | 10.0.1 | Grain 内建序列化 |
| K4os.Compression.LZ4 | 1.3.8 | >256B 才压缩，超低延迟 |
| Newtonsoft.Json | 13.0.4 | 遗留（Core/Model/Redis/WebApi） |
| protobuf-net | 3.2.56 | 遗留（Model） |
| FlatSharp.Runtime | 7.9.0 | 遗留（Core） |

### 3.3 数据与存储

| 技术 | 版本 | 用途 |
|------|------|------|
| Microsoft.EntityFrameworkCore.* | 10.0.2 | ORM（迁移管理） |
| Dapper | 2.1.66 | ORM（高频查询） |
| Microsoft.Data.SqlClient | 6.1.4 | SQL Server 驱动 |
| StackExchange.Redis | 2.10.14 | Redis 客户端（Grains） |
| CSRedisCore | 3.8.807 | Redis 客户端（Silo） |
| MongoDB.Driver | 3.6.0 | 文档存储（Grains） |
| LiteDB | 5.0.21 | 客户端本地嵌入式 DB |

### 3.4 平台能力

| 技术 | 版本 | 用途 |
|------|------|------|
| Microsoft.SemanticKernel | 1.57.0 | AI 内核（NPC / 对话） |
| MQTTnet | 4.3.7.1207 | IoT / 推送 |
| OpenTelemetry.* | 1.15.0 | 可观测性（Silo + Gateway） |
| Polly | 8.6.5 | 弹性重试 |
| AutoMapper | 16.0.0 | 对象映射 |
| MediatR | 14.0.0 | 中介者（Mapper） |
| IdentityServer4 | 4.1.2 | WebApi 鉴权 |
| Swashbuckle.AspNetCore | 6.9.0 | Swagger |
| AntDesign + ProLayout | 1.5.1 / 1.4.0 | WebAdmin Blazor |
| NBomber | 6.1.0 | 压测（PerformanceTests） |
| MiniExcel | 1.33.0 | Excel 导入导出 |

### 3.5 运行时

- **目标框架**：全栈 **`net10.0`**（仅 `Horizon.Game.GengDi.Installer` 用 `net48`）
- **SDK**：10.0.300（`HundunWorld/global.json` 锁定，`rollForward: latestPatch`）
- **Flax 客户端**：`net10.0` + `LangVersion=14.0`，`OutputType=Library`（构建为 `Game.CSharp.dll`）

---

## 4. 项目分层与依赖图

仓库是 **monorepo**：**3 个 `.sln` / 36+ 个 `.csproj`**。

### 4.1 解决方案文件

| 解决方案 | 路径 | 用途 |
|---------|------|------|
| **Horizon.sln** | `Horizon.sln` | 服务端主解决方案（27 个项目） |
| **HundunWorld.sln** | `HundunWorld/HundunWorld.sln` | Flax 客户端（FlaxEditor 生成） |
| **Horizon.Game.ECS.sln** | `Horizon.Game.ECS/Horizon.Game.ECS.sln` | ECS 子系统调试 |

### 4.2 5 层依赖图（自底向上）

```
Layer 0  契约/抽象（无项目依赖）
         ├─ Horizon.Core.Abstract   (Orleans.Abstractions, AutoMapper, FlatSharp)
         ├─ Horizon.Share           (log4net, Newtonsoft.Json)
         ├─ Horizon.Game.Message    ★ 客户端/服务端共享协议（MemoryPack + Orleans）
         └─ Horizon.IM.Message

Layer 1  领域核心
         ├─ Horizon.Core             → Core.Abstract (+ Consul, TouchSocket.Rpc)
         ├─ Horizon.Model            → Core.Abstract (+ EF Core SqlServer)
         ├─ Horizon.Game.ECS         → Game.Message  ★ 编译后复制 DLL 到 Flax
         ├─ Horizon.Game.ECS.Arch    → Game.ECS (+ Arch 2.0)
         ├─ Horizon.AI.Kernel        → Core.Abstract, Game.Message (+ SemanticKernel)
         └─ Horizon.Strategy.Storage.Redis

Layer 2  基础设施
         ├─ Horizon.Entities         → Core.Abstract, Model (+ EF 全栈)
         ├─ Horizon.Mapper           → Model, Share (+ AutoMapper, MediatR)
         ├─ Horizon.IoT.MQTT         (+ MQTTnet)
         └─ Horizon.Orleans.Interface → Game.Message, IM.Message, Share  ★ Grain 契约

Layer 3  实现
         ├─ Horizon.Orleans.Grains   → 100+ Grain 实现 (+ Mongo/支付宝/微信/MiniExcel)
         └─ Horizon.Game.Core        → Handlers / Interfaces / Sim (MovementValidator)

Layer 4  可执行宿主
         ├─ Horizon.Orleans.Silo     (主 Silo, Exe)
         ├─ Horizon.Game.Gateway     (游戏网关, Exe)
         ├─ Horizon.IM.Gateway       (IM 网关)
         ├─ Horizon.WebApi           (ASP.NET Core + IdentityServer)
         └─ Horizon.WebAdmin         (Blazor 管理后台)

客户端分支
         ├─ Horizon.Game.GengDi      (Avalonia 启动器)
         ├─ Horizon.Game.GengDi.PC   (WinExe 壳, GengDi.exe)
         └─ Flax Game.CSharp         ★ 通过 HintPath 引用 DLL（无 ProjectReference）
```

### 4.3 关键依赖链路

**Silo（服务端入口）依赖 10 个项目**：
```
Core.Abstract → Core → Entities → Game.Message → IM.Message
→ Orleans.Grains → Orleans.Interface → Share → Strategy.Storage.Redis → IoT.MQTT
```

**Gateway 依赖 11 个项目**：
```
Core.Abstract → Core → Entities → Game.Message → Game.Core
→ Mapper → Model → Orleans.Grains → Orleans.Interface → Share → Strategy.Storage.Redis
```

---

## 5. DLL 物理分发机制（Flax 客户端共享代码）★

### 5.1 问题

Flax 的 `Game.csproj` 使用 `RestorePackages=false` + `EnableDefaultItems=false`（Flax 接管编译项管理），**无法用标准 NuGet/ProjectReference**。因此客户端与服务端共享代码必须用"物理 DLL 分发"。

### 5.2 解决方案：CopyToFlax MSBuild Target

共享项目（`Horizon.Game.Message` / `Horizon.Game.ECS` / `Horizon.Game.ECS.Arch`）的 csproj 内置 `CopyToFlax` Target，编译后自动把 DLL + 依赖复制到 Flax 安装目录：

```xml
<Target Name="CopyToFlax" AfterTargets="Build">
  <ItemGroup>
    <FilesToCopy Include="$(OutputPath)$(AssemblyName).dll" />
    <!-- + 依赖 DLL -->
  </ItemGroup>
  <Copy SourceFiles="@(FilesToCopy)"
        DestinationFolder="C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\" />
</Target>
```

参考 `Horizon.Game.Message.csproj:51-76`。

### 5.3 Flax 端引用

`Game.csproj:160-230` 用 `<Reference HintPath>` 直接引用 Flax Tools 目录的 DLL：
```xml
<Reference Include="Horizon.Game.Message">
  <HintPath>C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\Horizon.Game.Message.dll</HintPath>
</Reference>
```

`Game.Build.cs:23-46` 在 Flax 构建时通过 `options.ScriptingAPI.FileReferences.Add()` 注册 22 个 DLL。

### 5.4 新增共享库的标准流程

1. 创建项目，`TargetFramework=net10.0`，引用 `Horizon.Game.Message` 等
2. 在 csproj 末尾加 `CopyToFlax` Target（复制模板见 `Horizon.Game.Message.csproj`）
3. 把新 DLL 加入 `Game.Build.cs:23-46` 的 `dlls[]` 数组
4. 把新 DLL 加入 `Game.csproj:160-230` 的 `<Reference HintPath>` 列表

详见 [DEVELOPMENT.md](./DEVELOPMENT.md#新增共享库的标准流程)。

### 5.5 遗留：UE5 迁移痕迹

csproj 中还保留 `CopyToUE5` Target（复制到 `HundunWorld\Binaries\Managed\net10.0\`），`TypeDumper` 项目仍引用 `UnrealSharp.dll` —— 这是项目曾尝试迁移 UE5 + UnrealSharp 的残留。详见 [TECH_DEBT.md](./TECH_DEBT.md)。

---

## 6. 双客户端架构

### 6.1 Flax Engine 游戏客户端（主体）

- **角色**：3D 游戏主体（武侠 MMO 世界）
- **入口**：`HundunWorld/Source/Game/HundunWorldGamePlugin.cs:15`（Flax `Plugin`）
- **网络**：自研 `NetworkManager`（TouchSocket TCP），**不使用 Flax ENet**
- **特点**：双世界（Flax Actor 视觉 + Arch ECS 逻辑），详见 [CLIENT.md](./CLIENT.md)

> ⚠️ `HundunWorld/Content/Settings/Network Settings.json` 配置了 Flax ENet 驱动（7777 端口），但项目**未启用**它。游戏实际使用 TouchSocket TCP 7789。

### 6.2 Avalonia 桌面启动器（"耕地"游戏中心）

- **角色**：启动器 / 管理中心 / 凭证注入
- **入口**：`Horizon.Game.GengDi`（Avalonia 11.3.12）+ `Horizon.Game.GengDi.PC`（Windows 启动壳）
- **存储**：LiteDB 本地数据库
- **集成**：MQTT 推送、NAudio 音频
- **打包**：`build-installer.ps1` + InnoSetup（`GengDi.Setup.iss`）→ `GengDi-Setup-<version>.exe`
- **凭证注入**：启动游戏时写 `HorizonGame.ini`（AuthToken / PassportId / 网关地址），Flax 客户端读取后优先使用，详见 [CLIENT.md](./CLIENT.md#凭证注入)

### 6.3 两者通信

启动器与游戏客户端**非直接网络通信**，而是通过**文件**（`HorizonGame.ini`）传递登录凭证与网关列表。游戏客户端的运行时通信全部走 TouchSocket → Gateway → Orleans。

---

## 7. 跨平台与机器标识

客户端线路协议每包注入机器标识用于多端识别：
- `HorizonMessageAdapter`（`HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs:336-475`）
- `MachineIdentifier.GetMachineGuid()` 跨平台读取：
  - **Windows**：注册表 `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
  - **Linux**：`/etc/machine-id` 或 `/var/lib/dbus/machine-id`
  - **macOS**：`sysctl IOPlatformUUID`

---

## 8. 可观测性

| 能力 | 实现 | 端点 |
|------|------|------|
| 链路追踪 | OpenTelemetry（Silo + Gateway） | 配置在 appsettings |
| 集群监控 | Orleans Dashboard | `http://192.168.1.78:1199`（用户 `Horizon`） |
| 指标采集 | Prometheus + Grafana | `monitoring/` 目录 |
| 告警 | Alertmanager | `monitoring/` 目录 |
| 日志 | Microsoft.Extensions.Logging.Console | 几乎所有项目 |

---

## 9. 相关文档

- [NETCODE.md](./NETCODE.md) — netcode 设计深度剖析
- [NETWORK_PROTOCOL.md](./NETWORK_PROTOCOL.md) — 帧格式与消息体系
- [SERVER.md](./SERVER.md) — 网关与 Orleans 集群
- [CLIENT.md](./CLIENT.md) — Flax 客户端架构
- [KEY_FILES_INDEX.md](./KEY_FILES_INDEX.md) — 关键文件索引
