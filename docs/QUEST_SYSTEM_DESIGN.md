# MMORPG 任务系统设计方案（已确认口径 · 落地中）

> 日期：2026-10-05
> 目标：在 Flax 编辑器中可视化编辑任务内容 / 完成条件 / 任务奖励；任务可关联 NPC、怪物、其它生物、物品作为发布者与触发者；奖励可关联全部游戏物品；预留不停服的实时发布 / 关闭 / 取消接口。
> 决策（用户已拍板）：**D1 权威源 = SQL Server 表** ｜ **D2 编辑器 = 独立 Flax 插件 QuestEditor** ｜ **D4 实时发布 = 三入口全做** ｜ **D9 编辑器写库 = 短生命周期 CLI 子进程** ｜ 节奏 = M1→M5 连续推进。

---

## 1. 现状（先认账，别重复造）

本仓库在 2026-10-04 已经有一条**能跑的任务运行时切片**（不是空壳）：

| 组件 | 位置 | 状态 |
|---|---|---|
| 模板表 | `Horizon.Model/GameModel/QuestTemplateEntity.cs` → `Game_HunduShijie_QuestTemplate` | 有实体、有迁移 `20261004055032_AddWuxingQuestTemplate`、有种子 `scripts/sql/012_seed_wuxing_entry_quests.sql`（90001–90005） |
| 模板读取 | `Horizon.Orleans.Grains/QuestGrain.cs:172 AcceptFromTemplateAsync` | 接任务只收 questId，名称/描述/目标/奖励一律读库（**fail closed**：客户端不能自带 rewards） |
| 网关路由 | `Horizon.Game.Core/Handlers/QuestHandler.cs` | Accept / Complete / QuestList 已接真 Grain；发奖 `GrantQuestRewardsAsync` 把 `item:<id>` 交 `IInventoryGrain`、`favor:<域>` 交好感 Grain |
| 击杀事件源 | `Horizon.Game.Core/Sim/Server/DsGrainContracts.cs:201 KillObjectiveTypeMatcher` + `OrleansDsGrainAdapters.cs OrleansQuestProgressWriter` | DS 击杀 → 目标推进 |
| 目标/奖励字面量 | `objectives` = `[{"type":"kill_domain:3","desc":"…","count":5}]`；`rewards` = `{"item:810101":1,"favor:1":20,"exp":80,"gold":1000}` | **协议，不可改名**；只认数字值（字符串值被静默丢弃） |
| 角色任务槽 | `GrainStateModels.cs`（`ActiveQuests`/`CompletedQuests`，上限 20） | 在用 |
| 客户端 UI | `UI/Ink/Pages/Combat/CombatHudTraditionalPage.cs`（右侧追踪）、`UI/Ink/Pages/Quest/QuestLogPage.cs`、`UI/Ink/Pages/MenuQuestsPage.cs` | 三处都改成服务端数据驱动（2026-10-05，编译级）；`QuestTrackerHUD.cs` 仍是**没有任何调用方的孤岛文件**，别误当生效的追踪器 |
| NarrativePro | `Plugins/NarrativePro/` | UE5 状态机移植，与"目标+计数"模型不通用；本次不并入、不改动 |

本次新增的是**缺失的三层**：可视化编辑（D2）、把模板扩到能表达"发布者/触发者 + 发布状态 + 前置"（数据层）、不停服的上下线（接口层）。

---

## 2. 架构

```
Flax 编辑器  Plugins/QuestEditor（真 EditorWindow，Tools 菜单）
   │  每次读/写起一个短命子进程（编辑器进程不碰 SqlClient，也不自带第二套校验器）
   ▼
Horizon.QuestCli   verbs: catalog | list | get | validate | draft save|list|get|delete | publish | online | unpublish | retire
   │  System.Text.Json（UTF-8）+ EF Core
   ▼
SQL Server  Game_HunduShijie_QuestTemplate  ←─ 唯一权威模板源（D1）
   │  读取：QuestManagerGrain（10s TTL 缓存 + 版本号对账）/ QuestGrain（接取侧）
   ▼
Horizon.Orleans.Grains  QuestManagerGrain（发布态·持有者·撤销队列）+ QuestGrain（实例状态机·发奖）
   │  MemoryPack 协议（QuestListRequest/Response 1383/1384 等）
   ▼
Horizon.Game.Core/Handlers/QuestHandler（网关编排：读等级 → 读实例 → 问 Manager → 落地撤销）
   ▼
Flax 客户端 QuestService / QuestLogPage / QuestTrackerHUD（M4 待去 Mock）
```

职责边界（沿用仓库既有纪律）：
- **模板层是配置不是数据**：不建实例表，进度留在 Orleans Grain 状态里。
- **判定全在服务端**：客户端只显示服务端下发的摘要；接取只传 questId。
- **校验器只有一份**（`Horizon.Game.Sim.Quests.QuestTemplateValidator`）：CLI 入库前、`PublishAsync` 发布前跑的是同一段代码。
- **Grain 之间不回 call**：强制取消走"撤销队列"，由网关取走执行（Orleans 10 无 `[Reentrant]`，互调会拧成超时）。

---

## 3. 数据模型

### 3.1 新增列（迁移 `20261005131122_AddQuestEditorColumns`）

| 列 | 类型 | 含义 |
|---|---|---|
| `giver_kind` | int | 发布者类别：1=NPC 2=怪物 3=物品 4=物件 5=区域（与 `QuestEntityKind` 同序） |
| `giver_template_id` | int | 发布者模板 id（0=未配置） |
| `giver_zone_id` / `giver_spawn_point_id` | int | 限定区域 / 限定生成点（0=不限） |
| `turn_in_kind` / `turn_in_template_id` | int | 交付者（0=沿用发布者） |
| `prerequisite_quests` | varchar(200) | 前置任务 id，逗号分隔 |
| `publish_state` | int | **0=下线 1=已发布**（玩法开关，与 `IsValid` 的数据作废分列） |
| `content_version` | int | 内容版本号，每次入库自增 |

`rewards` / `objectives` 两个 JSON 列沿用既有格式，不改名、不换结构。

### 3.2 实体引用（覆盖"人 / 怪物 / 其它生物 / 物品"）

`QuestEntityRef(Kind, TemplateId, ZoneId, SpawnPointId)`，`Kind ∈ {Npc, Monster, Creature→Monster 复用, Item, Prop, Zone}`。
用模板 id 而非 prefab guid：工程内 NPC 无 prefab/场景名注册表，运行时身份就是 `模板 id + 生成点 id`。

发布者合法性：`CanBeGiver = Npc | Item | Prop`（怪物与地点不发布任务）。

### 3.3 完成条件语法（`QuestObjectiveCodec`）

```
kill_monster:<怪物模板id>   kill_domain:<1..5>   talk_npc:<NPC模板id>
collect_item:<物品id>       use_item:<物品id>    reach_zone:<区域id>    interact_prop:<模板id>
```

`HasProducer()` 明确登记哪些类型**当前真有事件源**：只有 `kill_monster` / `kill_domain`（DS 击杀事件，经 `KillObjectiveTypeMatcher`）。其余五类——含 `talk_npc`——**校验器按错误拒绝**。

> **`talk_npc` 为什么被划回"无源"**（这一条是本仓库"判定权在服务端"纪律的具体化）：客户端的选中层只有 `SelectableEntityTag.EntityId`（`(spawnPointId<<32)|slot`）与 `SelectableKind`，**没有模板 id**；而 `QuestListRequestMessage.GiverKind/GiverTemplateId` 是客户端自己报的数。如果网关拿这两个值去推进对话类目标，就等于让玩家可以站在主城原地报"我刚和铁匠说过话"。因此：这两个字段现在**只作可接列表的显示过滤器**（列表内容仍由服务端按等级/前置/发布态算），推进 `talk_npc` 的权威事件必须来自 DS。可选的三条接法见 §11。

### 3.4 奖励（`QuestRewardSpec`）

结构化 ↔ `rewards` 列双向可逆；`item:<任意物品模板id>` 覆盖装备/丹药/材料/宝石/任务物品/秘籍（秘籍 id = `800000 + 技能id`），`favor:<1..5>`、`exp`、`gold`。

> 已知不一致（记入 `docs/TECH_DEBT.md`）：客户端 `ItemCatalog.ItemKind`（装备1/消耗2/材料3/任务4/宝石5/秘籍6）与服务端 `item_type`（0武器…8宝箱）**不同序**，`ItemCatalog.cs:20` 的"同序"注释只对秘籍成立。奖励选择器按 `ItemCatalog.Resolve` 的显示分组走，不再引入第三套枚举。

### 3.5 草稿 JSON（编辑器 ↔ CLI 的唯一交换格式）

字段名即协议。`QuestEditorContractTests.草稿顶层字段名不得漂移` 会在改名时立刻变红（System.Text.Json 对不认识的键是静默丢弃的，漂移的表现是"编辑器里填了、库里是空的"）：

```json
{ "questId":10001, "questName":"…", "description":"…", "questType":0, "levelRequire":1,
  "wuxingDomain":0, "repeatLimit":0, "publishState":1, "isValid":true, "contentVersion":1,
  "giver":{"kind":"npc","templateId":1,"zoneId":0,"spawnPointId":0},
  "turnIn":{"kind":"npc","templateId":2,"zoneId":0,"spawnPointId":0},
  "prerequisiteQuests":[90001],
  "objectives":[{"kind":"kill_monster","templateId":4,"count":3,"description":"…","mandatory":true}],
  "rewards":{"exp":120,"gold":500,"items":[{"templateId":810101,"count":1}],"favor":[{"domain":1,"points":10}]} }
```

---

## 4. Flax 可视化编辑器（已落地）

`HundunWorld/Plugins/QuestEditor/`（已在 `HundunWorld.flaxproj` 的 `References` 注册）：

```
QuestEditor.flaxproj
Source/QuestEditor/            运行时装：QuestEditor.Build.cs + QuestDraftModel.cs（客户端以后也读这份模型）
Source/QuestEditor/            QuestEditorTarget.Build.cs / QuestEditorEditorTarget.Build.cs
Source/QuestEditorEditor/      QuestEditorEditor.Build.cs / QuestEditorPlugin.cs（EditorPlugin → Tools 菜单）
                               QuestEditorWindow.cs（FlaxEngine.Windows.EditorWindow）
                               QuestCliBridge.cs（子进程 + JSON 信封）
_verify.sh                     离线 csc 编译自检（复用 HundunAgent 的真实响应文件当引用集）
```

