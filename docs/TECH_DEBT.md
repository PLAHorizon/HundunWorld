# 技术债清单 · TECH_DEBT

> **最后更新**：2026-06-15 · 已识别的技术债与改进建议
>
> 这份清单是研究阶段的产出，供后续重构规划参考。每项含**优先级 / 问题 / 现状 / 建议**。

---

## 优先级总览

| 优先级 | 项 | 说明 |
|--------|-----|------|
| 🔴 高 | 1 | 无根级文档（本次已部分解决） |
| 🔴 高 | 2 | 包版本分散，无集中管理 |
| 🔴 高 | 3 | 两套角色系统并行 |
| 🟡 中 | 4 | UE5 迁移残留 |
| 🟡 中 | 5 | 单 shard 硬编码 |
| 🟡 中 | 6 | 协议向前兼容仅告警 |
| 🟡 中 | 7 | 压测报告混入 git |
| 🟢 低 | 8 | 遗留序列化库并存 |
| 🟢 低 | 9 | 内网 IP 硬编码 |

---

## 🔴 高优先级

### 1. 无根级文档（本次部分解决）

**问题**：仓库长期缺乏集中文档。研究阶段前：
- ❌ 无 `README.md`（根级）
- ❌ 无 `docs/` 目录
- ❌ 无 `ARCHITECTURE.md` / `CONTRIBUTING.md` / `LICENSE`
- ❌ `.github/` 目录为空（无 Issue/PR 模板、无 Actions）

**现状**：本次研究已新建根 `README.md` + `docs/` 8 份文档（本文件即为其中之一），覆盖架构/netcode/协议/服务端/客户端/索引/技术债/开发指南。

**遗留**：
- `.github/` 仍为空，建议加 Issue/PR 模板与 CI（lint / build 校验）
- `CONTRIBUTING.md` / `CHANGELOG.md` / `LICENSE` 仍未建
- 客户端文档（`HundunWorld/Source/Game/**/*.md`，约 30 份）仍分散在子目录，未纳入 docs/ 索引

**建议**：把客户端分散的 README 逐步归并到 `docs/client/`，保持模块化。

---

### 2. 包版本分散，无集中管理

**问题**：每个 `.csproj` 独立声明 `PackageReference` 版本，**无 `Directory.Packages.props`**，同一库在不同项目可能版本不一致。

**现状示例**：
- `Microsoft.Extensions.Logging.Console`：部分项目 10.0.2，部分更早
- Newtonsoft.Json 13.0.4 在 Core/Model/Redis/WebApi/GengDi 重复声明
- AutoMapper 16.0.0 在 Core/Mapper 重复

**建议**：
1. 在仓库根加 `Directory.Build.props`（统一 `TargetFramework=net10.0`、`LangVersion`、公共 NoWarn）
2. 加 `Directory.Packages.props`（集中版本管理，csproj 用 `Version` 无值引用）
3. 用 `dotnet list package --outdated` 检测不一致

**风险**：迁移工作量中等（~36 个 csproj），但能彻底消除版本漂移。

---

### 3. 两套角色系统并行 ★（架构层面）

**问题**：项目存在**两套并行的角色/移动系统**，职责边界模糊：

| 系统 | 职责 | 持久化 | 频率 | 文件 |
|------|------|--------|------|------|
| **ZoneShardGrain** | 高频移动模拟、空间权威、AOI | **瞬态**（断线重建） | 1/60s Tick | `World/ZoneShardGrain.cs` |
| **CharacterGrain** | RPG 玩法（战斗/装备/任务/社交） | **落库**（GameStore） | 事件驱动 | `CharacterGrain.cs` |

**风险**：
- 两者通过 `CharacterId` 关联，但**不在同一 Grain 内**，状态同步可能不一致
- `CharacterGrain`（937 行）也包含 `Move` 等移动方法（老 RPC 风格），与 `ZoneShardGrain` 的权威移动语义重叠
- HP 等状态既存在于 `ZoneShardGrain.SimulatedEntity.Hp`，也存在于 `CharacterGrain`

**建议**（按改动量递增）：
1. **短期**：明确文档边界（已在 [NETCODE.md §10](./NETCODE.md) 完成），新代码严格按职责归类
2. **中期**：把 `CharacterGrain` 的移动方法标记为 `[Obsolete]`，业务逻辑只调 `ZoneShardGrain`
3. **长期**：统一为一套 —— 例如 `CharacterGrain` 持有 `CharacterId`，移动状态委托 `ZoneShardGrain`，HP 等状态由 `CharacterGrain` 唯一持有并订阅 `ZoneShardGrain` 的事件更新

---

## 🟡 中优先级

### 4. UE5 迁移残留

**问题**：项目曾尝试迁移到 UE5 + UnrealSharp，后转投 Flax，但残留代码未清理。

**残留清单**：
- `CopyToUE5` MSBuild Target（在多个共享项目 csproj 中，复制 DLL 到 `HundunWorld/Binaries/Managed/net10.0/`）
- `TypeDumper` 项目仍引用 `UnrealSharp.dll`
- `HundunWorld/Binaries/Managed/` 路径仍在使用

