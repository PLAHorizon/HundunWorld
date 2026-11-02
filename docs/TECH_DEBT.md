# 技术债清单 · TECH_DEBT

> **最后更新**：2026-10-06（T8 行为级回归已写；T9 物品号段启发式已查明并钉住；T10 目录版本号已修待部署；T13 Tools 刷新清单已补全并加了漂移闸门）· 已识别的技术债与改进建议
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
| 🔴 高 | T1 | ~~任务接取侧未复校等级门槛（可绕过）~~ **已修**（网关传权威等级 + Grain 侧纯函数判定） |
| 🟡 中 | T2–T7 | 任务系统其余遗留：repeat 精确次数、三类无事件源条件、ItemKind 序、NPC 角标、孤岛追踪器、现场未回归 |

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

### 2. 包版本分散，无集中管理（✅ 已解决）

**问题**：每个 `.csproj` 独立声明 `PackageReference` 版本，**无 `Directory.Packages.props`**，同一库在不同项目可能版本不一致。

**现状**：✅ 已创建 `Directory.Packages.props`，启用中央包管理（CPM），所有包版本集中管理。

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

## 🟡 任务系统遗留（2026-10-05 落地时明确记下的债）

> 这些不是"没做完不说了"，而是设计文档 §10 里逐条对过账的已知缺口；细节与验证口径见 [QUEST_SYSTEM_DESIGN.md](./QUEST_SYSTEM_DESIGN.md)。

### T1. 接取侧未复校等级门槛（✅ 已修，2026-10-05）

等级过去只在**可接列表**过滤，`AcceptFromTemplateAsync` 只校发布态/前置/周期——直连一条 questId 就能把高等级任务接走。现在网关把**从角色 Grain 读到的权威等级**一起传进接取调用（客户端只报 questId），Grain 侧用纯函数 `QuestGrain.MeetsLevelRequire(levelRequire, characterLevel)` 判一次；读不到等级传 0，有门槛的任务一律拒（fail closed）。边界由 `QuestEditorContractTests.接取侧必须自己再校一遍等级门槛` 钉住（6 个用例含"卡在线上算过"和"0 级不算没门槛"）。

### T2. `repeat_limit` 只是"允许按周期重复"

运行时状态只保留最近一次完成记录，**精确次数上限**（如"每日 3 次"）未实现。要精确计数就得改 `QuestState` 结构（MemoryPack 字段只能尾部追加）。

### T3. `collect_item` / `use_item` / `reach_zone` 仍无事件源

`QuestObjectiveCodec.HasProducer` 对这三类返回 false，校验器按错误（Q026）拒绝入库——**这是保护而不是缺陷**：接了推不动的任务比接不了更糟。`talk_npc` / `interact_prop` 已在 2026-10-05 接通（DS 实体身份上行 + 网关邻近裁决）。

### T4. `ItemKind` 与库里 `item_type` 不同序

客户端枚举与 `Game_HunduShijie_ItemTemplate.item_type` 的编号语义不是一一对齐，跨层传品质/类型时靠映射表硬翻。材料号段迁到 `700000+序号`（方案 A，代码已改、`015` 种子待入库）消解的是**号段重叠**，这条**枚举序不同**的债还在。

### T5. NPC 头顶"！/？"角标未接

身份通路已经够得着（网关注册表里有附近实体的模板 id 与坐标），差一条下行把"这个 entityId 身上有我能接/能推进的任务"投影给客户端。**是产品决定**（可接口径要不要从总表收敛成"走近发布者才开放"）而不是纯技术缺口。

### T6. `QuestTrackerHUD.cs` 是无调用方的孤岛文件

`HundunWorld/Source/Game/UI/QuestTrackerHUD.cs` 看着像生效的任务追踪器，实际没有任何调用方；真正在跑的是 `CombatHudTraditionalPage` 的右侧追踪。删它需要确认没有别的会话正在用它。

### T7. 现场视觉与整组回归仍未跑

所有改动只到编译/契约级。用户侧三步：管理员刷新 Flax Tools 副本 → 整组重启（Silo → Gateway → **DsHost**）→ PIE 与编辑器观察。`Horizon.Game.Gateway.Tests` 的既有任务回归在宿主运行时因文件锁无法构建，需停掉后补跑。

### T8. 任务管理 grain 的自锁修复：行为级回归 ✅ 真集群首跑通过（2026-10-06）