窗口分区：工具条（探测/刷新/新建/校验/保存/发布/下线/作废 + CLI 路径）｜左侧任务清单（`●`=已发布 `○`=下线）｜A 列基本信息 + 发布者 + 交付者 + 前置｜B 列完成条件 + 奖励（经验/铜币/物品/好感）+ 物品查询｜底部输出区。

要点：
- 引用一律按 **模板 id + 名称回显**（"4 银爪狸"），输入未知 id 不阻断编辑，但保存会被服务端校验拒掉；
- "＋添加"与"×"用固定行数池（80 任务行 / 6 目标 / 6 物品 / 5 好感），避开控件销毁 API 的不确定性；
- 构造期布局全部 `TopLeft` 锚点 + 显式 `Bounds`（`flax-anchor-presets-near-far` 的既有坑）；
- `CheckBox` 在 Flax 1.12 没有 `Text`，`EditorWindow` 在 `FlaxEditor.Windows`——这两条都是 `_verify.sh` 编出来的，不是猜的；
- 保存前做一次**字段名自检**（`CheckKeyNamesSelfTest`），把 JSON 键与服务端认识的键名对账。

MCP 侧 `quest_*` 工具（HundunAgent）列为后续项，未实施。

## 5. CLI 桥（Horizon.QuestCli）

- 只依赖 `Horizon.Entities / Horizon.Model / Horizon.Game.Sim`，**不引用 Gateway 与 Silo**；
- 连接串：环境变量 `DB_GAME_CONNECTION_STRING` 优先，否则从仓库根或 `Horizon.Orleans.Silo/bin/Debug/net10.0/.env` 读（EF 设计时不展开 `${VAR}`，所以必须自己取）；
- 输出恒为一段 JSON `{ok,verb,message,data}`，退出码 0/1；中文按 UTF-8 原样输出；
- **输入侧的编码契约（2026-10-06 重写；前一版写法本身是错的，别再改回去）**：编辑部现在**手工写 UTF-8 字节**（`Encoding.UTF8.GetBytes` 从不带 BOM），CLI 用 `Console.OpenStandardInput()` + 显式 UTF-8 读取并 `TrimStart('\uFEFF')`。三处错法都在现场或变异里复现过：① 编辑器原来设 `StandardInputEncoding = Encoding.UTF8`——这个 setter **只在输入被重定向时才允许设置**，而不带草稿的动词（catalog/list/publish…）根本不重定向 stdin，于是 `Process.Start()` 直接抛 `StandardInputEncoding is only supported when standard input is redirected.`，现场表现是"只要不带草稿，所有按钮都起不了 CLI 进程"；② 即便设成了，`Encoding.UTF8` 的编码器**会写 BOM**，草稿首字节成 `0xEF 0xBB 0xBF`；③ CLI 侧 `Console.InputEncoding = Encoding.UTF8` 在重定向句柄上会抛 IOException，被 `catch` 吞掉后**悄悄退回控制台旧码页（936）**，BOM 被解成 `ï` → STJ 报 `'0xEF' is an invalid start of a value`，中文任务名同样被读坏——一个 catch 把编码问题伪装成了"JSON/协议问题"。
  核验 `scripts/verify-quest-cli-encoding.py`（不带 stdin 的动词 / 带 BOM 的草稿 / 无 BOM 的草稿）；并把 CLI 解码器临时改成 936 做过**变异验证**：带 BOM 那条立刻复现 `'0xE9' is an invalid start of a value`，证明它抓的就是这个根因。⚠️ 判据要落在**语义**上：这套探针第一版写的 GBK 反向对照**无效**——JSON 结构字符全是 ASCII，中文坏成 U+FFFD 之后结构照样解析，而 `validate` 的回执不含草稿正文，从 stdout 上看不出区别。"编码没坏"不能只看回执文本，要看有没有把内容回显出来的通道。
- **草稿层（2026-10-06，用户拍板）**：`draft save/list/get/delete` 只碰 Redis，**保存 = 只写 Redis 草稿**（72 小时、每次保存续期），**发布是唯一写 SQL Server 的动作**（取草稿 → 校验 → `Upsert` → 置已发布 → 删掉 Redis 草稿）；"把库里已有的下线行放上线"改为 `online`（原 `publish` 语义），不把两个动作挤在一个词上。键布局：正文 `quest:draft:{id}`（值为 `QuestDraft.ToJson()` 原文，与 validate/upsert 同一份序列化），索引 `quest:drafts`（ZSET，score=最后写入毫秒）。索引会因正文先被 TTL 删除而留垃圾项，所以**惰性剔除**且从不作为存在性判据。再编辑一条已发布任务时，Redis 没有草稿就从 SQL 行反生成（`QuestTemplateMapper.ToDraft`），因此不存在"发布后改不动"。
  编辑器清单把草稿并进来（`✎` 标记 + 剩余小时，库里已有且 Redis 有草稿 = "改了但没发布"），点开时**有草稿就先读草稿**（`draft get` 与 `get` 的 data 形状刻意做成一样 `{questId, json}`，载入路径不分叉）；Redis 不可用时只提示，不影响目录刷新。
  失败路径写死：内容校验不过 → **一行都不写**、草稿原地保留；内容过了但发布那步被拒 → 明确回"已入库为**下线**状态"，把库里真实状态说出来，而不是笼统报失败让人以为没写。
  核验 `python scripts/verify-quest-draft-redis.py`：Redis 往返中文按**字段值**比对（`ToJson` 会把非 ASCII 转成 `\uXXXX`，拿字面串找会误判"/**中文丢了**"）、TTL≈72h、删除即失效、**无草稿时发布被拒且 SQL 侧查无此行**。
  ⚠️ **Redis 连接串的解析顺序（2026-10-06 现场补）**：环境变量（`HORIZON_QUEST_REDIS` → `HORIZON_ORLEANS_REDIS` → `GATEWAY_REDIS_CONNECTION_STRING` → `SILO_/REDIS_CONNECTION_STRING`）→ **各服务 `.env`**（与 DB 连接串同一批文件）→ 本机 dev 兜底 `127.0.0.1:9379`。**每个候选都必须真打一次 PING 才算可用**：`abortConnect=false` 时 `Connect()` 无论目标通不通都会返回对象，"没抛异常"不构成"连上了"。失败信息里回显**用的是哪一个来源**（实测为 `Horizon.Orleans.Silo\bin\Debug\net10.0\.env 的 GATEWAY_REDIS_CONNECTION_STRING`）——因为编辑器把 CLI 当子进程拉起时，子进程环境里**没有任何** `HORIZON_*` 变量，这正是"保存草稿失败：未设置 HORIZON_QUEST_REDIS / HORIZON_ORLEANS_REDIS"那条现场报错的成因。
- **作废与删除的分工（2026-10-06，用户要求"作废后可删除但不影响已接该任务的角色"＋"未发布的草稿可直接删除"）**：`retire` = 下线 + 停止新接取，行仍在库里（作者还能看、能改）；`delete` 按"这东西上没上线过"分两条路——**从未发布过的草稿（Redis 里那份）直接删，不要求先作废**（它没上线过、不可能有持有者；硬要作废只会让人走一步无意义流程，还会报"模板不存在"）；**库里的行必须先作废**（未作废会被拒并回"请先作废"），且这种情况下**不顺手删草稿**——那可能是作者正在改的东西，"放弃未发布的改动"是另一个意图（`draft delete`），不该由"删除"代做。作废是**终态**：已废的行不能再 `online`（库里实测出现过 `IsValid=0` 且 `publish_state=1` 的行，它不被接取却在清单里显示"已发布"，最容易误导运营）。`delete` 幂等：再删一次回"不存在"而不是抛异常；删已作废行时若还有同 id 草稿会一并清掉，回执里用 `deletedDraft`/`deletedRow` 两个标志把"清了什么"说清。
  **"不影响已接玩家"的依据是结构性的，不是运气**：接取时 `QuestGrain.AcceptFromTemplateAsync` 已经把 名字/描述/目标/奖励 抄进角色自己的 `QuestData`，而完成路径（`QuestGrain.CompleteQuestAsync` → 网关 `GrantQuestRewardsAsync` 用 `result.Rewards`）**全程不回查模板表**——模板上下文 `_templates` 在整个 `QuestGrain` 里只有接取那一个使用者。这条不变量由源码闸门 `QuestDeletionSafetyTests.模板上下文只许在接取路径里用` 钉住（**突变验证过**：在接取路径之外插一处 `_templates` 立刻判红），另有真库回归 `QuestAuthoringDbTests.作废后可物理删除_未作废不许删`（未作废不许删 / 作废后不许上线 / 删后查不到+发布被拒 / 重复删除幂等）。未验证的残余：**"持有者在模板删除后仍能提交并拿到奖励"这条整链路还没有真集群回归**（需要一个真实持有者角色 + 删模板 + 提交的端到端串起来），当前只有结构依据 + 单侧行为。
- 只读实况核验：`python scripts/verify-quest-authoring-cli.py`（案例 A~C 走临时文件，D 走管道）；`python scripts/verify-quest-material-seed.py`（015 入库前核验）；`python scripts/verify-quest-cli-encoding.py`、`python scripts/verify-quest-draft-redis.py`（上面两条）。
- 新任务写显式 id 时开 `SET IDENTITY_INSERT`，并在 `finally` 里关掉（一个会话只允许一张表为 ON）；
- `publish`（= 草稿落库并发布）前跑 `requirePublishable:true` 校验：没目标 / 没奖励 / 引用不存在 → 拒绝发布；内容不过**一行都不写**。库里已有的下线行要上线用 `online`；直接写库的旧通道 `upsert <file|->` 仍保留给运维，但编辑器已不再使用它。

- ⚡ **编辑器侧全部走异步（2026-10-06，现场反馈"任务编辑太卡"）**：`QuestCli.RunAsync` 把子进程与读流放到线程池，结果经 `FlaxEngine.Scripting.InvokeOnUpdate` **回灌主线程**（回调里可直接改控件）；窗口只有 `RunCli(...)` 一个入口，**同时只允许一个调用在跑**（忙时提示"上一个操作还在进行（X）"），`保存/发布` 后的目录刷新是回调里接着发起的第二次异步调用。`catalog` 顺带把 Redis 草稿一起返回（原来是"目录 + 草稿"两次子进程，每次刷新都多付一遍启动 + 连 Redis 的延迟）。回退点：把 `RunCli` 换回 `QuestCli.Run`（同步），但卡顿会立刻回来。

## 6. 服务端

