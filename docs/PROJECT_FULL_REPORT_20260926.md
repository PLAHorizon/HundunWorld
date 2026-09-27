# 混沌世界（HundunWorld）全项目综合报告

> 生成日期：2026-09-26
> 依据：`docs/` 设计文档基线 + 当前代码逐项核验（以代码为准；文中已标注文档滞后点）
> 范围：技术选型、技术栈、设计思想、架构、netcode 与服务器权威、玩法与数值公式、服务端/客户端功能全景、实现状态（已实现/开发中/未实现）、技术债与风险
> **2026-09-26 更新**：完成数值公式收敛与加点→服务端结算打通，本报告已按改动后状态同步。

---

## 章节文档导航（docs/report/）

| 章节 | 文件 | 对应本文 |
|---|---|---|
| 01 技术选型与规模 | [report/01-技术选型与规模.md](./report/01-技术选型与规模.md) | 一、二 |
| 02 架构与设计思想 | [report/02-架构与设计思想.md](./report/02-架构与设计思想.md) | 三 |
| 03 网络同步与服务器权威 | [report/03-网络同步与服务器权威.md](./report/03-网络同步与服务器权威.md) | 四 |
| 04 玩法与数值系统 | [report/04-玩法与数值系统.md](./report/04-玩法与数值系统.md) | 五 |
| 05 服务端功能全景 | [report/05-服务端功能全景.md](./report/05-服务端功能全景.md) | 六 |
| 06 客户端全景 | [report/06-客户端全景.md](./report/06-客户端全景.md) | 七（+启动器详情） |
| 07 实现状态与成熟度风险 | [report/07-实现状态与成熟度风险.md](./report/07-实现状态与成熟度风险.md) | 八、九 |

---

## 一、项目定位与总体规模

**混沌世界**是一款武侠题材分布式 MMORPG（成阳网络），采用"**双客户端 + Orleans Actor 集群 + 客户端预测/服务端权威 netcode**"形态：

- **游戏客户端**：Flax Engine 1.12（C#，产出 `Game.CSharp.dll`），水墨风 UI、第三人称武侠世界。
- **启动器**：Avalonia 11 超级应用"**耕地（GengDi）**"——集游戏启动/账号/花卉电商/AI 助手/IoT 大屏/IM 通话/音乐/天气于一体，四端壳（PC/Android/HarmonyOS/iOS）。
- **服务端**：.NET 10 + Orleans 10.0.1 集群（Silo）+ TouchSocket TCP 网关，SQL Server + Redis 哨兵持久化。

规模：monorepo，3 个 sln / 57 个 csproj（服务端主 sln 40 个）、Git 跟踪 6176 文件、服务端约 19 万行 C#、Ink UI 页面 75 个 dom-id 全注册、Grain 约 110 个、Gateway 单测 2611 用例。

---

## 二、技术选型与技术栈矩阵

| 领域 | 技术 | 版本 | 用途/选型理由 |
|---|---|---|---|
| 游戏引擎 | Flax Engine | 1.12 | 渲染与游戏逻辑宿主；C# 脚本（LangVersion 14） |
| 桌面 UI | Avalonia + FluentAvalonia（vendored fork，446 文件） | 11.3.12 | 耕地启动器跨平台 UI |
| Actor 框架 | Microsoft Orleans | 10.0.1 | 服务端全部业务对象 Grain 化；屏蔽激活/寻址/持久化/容错 |
| ECS | Arch / Arch.System | 2.0.0-beta / 1.1.0 | 客户端网络权威状态层（预测/协调/插值 System）；上游无 net10.0 资产，NoWarn NU1701 引用 net8.0 输出 |
| 网络传输 | TouchSocket TCP | 4.1.1 | 网关长连接（7789）；Flax 自带 ENet 配置未启用（死配置） |
| 序列化 | MemoryPack | 1.21.4 | 双端共享协议；每类型同时 `[MemoryPackable]`+`[GenerateSerializer]` 一套类型走 TCP 又走 Grain |
| 压缩 | K4os.LZ4 | 1.3.8 | 帧体 >256B 才压缩；4MB 解压上限防解压炸弹 |
| ORM | EF Core / Dapper | 10.0.2 / 2.1.66 | 7 个 DbContext 多库 |
| 缓存 | StackExchange.Redis / CSRedis | 2.10.14 / 3.8.807 | Orleans GrainStorage + presence + RediSearch 向量 |
| 其他存储 | MongoDB.Driver 3.6.0 / LiteDB 5.0.21 | — | LiteDB 为客户端本地库 |
| 认证 | IdentityServer4 | 4.1.2 | WebApi 账号体系（ROPC） |
| 后台 | AntDesign Blazor | 1.5.1 | WebAdmin（Game 10 页 + Flower 35 页） |
| AI | Microsoft Semantic Kernel | 1.57.0 | Azure OpenAI + RAG（花卉业务线） |
| IoT | MQTTnet | 4.3.7 | 自建 Broker + 设备→Grain 桥接 |
| 压测 | NBomber | 6.1.0 | 网络同步性能基线 |
| 可观测 | OpenTelemetry + Prometheus/Grafana/Alertmanager + Orleans Dashboard(1199) | 1.15.0 | 监控 |
| 运行时 | .NET 10（SDK 10.0.300 global.json 锁定） | — | 唯一例外：WPF 安装器 net48 |