`QuestManagerGrain.UnpublishAsync(forceAbandonActive:true)` 曾在持 `_writeGate`（不可重入 `SemaphoreSlim(1,1)`）期间再等同一把锁 → **单例 grain 永久卡死**，之后所有任务管理调用排队到超时且无日志。锁外记账已修（见 `QUEST_SYSTEM_DESIGN.md` §7.2），并补了一道**结构级闸门**：`Horizon.QuestContractTests/QuestGrainLockDisciplineTests.cs` 扫源码断言"持锁区间内不许 await 任何同样要这把锁的方法"，且做过突变验证（把调用挪回锁内立刻判红）＋两条防空转的自我约束（解析器必须认出这 7 个方法、必须识别出 ≥5 个锁持有者）。

**行为级回归已落地并真跑通过（2026-10-06）**：`Horizon.QuestContractTests/QuestManagerRevocationLiveTests.cs`（2 条，真集群 `ConnectAsync()` 宿主 + `HORIZON_ORLEANS_REDIS=127.0.0.1:9379` 环境门控，每个 Grain 调用套 5 秒死时限；探针任务用刻意保持下线的 20001，对库净零，收尾 `NotifyFinishedAsync` 摘幽灵持有者）。**首跑 2/2 通过**，实测断言到的：`unpublish(force)` 按时返回、`AbandonedCount==1`、队列里正是那个持有者、取走即清空、之后三条后续调用全部按时应答、不强制那次没有重复记账。
   - 它原先住在 `Horizon.Game.Gateway.Tests`，首跑当晚被并行会话的在途改动（`CharacterHealthPersistTests.cs` CS4008）挡住编译。处理方式：**不动别人的文件**，把这条回归搬到只引 Orleans 客户端 + Redis 成员提供方的 `Horizon.QuestContractTests`（不再引 Gateway/Silo 工程，也就不会再被常驻进程的 bin 锁死）——这本来就是这个工程存在的理由。
   - 首跑判红过一次，**错在测试自己**：第二条断言"不强制那次之后队列应为空"，忽略了强制那次记的账还没被取走。改成"队列里正好一条 + 取走后为空"后通过，顺带证明去重成立。

禁止退回的老路：进程内手写/Moq 代理 `IPersistentState<T>` 直接构造 Grain 会让 vstest 的 `testhost` 静默秒退（一行结果都不出，还会留下孤儿进程锁住 `Horizon.*.dll`）。

### T14. 任务编辑器"所有按钮都起不了 CLI 进程"＋"草稿 JSON 报 0xEF"（✅ 已修，2026-10-06，用户现场截图报障）

两个启动级缺陷叠在一起，看上去像"编辑器整个不能用"（截图里 `刷新目录/新建/保存/发布` 全失败，状态栏两条报错交替出现）。**根因都是编码/重定向，不是业务逻辑**：

1. **`StandardInputEncoding is only supported when standard input is redirected.`** —— `QuestCliBridge.Run` 原来无条件设 `StandardInputEncoding`，而它只在输入被重定向时才允许设置；不带草稿的动词（catalog/list/get/publish/unpublish/retire）压根不重定向 stdin，于是 `Process.Start()` 抛异常，被外层 catch 转成"起不了 CLI 进程"。**修**：彻底不设该属性，改成手工把 `Encoding.UTF8.GetBytes(stdin)` 写进 `process.StandardInput.BaseStream`（关流即 EOF）。回退点：`QuestCliBridge.cs` 的 `redirectInput` 分支。
2. **草稿 JSON 报 `'0xEF' is an invalid start of a value`** —— 双向都错：编辑器用 `Encoding.UTF8` 当 stdin 编码（该编码器**会写 BOM**，草稿首字节成 `0xEF 0xBB 0xBF`）；CLI 侧 `Console.InputEncoding = Encoding.UTF8` 在重定向句柄上抛 IOException 被 `catch` 吞掉，**悄悄退回控制台码页 936**，把 BOM 解成 `ï`。**修**：编辑器写无 BOM 字节；CLI 用 `Console.OpenStandardInput()` + 显式 UTF-8 + `TrimStart('\uFEFF')`，输出同样换成显式 UTF-8 的 `StreamWriter`（不再依赖 `Console.OutputEncoding`）。
   **证据**：`scripts/verify-quest-cli-encoding.py` 三条用例全绿（BOM 草稿现在是"校验通过"）；把 CLI 解码器临时改回 936，带 BOM 那条立刻复现 `'0xE9' is an invalid start of a value`（变异对照成立）。
   **判据教训**：这套探针第一版用 GBK 草稿做反向对照是**无效的**——JSON 结构字符全是 ASCII，中文坏成 U+FFFD 后结构照样解析，而 `validate` 回执不含草稿正文，stdout 上看不出区别。"编码没坏"必须靠变异对照或内容回显通道，不能只看回执文本。