- `IQuestManagerGrain`（`[GrainType("quest.manager")]`，主键 1）：目录缓存（默认 10s TTL）+ `PublishAsync` / `UnpublishAsync(forceAbandonActive)` / `UpsertTemplateAsync(draftJson)` / `CancelCharacterQuestAsync` / `GetOfferedQuestsAsync` / `TakeRevokedQuestsAsync` / `NotifyAccepted` / `NotifyFinished` / `CountHolders`。
- 持有者与撤销队列进 `IPersistentState("questManager","GameStore")`（进程内字段会被去激活静默清零，这是本仓库的既有教训）。
- 接取侧 `QuestGrain.AcceptFromTemplateAsync(questId, characterLevel)` 四道门：`IsValid` → **`publish_state==1`** → **等级门槛**（`characterLevel` 是网关从 `ICharacterGrain` 取的权威值，客户端只报 questId；读不到传 0，有门槛一律拒）→ 前置已完成；可重复任务按 `quest_type` 的自然日/自然周重置后才允许再接一轮。
- 等级门槛在 `GetOfferedQuestsAsync` 侧过滤，等级取自 `ICharacterGrain.GetSyncProfileAsync()`（**不用客户端报的数**）。
- 协议：`QuestListRequestMessage` 追加 `GiverKind` / `GiverTemplateId`（append-only，union 登记不变）；`QuestListResponseMessage` 追加 `Offers`（新类型 `QuestOfferInfo`）与 `CatalogVersion`。

## 7. 实时发布 / 关闭 / 取消（不停服）

| 入口 | 载体 | 生效速度 |
|---|---|---|
| 任务编辑器按钮 | CLI `publish/unpublish` → 改 `publish_state` | 最迟一个 TTL 周期（10s）；调 `GetCatalogAsync(forceRefresh:true)` 立刻。✅ **这条已被运行态证明**（`QuestLivePublishTests.库里改发布态正在跑的Grain不必重启就该看得见`，真集群 + 真库，2026-10-06）：只改库不碰 Grain 写接口，探针任务在 `GetOfferedQuestsAsync` 里 出现→消失→出现，且目录版本号跟着动；跑完探针行按 id 物理删除（复核 `probe_rows=0`，发布态回到 014 原样） |
| 服务端权威接口 | `IQuestManagerGrain.PublishAsync / UnpublishAsync / UpsertTemplateAsync / CancelCharacterQuestAsync` | 同步（写库 + 版本自增 + 审计）。⚠️ **这四个方法今天在生产代码里没有任何调用方**（实测 grep `IQuestManagerGrain`：只有 `QuestHandler.cs:91` 取目录/清单、`QuestGrain.cs:260,314` 登记与注销持有者，其余是 CLI 的提示文本、编辑器提示串与本仓测试）。它们是按 D4 预留的**运行期权威入口**，不是"已经接好的运营按钮"。✅ 但**接口本身不是死代码**：`QuestLivePublishTests.走Grain入口的热更与上下线也立刻可见`（真集群）已经执行过 `UpsertTemplateAsync`（改门槛+改名，库里 `content_version` 1→2）→ `PublishAsync` → 等级 60 看得见 / 等级 10 看不见（门槛在服务端判）→ `UnpublishAsync` 后消失。这也顺带证明了早先那个"发布校验按发布**之后**的样子"的修法是对的——否则 `PublishAsync` 每一步都会被 Q050 自相矛盾地拒掉。 |
| 游戏内 GM / 网关 | 现走 `QuestHandler` 的清单轮询落地撤销队列；未来的游戏内 GM 指令按 `ds-scripted-spawn-authority-model` 的"网关上行 → Grain 裁决"接 | 下一次清单刷新 |

> ⚠️ **`--force` 这一刀今天只有 Grain 路径会真的执行**：CLI 的 `unpublish --force` 只改库，并明确打印 `[F001]` 提示"要真的强制放弃玩家身上进行中的实例，须调 `IQuestManagerGrain.UnpublishAsync(questId, forceAbandonActive:true)`"（`Horizon.QuestCli/Program.cs:230-232`）。也就是说"实时**发布/关闭**"经 CLI 已经闭环（Grain 的目录缓存在 TTL 内按库里真值重建，见下条版本口径），而"实时**强制取消**"目前**没有运营入口**——它需要有人打 Grain。这一步刻意没有臆造：不开新 HTTP 面（红线），而游戏内 GM 指令要新增上行消息 + 权限判定 + 客户端输入口，属于另一个决定，等用户点头再排期。
>
> **CLI 写库为什么能算"实时"**：`EnsureCatalogAsync` 每次 TTL 到期都按 `IsValid` 重新查库，并用**内容指纹**当目录版本号（`QuestCatalogFingerprint.Compute(_cache)`，`QuestManagerGrain.cs:416`）。
> 所以编辑器/CLI 直接改库不需要 Grain 参与也会在下一次取目录时生效，玩家侧靠这个版本号做脏标记重画。
> 版本号一开始是 `Σ(ContentVersion + PublishState) + 行数`，那条口径有两个实测钝点（互补变化互抵、手改库不 bump `content_version` 就不前进），**2026-10-06 已换成按 `QuestId` 排序的 FNV-1a 指纹**，9 条回归 + 突变验证见 `TECH_DEBT.md` T10。生效同样要等整组重启。

> **不开新 HTTP 管理面**：本仓库口径是"WebAdmin 不承载 MMORPG 运营面，游戏权威操作走 GameGateway + Grain"，`GmScriptAdminController` 那条 HTTP 路子已被回退。原方案里的 WebApi 端点因此取消，改为 CLI（dev 期作者工具）+ Grain 接口（运行期权威）。

语义：**发布**＝立刻对符合门槛的在线玩家可见；**关闭**＝默认只停新接取、进行中实例仍可正常提交，`forceAbandonActive=true` 才把持有者写进撤销队列；**内容更新**＝只影响之后新接的实例（已接实例沿用接取时的目标/奖励快照）。所有动作写 `QuestManagerState.Audit`（尾部 200 条）。

### 7.1 可接列表的取数口径（2026-10-05 定）

`QuestListRequest(ActiveOnly=false)` 就是"给我这张总表"：服务端按 `publish_state` / 等级 / 前置 / 是否已交过滤后返回，**发布者显示名由服务端解析**（NPC·怪物查 `NpcTemplateCatalog`/`MonsterTemplateCatalog`，物品查 `ItemTemplateEntity`）随 `Offers[].GiverLabel` 下发，网关再解码成线格式的目标与奖励。

因此这条通路**不需要也不接受客户端声明任何身份**（2026-10-05 二次收紧）：请求里原来那两个 `GiverKind/GiverTemplateId` 整数已经**删掉**，换成 `GiverEntityId`（运行时实体 id）。网关拿它去 DS 上行的实体注册表解出权威的 (类别, 模板 id)，并核对玩家离它够不够近（`QuestInteractionAuthority.TryResolveGiver`，半径 800 世界单位）——**解不出就不给可接列表**，绝不退化成"给你整个目录"。于是"走近某个 NPC 才看到它能给的任务"第一次成为服务端可判定的事实；`GiverEntityId=0` 仍是明确的"我要总表"语义（任务日志页用这条）。

### 7.2 `UnpublishAsync(forceAbandonActive:true)` 曾自锁，已修（2026-10-05）

这一条是**试着把 Grain 拉进进程内跑**时才暴露的，静态读代码很容易看漏：

- 旧写法里 `UnpublishAsync` 先 `_writeGate.WaitAsync()`，**持锁期间**调用 `RecordRevocationsAsync`；
- 而 `RecordRevocationsAsync` 开头又是一句 `await _writeGate.WaitAsync()`；
- `_writeGate` 是 `new SemaphoreSlim(1, 1)`，**不可重入** → 自己等自己。

后果不是"这一次调用慢"，而是**永久**：`quest.manager` 是单例 grain（主键恒为 1），闸门被占住之后所有任务管理调用（发布、下线、热更模板、取撤销队列、登记持有者）全部排队到 Orleans 超时，且没有任何日志说明为什么。表现就是"运营点了一下『关闭并强制取消』，整个任务系统人间蒸发"，只能靠重启 Silo 或该 grain 被激活回收才恢复。修法是把记撤销账挪到**释放锁之后**（`QuestManagerGrain.cs` 里现在有一段注释钉住这条纪律）。

**这条修复的行为级回归已写好，但还没跑过**（进程内直接 `new QuestManagerGrain(...)` + Moq 代理 `IPersistentState<T>` 这条路依然禁止：vstest 的 `testhost` 会在跑到的瞬间静默退出，`已通过/失败` 一行都不出，还会留下三个 `testhost` 孤儿进程把 `Horizon.*.dll` 锁住，后续构建报 MSB3027）：

1. ✅ 已写（2026-10-06）：`Horizon.Game.Gateway.Tests/QuestManagerRevocationLiveTests.cs`，2 条，真集群 `ConnectAsync()` 宿主 + `HORIZON_ORLEANS_REDIS` 环境门控（与 `OrleansGrainLiveIntegrationTests` 同族）。断言的形状不是"没报错"，而是**每一步都套 5 秒死时限**：`NotifyAccepted(探针角色) → unpublish(force) 按时返回 → AbandonedCount==1 → 撤销队列里正是这个持有者 → 取走即清空`；第二条专门守"卡死发生在**这一次之后**"——`unpublish(force)` 后面接着打 `GetCatalogVersionAsync` / 再一次 `unpublish(不强制)` / `GetCatalogAsync(force)`，并确认不强制那一次**没有**再记人（只停新接取，不抹玩家进度）。探针任务默认用 **20001**（`014` 里那条刻意保持下线的示范任务）：`unpublish` 写的 `publish_state=0` 与它当前值相同，所以对库是净零改动；换成别的 id 要走 `HORIZON_QUEST_PROBE_ID`，且**必须挑一条当前下线的**，否则等于在测试里关掉玩家真在接的任务。收尾用 `NotifyFinishedAsync` 摘掉探针持有者——持有者表是 Grain 持久状态，不摘就留下一个幽灵持有者，之后每次 `unpublish(force)` 都会给一个不存在的角色记撤销账。
   ⚠️ **为什么现在跑不了**：在跑的 Silo 还是 M 系列改动之前的 DLL，进程里根本没有 `QuestManagerGrain` 这个类型，客户端打过去得到的是类型/反序列化错而不是超时——那只能证明"没部署"，不能证明死锁。**必须在 §9 那一轮整组重启之后再跑**：
   ```powershell
   $env:HORIZON_ORLEANS_REDIS = '127.0.0.1:9379,password=***'
   dotnet test Horizon.Game.Gateway.Tests --filter "FullyQualifiedName~QuestManagerRevocationLiveTests"
   ```
   目前对它的验证只到 `dotnet build -p:BaseOutputPath=bin/Verify/` **0 错误**。