传输拓扑：TCP 7789（游戏）/ UDP 8889 / WS 8890；IM 网关独立进程。数据面：SQL Server 7 库（Basic/Game/Article/Supports/Xingguang/Flower/IM）、Redis 哨兵集群（数据 9379、哨兵 6379；文档旧值 9379/9679/9779/9879）、MongoDB、对象存储。

---

## 三、架构分层与设计思想

### 3.1 五层单向依赖（契约与实现物理分离）

- **L0 契约**：Horizon.Core.Abstract / Share / **Game.Message（★双端共享协议）** / IM.Message
- **L1 领域**：Core、Model、Game.ECS（抽象）、Game.ECS.Arch、AI.Kernel
- **L2 基础设施**：Entities（EF）、Mapper、IoT.MQTT、**Orleans.Interface（★Grain 契约）**
- **L3 实现**：Orleans.Grains（100+）、Game.Core（Handler/校验/AOI）、Game.Sim（确定性模拟核心）
- **L4 宿主**：Silo、Game.Gateway、IM.Gateway、WebApi、WebAdmin

动机：Gateway 作为 Orleans Client 只引契约即可跨进程调 Grain，实现进程间解耦。

### 3.2 核心设计思想（八条）

1. **虚拟 Actor 为服务端灵魂**：玩家/实体/业务皆 Grain，"用单线程思维写高并发"；租约 90s + Despawn 重试兜底体现分布式最终一致性。
2. **双客户端 + 文件凭证注入**：Flax 客户端与耕地启动器不做网络通信，经 `HorizonGame.ini` 传 AuthToken/PassportId/网关列表——启动与运行时彻底解耦，游戏运行链路唯一（TouchSocket→Gateway→Orleans）。
3. **混合 Netcode（技术皇冠）**：本地玩家 = 客户端预测→服务端权威回放→回滚修正（0.5m）；远程实体 = 快照→自适应插值。三大关键决策：
   - `MovementFormula.Step()` **客户端/服务端共享同一实现**（FormulaVersion=2、重力 9.81、终端速度 50、禁 SIMD 保跨平台浮点逐位一致）；
   - `MovementValidator` 不依赖 Orleans，可在 Grain/单测/反外挂扫描器复用；
   - 协议 v1→v13 版本化演进（**文档基线仍写 v7，代码已到 v13**），每版修一个真实问题。
4. **双世界 ECS（客户端）**：自研 ECSManager 管视觉（Flax Actor），Arch ECS 管网络权威状态；ArchWorldHost 按 NetworkReceive→FixedUpdate→Update→Render→NetworkSend 五阶段流水线调度，`[ArchSystem]` 反射注册自动排序。动机：逻辑权威与视觉表现分离。
5. **双协议体系**：SyncPacket（系统高频同步，Kind fast-path 路由不解码）与 MessageUnion（用户请求）分通道互补。
6. **Observer + 有界 Channel fanout**：Grain→Gateway 用 `Channel(8192)+DropOldest` 无锁队列，"丢旧保新"（同步容忍丢帧不容忍延迟，8192≈6.8s@1200pkt/s）；弱网降级：带宽触阈值时快照 20Hz→10Hz，恢复需持续 3s（文档口径 100Kbps，项目记忆记录阈值已提升至 500Kbps——以运行配置为准复核）。
7. **物理 DLL 分发**：Flax Game.csproj 禁 NuGet/ProjectReference，共享库经 `CopyToFlax` MSBuild Target 复制 DLL 到 Flax Tools 目录，Game.Build.cs 注册 22 个 DLL——引擎限制下的务实妥协。
8. **文档同步原则**：docs/ 33 份设计文档 + 客户端内嵌约 30 份，注释携带决策依据（如 `#13 修复`、"安全审查 2026-08"），代码评审文化强。

---

## 四、Netcode 与服务器权威（代码级）

### 4.1 客户端预测

- 固定步长 `1/60s`（`LocalSimulationSystem.FixedDtSeconds`）。⚠️ 实现偏差：Flax 侧 `ECSUpdateDriver` 每渲染帧 Tick 且无 accumulator，即 tick 生成速率=渲染帧率、步长恒 1/60。
- 输入包 `InputPacket{ClientTick, InputBits, LookYaw/Pitch, MoveX/Y, CharacterId, PredictedEndX/Y/Z, MaxSpeed}`；发送队列软上限 256 DropOldest；未确认环形缓冲 64，`ClientTick - LastAcked > 5` 触发冗余重传，重传最近 10 个（`RetransmitMaxCount=10`；文档"全部重传"已过时）。
- 输入历史环形缓冲容量 256（≈4.3s@60Hz）。

### 4.2 服务端模拟与快照

- `ZoneShardGrain`（约 3400 行）60Hz Tick；**增量快照每 3 tick 广播（20Hz）**（60Hz 全速广播曾致 tick 堆积）；全量快照每 60 tick（1s）强制；静止实体 6 tick 心跳兜底。
- 增量字段阈值：Transform 0.01f、整数值差 ≥1；baseline 缺失自愈：`BaselineResyncRequestPacket`（Kind=16）→服务端下 tick 全量。
- Correction 不占快照节拍，以 `EventPacket` 随 fanout 立即下发。

### 4.3 服务器权威移动校验（`Horizon.Game.Sim/MovementValidator.cs`）