**同一轮的设计变更（用户拍板）**：任务草稿改住 Redis（`quest:draft:{id}`，72 小时、每次保存续期），**只有"发布"写 SQL Server**（落库 → 置已发布 → 删草稿），"把库里下线行放上线"改名 `online`。核验 `scripts/verify-quest-draft-redis.py`（含"无草稿时发布被拒且 SQL 查无此行"）。代价与注意：Redis 不可用时草稿层不可用（编辑器只提示、目录仍按库刷新）；索引 ZSET 会因 TTL 先删正文而留垃圾项，靠读取时惰性剔除，**不能拿索引当存在性判据**。

3. **`保存草稿失败：未设置 HORIZON_QUEST_REDIS / HORIZON_ORLEANS_REDIS，草稿层不可用`**（接上一条之后暴露出来的第三个缺陷）—— 草稿层的 Redis 连接串只认环境变量，而编辑器把 CLI 当**子进程**拉起时，子进程环境里没有任何 `HORIZON_*` 变量（父进程 FlaxEditor 也不知道这些）。**修**：`ConnectionConfig.RedisCandidates()` 按 环境变量 → 各服务 `.env`（与 DB 连接串同一批文件，`GATEWAY_REDIS_CONNECTION_STRING` 就在里面）→ 本机 dev 兜底 `127.0.0.1:9379` 的顺序给候选；`QuestDraftStore.TryConnect` **逐个真打一次 PING**，第一个有应答的胜出，并把"用的是哪个来源"回显在 `draft save/list` 的返回值里（实测：`Horizon.Orleans.Silo\bin\Debug\net10.0\.env 的 GATEWAY_REDIS_CONNECTION_STRING`）。
   **为什么必须 PING**：`abortConnect=false` 时 `ConnectionMultiplexer.Connect()` 无论目标通不通都会返回对象——只判断"没抛异常"，一个写错的地址也会被当成可用，作者点保存时才发现。核验脚本已改成**清掉全部 Redis 环境变量**再跑（复现编辑器的真实环境），全绿。
   回退点：`ConnectionConfig.RedisCandidates()` 的候选顺序 + `QuestDraftStore.TryConnect` 的 PING 判定。

4. **"任务编辑太卡"**（现场反馈）—— `QuestCli.Run` 是**同步阻塞**的（`Start → ReadToEnd → WaitForExit(120s)`），而每个按钮回调都跑在编辑器**主线程**上：一次 `catalog`（连库 + EF 查询）就能僵住几百毫秒到数秒，`保存/发布` 更糟——它后面紧跟一次目录刷新，等于两次串行阻塞叠加。**修**：`QuestCli.RunAsync` 把进程与读流放到线程池，结果经 `FlaxEngine.Scripting.InvokeOnUpdate` **回灌到主线程**（回调里可直接改控件）；窗口侧 `RunCli(...)` 统一入口，**同一时刻只允许一个调用在跑**（并发子进程会让"哪个结果先回来"不确定，而回调改的是同一批控件），忙时提示"上一个操作还在进行（X）"；`catalog` 顺带把草稿一起回（原来"目录 + 草稿"是两次子进程，等于每次刷新多付一遍启动+连 Redis 的延迟）。
   约束与回退点：① 主线程回灌一律走 `Scripting.InvokeOnUpdate`（本仓先例：`AuthenticationUI.RunOnMainThread`），不要在回调里假设线程；② 别把 `RunCli` 的忙判定去掉——它是"结果与控件一一对应"的唯一保证；③ 回退即把 `RunCli` 换回 `QuestCli.Run`（同步），但卡顿会回来。

### T9. 物品号段启发式把 15 件五行素材/信物当成秘籍（🟡 中，2026-10-06）

任务奖励要求"可关联所有游戏物品"，于是把发放链路整条核了一遍，结论分两层：