2. 已经先落了一道**结构级闸门**（`Horizon.QuestContractTests/QuestGrainLockDisciplineTests.cs`，3 条）：扫本文件源码，断言"在 `_writeGate.WaitAsync()` 与 `_writeGate.Release()` 之间不出现任何 await 调用同样要这把锁的方法"，并单独钉住"`RecordRevocationsAsync` 必须排在 `Release` 之后"。**做过突变验证**：把那行调用挪回锁内，两条断言立刻判红（另一条"解析器识别"仍绿，说明它不是靠解析失败蒙过的）。
3. 别再走"手写/代理 IPersistentState 直接构造 Grain"这条路（本仓库既有教训：`[Reentrant]` 在 Orleans 10 不存在、grain 之间不许互 call、裸对象脚手架绕过字段初始化器）。


## 8. 阶段与当前进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| M1 | 模板模型 + 校验器 + 编解码 + **Flax 编辑器窗口** | ✅ 代码完成，`_verify.sh` 两模块 0 错误 |
| M2 | 迁移 + `QuestManagerGrain` + 实时上下线三入口 | ✅ 代码完成；**013 迁移与 014 种子已在 dev 库执行**（14→23 列，history 已记） |
| M3 | `QuestGrain` 发布态/前置/周期门 + 对话事件源 + 协议字段 | ✅ 代码完成，`dotnet build` Grains/Core 0 错误 |
| M4 | 客户端 `QuestClientService` + 任务日志去 Mock + 追踪 HUD + NPC 角标 | 🟡 部分：新增 `Source/Game/Network/QuestClientService.cs`（拉清单/带发布者/接取/提交/进度照抄，回包在连接线程 → UI 用 `Revision` 脏标记在主线程重绘）；`QuestMessageHandler` 补声明 `AcceptQuest`/`CompleteQuest` 并新增两个回执事件（这两条回包以前**没人接收**，接完任务界面收不到任何反馈）；`QuestLogPage` 左栏改为按服务端 `quest_type` 分组渲染，删掉写死的"主线 3 条"假数据，无数据显示"暂无任务"。**已完成**：`CombatHudTraditionalPage`（默认主界面 HUD 的右侧任务追踪）从写死三行改为服务端清单前 3 条；`QuestLogPage` 右栏详情整块改数据驱动（标题/meta/标签/描述/逐条目标/奖励槽，奖励物品名由本地 `ItemCatalog` 按模板 id 解析，按钮按状态只出"接取"或"交付"——旧的写死版放了两个点了没反应的按钮）。**剩余**：§11 的对象身份通路**已打通**（见 §11.1/§11.2），世界内的触发点也已接（选中 Npc/物品/采集点后按 **G**，见 §11.2 最后一行），因此只剩 NPC 头顶"！/？"角标——它要的是"这个实体身上有没有我能接/能推进的任务"，数据已经在网关注册表里，差的是把这一步投影到客户端（下行不带模板 id，所以角标必须走一条新的下行或让客户端按实体 id 问一次）。可接口径仍是总表（§7.1），是否收敛成"走近发布者才开放"是一个产品决定，不是技术缺口。**以上全部只到编译/契约级验证，PIE 现场一次都没跑过**（要用户重启三进程 + Tools 刷新）。`Offers` 本身已可在现场出现（总表口径，见 §7.1），任务日志的"可接任务"组会带发布者名与"接取"按钮。 |
| M5 | 文档同步、`KEY_FILES_INDEX`、`TECH_DEBT`、活集群回归测试 | 🟡 本文件 + `docs/KEY_FILES_INDEX.md` §10（任务系统落点索引）+ `docs/TECH_DEBT.md` T1–T8 都已同步；行为级回归 `QuestManagerRevocationLiveTests`（2 条，环境门控）**已写并编译通过，但要等 §9 那轮整组重启之后才可能跑通**（现在的 Silo 里还没有 `QuestManagerGrain` 这个类型） |

测试：`Horizon.QuestContractTests`（新建，只引用 Sim/Core/Entities/Model/Grains，**能在 Silo+Gateway 跑着的时候单独执行**）——**130 条全绿**（2026-10-06 含真集群 4 条：T8 撤销队列 2 + 实时发布/关闭 1 + Grain 热更与门槛 1；名称解析 16 条；奖励使用分档 12 条、网关交互胶水 7 条、**网关可接收窄胶水 4 条**（`QuestHandlerOfferListTests`：不带发布者按 0/0 请求全目录、实体认不出时**一次都不请求可接表**、传给管理 Grain 的发布者对由注册表权威解析而非客户端报什么用什么、超距同样不给总表）；嵌套层字段对账与交付者限定丢失各 1 条）（`dotnet test Horizon.QuestContractTests/... -p:CopyToFlax=false`）：编辑器/契约口径一组（编解码可逆、越界拒编、匹配器同语法、无事件源点名、发布必须有目标与奖励、发布校验按"发布之后"的样子、字段名漂移、库行↔草稿映射、列宽截断、日常/周常重置、接取侧等级门槛 6 个边界）、§11 实体身份与交互裁决一组（编解码可逆、类别映射、分片隔离与撞号防护、整片丢弃、邻近与换轴口径、五条拒绝短码、一次交互只推进自己那类目标、`talk_npc` 可入库并带 Q039 部署前提提示），另有 3 条**锁纪律闸门**（§7.2：扫源码断言持锁区间内不得再等同一把锁，突变验证过），以及**执行级的 DS 侧产出核验**（`DsEntityDescriptorProductionTests`：直接开一个真 `DsWorldSession`，刷怪→看 tick 里到底有没有身份、坐标是不是生成坐标、无变更的 tick 不重播、死亡必须撤销、全量补发与逐条变更算出同一个世界、描述符喂进注册表后裁决能解出权威身份）。这条以前只有编译级证据，现在是跑出来的。

三条自检通路（都可在宿主进程运行时执行，不写 Program Files、不停用户进程）：

| 目的 | 命令 |
|---|---|
| 服务端任务契约 | `dotnet test Horizon.QuestContractTests/Horizon.QuestContractTests.csproj -p:CopyToFlax=false` |
| QuestEditor 插件（两模块） | `bash HundunWorld/Plugins/QuestEditor/_verify.sh all` |
| 客户端脚本（源文件数由脚本自报，最近一次 460） | `bash scripts/verify-client-scripts.sh <id>` |

客户端自检那条有个必须写下来的坑：Flax.Build 解析脚本程序集**优先用引擎 `Binaries\Tools` 里的副本**，`dotnet build` 出来的新 dll 不覆盖它，编辑器里就会报 CS0246「新类型不存在」（而类型确实在源码里）。所以那条通路把仓库新鲜产物列在最前、并把响应里 `/reference:` 指向 Tools 同名副本的行**剔掉**——留两条同名引用时 csc 取到的可能是旧的那份。

> ⚠️ 因此：**下一次在 Flax 编辑器里编译客户端脚本前，必须先提权刷新 Tools 副本**，否则新增的 `QuestOfferInfo` / `GiverKind` 会让客户端编译失败：
> `powershell -ExecutionPolicy Bypass -File scripts\copy-shared-dlls-to-flax-tools.ps1`（需管理员；脚本会打印每个文件的 SHA-256 前 16 位做前后对账，不启停任何进程）。
>
> 2026-10-06 实测把这条前提**补全**了：脚本原先只刷 `Horizon.Game.Message / Horizon.Game.Core / Horizon.Orleans.Interface` 三个，而客户端脚本工程 `HundunWorld/Source/Game/Game.Build.cs` 的引用清单里还有 **`Horizon.Game.Sim`（任务目标编解码/`HasProducer` 就住在这里）、`Horizon.Game.ECS.Arch`、`Horizon.Game.World`、`Horizon.Game.ECS`、`Horizon.Core.Abstract`**。逐个数过两边产物头 16 位：`Message`/`Core` 已一致，而 `Sim`（repo `644134bd…` vs Tools `0e788dd1…`）、`ECS.Arch`、`World` **三个在 Tools 里是陈旧的**——编辑器按 Tools 优先解析，等于"仓库门禁吃新版本、编辑器编译吃旧版本"。清单已扩到与 `Game.Build.cs` 对齐（PowerShell 解析器 0 错；8 个 `<name>\bin\Debug\net10.0\<name>.dll` 全部实测存在，脚本按这个路径规则逐个解析）。细节记 `TECH_DEBT.md` T13。

## 9. 部署顺序

1. ✅ 已执行（2026-10-05，用户授权）：
   ```bash
   sqlcmd -S . -d Game -U sa -P <pwd> -C -f 65001 -i scripts/sql/013_quest_editor_columns.sql
   sqlcmd -S . -d Game -U sa -P <pwd> -C -f 65001 -i scripts/sql/014_seed_quest_editor.sql
   sqlcmd -S . -d Game -U sa -P <pwd> -C -f 65001 -i scripts/sql/015_seed_material_item_templates.sql
   ```
   对账：列 14 → 23、`__EFMigrationsHistory` 有 `20261005131122`、表内 7 行有效任务（10001 已发布 / 20001 下线 / 90001–90005 已发布）；`015` 侧 `item_type=4` 41 行、物品模板总行 171、`700001..700041` 命中 41、空名 0、重复 `item_id` 0（详见 §12.1，那一条第一次执行是**静默空跑**的）。回退：`DELETE FROM Game_HunduShijie_ItemTemplate WHERE item_id BETWEEN 700001 AND 700041;`
   CLI 闭环自检也已跑过一遍并清理：`validate` → `upsert 30001` → `get 30001`（发布者/交付者/前置/两条目标/奖励/版本全部原样读回）→ `publish` → `unpublish --force` → `retire` → 物理删除该行。这一轮真抓出两个只有连库才会暴露的缺陷：
   - `ExecuteSqlRaw("SET IDENTITY_INSERT …")` 另开一条连接、执行完归还池，SET 立刻失效 → 插入报"标识列不能插入显式值"。改成 `Database.OpenConnection()` 占住连接后再 SET + SaveChanges。
   - 发布校验拿的是"当前（还是下线）状态"，`Q050` 会把每一次发布都自相矛盾地拒掉。改成按**发布之后**的样子校验（CLI 与 `QuestManagerGrain.PublishAsync` 同步修）。