| 规则 | 阈值 | 违规原因码 |
|---|---|---|
| 位置漂移基准 | 0.5m（动态：`base + maxSpeed×rtt` 上限 12m） | PredictionDrift |
| 硬性速度帽 | 200 m/s | SpeedHackSuspected |
| 自报速度启发式 | 水平位移 > maxSpeed×1.5 | SpeedHackSuspected |
| 跳跃次数 | 普通 ≤2、轻功 ≤3（冲量 5.5/4.5/3.5） | JumpCountExceeded |
| 加速度 | 50 m/s²（≈5G，带时间窗防静止误判） | AccelerationExceeded |
| 瞬移 | 水平 100m | TeleportDetected |

- 地面约束：未注入 `GroundHeightSampler` 时信任客户端 `PredictedEndZ`，有三重防伪造护栏（|dz|≥10m 不信任、z==0 视为未设置、drift 兜底）。
- 校验失败→`CorrectionPacket{LastProcessedClientTick}`，防"重放已确认输入→Correction 风暴"死循环。
- P-F12 输入间隙吸附：批首 tick 不连续时权威位置直接对齐 PredictedEnd。

### 4.4 客户端和解（ReconciliationSystem）

- Ack 只清历史不重放；Correction：drift>0.5m → 过期判定 → 从权威位置重放未确认输入（截断最近 12 帧=200ms，同 `IMovementStepper`）→ `SmoothDamp3` 临界阻尼追平（15/s≈4 帧）。
- 修正风暴抑制：5 次/2s 窗口 → 1s 冷却；参数可经 appsettings `SyncReconciliation` 注入。

### 4.5 快照插值（远程实体，SnapshotApplySystem + InterpolationSystem）

- 自适应插值延迟 `targetDelay = max(avgInterval + 2·jitter, rtt/2 + rttJitter)`，clamp **[100, 200]ms**（文档写 400ms 上限已过时）；RTT EWMA α=0.125、抖动 β=0.25。
- Mirror 式快照时间缓冲 16 帧；`renderTick = latestServerTick − ceil(delay×60)`。
- 三路策略：缓冲时间插值（blend 8/s）/停止态温和收敛（0.01m）/移动态 Lerp+外推（上限 0.25s、宽限 0.1s、陈旧衰减 3/s）。
- 传送三档：≤200m 普通 Lerp；200~500m 加速混合 200ms smoothstep；>500m 硬跳。
- 实体生命周期状态机 Initializing/Active(≤1s)/Idle(1~5s)/Stale(5~90s)/Offline；90s 超时清理。

### 4.6 AOI 与双通道

- AOI：Morton 键空间索引（`Horizon.Game.World/ZoneShardAoi.cs` + `MortonCodec.cs`），chunk 进出订阅；SQL 侧 chunk_state/diff_log 按 morton 8 分区（`scripts/sql/001_world_state.sql`）。
- **双通道并存**：Orleans 通道为默认主轨（P-F3 合并 RPC：ReceiveInput+SubmitInput+BuildAck 单次 Grain 调用，RTT 3→1）；**DS 灰度通道**（`DsWorldSession`/`DsDataPlaneHost`/`DsDownlinkIngress`）走确定性内核 `Horizon.Game.Sim`，支持金轨迹对拍；已修复"晚加入收不到 Spawn（按会话补发）""RemovePlayer 不广播 Despawn（幽灵残留）""快照 Transform 漏换轴（3D 位置错乱）"等缺陷。

### 4.7 帧协议与安全

- 帧格式：6–8B 头（Length4+Kind1+Flags1，Kind 冗余写入支持 fast-path 路由）+ MemoryPack + LZ4（>256B）；4MB 解压上限。
- 反作弊/防护：移动校验六规则（§4.3）、攻速下限 500ms、输入去重、IP 连接频率限制、握手 FormulaVersion 校验（缺版本被拒）、角色指纹抢占（重复登录踢旧）、断线超时退回选角 + 按需重连流程。
- ⚠️ 未收口：NETCODE.md 会话绑定硬校验（interactorId==characterId）仍是 TODO，依赖灰度开关。

---

## 五、玩法与数值系统（公式级）

> 总前提：**服务端权威**。客户端只发意图（`CombatActionPacket` 仅含 AttackerId/TargetId/ActionKind/SkillId/ClientTick/Yaw），伤害数字全部由服务端结算后回包；客户端仅做表现。但代码中存在**多条并行公式路径**（见 §5.5 风险）。

### 5.1 角色属性模型

**运行时组件**（`HundunWorld/Source/Game/Character/Attributes/CharacterAttributesComponent.cs`，真正数据源）：
- 基础：Level、Exp、CurrentHealth/MaxHealth、PhysicalAttack/MagicAttack、PhysicalDefense/MagicDefense
- 能量：CurrentEnergy/MaxEnergy（内力，回复默认 10/s）、CurrentStamina/MaxStamina（体力，回复默认 5/s）
- 五行亲和 Metal/Wood/Water/Fire/Earth（0–10000）
- 成长阶段 CharacterStage：武侠 1–50 / 修仙 51–150 / 玄幻 151–300