**建议**：
1. 删除所有 `CopyToUE5` Target（确认无运行时依赖后）
2. 评估 `TypeDumper` 项目是否仍有用，否则删除或改基于 Flax
3. 清理 `HundunWorld/Binaries/Managed/net10.0/` 下不再使用的 DLL

---

### 5. 单 shard 硬编码

**问题**：`ZoneShardGrain` 的 shard 身份 = Orleans grain 的 long 主键（`GetPrimaryKeyLong()`），支持多 shard。但业务侧**硬编码使用 shard 0**：
- `SyncDispatcherHostedService` 启动时调 `IZoneShardGrain(key=0).SubscribeFanoutAsync(...)`
- 客户端握手时也固定到 shard 0

**现状**：单 shard 模式是多 shard 水平扩展的初始阶段。

**建议**：
1. 设计 shard 路由策略：按区域（地图分片）/ 按负载（一致性哈希）/ 按玩家密度
2. 客户端握手时由服务端返回 shardId，后续 Input/Snapshot 都路由到对应 shard
3. 实现 shard 间迁移（玩家跨区域时）

---

### 6. 协议向前兼容仅告警

**问题**：`SyncPacketHandler`（`Horizon.Game.Core/Handlers/SyncPacketHandler.cs:43-50`）校验 `SyncProtocolVersion` 时，**不匹配仅告警，不拒绝连接**。

**风险**：
- 协议升级时旧客户端仍能连接但可能行为异常（字段错位、崩溃）
- 生产环境难以强制升级

**建议**：
- 开发/测试环境：保持"仅告警"便于调试
- 生产环境：引入严格模式（`RequireStrictVersionMatch` 配置开关），不匹配拒绝连接

---

### 7. 压测报告混入 git

**问题**：`Horizon.PerformanceTests/bin/.../reports/` 下有 27 份 NBomber 报告（.md）混入版本控制。

**建议**：
1. `.gitignore` 加 `Horizon.PerformanceTests/bin/`、`Horizon.PerformanceTests/reports/`
2. `git rm --cached` 移除已跟踪的报告文件
3. 如需保留历史报告，移到 `docs/perf-reports/` 或用 release artifact

---

## 🟢 低优先级

### 8. 遗留序列化库并存

**问题**：项目并存多种序列化库：
- **MemoryPack 1.21.4**（主，消息/SyncPacket/组件）
- Orleans.Serialization 10.0.1（Grain 内建）
- Newtonsoft.Json 13.0.4（Core/Model/Redis/WebApi/GengDi）
- protobuf-net 3.2.56（Model）
- FlatSharp.Runtime 7.9.0（Core）

**现状**：MemoryPack 是新代码标准，旧库主要在 Model/Core 等历史模块。

**建议**：
- 新代码统一用 MemoryPack
- 旧模块逐步迁移（优先级低，除非有性能瓶颈）
- 同一数据流不要混用序列化库（避免兼容问题）

---

### 9. 内网 IP 硬编码

**问题**：`network_config.json`（`Network/NetworkConfig.cs:144-157`）与 appsettings.json 硬编码内网 IP：
```
192.168.1.78:7789 (华东)
192.168.2.78:7789 (华南)
192.168.3.78:7789 (华北)
```
Orleans Dashboard 也硬编码 `192.168.1.78:1199`。

**建议**：
- 改为环境变量（`GATEWAY_HOST` / `GATEWAY_PORT`）
- 或服务发现（Consul 已在 Core 项目引用，可利用）
- 客户端的网关列表应从 WebApi 动态拉取，不写死配置

---

## 额外观察（不计入优先级）

### A. Flax ENet 配置未启用

`HundunWorld/Content/Settings/Network Settings.json` 配置了 Flax 内置 ENet 驱动（7777 端口），但项目**未使用**它 —— 游戏实际用 TouchSocket TCP 7789。建议删除或加注释说明"未启用，保留以备 Flax 原生网络方案"。

### B. SQLite 在 ECS.Arch

`Horizon.Game.ECS.Arch.csproj:11` 显式 `NoWarn NU1701;NU1603;CS1591`，注释说明 Arch 2.0.0-beta 上游尚未发布 net10.0 资产，引用其 net8.0 输出。当 Arch 发布正式版支持 net10.0 时应升级并移除 NoWarn。

### C. 安装器用 net48

`Horizon.Game.GengDi.Installer` 是 WPF 项目，用 `net48` + `LangVersion=9.0`（仓库中唯一非 net10.0 项目）。这是 InnoSetup/MSI 工具链的限制，可接受。但长期看可考虑用 Velopack 或 MSIX 替代。

---

## 相关文档

- [ARCHITECTURE.md](./ARCHITECTURE.md) — 当前架构（含本文件提及的设计）
- [NETCODE.md](./NETCODE.md) — 两套角色系统边界（§10）
- [DEVELOPMENT.md](./DEVELOPMENT.md) — 开发流程（含包管理建议）