2. ✅ **已执行（2026-10-06 14:05，用户侧起的新构建）**：五个宿主（Silo / Gateway / DsHost / IM.Gateway / WebApi）都在跑 14:01 的产物。二进制核过部署面：`Horizon.Orleans.Silo\bin\Debug\net10.0\Horizon.Orleans.Grains.dll` 里能扫到 `QuestManagerGrain`(20 处)、`TakeRevokedQuestsAsync`(3)、`QuestCatalogFingerprint`(1)，网关侧 `Horizon.Game.Core.dll` 能扫到 `DsEntityRegistry`。所以"接取侧按 `publish_state` fail closed""可接列表 `Offers`""撤销队列""目录版本号指纹"这三件事**已经生效**，不再是旧 DLL（原先那段"要重启后才生效"的待办已被这次部署满足；`014` 把 90001–90005 置为已发布，所以重启后既有可接行为不变）。
   ⚠️ **这次重启不能分批**：`IQuestGrain.AcceptFromTemplateAsync` 的签名多了一个 `characterLevel` 参数（T1 那道门）。Grain 接口签名变了，旧 Silo 与新网关混跑会在调用处炸反序列化/类型加载（本仓已因新旧 Silo 混跑踩过 `TypeLoadException`），所以 Silo 与 Gateway 必须一起换新，别只重启其中一个。
   ⚠️ **14:05 那轮之后工作树又长了新东西，下一次重启才会带上**：`InventoryGrain` 的号段误判修法（信物好感不再被吞，T9①）、客户端 `QuestMessageHandler` 的"可接数/目录版本"日志、任务编辑器插件的名称直填与继承收口（这三处都不需要迁移，但服务端/客户端那两处要新 DLL；插件那处要编辑器重编脚本）。所以"已部署"只代表**当时那批**生效，别把它当成永远同步。
   **DS 宿主也必须重启**（§11 的实体描述符是 DS 侧新发的上行消息，旧 DsHost 不会发；旧网关遇到未知 kind 只跳过，所以顺序上先网关后 DS 更安全）。重启后对账：DS 日志应出现 `实体身份补发 N 条`，网关侧 `DsControlPlane.EntityDescriptorCount` 随刷怪递增。
3. ⛔ 待办：Flax 编辑器重启（菜单 Tools → 任务编辑器），首屏点"刷新目录"。
4. ✅ **已首跑（2026-10-06，真集群）**：T8 行为级回归**通过 2/2**，命令 `HORIZON_ORLEANS_REDIS='127.0.0.1:9379' dotnet test Horizon.QuestContractTests -p:CopyToFlax=false --filter "FullyQualifiedName~QuestManagerRevocationLiveTests"`。断言到的实况：`unpublish(force)` 在 5 秒时限内返回、`AbandonedCount==1`、撤销队列里正是那个持有者、取走即清空、**之后** `GetCatalogVersionAsync` / 再一次 `unpublish(不强制)` / `GetCatalogAsync(force)` 全部按时应答（自锁的表现恰恰是这三条会排队到超时），且不强制那次没有重复记账（`queue.Contains` 去重成立）。
   ⚠️ **这条回归从 `Horizon.Game.Gateway.Tests` 搬到了 `Horizon.QuestContractTests`**：2026-10-06 首跑时 Gateway.Tests 被并行会话的在途改动（`CharacterHealthPersistTests.cs` 当天 14:28 被改，CS4008）挡住编译，而我不去动别人的文件；本工程只需 Orleans 客户端 + Redis 成员提供方（两个包引用），不再引 Gateway/Silo 工程，也就不会再被常驻进程的 bin 锁住。附带一个**测试自己的错**：原先第二条断言"不强制那次之后队列应为空"，实况是**强制那次记的账还在**（本测试没提前取走）——判红的是我的预期，不是产品；已改成"队列里正好一条 + 取走后为空"。
   ✅ **复跑（2026-10-06 15:56，代码仍与并行会话改动共存）**：离线门 126/126 绿（**随后又补了 4 条网关可接收窄胶水 `QuestHandlerOfferListTests`，此刻 130/130**——同一次跑里第一条"不带发布者"证明调用记录器真的会记，所以后三条的 `Assert.Empty` 不是空转）；带 `HORIZON_ORLEANS_REDIS='127.0.0.1:9379' HORIZON_QUEST_DB='1'` 单跑那 4 条真集群回归也 4/4 绿，逐条耗时 286 ms / 1 s / 495 ms / 3 s——**必须看耗时**，因为这 4 条的门控是"环境变量缺了就 `return`"，早退时耗时接近 0，光看"通过"两个字证明不了它们真的跑过。跑完对账 `probe_residue=0`、种子 7 行、在线 6（=`014` 原样）。⚠️ 这里我自己踩了一个**对账口径的错**：第一次用 `quest_id>=90000` 找探针，数出 5 行，差点写成"清理没生效"——90001..90005 是 `014` 正儿八经的种子，测试真正铸出的探针在 `950000+Ticks%5000` / `960000+Ticks%5000`。**对账谓词要用被测代码实际生成的那批 id，不能按号段外推**。

> 重启前先做一次**不带旁路 OutputPath 的完整 `dotnet build`**：`bin/Debug/net10.0` 布局被挪动过会让客户端与 Silo 拿到陈旧副本（本仓库既有教训）。Silo/Gateway 正在跑时构建它们的工程会报 MSB3027 文件锁——那是锁，不是代码错。

### 9.1 重启后现场验收清单（逐条看，缺一条就不算通过）

| # | 看什么 | 期望 | 不通时先查 |
|---|---|---|---|
| a | Silo 起来后的第一行任务相关日志 | 目录缓存构建成功、条目数 = 7 | 库里 `publish_state`（`014` 是否被回滚） |
| b | 主界面右侧任务追踪（`CombatHudTraditionalPage`） | 显示服务端前 3 条，不再写死三行 | 客户端有没有拿到 `QuestList` 回执 |
| c | 任务日志页"可接任务"组 | 有条目，且每条带服务端解析出的发布者名 | 网关 `QuestHandler` 的 Offers 分支 |
| d | 菜单卷宗页 `MenuQuestsPage` | 标签页按 `quest_type` 分组；协议没有的字段显示 "—"/"未评级"；只出接取/交付一个按钮 | `QuestClientService.Revision` 是否变过 |
| e | 选中一个脚本生成的 NPC 按 **G** | toast「已推进 N 条任务目标」；若失败，toast 会说清是哪一条（`unknown_entity`/`too_far`/`not_interactable`） | DsHost 是不是旧 DLL（不发描述符）；NPC 是不是 DS 脚本生成的（`EntityId` 非 0） |
| f | 按 G 之后再看可接列表 | 只剩这个 NPC 给的任务；解不出身份时**列表为空**而不是退回总表 | 网关 `DsControlPlane.EntityDescriptorCount` 是否 > 0 |
| g | 编辑器 Tools → 任务编辑器 → 刷新目录 | 目录列出 7 条任务 + 物品/技能/NPC 清单；奖励槽里能选到材料（`700001..700041`）；**奖励/目标/发布者的 id 框现在可以直接打名字**（如"铁矿石"）——hint 会回显 `铁矿石·材料（品质 x）`，歧义（如"新手"）会说"匹配到 N 项"并不写入 | Flax Tools 里的 `Horizon.Game.Message.dll` 是否刷新（不刷新会 CS0246） |

> ⚠️ **做 g 项之前先确认编辑器进程比插件脚本旧还是新**：本轮实测 `FlaxEditor` 起于 14:04，而 `QuestEditorWindow.cs` 的名称直填与继承收口是 15:2x 之后才写的，Tools 副本也是 15:36 才刷新——**Flax 不会自动重编插件脚本**，所以不重启编辑器就打开"Tools → 任务编辑器"，看到的仍是旧界面（没有名字回显，交付者那两格也仍会清零）。同理，`Horizon.Game.Gateway` 已于 15:35 重启过一次，而**当时 Silo 不在进程列表里**（有会话正在部署）——重启前先确认 Silo 起来了，否则 `unknown_entity` 与"任务列表全空"都是部署态造成的，不是代码问题。

**已经自动拿到的现场证据（2026-10-06，来自 14:04–14:17 那次真客户端会话的日志 `HundunWorld/Logs/Log_2026_10_06_14_04_02.txt`）**：`QuestListRequestMessage` → `QuestListResponse` 每 10 秒一轮、`[QuestMessageHandler] 收到任务列表` 有回执（任务数=0 是该角色身上没任务，属正常）。也就是说**传输与 Grain 往返在真实运行环境里已经通了**，a–g 剩下的差别只在"界面画没画出来"。为方便区分"服务端没给 Offers"和"UI 没画"，`QuestMessageHandler` 那行日志现在同时打 `可接数` 与 `目录版本`（客户端 460 源文件 0 错；下次在编辑器里重编客户端脚本后生效）。

跑完 a–g 之后，"实时发布/关闭/取消"这一条的**服务端部分已有真集群证据**（§9 第 4 步 T8 回归 2/2 + `QuestLivePublishTests` 的"改库即生效"1/1 通过），剩下的**只有界面级确认**：a–g 里 a/b/c/d 是"看不看得见"，e/f 是"按 G 推不推得动"，g 是"能不能选到所有物品（现在也能直接打名字）"。这三类我都无法替你判定，且它们才是目标第一句的验收面。

### 9.1.1 目标条款 ↔ 证据 ↔ 缺口（2026-10-06 逐条对照，代码级证据都是今天跑出来的）