**服务端持久**：`CharacterEntity`（Level/Experience/RealmLevel境界/CombatPower/Health/AttackPower/Defense/WuxingElement 0无1金…5土）+ `CharacterAttributeEntity`（表 `Game_HunduShijie_CharacterAttribute`：根骨/悟性/身法/臂力/内劲，派生列命中/闪避/暴击/暴伤/移速/攻速、五行攻抗、五行加点列 MetalPoint…EarthPoint、格挡/韧性/减伤/反伤/气盾/回血/回气）。

### 5.2 加点规则与派生属性公式

**点池**：每级 5 点，`AllocationPointsTotal = Level × 5`；九项加点：力量/敏捷/体质/内力/金/木/水/火/土。

**每点换算（服务端单一来源 `Horizon.Game.Core/Combat/AttributeDerivation.cs`，2026-09-26 起；客户端 `CharacterAttributesComponent.cs:270-320` 为展示镜像）**：

| 加点 | 每点效果 |
|---|---|
| 力量 | 生命上限 +20、攻击 +10 |
| 敏捷 | 防御 +20、移速 +3 |
| 体质 | 生命上限 +100 |
| 内力 | 内力上限 +10 |
| 金 | 亲和+10；攻+50、金伤+30、金防+5、破木防+30、暴击率+0.1%、暴伤+3% |
| 木 | 亲和+10；血+300、木伤+20、木防+5、破土防+30、回血+15/s、毒伤几率+0.1%、治疗率+0.2% |
| 水 | 亲和+10；内力+200、水伤+30、水防+5、破火防+30、冰冻几率+0.1%、减速+0.2%（上限70%） |
| 火 | 亲和+10；攻速+2、火伤+30、极速攻击几率+0.1%（触发×3攻速）、破金防+30、移速+5 |
| 土 | 亲和+10；防御+50、土伤+30、土防+5、破水防+30、反伤几率+0.1%（反弹10%） |

**合成公式（基值 + 装备 + 加点 三层）**：
- `MaxHealth = base + equip + 力量×20 + 体质×100 + 木×300`
- `PhysicalAttack = base + equip + 力量×10 + 金×50`
- `PhysicalDefense = base + equip + 敏捷×20 + 土×50`
- `MaxEnergy = base + equip + 内力×10 + 水×200`
- `MagicAttack/MagicDefense = base + equip`（**不吃加点，占位**）
- 派生：暴击率%=金×0.1、回血/s=木×15、移速加成=敏捷×3+火×5；元素伤/防/破防按上表乘点。

**数据流（权威闭环）**：客户端 `AttributeAllocationService` 只提交**期望终态**（九项全量，协议 235/236）→ 服务端 `CharacterGrain.AllocateAttributeAsync`（:813-849）校验 `total=5×max(1,Level)`、非负、`spent≤total` → Redis 状态 + SQL 落库 → 回执灌回组件（负点/超池直接清零）；全零=重置。

### 5.3 伤害计算（2026-09-26 收敛后：权威口径统一为 CombatCore 算式）

**统一口径**：`基础伤 = 攻击×0.5 + 技能附加`；`伤害 = 基础 × 五行 × (1 − 防/(防+100))`；暴击 ×倍率（默认 1.5）；格挡 ×0.5；round 后**保底 1 点**；真实伤害穿透免疫防御。

**A. Orleans 路径（CombatGrain，两段式）**：
1. 闪避 `roll < DodgeRate`；
2. 基础伤 `AttackPower×0.5 + SkillRawDamage`；
3. 五行 `×GetWuxingMultiplier`（仅 elementType>0）；
4. 格挡成功 ×0.5；
5. 暴击 `roll < CritRate` → ×CritMultiplier（默认 1.5）；
6. **防御减免统一在 HP 裁决点** `CharacterGrain.RequestHpChangeAsync`（HP 单一权威，有契约测试）：`减伤率 = defense/(defense+100)`，`mitigated = dmg×(1−减伤率)`，再叠 DamageReduction buff，`True` 伤害穿透。减免所用防御 = 基础防御 + 加点派生（与 CombatGrain 同源 `AttributeDerivation`）。
- ✅ 战斗属性已不再硬编码：`GetOrCreateCombatInfo` 读取角色基础值 + 属性表加点派生（攻/防/血上限/内力/暴击/暴伤），闪避/格挡优先取属性表列值、未落列时保留历史默认（0.05/0.1）；属性表不可用时降级为"未分配"。
- ⚠️ 掷骰用 `Random.Shared`（非确定性），与 DS 路径并存未合并。

**B. 确定性 DS 路径（`Horizon.Game.Sim/CombatCore.Resolve`，公式基准）**：
- 合法性/死亡/距离/朝向扇区（点积 vs 预存余弦，不调三角函数）→ 闪避（splitmix64 确定性掷骰）→ `base = AttackPower×0.5 + SkillRawDamage` → `damage = base × 五行 × (1 − d/(d+100))` → 暴击 ×(CritMultiplier||1.5) → `round`，**保底 1 点**。同输入序列同结果，为灰度对拍/金轨迹回放设计。
- ⚠️ `DsWorldSession.BuildPlayerStats` 仍用固定配置攻击/防御/暴击且 Element=0——DS 通道的加点闭环需网关控制面注入角色属性，属后续重构。

**C. CombatSystemGrain（P1.4）**：✅ **已收敛**（2026-09-26）——原"减法式" `Attack − Defense×100/(Defense+100)` 改为 CombatCore 口径乘法式（基础=攻×0.5，×(1−d/(d+100))，暴击 ×1.5、格挡 ×0.5、保底 1）。