**发放这一侧是无条件的**（不是缺陷，但要知道）：`QuestHandler.GrantQuestRewardsAsync`（`Horizon.Game.Core/Handlers/QuestHandler.cs:431-443`）只要 `templateId>0 && 数量>0` 就投给 `IInventoryGrain.AddItemAsync`；后者明写"服务端没有运行时物品表可校验其存在性"（`Horizon.Orleans.Grains/InventoryGrain.cs:114-121`），**不查模板、不查 `item_type`、也不查 `max_stack`**（堆叠只按 TemplateId+Quality 无限累加，`:136-143`；`max_stack` 这一列全仓没有读它的地方，客户端 `MaterialData.MaxStack = 999` 同样无人消费，属于"写了没人认的字段"）。所以给一条任务发 `item:999999` 也会"成功"，玩家拿到一件解析不出定义的空格子；给材料发 999 个也不会撞堆叠上限。

**真正的问题在使用与显示两侧**，两边共用同一条号段启发式"id ≥ 800000 就是秘籍"：
- 服务端 `InventoryGrain.UseItemWithResultAsync:234` 先把这类 id 转投 `SkillGrain.LearnFromBookAsync(id-800000)`，于是**永远走不到** `:272` 那条按 `use_effect` 加五行好感的分支；
- 客户端 `HundunWorld/Source/Game/Inventory/ItemCatalog.cs:172` 同样先判号段，把这类物品显示成"无名残页 / 秘籍"。

库里 `910001..910015` 这 15 件五行素材与信物（`item_type=3` 素材 10 件、无 `use_effect`；`item_type=2` 信物 5 件、带 `{"kind":"favor","domain":1..5,"delta":10}`）正好落进这个号段，是**五行系统的历史号段选择**（`800000+技能id` 这条契约只覆盖了秘籍，没给别的物品留出边界）。后果不是崩溃而是静默失效：**5 件信物用下去好感一点都不会加**（效果被号段分支吞掉），10 件素材点了没用，且 15 件全都显示成"残页"。任务奖励一旦指向它们，玩家拿到的就是这种东西。

现状由 `Horizon.QuestContractTests/QuestAuthoringDbTests.秘籍号段里不得混入非秘籍的物品_现有15件五行素材登记在案` 钉住（连真库跑，`HORIZON_QUEST_DB=1`）：**只许这 15 件，多一件立刻判红**。

修的方向：**① 已实施（2026-10-06）**——使用路径改成先问模板声明了什么效果再认号段：`InventoryGrain.UseItemWithResultAsync` 把 `ReadFavorEffectAsync` 提到分支判定之前，判据抽成纯函数 `InventoryGrain.RoutesAsSkillBook(templateId, profileResolved, itemType, declaresFavorEffect)`（`号段内 && 档位是秘籍(6) && 未声明 favor`；**查不到模板时退回号段旧口径**，绝不能因为读不到就把所有书禁掉），回退就是删掉那三行换回 `if (item.TemplateId >= BookItemTemplateBase)`。代价：真秘籍每次"使用"多一次主键查询（原先非秘籍分支本来就要查）。回归 `QuestRewardRoutingTests`（12 条：11 个边界 case（含"查不到模板要退回号段旧口径"那条反向保护）+ 真库不变式：带 favor 的行一个都不许被当秘籍、秘籍不许声明 favor、15 件 strays 一个都不许进研读、91 本真秘籍一个都不许被排除），**119 绿**。
**② 也已实施（2026-10-06，同一处改动里）**：号段契约收紧成"段内 **且** `item_type==6` **且** 未声明 favor"，于是 `910001..910010` 那 10 件素材不再被拿去研读不存在的技能——同一次模板查询顺带把档位读了，没有额外往返（原先两个分支各查一次，现在合成一次）。
**只剩 ③ 没修**：物品名与类别随背包下行给出，客户端不再靠本地硬编码目录猜（那 15 件的**显示**占位归这条；`item_type=5/7/8` 目前没有客户端定义也归它，而库里现在没有这三类行，所以暂无实况对象）。

### T9b. 全量普查：171 件里客户端叫得出名字的到底有几件（2026-10-06，已量化）

T9 原先那句"15 件显示成无名残页"是**按 id 区段推出来的**。现在改为实测：`scripts/verify-quest-reward-display-coverage.py`（只读库与源码，产出一份 `docs/quest_reward_display_coverage.json`）按 `ItemCatalog.Build` 的真实解析路径逐个数过 171 行：