| 目标原句 | 已有证据（可复跑） | 仍缺的那一半 | 归属 |
|---|---|---|---|
| 在 Flax 编辑器中**可视化编辑**任务内容 / 完成条件 / 奖励 | 插件两模块离线编译 `errors=0`（`Plugins/QuestEditor/_verify.sh`）；嵌套层字段名两边一一对上、编解码可逆、越界拒编（`QuestEditorContractTests`）；名称直填三步顺序两侧不漂移（`QuestCatalogLookupTests` 15 条 + 结构闸门） | **没有任何人在真编辑器里打开过这个窗口**（§9.1 g 项）：进程比脚本旧，必须重启 FlaxEditor | 待用户执行 |
| 可关联 **NPC / 怪物 / 其它生物 / 物品** 作为发布或触发对象 | 作者侧：`giver=怪物` 被 Q012 拒且有测试；`giver=item` 可入库并出 Q043 Info（知情标注）；四类发布者 id 都能名称直填。触发侧：`talk_npc`/`interact_prop` 有服务端权威事件源（`QuestInteractionAuthority` + `DsEntityIdentityTests` + 真 `DsWorldSession` 产出核验 + 网关胶水 11 条） | **世界里没有"物品型脚本实体"**：DS 只上报怪物/NPC/物件/触发区四类，所以挂在物品上的任务只能出现在总表，走近那个对象不会收窄（#9） | 待用户拍板 |
| 奖励关联**所有游戏物品**（装备 / 技能书 / 材料 / 特殊物品…） | 写库闸门实测：`QuestStore.Upsert` 先校验、有 Error **一行都不写**（`QuestAuthoringDbTests.cs:118` 钉 `FindQuest=null`）；三档奖励逐条往返（装备/秘籍/材料，含数量 1/2/5）；171 件里 156 件叫得出名字（普查脚本，方向化失败信息）；"用得了"这半已修（`RoutesAsSkillBook` 四参 + 12 条回归） | ① `item_type=5/7/8`（任务物品/配方/宝箱）**库里 0 行**，作者今天选不到这一档 → 016 已备好待批准（#13）；② 那 15 件五行素材/信物在游戏内仍显示"神秘物品"（T9③） | 待用户拍板 |
| **预留**实时发布 / 实时关闭 / 取消的接口（不停服） | 真集群 4/4：只改库 `publish_state` → 正在跑的 Grain 的可接列表与目录版本自行出现→消失→出现；`UpsertTemplateAsync`→`PublishAsync`→等级 60 可见 / 10 不可见→`UnpublishAsync`；T8 强制下线不死锁 + 撤销队列取走即清空；版本号=内容指纹（突变验证过） | 这四个 Grain 入口**在生产代码里零调用方**：没有 GM 指令 / Agent 工具把它们接出去（#8）。另外部署态仍落后工作树（Silo 是 14:01 那批，#14） | 待用户拍板 |
| 先出开发计划，确认后落地 | 本文件 §1–§10 + `TECH_DEBT`（T1–T13，含每条的代价与回退写法）；D1/D2/D4/D9 与号段、顺序口径按用户拍板执行 | 无（计划已确认并据此改过原设计） | 已闭合 |

此刻门禁：`Horizon.QuestContractTests` **130/130 绿**（含 env 门控的真集群 4 条，按逐条耗时确认它们真的执行过）；插件 0 错、客户端 460 源文件 0 错；库内 `probe_residue=0`、014 种子 7 行 / 在线 6 原样；CLI 作者侧核验脚本已改成**自查库**判据（不再靠人记得设环境变量）。

**结论：目标不能被判定为已达成。**代码级与运行态级的缺口只剩上面四行"仍缺的那一半"，其中三行要用户拍板、最后一行（g 项）只能在真编辑器/PIE 里看。我不把"全绿"当成达成——全绿只覆盖了我已经写测试的那些面。

### 9.2 接手清单（2026-10-06 收工状态，代码级已闭合，现场级未验证）

仓库门禁此刻全绿，都是**可复跑**的，不是"我记得绿过"：

| 门禁 | 命令 | 此刻实况 |
|---|---|---|
| 服务端契约 | `HORIZON_QUEST_DB=1 QUEST_MATERIALS_IN_DB=1 dotnet test Horizon.QuestContractTests -p:CopyToFlax=false` | **85 绿**（含奖励三档往返、号段一致性、目录指纹＋突变验证、Tools 清单漂移闸门＋突变验证） |
| 编辑器插件 | `bash HundunWorld/Plugins/QuestEditor/_verify.sh all` | 两模块 0 错误 |
| 客户端脚本 | `bash scripts/verify-client-scripts.sh <id>` | 460 源文件 0 错误（**注意**：这条把仓库产物排前、剔掉 Tools 同名引用，所以它绿≠编辑器绿，见 T13） |
| 宿主工程 | `dotnet build <Silo\|Gateway\|DsHost> -p:BaseOutputPath=bin/Verify/` | 三个 0 错误（不挤占正在跑的进程） |
| 种子与作者链路 | `python scripts/verify-quest-material-seed.py`、`QUEST_MATERIALS_IN_DB=1 python scripts/verify-quest-authoring-cli.py` | 前者 ALL CHECKS PASSED；后者四案例符合预期 |

**还差的只有现场**（用户侧，按顺序）：① 提权跑 `scripts\copy-shared-dlls-to-flax-tools.ps1`（清单本轮已补全，会连带刷新 `Horizon.Game.Sim`/`ECS.Arch`/`World` 这三个实测陈旧的库）；② 完整 `dotnet build` 后 **Silo → Gateway → DsHost 一次性**换新（`IQuestGrain` 签名变了，禁止分批）；③ 按 §9.1 的 a–g 在 PIE 与编辑器里逐条看，重点 e/f（选中 NPC 按 G）与 g（奖励下拉能选到材料）；④ 跑 `QuestManagerRevocationLiveTests` 首跑。

**还差的用户拍板事项**（都不是技术缺口，别当已完成）：#8 强制取消的运营入口、#9 `giver=item` 的世界侧解出、#10 三个"填了没人读"字段接消费方还是摘掉。（原 #11"刷新清单与 `Game.Build.cs` 的一致性断言"本轮已落地：`FlaxToolsRefreshListTests` 4 条 + 突变验证。）另：本仓库工作区里任务系统的文件**全部处于未提交状态**（工作树同时有并行会话的改动，切分提交范围由用户决定）。

## 10. 已知缺口（写清楚，不留"看起来完成了"）

- ~~等级门槛只在可接列表侧生效，接取侧尚未复校~~ **已修（2026-10-05）**：网关把从 `ICharacterGrain` 读到的**权威等级**随 `AcceptFromTemplateAsync(questId, characterLevel)` 一起传进 Grain，Grain 用纯函数 `MeetsLevelRequire` 判一次；等级读不到时传 0，有门槛的任务一律拒（fail closed，绝不把"读不到"解释成"没门槛"）。客户端全程只报 questId，插不上等级这个数。
- `collect_item` / `use_item` / `reach_zone` 三类条件**没有事件源**，校验器拒绝入库；等采集/使用/到达上报接上再放开 `HasProducer`。
  （`talk_npc` / `interact_prop` 自 §11.2 起有服务端裁决链路 + 世界内触发点（选中对象按 G）；校验给 Q039 Info 提示部署前提。）
- 实体描述符只在**变更时刻**发一次，不做周期刷新：网关看到的坐标是生成时刻的。NPC 站定没问题，游荡怪物做邻近判定是近似；DS 重连后靠 `CollectAllEntityDescriptors` 全量补，**强杀且不再回来**的那片 shard 上的条目会残留到网关下次重启（那条 shard 已无人可路由，实际影响面是"一个不存在的对话对象仍可被认出来"，但玩家也发不出请求）。
- 协议号段实况（实测，不是记忆里的旧值）：`MessageType` 已用到 **1659**（`QuestInteractResult`），空闲从 **1660** 起；`MemoryPackUnion`/`[NetworkMessage]` tag 已用到 **242**（`QuestInteractRequest=241`、`QuestInteractResult=242`），空闲从 **243** 起；`DsUplinkMessageKind` 已用到 **9**（`EntityDescriptor`），空闲从 **10** 起。
- 可重复任务的 `repeat_limit` 目前只当"允许按周期重复"用，**精确次数上限**未实现（状态结构只留最近一次完成记录）。
- 副本 `CompleteInstance` 无源（工程还没有副本系统）。
- 客户端：**三处任务界面都已接服务端数据**（追踪 HUD、任务日志、菜单卷宗 `MenuQuestsPage`），但**现场一次都没跑过**——需要整组重启 + Flax Tools 副本刷新后在 PIE 里看。`MenuQuestsPage` 本轮的处理：写死的六条卷宗整块删掉，改由 `QuestClientService` 的两份快照（身上的任务 + 目录里的可接任务）映射；标签页跟着服务端 `quest_type` 改成 主线/支线/日常/周常；协议里没有的字段（难度星级、区域、委托人）显示 "—"/"未评级" 而**不本地编造**；时限由任务类型推（日常=每日重置、周常=每周重置）；"追踪/放弃/分享"三个没有服务端通道的死按钮整体删除（含 `_trackedQuest` 状态），只留一个主动作=接取/交付；打开页面即自己 `RequestList()`（节流 8 秒），否则空缓存会显示成"你没有任务"。`QuestTrackerHUD.cs` 仍无调用方（孤岛），本轮没动它。编辑器窗口的滚动表现与非 16:9 下的排布、以及这三处界面的视觉，都需用户在 PIE/编辑器里看一眼才敢宣布视觉正确。
- 奖励域已按"库为准"整条核过（2026-10-06）：`QuestCli` 的物品下拉走 `QuestStore.AllItems()`（整张 `Game_HunduShijie_ItemTemplate`，无类型过滤），库里现有 **171 行 / 类型 0·1·2·3·4·6 六档齐全**；发放侧 `QuestHandler.GrantQuestRewardsAsync` → `InventoryGrain.AddItemAsync` **不查类型也不查 `max_stack`**，任何 `item:<id>` 都会到账。**残余问题不在任务侧**：使用与显示两侧共用"id ≥ 800000 就是秘籍"这条启发式，而 `910001..910015` 这 15 件五行素材/信物正好落进该号段，导致 5 件信物的 `use_effect=favor` 被吞、10 件素材点了无效、15 件全显示成"无名残页"；由 `QuestAuthoringDbTests.秘籍号段里不得混入非秘籍的物品_现有15件五行素材登记在案`（连真库）钉住"只许这 15 件"，修法与归属见 `TECH_DEBT.md` T9。另外 `item_type=5/7/8`（任务物品/配方/宝箱）库里现在**一行都没有**，所以"客户端显示占位"这条暂无实况对象；真要出现，得先给它们补客户端定义或走名称下行。
- 编辑器**可视化编辑的覆盖面**逐字段核过（2026-10-06）：草稿模型的 15 个顶层字段里，除三个以外都有控件（含五行域下拉、前置清单、重复次数、交付者"与发布者相同"开关、逐条目标 7 种类型 + 数量 + 描述、奖励的物品/好感/经验/金钱）。那三个是 `objectives[].mandatory`、`giver.zoneId`、`giver.spawnPointId`——**能填能存，运行态无人读**（库里 `objectives` 走 `type:target#count` 串，格式里根本没有 mandatory 的位置）。另外还查出一处**存储层的不对称**（修正我先前的说法）：`turnIn.zoneId/spawnPointId` 不是"被编辑器清零"，而是**库里从来没有承载它们的列**（013 只给了发布者两列），所以 upsert 时就丢了，且不产生任何警告——真库往返已证：同草稿 `giver.zoneId/spawn=12/34` 原样读回、`turnIn.zoneId/spawn=56/78` 读回 0。三条出路（补 `turn_in_*` 列并做成真过滤 / 从草稿契约摘字段 / 只摘 mandatory）记在 `TECH_DEBT.md` T12 + 任务 #10，等用户选。
  本轮为此加的两道闸门：① **嵌套层字段名双向对账**（`QuestEditorContractTests.嵌套层的字段名两边必须一一对上`：插件四份 `Known*Keys` 从源码抠出，与服务端 `ToJson()` 逐层比集合，再把整份 JSON 回读逐值断言非默认——STJ 对不认识的键静默忽略，不钉住就是"点了保存、下次打开字段变空"）；② **交付者限定入库即丢**（`QuestAuthoringDbTests.发布者的范围限定入库而交付者的会被丢弃`，断言按现状写，将来补列它会变红来提醒改文档）。插件侧另做了一处无害收口：`mandatory` 与交付者限定在保存时**从已加载草稿继承**（判据"类型 + 模板 id 未变"），不再无条件写死/新建清零。