**D. 客户端本地 DamageCalculator（仅预览/单机模拟，非权威、未接网络链路）**：
基础(物攻/法攻/元素/真实=Level×10 ×倍率) → 类型修正（守方 ×(1−min(resist,0.75))，抗性上限 75%）→ 五行（✅ 已对齐服务端：克 ×1.25、被克 ×0.8、相生连招 ×1.15）→ 暴击（倍率 1.5+CriticalDamage）→ 防御（×100/(100+Defense)，与乘法式同形）→ 随机波动 ±10%。
- ⚠️ `CombatSystemManager.ConvertToCombatStats` 把五行/加成字段全填 0，`CharacterAttributeManager` 基础属性硬编码（攻 100/防 50）——本地预览**仍不反映真实加点/装备/五行**。

### 5.4 五行、技能、经验、轻功

**五行**：None=0 金1 木2 水3 火4 土5。相克：金克木、木克土、土克水、水克火、火克金；相生：金生水、水生木、木生火、火生土、土生金。
- ✅ **倍率已收敛**（2026-09-26）：全端统一为服务端权威口径——相克 **×1.25**、被克 **×0.8**、相生 **×1.15**（原客户端预览 1.5/0.7/1.3 与 CombatSystemGrain 无五行已修正）。
- ⚠️ 遗留：服务端"被克"表保留历史非标准映射（`CombatCalculator.cs:46-51` 注释自认），修改影响权威数值与金轨迹，留待平衡性决策。
- 服务端另有：同元素协同 ×1.10；团队每个相生对 +5%、五行齐全 +20%；共鸣（基础 10%伤/5%防，高级 20%/10%，混沌五系齐全 35%/20%）；元素属性加成（金：暴击+5%物伤+15%；木：回血2%；水：闪避+4%冰冻3%；火：灼烧10%/tick；土：防御+12%护盾8%）。

**技能**：`SkillDatabase`（硬编码样例集）每技能带 Element+DamageMultiplier+EnergyCost（如裂金斩 ×1.5 耗30、万剑归宗终结 ×3.5 耗60、焚天灭地 ×4.0 耗80、磐石护体倍率 0 纯护盾）；升级倍率 `DamageMultiplier + DamagePerLevel×(level−1)`；五系技能文件 `Combat/WuxingSystem/{Metal…Earth}Skills.cs`。类型：攻击/控制/位移/辅助/终结；EffectSystem 支持 Buff/Debuff/HoT/控制。

**等级/经验**：仅客户端展示曲线 `MaxExp = 1000 + Level×500`；**服务端无 AddExp/升级结算**（GM 加经验被注释、升级事件仅打日志）、无升级属性成长公式——成长系统未实现，等级只影响加点点池。

**轻功（QinggongSystem，330 行，纯客户端）**：三段跳（段高系数 1.0/0.7/0.5，`base=min(2.0+(轻功等级−1)×0.5, 5.0)`，起跳速 `√(2×9.81×h)`）；耗能硬编码一段10/二段15/三段20/更多25 内力；滑翔重力 ×0.3、耗体力 5/s、上限 10s；空中/滑翔经输入位 bit3/bit4 上报，服务端按"轻功跳跃 ≤3"豁免。⚠️ 半成品：滑翔水平位移与动画调用被注释、QinggongLevel 无成长来源。

### 5.5 数值链路现状与残余风险

**2026-09-26 已完成**：① 五行倍率全端统一 1.25/0.8/1.15；② 防御减免全服务端乘法式统一（`d/(d+100)`，基准 100），CombatSystemGrain 减法式已收敛；③ 加点→服务端结算打通（`AttributeDerivation` 单一来源接入 CombatGrain 六项战斗属性：攻（力量/金）、防（敏捷/土）、血上限（力量/体质/木）、内力上限（内力/水）、暴击率与暴伤（金）；`RequestHpChangeAsync` 防御减免同源）。移速/攻速派生仍只在客户端表现层，未进服务端结算。

**残余**：
1. DS 灰度通道 `DsWorldSession.BuildPlayerStats` 仍用固定配置属性（需网关控制面注入，较大重构）。
2. 服务端"被克"表历史非标准映射未动（牵涉权威数值与金轨迹，留待平衡决策）。
3. 等级/经验/升级成长为空白；MagicAttack/MagicDefense 不吃加点；面板 struct `FromComponent` 错把基础属性填为 Level（展示占位）。
4. 五行防御缺火系（GetElementDefense(Fire)=0，规格未给）；客户端本地预览链（DamageCalculator/CharacterAttributeManager）仍为硬编码模拟、不反映真实加点。

---

## 六、服务端功能全景（Orleans Grain 谱系）

存储拓扑：**所有 GrainStorage（Default/GameStore/PassportStore/WorldSqlStore/FlowerStore/AIStore/PubSubStore）当前走 Redis**（原 SQL 配置注释保留为故障备份方案）；权威业务数据 SQLServer；presence TTL 90s；Reminders Redis。

