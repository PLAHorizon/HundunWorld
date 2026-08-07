# 开发指南 · DEVELOPMENT

> **最后更新**：2026-06-15 · 配套文档：[ARCHITECTURE](./ARCHITECTURE.md) · [TECH_DEBT](./TECH_DEBT.md)

---

## 1. 开发环境要求

### 1.1 必需工具

| 工具 | 版本 | 说明 |
|------|------|------|
| **.NET SDK** | 10.0.300 | `HundunWorld/global.json` 锁定，`rollForward: latestPatch` |
| **Flax Engine** | 1.12 | 安装到 `C:\Program Files (x86)\Flax\Flax_1.12\` |
| **SQL Server** | 2019+ | 库 `Orleans` / `Basic` / `Game` / `Article` / `Support` / `Xingguang` |
| **Redis** | 6+ | 哨兵集群（Masters 9379/Slaves 9679/9779/9879/Sentinels 6379） |
| **IDE** | VS 2022 / Rider / VSCode | 推荐 Rider（Orleans 插件支持好） |

### 1.2 可选工具

| 工具 | 用途 |
|------|------|
| MongoDB | 部分领域文档存储（Grains 引用） |
| Inno Setup 6 | 构建耕地启动器安装包 |
| Docker | `monitoring/` 含 Prometheus/Grafana 配置 |

### 1.3 验证环境

```bash
dotnet --version          # 应输出 10.0.300 或更高
# 检查 Flax 安装
dir "C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\"  # 应有 FlaxEngine.CSharp.dll
```

---

## 2. 构建与运行

### 2.1 服务端（Silo + Gateway）

```bash
# 启动 Orleans Silo（主集群）
dotnet run --project Horizon.Orleans.Silo

# 启动游戏网关（TCP 7789）
dotnet run --project Horizon.Game.Gateway

# （可选）启动 IM 网关
dotnet run --project Horizon.IM.Gateway

# （可选）启动 Web API
dotnet run --project Horizon.WebApi
```

**启动顺序**：Silo → Gateway → 客户端。Silo 启动较慢（等 Orleans clustering 表），Gateway 会通过 `OrleansStartupConnectionRetryFilter` 重试连接。

### 2.2 客户端（Flax）

```bash
# 方式 1：用脚本打开 FlaxEditor
powershell -File HundunWorld/LaunchFlaxEditor.ps1

# 方式 2：直接打开解决方案
# 在 FlaxEditor 中 File → Open Project → HundunWorld.flaxproj
```

FlaxEditor 内按 F5 或点 Play 运行游戏。

### 2.3 耕地启动器（Avalonia）

```bash
# 构建安装包（PowerShell）
powershell -File build-installer.ps1
# 输出：dist/GengDi-Setup-<version>.exe
```

`build-installer.ps1` 流程：
1. `dotnet publish Horizon.Game.GengDi.PC`（win-x64，framework-dependent）
2. 编译 `Horizon.Game.GengDi.Installer`（WPF net48）
3. 调用 Inno Setup（`GengDi.Setup.iss`）打包成 exe

### 2.4 测试与压测

```bash
# 单元测试
dotnet test Horizon.Game.Gateway.Tests
dotnet test Horizon.IM.Gateway.Tests
dotnet test Horizon.Game.GengDi.Tests

# 性能压测（NBomber）
dotnet run --project Horizon.PerformanceTests
# 报告输出到 bin/.../reports/
```

---

## 3. 调试入口

| 服务 | 端点 | 凭证 |
|------|------|------|
| **Orleans Dashboard** | `http://192.168.1.78:1199` | 用户名 `Horizon` |
| **HundunAgent MCP** | `http://localhost:21901/mcp` | 无（仅编辑器模式，本地） |
| **HundunAgent HTTP** | `http://localhost:21900/` | 无（仅编辑器模式，本地） |
| **Prometheus** | 见 `monitoring/` | — |
| **Grafana** | 见 `monitoring/` | — |

**HundunAgent 常用调试命令**（在 FlaxEditor 运行时）：
```bash
# 健康检查
curl http://localhost:21900/health

# 工具清单
curl http://localhost:21900/api/tools

# 查场景层级
curl -X POST http://localhost:21900/api/tools/scene_hierarchy -d "{}"

# 截图（返回 PNG 路径）
curl -X POST http://localhost:21900/api/tools/viewport_screenshot -d "{}"

# MCP 工具列表（JSON-RPC）
curl -X POST http://localhost:21901/mcp -H "Content-Type: application/json" -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"
```

---

## 4. 新增共享库的标准流程 ★