| 档位 | 行数 | 叫不出名字 |
|---|---|---|
| 武器 / 防具 | 3 / 10 | 0 |
| 饰品 | 7 | 5（`910011..910015` 信物） |
| 消耗品 | 19 | 10（`910001..910010` 素材） |
| 材料 | 41 | 0 |
| 秘籍 | 91 | 0 |
| **合计** | **171** | **15** |

结论两条：① **"奖励可关联所有游戏物品"在显示侧的覆盖面是 156/171**，缺的正是 T9 那 15 件，脚本已把"只许这 15 件、多一件就 FAIL"钉住（新加物品忘了同步客户端目录会立刻变红）；② 另有一条**依赖而非缺陷**：**91 本秘籍没有一本在客户端静态技能种子里**，它们的名字全部来自**服务端技能模板下行**（库里 `Game_HunduShijie_SkillTemplate` 117 行、`skill_id` 1001..90001，下行后合并进 `SkillDatabase`）。也就是说下行到达之前，玩家手里的技能书一律显示"无名残页"——这不是号段错误，而是"名字的第二真相源在运行期"，`ItemCatalog` 那句 `目录里查无此技，需等服务端技能模板下行` 就是在说它。

顺带记下我这次普查**自己判断错过的地方**：第一版拿客户端静态技能表数了一遍，得出"91 本秘籍全叫不出名字"，差点把这条当成大缺陷报出去；实际是我漏看了 `SkillDatabase` 接收 `IList<SkillTemplateEntryInfo>` 的合并入口。**教训：判"覆盖不全"之前，先确认运行态那份数据从哪儿来。**

### T10. 任务目录版本号用"求和"，会互抵也会漏掉手工改库（✅ 已修并已部署，2026-10-06）

"不停服实时上下线"依赖一个脏标记：`QuestManagerGrain.EnsureCatalogAsync` 每次 TTL 到期重查库，并用
`observed = Σ(ContentVersion + PublishState) + 行数` 当目录版本号（`Horizon.Orleans.Grains/World/QuestManagerGrain.cs:416-424`），客户端 `QuestClientService` 按它重画任务日志/追踪栏。两个已核实的钝点：

1. **求和互抵**：A 任务下线（`PublishState` 1→0，−1）恰好被 B 任务内容版本 +1 抵掉时，`observed` 不变，版本号不动。库里内容确实换了（`_cache` 每次 TTL 都重建，所以**新拉清单的玩家能看到**），但按版本号做增量的那一路不会重画。
2. **手工改库不 bump 就不变**：直接 `UPDATE … SET quest_name/description/objectives` 而不动 `content_version`（种子与手改都会这样），`observed` 一字不变。经 CLI/编辑器写不会踩这条（`upsert` 会 bump `ContentVersion`），踩的是"运营直接改库"。

修法（已按此实施，2026-10-06）：版本号改成**按行有序的内容指纹**——对 `_cache` 按 `QuestId` 排序后，把 `QuestId / ContentVersion / PublishState / LevelRequire / GiverKind / GiverTemplateId / RepeatLimit / 前置串 / objectives / rewards` 原文喂进一个 FNV-1a（`unchecked`），空目录返回固定值。纯函数落在 `Horizon.Orleans.Interface/World/QuestCatalogFingerprint.cs`（**不是** Sim：`QuestCatalogEntry` 定义在这个程序集，而 Sim 与 Interface 互不引用，类型能到的地方只有一处），调用点仍是原来那一行。回归为 `Horizon.QuestContractTests/QuestCatalogFingerprintTests.cs` 9 条（确定性、顺序无关、空目录、名字/目标串/奖励串三处可见性、前置集合 `{1,3}`≠`{2,2}` 而 `{3,1}`=`{1,3}`，加上面两个钝点各一条）。**做过突变验证**：把 `Compute` 换回那句求和，9 条里 6 条立刻判红，幸存的 3 条恰好是求和本来也能过的（同内容、顺序无关、空目录≠单条），说明红的是真断言而不是全表泛红。改动只影响版本号语义，不影响任何已接实例（快照在接取时定）。**已核实：全仓只有这一处算版本号**——`TouchCatalogAsync`（`:467-471`）没有自己的加一逻辑，它只是 `EnsureCatalogAsync(forceRefresh:true)` + 写审计，所以换算法是**单点改动**，不会出现"Grain 自己写的能前进、外部改库不能"的第三种不一致。