| 家族 | 代表 Grain | 职责/状态 |
|---|---|---|
| 账号会话 | PassportGrain、GameUserGrain、UserCharacterManagerGrain、SessionManager、GameServerGrain、CharacterCreateIdempotencyStore | 通行证→用户→角色三层模型；建角幂等 |
| 角色成长 | **CharacterGrain**（1200+ 行）、InventoryGrain、SkillGrain、GameEquipmentGrain、AchievementGrain、RankingGrain、CraftingGrain、**WuxingAlchemyGrain（五行炼丹，品质 0–4）** | Redis 热缓存+SQL 权威落库；位置双轨（Redis 永久存储+GrainState 降级+归档服务） |
| 世界空间 | **ZoneShardGrain**（3400 行，60Hz 权威模拟）、PlayerSessionGrain、CharacterStateBridge、ShardManagerGrain（动态扩缩容）、WorldChunkCellGrain/WorldDiffLogGrain | 高成熟度；单 shard 硬编码正重构（IShardRouter） |
| 战斗 | CombatGrain、CombatSystemGrain、MonsterGrain（Idle→Patrol→Chase→Attack→Return→Dead + 仇恨表）、CombatCalculator | 怪物掉落/经验分配 Phase 2 TODO；**服务端无 NPC Grain（占位）** |
| 玩法系统 | QuestGrain（新旧两套）、InstanceGrain/InstanceManagerGrain、AuctionHouseGrain、GuildWarGrain、GmCommandGrain、ComplianceGrain（实名防沉迷）、EconomyMonitorGrain、LootTableGrain（掉落+保底） | **骨架/TODO 密集**：发奖、物品金币交割、副本回收几乎全未接线 |
| 社交 | ChatGrain（游戏内聊天，频道分片+SQL 落库+举报）、SocialGrain 好友、GuildGrain、TeamGrain、MailBoxGrain（30 天过期）、MessageRouter 等 6 频道 Grain、敏感词过滤、消息限频 | 聊天完整（刚上线） |
| 交易 | TradeGrain 面对面、MarketGrain 摆摊/拍卖 | 部分：状态持久化有、核心交割 TODO |
| PVP/跨服 | Arena 五件套（Battle/Matchmaking/Player/Ranking/Season）、CrossServer 三件套（Director/Island/Transfer） | 契约+实体+表齐备，逻辑深度中等 |
| 事件 | GameEventPublisher/Stream/ConsumerGrain（Orleans Memory Streams） | 事件总线在用 |
| 花卉电商 | 约 50 个 Flower*/Payment/物流（快递鸟）/对账/结算/订阅 Grain | **完整商业系统非 Demo**（双通道支付+掉单补偿） |
| IM | IMUserGrain、IMGroupGrain、Redis Outbox | 文本/媒体完整；群历史查询 NotSupportedException |
| IoT/AI | SensorDataGrain、IoTDeviceGrain、AlertRule、AIAnalyst/Embedding/RAGRetriever/KnowledgeBase/PlantingAdvice | 服务花卉业务线（RediSearch HNSW 1536 维向量），**与武侠玩法无关** |

**账号与安全**：IdentityServer4 仅 ROPC（Token 7 天 + 滑动 refresh 5 天），账密校验转发 IPassportGrain；WebApi 25+ Controller；WebAdmin（Game 10 页 + Flower 35 页）；已知短板：固定 AES Key/IV、遗留 LongAes、明文生产凭据入库（P0，待轮换）、cert.pfx 被跟踪。

---

## 七、客户端（Flax）UI、玩法交互与内容资产

### 7.1 UI 技术体系

- **100% C# 代码构建的 Ink 页面体系**：70+ `RegisterPage`、**75 个 dom-id 导航契约**、星型拓扑（战斗 HUD 为枢纽）、`InkPageShell` 五层结构（背景/暗角/内容/粒子/返回按钮）、墨韵涟漪粒子反馈。
- **1920×1080 设计空间等比缩放**（`UILayout.Scale=min(ScaleX,ScaleY)`；战斗 HUD 顶部对齐防信箱下沉）。
- **设计 Token 水墨风**：深墨黑 #0E1016、鎏金 #C8A858、朱红 #C0392B、纸色 #F5F0E8（对应设计稿 `hundun-yy-ui/colors_and_type.css`）。
- 旧体系 GameMain UI 与 Prefab 库（60 个 .prefab）并存但进世界后不激活；`PopupVerification` 弹窗层 8 页不铺墨底。

### 7.2 页面全景（设计稿 56 页全部映射，全部有实现）

创角/开场 7 页（行囊式角色列表 + 五步创角 gender→face-preset→face-sculpt→name + Loading/分镜参考）；战斗/引导 10 页（combat-hud v1 100%、**combat-hud-traditional 默认枢纽**、V2 77%、guide 系 64–67%、死亡 80%、QTE 80%、点穴 53%、元素视野 50%）；角色养成 9 页（CharacterPanelPage 背包已并入、SkillPanel、外观 95%、备战 94%、门派 85%、装备 42% 排除范围）；任务/图鉴（任务 98%、图鉴 92%、世界地图/罗盘/副本入口）；生活/社交/商城 10+ 页（64–100%）；商业化（抽卡 90%、活动 100%、战令 79%）；弹窗/奖励/系统工具（设置 98%、拍照 89%、F9 UI 画廊）。

### 7.3 传统 HUD 布局（默认主界面）