由于 Flax 的 `Game.csproj` 使用 `RestorePackages=false` + `EnableDefaultItems=false`，**客户端与服务端共享代码必须用 DLL 物理分发**。详见 [ARCHITECTURE.md §5](./ARCHITECTURE.md#dll-物理分发机制flax-客户端共享代码)。

### 4.1 步骤

1. **创建项目**，`TargetFramework=net10.0`，引用 `Horizon.Game.Message` 等：
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <LangVersion>14.0</LangVersion>
     </PropertyGroup>
     <ItemGroup>
       <ProjectReference Include="..\Horizon.Game.Message\Horizon.Game.Message.csproj" />
     </ItemGroup>
   </Project>
   ```

2. **加 `CopyToFlax` MSBuild Target**（复制模板见 `Horizon.Game.Message.csproj:51-76`）：
   ```xml
   <Target Name="CopyToFlax" AfterTargets="Build">
     <ItemGroup>
       <FilesToCopy Include="$(OutputPath)$(AssemblyName).dll" />
       <!-- 加上所有依赖 DLL（如 MemoryPack.Core.dll、Arch.dll 等） -->
     </ItemGroup>
     <Copy SourceFiles="@(FilesToCopy)"
           DestinationFolder="C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\" />
   </Target>
   ```

3. **加入 `Game.Build.cs:23-46`** 的 `dlls[]` 数组：
   ```csharp
   string[] dlls = {
     "YourNewLib.dll",   // 新增
     "Horizon.Game.Message.dll",
     // ...
   };
   ```

4. **加入 `Game.csproj:160-230`** 的 `<Reference HintPath>` 列表：
   ```xml
   <Reference Include="YourNewLib">
     <HintPath>C:\Program Files (x86)\Flax\Flax_1.12\Binaries\Tools\YourNewLib.dll</HintPath>
   </Reference>
   ```

5. **重启 FlaxEditor**（让它加载新 DLL）

> ⚠️ **常见坑**：忘记加依赖 DLL 到 `CopyToFlax` → FlaxEditor 启动报 `FileNotFoundException`。建议用 ILSpy 检查新 DLL 的依赖，全部加入。

---

## 5. 新增网络消息的标准流程 ★

### 5.1 新增业务 RPC 消息

详见 [NETWORK_PROTOCOL.md §11](./NETWORK_PROTOCOL.md)。

```
1. Horizon.Game.Message/Enums/MessageType.cs    加枚举值（选对区间）
2. Horizon.Game.Message/Network/                创建 [MemoryPackable] 消息类
3. MessageUnion                                  加 [MemoryPackUnion(id, typeof(...))]
4. Horizon.Game.Core/Handlers/                  加 Handler（继承 MessageHandlerBase）
5. HundunWorld/Source/Game/Network/MessageHandlers/  加 Handler
6. dotnet build Horizon.Game.Message            （CopyToFlax 自动复制 DLL）
7. 重启 FlaxEditor
```

### 5.2 新增 SyncPacket 类型

```
1. SyncPacketKind 枚举（SyncPackets.cs）         加值
2. 创建 [MemoryPackable] 的 SyncPacket 结构
3. SyncPacket union                              加 [MemoryPackUnion]
4. SyncPacketHandler                             加 Kind 分派分支
5. SyncPacketMessageHandler                      加事件
6. HundunWorldGame.SubscribeSyncHandlerEvents    订阅
7. ★ 递增 SyncProtocolVersion.Current            （重要！）
```

---

## 6. AI 辅助开发工作流

### 6.1 `.trae/specs/` 任务规格范式

仓库用 `.trae/specs/` 目录管理结构化任务（5 个 spec 包，每个含 `spec.md` / `tasks.md` / `checklist.md`）：
- `fix-character-data-not-loaded`
- `fix-character-fingerprint`
- `fix-network-sync-input-sending`
- `fix-network-sync-pipeline`
- `fix-network-sync-visual-bridge`

**新增任务时建议沿用此范式**：
- `spec.md` —— 任务背景与验收标准
- `tasks.md` —— 拆解的子任务清单
- `checklist.md` —— 完成检查清单

### 6.2 GitHub Copilot 工作流

提交历史可见 `copilot/fix-*` 分支，配合 Copilot Workspaces 做 PR。典型流程：
1. 在 GitHub Issue 描述问题
2. Copilot Workspace 生成分支与修改
3. 人工 review + 测试 + merge

---

## 7. 项目依赖管理建议

### 7.1 现状（问题）

每个 csproj 独立声明 `PackageReference` 版本，无 `Directory.Packages.props`。详见 [TECH_DEBT.md §2](./TECH_DEBT.md)。

### 7.2 改进方向

1. **加 `Directory.Build.props`**（仓库根）：
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <LangVersion>14.0</LangVersion>
       <Nullable>enable</Nullable>
     </PropertyGroup>
   </Project>
   ```

2. **加 `Directory.Packages.props`**（集中版本）：
   ```xml
   <Project>
     <PropertyGroup>
       <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
     </PropertyGroup>
     <ItemGroup>
       <PackageVersion Include="Microsoft.Orleans.Core" Version="10.0.1" />
       <PackageVersion Include="MemoryPack.Core" Version="1.21.4" />
       <!-- ... -->
     </ItemGroup>
   </Project>
   ```

3. csproj 改为无版本引用：
   ```xml
   <PackageReference Include="Microsoft.Orleans.Core" />
   ```

---

## 8. 代码风格与约定

### 8.1 命名

| 元素 | 约定 |
|------|------|
| 命名空间 | `Horizon.<Domain>.<Sub>`（如 `Horizon.Game.Message.Sync`） |
| 项目名 | `Horizon.<Domain>[.<Sub>]` |
| Grain 接口 | `I<Entity>Grain`（如 `ICharacterGrain`） |
| Grain 实现 | `<Entity>Grain`（如 `CharacterGrain`） |
| Handler | `<Action>Handler` / `<Action>ResponseHandler` |
| SyncPacket | `<Name>Packet`（如 `SnapshotPacket`） |
| System | `<Name>System`（如 `LocalSimulationSystem`） |
| Component | `<Name>Component`（如 `PredictedTransformComponent`） |

### 8.2 序列化约定

- **新代码统一用 MemoryPack**：`[MemoryPackable]` + `[MemoryPackUnion]`
- 消息类用 `partial struct`（值类型，减少 GC）
- Grain 内部状态可用 `[GenerateSerializer]`（Orleans 内建）

### 8.3 注释与文档

- 公共 API 加 XML 注释（`///`）
- 复杂逻辑加 `//` 行内注释说明意图
- 修改 netcode/协议相关代码时，**同步更新 docs/** 对应文档

---

## 9. Git 工作流

### 9.1 分支约定（从提交历史推断）

- `main` —— 主分支
- `copilot/fix-*` —— Copilot Workspace 生成的修复分支
- `<author>/<topic>` —— 人工开发分支

### 9.2 提交信息

提交信息以**中文为主**（如"游戏网关配置文件"、"新增耕地客户端"、"角色界面"、"客户端动画尝试"），简短描述主题。

### 9.3 PR 流程

1. 分支开发 + 自测
2. 提 PR，描述变更范围与测试结果
3. Review（关注 netcode/协议变更的影响）
4. 合并到 `main`

---

## 10. 常见问题排查

### 10.1 FlaxEditor 启动报 `FileNotFoundException`

**原因**：缺少共享 DLL（未加到 `CopyToFlax` Target 或 `Game.Build.cs` 的 dlls[]）。

**解决**：
1. 用 ILSpy 检查报错 DLL 的依赖
2. 把缺失 DLL 加到 `CopyToFlax` 的 `<FilesToCopy>`
3. `dotnet build Horizon.Game.Message`（触发复制）
4. 重启 FlaxEditor

### 10.2 角色频繁"瞬移"

**原因**：`MovementFormula` 客户端/服务端不一致，或 `DefaultPositionEpsilon` 过小。

**解决**：检查 [NETCODE.md §2](./NETCODE.md) 的公式常量是否两端一致。

### 10.3 Gateway 连不上 Silo

**原因**：Orleans clustering 表未初始化 / SQL Server 未启动 / 连接串错误。

**解决**：
1. 确认 SQL Server `Orleans` 库存在且有 clustering 表
2. 检查 `appsettings.json` 的 `ClusteringSiloOptions`
3. 看 Silo 日志是否成功注册到集群
4. Gateway 有 `OrleansStartupConnectionRetryFilter` 自动重试

### 10.4 远程角色卡顿

**原因**：`InterpolationSystem` 的 100ms 插值窗口与网络延迟不匹配。

**解决**：调整 `InterpolatedTransformComponent` 的插值速率（当前 `1/0.1 = 10`，即 100ms 到达目标）。

### 10.5 输入丢失

**原因**：`InputAcceptResult.TooOld` —— `PlayerSessionGrain` 的去重窗口太短。

**解决**：检查 `PlayerSessionGrain` 的输入去重逻辑，扩大窗口或排查网络抖动。

---

## 相关文档

- [README.md](../README.md) — 项目入口
- [ARCHITECTURE.md](./ARCHITECTURE.md) — 整体架构
- [TECH_DEBT.md](./TECH_DEBT.md) — 技术债清单
- [KEY_FILES_INDEX.md](./KEY_FILES_INDEX.md) — 关键文件索引