### T11. 发布者可以写成"物品"，但世界里的实体身份永远解不出 item（🟡 中，2026-10-06）

`QuestEntityRef.CanBeGiver` 允许 `npc / item / prop` 三类做发布者（这是目标里"发布或触发任务的对象可以是人也可以是某个物品"的直接落地），校验器也真的会去库里查这个物品存不存在（Q014），显示名也能从 `ItemTemplateEntity` 解析出来。**但运行态的身份只有四类**：DS 上行的 `ScriptedKind` 经 `DsEntityRegistry.ToQuestKind` 只映射成 `Monster / Npc / Prop / Zone`（`0/1→Monster, 2→Npc, 3→Prop(事件物件), 4→TriggerZone`），**永远不会产出 `Item`**。

后果是精确的、不是崩溃：一条 `giver = item:<物品模板id>` 的任务能入库、能发布、在总表里正常显示（带物品名），但**永远不会被"走近某个对象 → 只看这个对象给的任务"那条按实体 id 收窄的通路命中**（`QuestInteractionAuthority.TryResolveGiver` 解出的类别里没有 item）。玩家要在总表里自己找到它。

本轮的处理是把这件事**说破而不是藏起来**：`QuestTemplateValidator` 对 `giver.kind=item` 常驻一条 **Q043 Info**（"不会命中走近发布者的收窄可接列表，只会在总表里出现"），刻意不判 Error——把"物品能做发布者"这条能力从编辑器里摘掉是缩目标，不是修缺陷。回归 `QuestEditorContractTests.发布者是物品时能入库但要当场看清它走不通收窄可接` 同时钉住"不许有错误"和"npc / prop 不许跟着吃这条提示"（否则提示会变噪声）。

真修的两条路（都需要另一个域配合，别在任务侧单方面动）：① 给 DS 脚本生成通道加一类"物品型脚本实体"，让 `ScriptedKind` 能上报 item 并带上物品模板 id；② 认定世界里的"物品发布者"其实就是事件物件，把 `prop` 与物品模板 id 建立对应（`ToQuestKind` 里 `3→Prop` 时按模板 id 归属回判 item）。两条都比加一条 Info 贵，选哪条是产品决定。

### T12. 作者面上三个"填了没人读"的字段（🟡 中，2026-10-06）

编辑器/草稿契约里有三个字段能写、能存、**运行态没有任何消费方**（逐条 grep 过 `Horizon.Game.Sim` / `Grains` / `Game.Core` / `Entities` / `QuestCli` / 客户端 `HundunWorld/Source`）：

| 字段 | 现在到哪一步 | 谁读它 |
|---|---|---|
| `objectives[].mandatory` | 只活在建稿 JSON 里（`QuestDraft.cs:218` 序列化、`:243` 反序列化）；库里那条 `objectives` 列走 `QuestObjectiveCodec` 的 `type:target#count` 串，**格式里没有它的位置** | 无人 |
| `giver.zoneId` / `giver.spawnPointId` | 随草稿存着；校验器只做非负检查（`QuestTemplateValidator.cs:311`） | 无人 |
| `turnIn.zoneId` / `turnIn.spawnPointId` | **库里根本没有承载它们的列**（013 只给发布者加了 `giver_zone_id`/`giver_spawn_point_id`），upsert 时静默丢掉，且不进任何 Warnings | 无人；**也无处可存** |

> 2026-10-06 用真库往返把这条钉成了事实（`QuestAuthoringDbTests.发布者的范围限定入库而交付者的会被丢弃`）：同一份草稿里 `giver.zoneId=12 / spawn=34` 原样读回，`turnIn.zoneId=56 / spawn=78` 读回是 0，且 `Upsert` 返回的 Warnings 为空。**先前我写过"编辑器保存会把 CLI 填过的交付者限定清零"，那句不准确**——交付者限定从来没有一处可落，编辑器不保存它也一样丢；断言现在按现状写（读回 0），将来补了列它会变红，那时再来一起改这段说明，别顺手放宽。

同一轮在插件侧做了两处**只往好处走**的收口（`_verify.sh` 两模块 0 错）：目标条目的 `mandatory` 与交付者的两格范围限定，在保存时**从已加载的草稿继承**（判据是"类型 + 模板 id 没变"），不再无条件写死/新建清零——今天因为上面那条存储不对称它救不回多少东西，但它是正确的方向，且一旦补了列就不会再出现"编辑器一次保存抹掉一次作者意图"。