左上角色面板 → 顶部中央罗盘带（方位+POI+坐标条，置顶距离 10px）→ 右上 160px 圆形小地图+8 环绕按钮 → 左下聊天框（7 频道 Tab：附近/世界/门派/队伍/团队/密聊/系统）→ 右下功能按钮（含沉浸模式切换，传统⇄V2 互切）→ 底部经验条。快捷键：C=角色/行囊、F9=UI 画廊、-=切头顶名牌。

### 7.4 3D 角色预览（现役 CharacterPreview3D，1415 行）

SceneRenderTask 离屏渲染（CustomActors 白名单不污染主场景）+ 自建三点光 + 显式 BindSceneSky + 场景卸载野指针防护；**禁用动画图、程序化待机**（缓存基准骨骼矩阵后逐帧叠加呼吸微动，UE 命名骨骼 spine_01..03/head 等）；拖拽旋转 0.01 rad/px、俯仰限 −18°~26°；Ry(180)·Rx(180) 姿态校正；MetaHuman 为材质/JSON 预设框架（无实模）。

### 7.5 玩法交互现状

- **第三人称相机**（ThirdPersonCamera 3900+ 行：碰撞射线三档、异步缓存、震屏、弹簧臂）。
- **点击移动**：Alt+左键，注入与 WASD 等价的方向输入走 ECS 主链路；无烘焙 NavMesh 故直线移动；看门狗 25s/到达半径 120；目的地标记组件进行中（工作区未提交）。
- **游戏内聊天**：完整接线（ChatClient↔ChatGrain，发送/历史/撤回/举报/静音/未读）。
- **本地贴地/传送**：FastTravelHandler 经权威对齐通道（防悬空）。

### 7.6 编辑器 AI Agent（HundunAgent，取代已删除的 TraeBridge）

MCP 服务器（Streamable HTTP `localhost:21901/mcp`，JSON-RPC 2025-03-26）+ REST 21900 + 编辑器内聊天窗（任意 OpenAI 兼容 API）；约 30 工具/16 类：场景/Actor/预制体/材质/资产取证/视口截图/日志扫描/代码读写+构建等待/**PIE 运行态 UI 诊断（ui_tree/log_scan）**/UI 设计与审计/任务控制+undo 检查点回滚；危险操作需确认，审计日志 jsonl。

### 7.7 内容资产

- 场景 7 个 .scene（Login/Character/World/Main/RootScene/TransitionScene/PrefabGalleryTest）；World 体量小（22KB）。
- **唯一身体模型** = UEFN mannequin FBX；⚠️ 其导入产物在 Content 中当前缺失，依赖编辑器自愈插件重导（历史上反复丢失）。
- 动画资产约 45 个（Pro Magic Pack 等）+ Animation Graph，但**链路不可靠**（曾空图/坏 GUID；预览主动禁用动画图；CharacterAnimator 全 log 占位）——运行时表现以绑定姿态+程序化待机为主。
- PCG 插件：运行时 Phase 1 完成（数据层/执行器/3 节点），**图形编辑器 UI 未实现**，未见 PCG 图接入 World。

---

## 八、实现状态总表

### 8.1 已实现（可运行、权威闭环）

- 双端连接/握手/心跳/断线重连/角色指纹抢占/连接频率限制
- 客户端预测+服务端校验和解+快照插值全链（含自适应延迟、传送三档、风暴抑制）
- AOI（Morton）+ 增量/全量快照 + baseline 自愈 + 双通道（Orleans 主轨 + DS 灰度）
- 登录→选角→创角→主 HUD→游戏世界全流程；75 页水墨 Ink UI 导航体系
- 移动（WASD/点击移动/轻功跳）、服务器权威校验六规则
- **属性加点（客户端提交→服务端校验落库→回执）与派生属性公式，且已接入战斗结算**（2026-09-26：`AttributeDerivation` 单一来源，CombatGrain/HP 裁决读取基础值+加点派生）
- 背包/物品/装备（服务端完整+客户端面板）、游戏内聊天（全频道）、FastTravel 传送
- 服务端伤害结算（CombatGrain/CombatCore 双路径、公式口径已统一）、五行相克/协同/共鸣（倍率全端一致）、技能施法冷却
- 账号体系（IdentityServer+PassportGrain）、WebAdmin 后台
- 花卉电商全链（支付/对账/物流/AI 分析）、IM 文本+媒体+信令通话（局域网可用）、IoT MQTT、启动器 12 模块
- HundunAgent 编辑器 MCP、UE5ToFlaxConverter 工具、压测与监控体系

### 8.2 正在开发（当前冲刺）

- **网络同步 1.0.x 迭代**（commit 序列 1.0.1→1.0.7；1.0.4 自述仅 1/3 能力；当前主战场）
- 角色基础信息同步（最新 commit `97d7c1f`）
- 点击移动目的地标记（工作区未提交 `ClickToMoveDestinationMarker.cs`）
- 耕地 UI 重构（**未完**，设计稿在 `gengdi-trading-ui-redesign/` 等）
- Shard 路由重构（IShardRouter，替代硬编码 shard 0）
- 设计稿文案落地复核（约 30 页示例数据待替换为设计初值）
- Ink 装备槽/数据绑定/时辰罗盘/经脉图（.trae/specs 83/86 等收尾）

### 8.3 未实现 / 占位