- **显示侧覆盖面已量化（2026-10-06，`scripts/verify-quest-reward-display-coverage.py`）**：171 件物品里客户端叫得出名字的 **156 件**，叫不出的正是 T9 那 **15 件**（`910001..910010` 素材 + `910011..910015` 信物），脚本把"只许这 15 件"钉成 FAIL 条件（新加物品没同步客户端目录会立刻变红）。另一条是依赖不是缺陷：**91 本秘籍一本都不在客户端静态技能种子里**，名字全部来自服务端技能模板下行（库里 117 行，`skill_id` 1001..90001）——下行到达前技能书一律显示"无名残页"。详见 `TECH_DEBT.md` T9b（含我自己第一版把这条误判成"91 本全叫不出"的经过）。
- **"特殊物品"这一档今天库里 0 行**（`item_type=5/7/8` 一件都没有），所以目标那句"奖励可关联所有游戏物品（…特殊物品）"在数据上无法演示——不是校验拦着，是没东西可选。已按"只生成不执行"的口径备好 `scripts/sql/016_seed_special_item_templates.sql`（任务物品 5 件 / 配方 2 件 / 宝箱 2 件，号段 750001·760001·770001，全部 **< 800000** 以免被秘籍启发式吞掉）；结构核验用新的通用工具 `scripts/verify-item-template-seed.py`（同一套检查也反跑过 015 与真库对账）。**执行要等用户点头**，且执行前后要清楚：作者侧立刻可选可校验（CLI 目录走整张表），但显示侧仍会落"神秘物品"（要 T9③ 的物品名下行），宝箱没有开箱逻辑、配方没有学习入口 —— 所以**建议只先入 `item_type=5`**，7/8 等对应机制接上再入，免得往库里塞九件"发得出、点开没反应"的东西。
- 发布者选 **物品** 这一格是"能写、能发、进不了收窄可接"：`CanBeGiver` 允许 `npc/item/prop`，校验器按库查这个物品存在性，名字也从库里解析；但 DS 上行的 `ScriptedKind` 经 `ToQuestKind` 只会产出 `Monster/Npc/Prop/Zone`，**永远没有 item**，所以 `giver=item` 的任务不会被"走近那个对象"那条按实体 id 收窄的通路命中，只在总表里出现。本轮把它**说破**（校验器常驻 `Q043` Info），不缩目标也不臆造运行态通路；两条真修（DS 加物品型脚本实体 / 让 prop 按模板 id 归属回判 item）写在 `TECH_DEBT.md` T11，都需要另一个域配合，属产品决定。
- `Horizon.Game.Gateway.Tests` 里既有任务回归（`QuestObjectiveContractTests` / `OrleansGrainLiveIntegrationTests`）在宿主进程运行时无法构建（文件锁），需要在停掉 Silo/Gateway 后补跑一次。

## 11. 把"玩家与这个对象说话"变成权威事件（已落地：方案 1）

现状事实（逐条核过代码）：

| 事项 | 证据 |
|---|---|
| 运行时实体 id = `(spawnPointId<<32) \| (slot+1)`，slot 0 是"无实体"哨兵 | `Horizon.Game.Sim/SpawnDirectorCore.cs:185-190` |
| 脚本生成点用**负数** `SpawnPointId` 域，故与静态点不撞号；**类别不能从 id 位里读出来** | `DsWorldSession.cs:2104-2109`、`:1831-1834` |
| 玩家 id 直接就是 `characterId`（高 32 位为 0） | `DsWorldSession.cs:601,617` |
| `entityId → (TemplateId, ScriptedKind)` 只有 DS 会话里有：`DsMonster{EntityId,SpawnPointId,TemplateId,ScriptedKind}` 存 `_monstersById`，公开口子只有 `TryGetMonster`（注释写着"测试/诊断"） | `DsWorldSession.cs:234-263,422,2696` |
| 网关进程**完全没有** `DsWorldSession`（grep `Horizon.Game.Gateway/` 零命中），`DSRegistry` 只有 `shardId → DsEndpointInfo` | `Horizon.Game.Gateway/Services/DSRegistry.cs:175,189` |
| 网关→DS 目前是**只送不问**：`IDsScriptDispatcher.DispatchAsync` 返回 `Task<bool>`，没有关联 id、没有响应通道 | `DsScriptDispatch.cs:28`、`DsUplinkServer.cs:125` |
| 已有先例：DS 自己在 gameplay 事件里带 `MonsterEntityId + MonsterTemplateId` 上报，Orleans 侧消费 | `DsWorldSession.cs:1023,1035,1250` → `OrleansGameplayWiring.cs:323` |
| 下行同步包根本不带模板 id（`EntityDelta` 只有 EntityId/Kind/Identity/Transform…），所以客户端也**不可能**自己知道 | `SyncPackets.cs:375-405`、`SyncComponents.cs:13-44` |

三条候选接法里选了 **方案 1（DS 主动发实体描述符，网关建缓存）**，方案 3 仍不可取，方案 2 不需要了。落地记录（2026-10-05）：

### 11.1 已落地：DS → 网关的实体身份通路

| 层 | 改动 |
|---|---|
| 协议 | `DsUplinkMessageKind.EntityDescriptor = 9`（实测 9 起空闲）；`DsEntityDescriptorBatch{ShardId, Descriptors[]}` + `DsEntityDescriptorDto{EntityId,ScriptedKind,TemplateId,SpawnPointId,X,Y,Z,Removed}`，与内核结构 `DsEntityDescriptor` 互逆转换（线上契约与内核字段刻意分离，同 `DsGameplayEventDto` 的纪律） |
| DS | `DsWorldSession` 在**生成**与**死亡/清除**两处登记描述符（移除统一走 `MarkMonsterDead`，一条钩子覆盖重生、脚本清除、TTL 到期），随 tick 结果 `EntityDescriptors` 批量带出、送出后清空；`CollectAllEntityDescriptors()` 供全量补发 |
| 宿主 | `DsHostRunner` 与玩法事件同批发送并**分开计数**（`EntityDescriptorsProduced/Sent/Failed`）；`Horizon.DsHost/Program.cs` 每次注册成功后补发全量——注册表是内存态，网关重启后没有这一步，邻近判定会一律落空且不报错 |
| 网关 | `DsUplinkServer` 新增 case → `DsControlPlane.HandleEntityDescriptorBatch`（三道闸门：灰度开关、**shard 归属必须由 `DSRegistry.TryResolve` 认**、接收端存在）；注册时整片 `ClearShard` 再等补发，摘除时同样丢弃 |
| 消费 | `DsEntityRegistry`（`Horizon.Game.Core/Sim/Server/DsEntityRegistry.cs`）：按 shard 分片、`TryResolve` / `FindNear(半径+类别过滤，结果按 entityId 升序稳定)`；DI 恒注册（DS 通道关闭时它就是空的） |

关键口径（都有测试钉住）：

- **实体 id 跨 shard 会撞号**：`id = (spawnPointId<<32)|(slot+1)`，两片世界用同一份刷怪表就产出同样的 id。所以只许按角色路由（`IShardRouter.Resolve(characterId)`）解析出的那一片查，"全 shard 找一个"等于给伪造 id 开门。
- **坐标空间只有一个约定**：描述符沿用下行协议的 **Flax Y-up**；角色位置缓存（`ICharacterGrain.GetLastPositionAsync`）是 **ECS Z-up**。换算规则与 `DsPositionWritebackService` 的写回严格互逆：`ecs(x,y,z) = flax(x,z,y)`。漏做换轴不是"偏一点"，而是把水平距离算成高度差，邻近判定全体失效。
- **NPC 站得住在近似内成立**：脚本 NPC 被强制 `MoveSpeed=0`，生成时刻的坐标就是它一直待着的坐标；会游荡的怪物只适合粗粒度邻近，需要精确距离的判定必须回到 DS 内做。

### 11.2 已落地：对话/交互的服务端裁决（`talk_npc` / `interact_prop` 从此有事件源）

| 环节 | 实现 |
|---|---|
| 请求 | `QuestInteractRequestMessage{CharacterId, EntityId}`（`MessageType=1658`，union tag `241`）——**只带实体 id，不带模板 id** |
| 裁决 | `QuestInteractionAuthority.Evaluate(注册表, shardId, entityId, 角色Flax位置)` 纯函数，四条短码：`no_registry` / `unknown_entity` / `not_interactable` / `no_position` / `too_far`（默认半径 800 世界单位） |
| 推进 | `QuestHandler.HandleQuestInteractAsync` 拿裁决出的**服务端**模板 id 去匹配目标串（`QuestObjectiveCodec` 精确解析，不做关键词包含），命中才 `UpdateQuestProgressAsync`；回执 `QuestInteractResultMessage`（`242`）按 1658 原路回 |
| 失败口径 | 位置读不到 → **不采信**（不降级成"免检通过"）；注册表未接线 → 一律不采信（不回退成相信客户端） |
| 入库门 | `HasProducer(TalkNpc)`/`HasProducer(InteractProp)` 翻成 `true`（事件源真的存在了）；`KindsWithoutProducer()` 只剩 `collect_item`/`use_item`/`reach_zone` |
| 客户端 | `QuestClientService.ReportInteract(entityId)` + `QuestMessageHandler` 声明 1658 与 `InteractResponded` 事件；`LastInteractionOk/InteractionReason/InteractionAdvanced` 供界面分派表现 |
| 世界内触发点 | `WorldSelectionService.TryInteractSelected()`：**选中一个 Npc/Item/GatherKind 对象后按 G** 发这次交互（`E`/`Q` 归相机锁定切换、`F` 归攀爬，所以用 G）。本地 `Kind` 只决定"要不要发这一枪"，判据全在服务端；`EntityId=0` 的纯场景对象本地直接挡掉（发出去也只会是 `unknown_entity`）。回执在连接线程只置标志，toast 由选中服务在主线程 `OnUpdate` 里取走发（`TryTakeInteractionResult`，一次结果只发一次）。种类链路是通的：DS 的 `ScriptedEntityTypeOf` 对脚本 NPC 下发 `EntityType.Npc` → `FlaxActorSyncSystem.KindFromWireType` → `SelectableKind.Npc` |