两条出路，**需要用户选，不要两边都留**：
1. 接上消费方：把 `zoneId/spawnPointId` 变成"限定发布者必须出现在某区域/某生成点"的真过滤（需要 DS 描述符里带 `SpawnPointId`——已经带了，见 `DsEntityDescriptorDto.SpawnPointId`，所以这条只差网关比较）；`mandatory` 要先决定它在 `objectives` 列里的编码位置，属于目标串格式变更，牵动编解码 + 校验 + 客户端显示，代价最大。
2. 从草稿契约里摘掉：删字段会同时动 `QuestDraft`、`QuestDraftModel.KnownTopLevelKeys`/键清单、`QuestEditorContractTests` 的漂移测试与 `QuestCli`，是**破坏性变更**，所以必须点头之后再做。

本轮先做的是**不许它假装有效**：把发布者区域/生成点两个输入框的占位文案从 `0=不限` 改成 `T12暂无消费方`（一眼能看到，不用去翻文档），交付者那两个字段维持现状但在本条里点名。真正的二选一留给上面两条。

### T13. Flax Tools 刷新清单少于客户端引用清单（✅ 已修并已执行刷新，2026-10-06）

`scripts/copy-shared-dlls-to-flax-tools.ps1` 默认只刷 `Horizon.Game.Message / Horizon.Game.Core / Horizon.Orleans.Interface`，但客户端脚本工程 `HundunWorld/Source/Game/Game.Build.cs` 的引用清单里还有 `Horizon.Game.Sim`、`Horizon.Game.ECS.Arch`、`Horizon.Game.World`、`Horizon.Game.ECS`、`Horizon.Core.Abstract`。Flax.Build 解析脚本程序集**优先用引擎 `Binaries\Tools` 里的副本**，所以这三类改动（含任务侧的 `QuestObjectiveCodec`/`HasProducer`）在编辑器里编译时吃的是旧 DLL，而仓库门禁 `scripts/verify-client-scripts.sh` 特意把仓库新鲜产物排在前、剔掉 Tools 同名引用——**两边看到的不是同一份代码**。

实测（比对两边 SHA-256 头 16 位，只读）：`Horizon.Game.Message` `e18a1070…` 一致、`Horizon.Game.Core` `7ba752ff…` 一致，而 `Horizon.Game.Sim` repo `644134bd…` / Tools `0e788dd1…`、`Horizon.Game.ECS.Arch` `3016e19b…` / `48b156eb…`、`Horizon.Game.World` `c8568654…` / `52c448a9…` **都是陈旧的**。

已修：清单扩到与 `Game.Build.cs` 对齐（PowerShell 解析器 0 错；8 个 `<name>\bin\Debug\net10.0\<name>.dll` 逐个实测存在，因为脚本就是按"程序集名 == 工程目录名"这条规则拼源路径的，`Horizon.Core.Abstract` 也自带一份产物，不靠别的项目拷过去的那份）。**还差用户跑一次提权刷新**（脚本要写 Program Files (x86)，非管理员会 fail fast，不会半拷）。

留下的结构性隐患**也已收口（2026-10-06）**：这两份清单（`Game.Build.cs` 里一份、刷新脚本里一份）以前只是"文档里提醒过"。现在有闸门钉住——`Horizon.QuestContractTests/FlaxToolsRefreshListTests.cs` 4 条：客户端引用的每个 `Horizon.*` 都必须出现在 `$Assemblies` 里；清单里不得留客户端不引用的名字（`Horizon.Orleans.Interface` 作为唯一例外写在断言里，不许靠放宽断言蒙过去）；清单里每个名字必须满足脚本自己的路径规则 `<name>\<name>.csproj` 真实存在；外加一条正向锚（Sim/ECS.Arch/World 必须被解析到，防止"两边都解析成空集所以绿"）。**做过突变验证**：从清单里删掉 `Horizon.Game.ECS` 与 `ECS.Arch` 两项，第一条断言立刻判红（另三条按预期仍绿，说明红的就是缺项那条）。恢复后 PowerShell 解析器 0 错，契约测试 **85 绿**。

---

## 相关文档

- [ARCHITECTURE.md](./ARCHITECTURE.md) — 当前架构（含本文件提及的设计）
- [NETCODE.md](./NETCODE.md) — 两套角色系统边界（§10）
- [DEVELOPMENT.md](./DEVELOPMENT.md) — 开发流程（含包管理建议）