- **等级/经验/升级成长系统**（服务端零实现，仅客户端展示曲线）
- DS 灰度通道战斗属性未接角色数据（`BuildPlayerStats` 固定配置）；服务端"被克"表历史映射待平衡决策（原"三套五行倍率、两套减免公式并存、战斗属性硬编码"已于 2026-09-26 收敛打通）
- 拍卖行物品/金币交割、GM 命令、副本关闭回收、公会战发奖、任务发奖（TODO 桩）
- 服务端 NPC/怪物掉落与经验分配、AI 行为树增强
- 音频系统（未实现）、完整动画状态机/混合树/根运动、GAS Cost/Cooldown、存档（仅框架）、NarrativeSyncManager（返回 null）
- 客户端旧社交层（ChatSystem/TeamSystem/TradeSystem 本地模拟未联网，现役聊天走新 ChatClient）
- 传统 HUD 技能栏真实施法接线（现 mock；真 TryCast 在弃用旧 UI）；任务点击寻路导航
- PCG 图形编辑器、MetaHuman 实模、多人玩法内容（组队/门派/副本玩法闭环）
- NAT 穿透/TURN/SRTP（通话公网场景缺位）
- 工程纪律：零 CI、无 CONTRIBUTING/LICENSE、明文凭据入库（P0）、131 处 async void、Nullable 不统一、Flax 客户端零自动化测试、仓库卫生（21 个 .log 等混入 git）

---

## 九、成熟度评价与风险结论

| 子系统 | 成熟度 | 关键风险 |
|---|---|---|
| Gateway/Netcode | ★全仓最高 | 会话绑定硬校验未收口 |
| ZoneShard 空间权威 | 高 | 3400 行上帝类、单 shard 重构中 |
| 移动同步链路 | 高 | 文档基线滞后代码（v7 vs v13） |
| 角色/加点/背包 | 高（2026-09-26 后更高） | **两套角色系统语义重叠（瞬态移动 vs 持久 RPG，HP 双写风险）为全仓最大架构隐患**；已由契约测试锁 HP 单一权威；加点→结算已闭环 |
| 数值/战斗结算 | 中高 | 公式已统一 CombatCore 口径、属性已接加点；成长曲线空白、DS 通道属性未接入 |
| MMO 玩法（任务/副本/拍卖/公会战/GM） | 中偏低 | 骨架齐、交割 TODO 密集 |
| 花卉电商/支付/IM/IoT/RAG | 高 | 成熟度 > MMO 玩法本身；属存量业务线 |
| 客户端 UI/Ink | 高 | 30 页文案待落地；动画/音频为短板 |
| 内容资产 | 低 | 唯一人偶模型且导入产物易丢失；动画空图史 |
| 工程纪律（横切） | 低 | 凭据入库/零 CI/仓库卫生——**短板不在架构，而在纪律** |

**总评**：架构成熟度呈"倒挂"形态——基础设施（netcode/网关/Actor/存储/UI 框架）达生产级教科书水准并有大量用例守护，而上层玩法内容（成长/任务/副本/社交玩法闭环）大量占位。近期路线：①安全止血（轮换凭据+CI）；②~~数值收敛~~（**已于 2026-09-26 完成公式收敛与加点打通**，剩余：经验成长、DS 通道属性接入、"被克"表标准化）；③玩法交割（拍卖/任务/掉落 TODO 接线）；④表现补齐（动画状态机/音频/技能栏施法）。

---

## 附：关键证据索引

- 加点公式：服务端单一来源 `Horizon.Game.Core/Combat/AttributeDerivation.cs`（2026-09-26）；客户端镜像 `HundunWorld/Source/Game/Character/Attributes/CharacterAttributesComponent.cs:53-384`；服务端校验：`Horizon.Orleans.Grains/CharacterGrain.cs:813-945`；协议：`Horizon.Game.Message/Network/CharacterAttributeMessages.cs`（235/236）
- 伤害（确定性）：`Horizon.Game.Sim/CombatCore.cs:173-341`；（Orleans）：`CombatGrain.cs:72-164` + `CharacterGrain.cs:1062-1150`；（客户端预览）：`Source/Game/Combat/DamageCalculator.cs:57-273`
- 五行规则：`Source/Game/Character/Attributes/WuxingElement.cs:38-99`；服务端倍率：`CombatCalculator.cs:31-57`（1.25/0.8）
- 移动校验：`Horizon.Game.Sim/MovementValidator.cs:24-74`；预测/和解/插值：`Horizon.Game.ECS.Arch/Systems/{LocalSimulationSystem,ReconciliationSystem,SnapshotApplySystem,InterpolationSystem}.cs`
- 空间权威：`Horizon.Orleans.Grains/World/ZoneShardGrain.cs`；AOI：`Horizon.Game.World/ZoneShardAoi.cs`
- UI 路由：`HundunWorld/Source/Game/UI/Ink/InkPageRouter.cs`、`MainUIManager.cs:867-956`；3D 预览：`UI/Ink/Components/CharacterPreview3D.cs`
- 存储拓扑：`Horizon.Orleans.Silo/Program.cs:456-560`；SQL：`scripts/sql/001_world_state.sql`
- 文档基线：`docs/ARCHITECTURE.md`、`docs/NETCODE.md`、`docs/CLIENT_FEATURES.md`、`docs/TECH_DEBT.md`、`docs/DESIGN_PHILOSOPHY_REPORT.md`、`docs/DEEP_ANALYSIS_REPORT_20260922.md`