⚠️ **新增线上消息要登记两处**：`[NetworkMessage(tag, service)]` 只让生成器产出 `UnionKinds` 常量表，**MemoryPack 的 union 特性仍然手写在 `Horizon.Game.Message/MessageUnion.cs` 的 `<generated-union-block>` 里**（本项目实测：漏了它消息会静默发不出去/解不开，而 `Horizon.Game.Gateway.Tests.UnionRegistrationParityTests` 会把两边不一致判成失败）。本轮 241/242 两边都补了，并用一份脚本对账过：生成表 242 条 ↔ 手写 242 条，无 tag 冲突、无越界。

触发点接上后，`Q039` 从 Warning 降成 **Info**：现在挡在"现场推不动"前面的不再是代码缺口，而是两件部署/内容前提——① Silo/Gateway/**DsHost** 都已重启到带实体身份上行的这一版（旧版下服务端解不出身份，一律 `unknown_entity` 且不报错）；② 那片世界里真的生成了一个 NPC（走 DS 脚本生成通道）。这条 Info 常驻校验输出就是为了让"任务发不出去"在现场能一眼归因，别当噪声删掉。**代码到位 ≠ 现场验证过**：G 键这条链路还没在 PIE 里跑过（要用户重启三进程 + 脚本生成一个 NPC 才算证）。

`014` 种子里那条被换掉的"与 NPC 对话"示范目标现在**允许**回归（语法可入库了），但本轮没有改动已执行的 `014`——要恢复对话示范目标请另出一份幂等种子，别重跑 `014`（它会连带把发布态与前置一起写回）。

## 12. 材料要进奖励域，先过一次号段关（已定：方案 A）

真库闭环测试顺手量出一个**结构性冲突**，它和任务系统无关，但直接决定"奖励可关联所有游戏物品（含材料）"能不能成立：

| 来源 | id |
|---|---|
| 客户端 `MaterialDatabase`（42 种材料） | `10001-10005`、`20001-20005`、`30001-30005`、`40001-40005`、`50001-50005`、`60001-60003`、`70001-70002`、`80001-80002`、`90001-90004`、`100001-100002`、`110001-110002` |
| 库里 `Game_HunduShijie_ItemTemplate`（130 行） | 武器 `10001-10007`、防具 `10002-30001`、饰品 `10008-910015`、消耗品 `2001-910010`、秘籍 `810101-890001` |

也就是说"奖励 `item:10001`"这句话现在读不出唯一答案：按库里它是**装备**，按客户端材料表它是**铁矿石**（`ItemCatalog` 的"装备表与材料表存在重叠 ID，装备优先"注释就是在记这件事）。材料表还整表不在库里（`item_type=4` 零行），所以任务编辑器的奖励下拉目前覆盖不到材料——**这不是漏实现，是号段没定**。

三条路（需拍板）：

| 方案 | 做法 | 代价 |
|---|---|---|
| **A. 材料另起号段**（建议，与 `800000+技能id` 的"段即语义"一致） | 材料模板 id 改成 `700000 + 材料序号`；`MaterialDatabase` 同步改值，`ItemCatalog.FromMaterial` 按段反查；新增 `015_seed_material_item_templates.sql`（幂等、INSERT+UPDATE 双分支写全列、自检"id 不与其他类型重叠"）入 `item_type=4` | 改一处客户端表 + 一份种子；旧存档里若已有材料 id 需一次映射（背包材料目前不落库，风险低） |
| B. 材料暂不进奖励域 | 什么都不做，记进 `TECH_DEBT`，奖励限于库里已有的装备/饰品/消耗品/秘籍/信物 | 目标里"奖励可关联材料"这一项暂时不满足 |
| C. 按现有 id 直接建材料行 | 10001 等号与装备**硬撞**，`ItemCatalog` 的"装备优先"回退会静默吞掉材料语义 | 不可取：会把一个可读的数据问题变成运行时的错物发奖 |

选 A 后要一并翻掉的断言：`QuestAuthoringDbTests.奖励域覆盖库里真有的物品类型` 里的 `Assert.DoesNotContain(4, types)` 改成 `Contains(4, types)`（这条断言现在就是那次改动的地面）。

### 12.1 执行记录（2026-10-05，用户拍板 A）

已落地（代码侧，编译级已验）：

| 项 | 内容 |
|---|---|
| `HundunWorld/Source/Game/Equipment/Material/MaterialData.cs` | 41 种材料模板 id 整体迁到 `700001..700041`（声明 41 处 + `GetMaterial` switch 分支 41 处，脚本改完后 `grep -c` 双向核数为 41/41，旧号零残留）；表头写明"段即语义"，与秘籍 `800000+技能id` 同律 |
| `HundunWorld/Source/Game/Inventory/ItemCatalog.cs` | 类注释里的"装备表与材料表存在重叠 ID，故装备优先"改成历史记录：重叠由号段迁移消解，`015` 是这份表的入库来源，改材料要代码与种子一起改 |
| `scripts/sql/015_seed_material_item_templates.sql` | 41 行 `item_type=4`，每行 `IF EXISTS UPDATE / ELSE INSERT` 且**双分支都写全列**；`element` 按该列自己的注释取值（0金1木2水3火4土5无）、`material_grade` 取 `MaterialTier`、`source_type=1`、`bind_type=1`；末尾三条自检（行数=41、`item_id` 不重复、名称非空），任一不过即回滚 |
| 客户端脚本自检 | `bash scripts/verify-client-scripts.sh` → 460 源文件 **0 错误** |

✅ **已执行（2026-10-05，用户点头后）**：入库前先跑只读核验（当时就抓到 UPDATE 分支漏 19 列，见下），随后 `sqlcmd` 执行：

```bash
python scripts/verify-quest-material-seed.py          # 只读；连库部分只跑 CLI 的 catalog
```

它盯的是三处互不引用、错了也不会报错的真相：**SQL 自己的 INSERT/UPDATE 双分支**、**客户端 `MaterialData` 的 (id, 名称)**、**库里现有 item_id 空间**。这条核验不是走过场——它当场抓出一个真缺陷：**015 的 UPDATE 分支原本漏了 19 个内容列**（`sub_type`/`icon_path`/`use_effect`/`valid_days` 等），意味着这条种子第一次跑是好的、第二次重跑会把已存在行的这些列留在旧值上静默不一致（正是本仓记过的"幂等双分支是两个真相源"那条坑）。现已补全为除 6 列外写全：`item_id`（主键）、`create_time`（已存在那行的创建时间是现场事实）、`GameId/AreaId/ServerId/GameUserId`（多租户归属键，被占用时该让自检暴露，而不是被 UPDATE 悄悄抹平）。核验通过项：41 行 / 41 列 / 值数逐行对齐 / 与 006 那份**已在真库跑过**的列清单完全一致 / id 连续且与现有 130 个 item_id 零重叠 / 名称与 `MaterialData` 一一对应 / 库实况 `item_type=4` 当前 0 行（材料确实还没进奖励域）。

入库命令与 013/014 同形：

```bash
sqlcmd -S . -d Game -U sa -P <pwd> -C -f 65001 -i scripts/sql/015_seed_material_item_templates.sql
```

回退方式（可逆，只影响 `700001..700041` 这一段的物品模板行）：
`DELETE FROM Game_HunduShijie_ItemTemplate WHERE item_id BETWEEN 700001 AND 700041;`

执行这一轮又踩到一次**同族的静默失败**，值得记下来：补列覆盖时我在每条 UPDATE 末尾又追加了一次 `update_time = GETUTCDATE()`，而原 body 里本来就有 → SQL Server 报 `Msg 264`（SET 子句重复列）→ `SET XACT_ABORT ON` + 尾部 `ROLLBACK` 让整批回滚，**而 sqlcmd 仍返回 rc=0**。第一次"执行成功、0 行入库"就是这么来的（对账查询看到 `item_type=4` 还是 0 行才发现）。所以核验脚本 `scripts/verify-quest-material-seed.py` 补了一条**同一 UPDATE 内重复列赋值**检查，并且从此"跑完必须用库里独立算的数对账"，不看脚本措辞也不看退出码。

事后对账（库内独立查询）：`item_type=4` 行数 **41**、物品模板总行数 **171**（130+41）、`700001..700041` 命中 **41**、名字为空 **0**、重复 `item_id` 组 **0**；中文名字逐行按码点核过没被控制台码页打坏（700001=铁矿石、700002=青竹）。跟着翻掉的断言与门禁：`奖励域覆盖库里真有的物品类型` 已从 `DoesNotContain(4)` 改成 `Contains(4)` 并加上"41 行 / id 全在 700001..700041 / id 不重复"，DB 门控全跑一遍 **70 条全绿**；`QUEST_MATERIALS_IN_DB=1 python scripts/verify-quest-authoring-cli.py` 的案例 C（奖励指向材料 700001）从"必须被 Q034 拒"翻成**通过**。至此"奖励可关联所有游戏物品（含材料）"这条在权威源上成立。

这个翻转不用靠人记得改断言：`scripts/verify-quest-authoring-cli.py` 会**真的调用 CLI 的 `validate`**（只读，走"编辑器写临时草稿文件 → CLI"这条实路）跑三个案例，其中案例 C 是"奖励指向材料新号段 700001"，期望值随环境变量翻转：

```bash
python scripts/verify-quest-authoring-cli.py                           # 015 未入库：C 必须被 Q034 拒
QUEST_MATERIALS_IN_DB=1 python scripts/verify-quest-authoring-cli.py   # 015 已入库：C 必须通过
```

2026-10-05 实况：默认模式三条全中（A `talk_npc` 通过且只带 Q039 Info、B 未登记 NPC 被 Q027 拒、C 材料被 Q034 拒）；带标志模式如期报"应该通过却被 Q034 拒"，正是 015 尚未入库的正确信号。踩到的一个传输坑也记下来：草稿**必须走文件不能走管道**——CLI 读 stdin 用的是控制台码页，中文草稿会被读坏成 JSON 解析错误，而 `File.ReadAllText` 默认 UTF-8，那才是编辑器插件实际调用的路径。
