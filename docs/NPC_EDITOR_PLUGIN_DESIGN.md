# NPC 编辑器插件设计方案（MMORPG NPC 全属性）

> 目标原话：**为 NPC 编号，制定技能、行为、外观、AI 智能程度、发布任务（或任务链几点 NPC）、售卖物品、技能学习、
> 制作装备或物品、自动寻路（参照 Flax 原生 API 与 NarrativePro 实现）、掉落物品及掉落几率、
> 死亡贡献经验（PVP 时或敌对时有效，随玩家等级变化反比例衰减）等等 MMORPG NPC 的所有属性，
> 完全参考魔兽世界 NPC 行为在玩家视觉的实际表现（逆向分析然后实现它们）。**
> 规格输入：`docs/WOW_NPC_BEHAVIOR_REVERSE_ANALYSIS.md`（本文所有"逆向"结论的出处与证据分级都在那篇里）。
> 形态模板：`docs/MONSTER_EDITOR_PLUGIN_DESIGN.md` + `Plugins/MonsterEditor`（已落地的同类件，照形克隆，不另发明）。

---

## 0. 范围

**做**：NPC 的模板/编号/外观/等级/阵营/服务位图/任务发布与指示器/商人/训练师/制作设施与配方/
行为档位与巡逻寻路/掉落绑定/击杀贡献经验（含 PvP 与敌对）/刷点与刷新，以及承载这些的**表 + 内核 + CLI + 编辑器插件 + 运行期接线 + 客户端表现**。

**不做**（明确排除，防止范围自我缩小）：
- 不写第二套 AI/寻路/经验/掉落——一律复用 `Horizon.Game.Sim/Monsters/`（理由见规格 §15 复用矩阵）。
- 不补 NPC 美术资产（模型本体走 UE 重导是独立议题；本期做"配置 + 分流 + 名牌表现 + 缺失如实上报"）。
- 不碰拍卖行/邮件/公会银行（与 NPC 无关）。
- 不自行动数据库：迁移与种子**只生成不执行**，等用户点头（本项目既有约定）。

---

## 1. 目标原话 → 交付物对照（完成审计用这张表，不看别的）

| # | 原话 | 交付物 | 证据形态 |
|---|---|---|---|
| A1 | 为 NPC 编号 | `NpcTemplate.npc_id` + 号段规则（400000+/500000+）+ 旧 1/2/3 兼容 | 表 + CLI `catalog` + 冲突校验码 |
| A2 | 制定技能 | `NpcTrainerEntry`（教什么、多少钱、门槛）+ 敌对 NPC 复用 `MonsterSkillMount` | 表 + 内核 + 面板 + 真跑 `learn` |
| A3 | 行为 | `ai_bits` 能力位 + `NpcBehaviorCore`（转身/台词/自卫/呼叫增援档位） | 单测 + 预览视图 |
| A4 | 外观 | `model_key/prefab/scale` + 接怪物外观链（`MonsterVisualCatalog` 同形表） | 导出表 + 客户端分流 + 目视 |
| A5 | AI 智能程度 | 规格 §9.1 的 0–7 档 = 能力位组合；面板可勾选、可存 | 档位表对账测试 |
| A6 | 发布任务（任务链几点 NPC） | `NpcQuestIndicatorCore` 四态 + 反查索引 + 头顶指示器渲染 + **交付 NPC 校验补漏** | 单测 + 客户端截图 |
| A7 | 售卖物品 | `NpcVendorEntry` + `NpcCommerceCore` + 金币账本 + 商店消息通路 + 真数据替换 mock | 契约测试 + 端到端买卖 |
| A8 | 技能学习 | NPC 权威学习通路（服务端解析"这个 NPC 教不教"，费用与门槛叠加） | Live 测试 |
| A9 | 制作装备或物品 | `NpcCraftRecipe` + 设施位图 + **修 `CraftingGrain` 两个恒真/无校验方法** | 独立暴力参照对拍 |
| A10 | 自动寻路（Flax 原生 + NarrativePro） | 共用到图属性：`MonsterPathCore` + waypoint 表 + 引擎 `Navigation.*` 校验工具 | `navcheck` 真跑 + 烘焙/采样导出（见 D6） |
| A11 | 掉落物品及几率 | 复用 `MonsterLootBinding/DropEntry`，**owner 扩成生物 id** | `preview-drop` 蒙特卡洛 |
| A12 | 死亡贡献经验（PVP/敌对，随等级反比衰减） | `KillSource` 分派 + 复用 `MonsterDifficultyCore/MonsterXpCore` + 荣誉记录 | 与怪物同曲线的对拍测试 |
| A13 | 玩家视觉的实际表现 | 规格 §13 清单 15 行，逐行有"绘制点"与现状 | 每条有真跑证据或如实标缺 |
| A14 | 编辑器插件 | `Plugins/NpcEditor`：`Tools▸NPC 编辑器` + N 面板 + 自绘视图 + `_verify.sh` 0 错 | 离线 csc 门 |

> **审计纪律**：本表每一行的"证据形态"必须落到具体的测试名/命令回执/截图，不接受"编译通过"。
> 任何一行做不到就写"未做 + 为什么"，不许用相邻功能的绿灯替代（记忆 [自检全绿可能只是那行从没执行]）。

---

## 2. 分层

```
数据(SQL 权威) ── Redis 草稿 ── Horizon.NpcCli(唯一写入口) ── Plugins/NpcEditor(编辑器 UI)
        │                                                            │
        ├── NpcContent.Install(装配点，与 MonsterContent 同一份快照/同一个 rev)
        │        ├── DsHost / DsWorldSession（生成、交互、服务事务）
        │        └── Horizon.Orleans.Grains（权威端读同一份，见 D5）
        │
        └── 下发：Spawn Identity 追加服务摘要 → 客户端名牌/菜单/窗口（只画结果，不自判）
```

三条硬纪律（都是从本项目既有教训里来的）：
1. **判据只有一份**：让分/经验/颜色/门槛继续只在 `MonsterDifficultyCore`/`SkillGrain.CheckLearnGates`；
   NPC 侧任何新判据都要写明"为什么不能复用"。
2. **不许客户端上报身份换授权**：NPC 服务一律服务端解析（先例：`SkillHandler.cs:184-203` 丢掉 `LearnMethod`）。
3. **声明了就得有人读**：每个新列都要有唯一消费者 + 一条"值到了吗"的断言（记忆 [schema-key-declared-but-never-applied]）。

---

## 3. 数据层

沿用怪物侧的公共列口径：`publish_state`（草稿/已发布/退役）、`content_version`（乐观锁）、`enabled`。

| 表 | 关键列 | 唯一消费者 |
|---|---|---|
| `NpcTemplate` | npc_id, name, title, model_key, prefab_guid, scale, level, level_variance, reaction, faction_group, wuxing_domain, **service_flags**, ai_bits, movement_type, speed_walk, speed_run, can_be_attacked, kill_credit_npc_id, gossip_group_id, rank, publish_state, content_version | `NpcTemplateCatalog`（改为可注入源）+ `NpcServiceCore` |
| `NpcSpawnPoint` | spawn_point_id, zone_id, shard_id, x, y, z, yaw, npc_id, level_override, max_alive, respawn_seconds, route_id, schedule_id, facing_mode, enabled, publish_state, content_version | DsHost/NPC 生成器 |
| `NpcVendorEntry` | entry_id, npc_id, **slot**, item_id, buy_price_override?, sell_price_override?, stock_max, restock_seconds, cost_kind, cost_amount, category, enabled | `NpcCommerceCore` |
| `NpcTrainerEntry` | entry_id, npc_id, skill_id, service_kind, cost_gold, cost_items_json, req_level, req_realm, req_prerequisite_skill_id, seq, enabled | `NpcTrainerCore`（门槛继续问 SkillGrain） |
| `NpcCraftRecipe` | recipe_id, name, product_item_id, materials_json, cost_gold, station_npc_id, skill_req, quality_curve_json, learn_source_kind, publish_state | `NpcCraftCore` + 修好的 `CraftingGrain` |
| `NpcRecipeTaught` | recipe_id, npc_id, cost_gold, req_level | 教学通路 |
| `NpcGossipOption` | option_id, npc_id, seq, text_id, **action_kind**(menu/quest/vendor/trainer/craft/station/flight), action_arg, next_option_id, req_min_level, req_max_level, req_reputation, req_quest_state, req_alive_flag | `NpcServiceCore` + 对话面板 |
| `NpcQuestLink` | link_id, npc_id, quest_id, role(1=giver 2=turnin), order_seq | 指示器反查索引（**倒排，不在 UI 里现算**） |
| `NpcWaypointPath` / `NpcWaypointNode` | 照怪物侧 `MonsterPatrolRoute`/`MonsterPatrolPoint` 逐列同形 | 复用 `MonsterPatrolCore` |
| `NpcScheduleSlot` | schedule_id, npc_id, from_tick, to_tick, route_id, posture, gossip_group_id | 作息求值（服务端 tick） |
| `NpcDropOwner`(或扩 `MonsterLootBinding`) | owner_kind(mob/npc) + owner_id → 现有绑定 | 见 D4，**优先扩列而不是新表** |

**兼容与迁移**：旧 `giver_template_id` 里的 1/2/3 继续有效（新表用同号登记这三条，作为种子的一部分）；
`NpcTemplateCatalog` 的 `Get/TryGetRegistered/DomainOf/TryFindByName` **保持签名**，只把数据源换成可注入表——
`QuestStore.cs:73`、`QuestManagerGrain.cs:556`、`QuestHandler.cs:238` 三处调用点不动。

---

## 4. 内核（`Horizon.Game.Sim/Npcs/`，零存储零框架依赖的确定性纯函数）

| 文件 | 职责 | 关键签名 |
|---|---|---|
| `NpcServiceCore.cs` | 服务位图 → 玩家可见入口列表（能力位 ∧ 玩家态） | `Resolve(uint serviceFlags, in NpcPlayerContext ctx) -> NpcServiceEntry[]` |
| `NpcQuestIndicatorCore.cs` | 四态 + 计数（规格 §4） | `Resolve(offerState, progressState, gateState) -> (IndicatorKind, int Count)` |
| `NpcReactionCore.cs` | 反应档 → 名牌色 / 可否攻击（与让分色**正交**） | `Of(npcReaction, playerFaction) -> NpcNameColor` |
| `NpcCommerceCore.cs` | 定价解析（关系值优先模板值）、库存与补货时钟、买卖拒绝理由位 | `ResolvePrice(vendorRow, itemRow) -> (buy, sell)`；`CanSell(...)` |
| `NpcTrainerCore.cs` | NPC 教不教 + 费用；门槛**透传** `CheckLearnGates` 的理由码 | `CanTeach(entry, gateResult) -> TeachVerdict` |
| `NpcCraftCore.cs` | 配方教学/制作可行性（材料逐条缺多少）、设施在场校验 | `CheckMaterials(recipe, inventorySnapshot) -> CraftVerdict` |
| `NpcBehaviorCore.cs` | 能力位档位（0–7）+ 覆盖；含 alert（呼叫增援）选取规则 | `BitsFor(byte tier)`；`PickReinforcement(candidates, selfId)` |
| `NpcScheduleCore.cs` | tick → 当前路线/姿态/台词组 | `SlotAt(schedule, tickSeconds)` |
| `NpcContent.cs` | **装配点**（与 `MonsterContent` 同形：`Install/Revision/TryGetConfig/RegisteredIds`） | 同一份快照、同一个 rev 号 |

**必须复用的既有件**（不许重写）：`MonsterDifficultyCore`、`MonsterXpCore`、`MonsterThreatCore`、
`MonsterCastingCore`、`MonsterPathCore`、`MonsterPatrolCore`、`MonsterStatsCore`。

---

## 5. CLI（`Horizon.NpcCli`）

输出契约逐字照 `Horizon.QuestCli/Program.cs:19` 与 `Horizon.MonsterCli`：一行 JSON `{ok,verb,message,data,warnings}` + exit 0/1；
`.env` 口径照 `ConnectionConfig.cs:18`（`DB_GAME_CONNECTION_STRING` → 三处 `.env` → 回落）。

```
help | catalog | list [--segment --service --state] | get <id>
validate -                ← stdin 草稿；结构化错误含字段路径与码（N001…）
save -                    ← 模板/刷点（upsert + content_version 乐观锁）
draft save|get|list|delete -   ← 货架/训练清单/配方/对话/路线/日程
publish <id> | retire <id> | delete <id>
preview-indicator --npc <id> --player <lvl> --state accepted,done,none
preview-vendor  --npc <id>                 ← 槽位顺序/价格解析/库存回退
preview-train   --npc <id> --player <lvl>  ← 叠加 CheckLearnGates 的灰行理由
preview-craft   --recipe <id> --inventory <json>
preview-drop    --npc <id> --trials 10000
preview-xp      --npc <id> --player <lvl> [--pvp]     ← PvP 与敌对走同一曲线
route import <points.json> | navcheck --path <id>
export-content  --out <json>               ← 装配快照（与怪物 export 同一 rev 语义）
export-visuals  --out <json>               ← 外观表（喂客户端 NpcVisualCatalog）
```

---

## 6. 编辑器插件（`HundunWorld/Plugins/NpcEditor`）

照 `Plugins/MonsterEditor` 克隆：`.flaxproj` + `Source/NpcEditor`（GameModule，纯 DTO）+
`Source/NpcEditorEditor`（GameEditorModule，窗口/视图/CLI 桥）+ 两个 Target + `_verify.sh`（离线 csc 门，
复用 HundunAgent 的 `.CSharp.response`——**`dotnet build` 在这条链上会假绿**）。
`Tools▸NPC 编辑器`，注册用 `Editor.UI.AddMenuButton` + `EditorUpdate` 重试（`QuestEditorPlugin.cs:37-47` 的先例：
`InitializeEditor` 期 `Editor.UI` 未就绪）。

面板（每个都直接对应 §1 的一条原话）：

| 面板 | 覆盖 | 自绘视图 |
|---|---|---|
| 1 列表与编号 | A1 | — |
| 2 基础与外观 | A1 A4 | 3D 预览（走怪物侧已通的 `export-visuals`→prefab 分流） |
| 3 服务与交互（位图勾选 + 对话树） | A5 A6 A7 A8 A9 | 对话选项树 |
| 4 任务发布 | A6 | **任务链图**（giver/turn-in 双列 + 前置反查；照 `QuestFlowView.cs:88` 形状） |
| 5 商人货架 | A7 | 槽位网格（所见即玩家窗口顺序） |
| 6 训练与配方 | A2 A8 A9 | 技能阶梯（灰行理由列出来） |
| 7 行为与 AI 档位 | A3 A5 | **AI 档位状态机图**（照 `BehaviorGraphView`） |
| 8 巡逻与寻路 | A10 | **俯视路径图**（复用 `PatrolMapView` 形状 + 场景拾取点） |
| 9 掉落与经验 | A11 A12 | 经验曲线（含 PvP 叠画；复用 `XpCurveView`） |
| 10 校验与发布 | 全部 | 结构化错误列表（可定位到字段） |

---

## 7. 运行期接线（改动点逐个列，防止"改了没人读"）

| 接缝 | 现状（已核实） | 改动 |
|---|---|---|
| `Horizon.Game.Sim/NpcTemplateCatalog.cs:37` | 三行硬编码 | 数据源换成 `NpcContent` 已装配表，签名不变；`Get` 兜底只服务显示，授权仍只走 `TryGetRegistered` |
| `Horizon.Game.Core/Sim/Server/DsWorldSession.cs:2545-2554` | NPC 只从 GM 脚本生成，零战斗参数 | NPC 生成器读 `NpcSpawnPoint`；敌对 NPC 接威胁/施法；友好 NPC 走 `can_be_attacked=false` |
| 同文件 `:2187-2200` 下发 Identity | 只带名字 | **追加服务摘要**（指示器四态 + 可见入口 + 反应档）——注意这是消息契约，要同时进 `Game.Build.cs` 与 `copy-shared-dlls-to-flax-tools.ps1` 两份清单，否则 `FlaxToolsRefreshListTests` 红 |
| `FlaxActorSyncSystem.cs:119` | 只有 `MonsterPrefabPath` | 加 NPC 侧分流（接怪物外观链同一张表形状），**加载失败显式回退并记诊断** |
| `NameplateUI.cs:195` | 头衔/名字/等级/血条 | 加指示器槽 + 反应色（两轴分开；让分色继续走 `MonsterDifficultyCore`） |
| `QuestHandler.cs:107-154` | offer 过滤已实现但客户端不传 giver | 客户端交互时**带真实 giverEntityId**；指示器与菜单都读这份结果 |
| `CompleteQuest` 通路 | **只带 CharacterId+QuestId，交付 NPC 未校验**（缺陷） | 补交付校验 + 存量任务全量对账（规格 §14 待读 5） |
| `ScriptAuthorityCore.cs:384/452` | 白名单 = `TryGetRegistered` | 数据源换表；拒绝理由带状态位 |
| `MessageType.Shop = 502` | **无 handler** | 建 `ShopHandler`：列表/买/卖/修理，全服务端定价 |
| `CurrencyEntity` | 表在、**零消费者** | 金币账本走它（`total_earned/total_spent` 当对账口径），写侧必须配拒绝分支对账字段 |
| `CraftingGrain.cs:57 LearnRecipeAsync` / `:89 CheckMaterialsAsync` | 捏配方 / 恒真 | 按 `NpcCraftRecipe` + 真背包修；配独立暴力参照 |
| `MenuShopPage.cs:926-996` | 硬编码 mock | 换真数据（保留视觉不动，只换数据源） |
| `Horizon.DsHost/Program.cs:441-463` | 只生成怪 | NPC 刷点同一入口，`--npc-content` 开关（照 `--monster-content` 的回执写法） |

---

## 8. 验证闸门（每条真跑，不接受编译当证据）

| 层 | 闸门 |
|---|---|
| 插件编译 | `Plugins/NpcEditor/_verify.sh` 离线 csc 两模块 0 错（这同族闸门在怪物侧当场拦下过 3 个猜错的引擎 API 名） |
| JSON 契约 | `Horizon.NpcContractTests`：两侧键清单逐字相同 + **`IncludeFields` 断言**（记忆 [STJ 默认不绑公开字段]） |
| 内核 | 单测：指示器四态 × 边界、服务位图 ∧ 玩家态、定价回退、库存时钟、档位表对账；**与怪物侧共用判据的部分要做"同输入同输出"对拍** |
| 经济 | 买卖幂等与守恒测试（买 N 件 → 背包 +N、金 −N、日志一条；拒绝分支不产生半成品状态） |
| SQL | 迁移/种子**生成不执行**；执行前 `sqlcmd -E` 从库里独立算期望值对账（记忆 [本地 SQL Server 用 sqlcmd 免密直连]） |
| 端到端 | CLI `validate/save/publish` → DsHost 装 NPC → 客户端名牌出现 `!` → 对话 → 买/学/做 → 击杀 → 经验数字 |
| 目视 | 指示器/菜单/商店/巡逻四张表现要**截图**（记忆 [程序化建模：数值门全绿也要看渲染图]） |
| 回归 | 全量 `dotnet test`；怪物侧 36 条 + 全量 3289 条不许红（NPC 复用它们的判据，红了说明我动了共用代码） |
| 注释 | 新代码注释一律简体中文；`python Tools/comment-sweep-scope.py` 口径 |

**断言纪律**（本项目踩过的坑，直接进测试设计）：拒绝分支回同一套对账字段 + 正向同夹具对照；
扫描型检查要有条数下限；追加序列化成员测槽位对位；期望值凭数据不凭记忆；
"结构体默认 0 撞合法下标"这类坑在指示器四态与档位表上都要防。

---

## 9. 实施顺序（每步带闸门，跨步不并行改同一文件）

1. **内核 + 表**：`Npcs/` 六个纯函数 + 实体 + 迁移 + `scripts/sql/021_npc_editor_tables.sql`、`022_seed_npc_templates.sql`（**生成不执行**）+ 单测（含与怪物侧同曲线对拍）。
2. **CLI 骨架**：`Horizon.NpcCli` catalog/list/get/validate/save/publish（真跑）。
3. **插件骨架**：能打开 `Tools▸NPC 编辑器`（`_verify.sh` 先绿）。
4. **模板 + 服务位图 + 刷点** → 打通"编辑 → 发布 → DsHost 生成 NPC → 客户端出现一个有名有姓、可交互的 NPC"。
5. **任务指示器**：内核 + 反查索引 + 客户端名牌 + 交付校验补漏。
6. **商人**：`ShopHandler` + 金币账本 + 定价/库存 + `MenuShopPage` 换真数据。
7. **训练师 + 技能学习**：NPC 权威通路 + 费用 + 灰行理由透传。
8. **制作**：配方表 + 设施 + 修 `CraftingGrain` + `CraftingPage` 换真数据。
9. **行为档位 + 巡逻/寻路**：waypoint 表 + 场景拾取 + `navcheck` + 呼叫增援。
10. **掉落绑定（扩 owner）+ 贡献经验含 PvP 分支**。
11. **外观分流 + 目视验收 + MCP/Agent 工具**（`NpcTools`：`npc_draft_get/npc_validate/npc_preview_indicator`，让自己能闭环取证，不要用户配合测试）。

---

## 10. 决策清单（**需要用户拍板**；每题都给了推荐档）

| # | 问题 | 选项 | 我的推荐 |
|---|---|---|---|
| **D1** | NPC 与怪物是否合成一张 creature 表 | a) 合并（NPC = creature + 服务位图，最贴 WoW） b) 分表，共用内核（怪物侧 8 表已建、3289 测试已钉，合并=重做迁移与全部断言） | **b**：分表存、**内核与装配点共用**。这是"最完整实现"里代价最低且不变形的一档——服务层照样是位图驱动，玩家看不出差别 |
| **D2** | NPC 号段 | a) 400000 服务 / 500000 敌对（旧 1/2/3 保留） b) 与怪物混用同一号段 | **a**，理由见规格 §1.3（"能不能打"不该靠猜列） |
| **D3** | 头顶指示器的求值位置 | a) 服务端算完随 Identity 下发摘要 b) 客户端拿任务列表自己算 | **a**。b 会让"能不能接"两套判据漂，且等于把门槛逻辑交给客户端——与 §2 纪律 2 冲突 |
| **D4** | NPC 掉落的载体 | a) 扩 `MonsterLootBinding` 的 owner（加 `owner_kind`） b) 新建 `NpcDropEntry` 一套 | **a**：两份掉落掷判据必然漂，且 `preview-drop` 与蒙特卡洛测试可直接复用 |
| **D5** | NPC 内容的权威通路 | a) 只装 DS（照怪物侧现状） b) DS + Orleans Silo 两条都装 | **b 是目标形态**，但**怪物侧的同类前提已被证伪**（`ZoneShardGrain` 里 `Monster` 出现 0 次，模拟只在 DS 通路）。若 NPC 也只在 DS 通路跑，则 a 与 b 等价——**开工第 4 步先验证 NPC 有没有第二条通路**，有就装，没有就如实记录而不是硬造一个 |
| **D6** | 寻路的真图来源 | a) 沿用怪物侧高度图网格 A\*（已交付，现在是 32×32 fixture） b) 用引擎 `BuildNavMesh` 烘焙 + 导出多边形图 c) 本期继续走 fixture + 编辑器校验走 `Navigation.TestPath` | **a 为主 + b 为增量**：NPC 与怪共用同一张图；引擎烘焙这条要先答"烘焙产物能否离线导出给服务端"（规格 §14 待读 4），答不出就走 a |
| **D7** | 金币权威存储 | a) 用已在盘上但零消费者的 `CurrencyEntity` b) 新表 c) 物品化（金币当普通物品进背包） | **a**：表已存在且 `total_earned/total_spent` 天然是对账口径；选 b 会留下"两张钱包表"的第二个 `CurrencyEntity`（声明没人读的复刻）。要配守恒测试 |
| **D8** | 买卖价与 `ItemTemplate.sell_price/buy_price` 的关系 | a) 关系表优先、模板兜底 b) 只用模板 c) 只用关系表 | **a**：让那两列第一次有消费者（治"有列没人读"），又保留"这个商人卖得贵"的表达力 |
| **D9** | 技能学习要不要引入"NPC 上报"通路 | a) 服务端由交互目标解析 NPC，客户端不带 id b) 客户端带 npcId，服务端校验 | **a**。`SkillHandler` 已有"不信客户端 LearnMethod"的先例；交互目标本身已经是服务端已知的实体 id，所以 b 并不更弱——但 a 少一条消息字段，选 a |
| **D10** | 本期是否修 `CraftingGrain` 的恒真校验 | a) 修（做真配方） b) 只做数据、制作留二期 | **a**。不修的话"制作装备或物品"这条原话只有表、没有行为，等于范围被偷偷缩小；这是本期最大的一笔既有缺陷清理 |
| **D11** | 交付 NPC 校验补漏的影响面 | a) 补校验 + 先跑存量任务全量对账 b) 暂不补（保持现状） | **a**，但**顺序上先对账再上校验**——直接补会让已发布任务里"交付 NPC 没生成"的那批突然交不掉（规格 §14 待读 5） |
| **D12** | 一期范围 | a) 全链（内核+表+CLI+插件+运行期+客户端表现+目视） b) 数据+内核+编辑器 c) 只做编辑器 | **a**（目标原话包含"玩家视觉的实际表现"，且怪物侧已验证全链走得通）。按记忆 [用户拍板口径]，此项预期你会选最完整那档 |

### 10.1 决策结果（2026-10-25 用户拍板，四问全选推荐档）

| # | 结论 |
|---|---|
| D1 表结构 | **分表 + 共用内核**（不动怪物侧已建的 8 张表与钉住它们的 36+3289 条测试） |
| D7 金币权威 | **用已在盘上的 `CurrencyEntity`**（让这张零消费者的表第一次有读者；配守恒与拒绝分支对账测试） |
| D10 制作 | **本期一并修 `CraftingGrain`** 的 `LearnRecipeAsync` / `CheckMaterialsAsync`（配独立暴力参照对拍；改前先普查调用面） |
| D12 范围 | **全链**：内核 + 表 + CLI + 插件 + 运行期接线 + 客户端表现 + 目视 |

其余（D2–D6、D8、D9、D11）按本文推荐档执行：号段 400000/500000、指示器服务端求值、
掉落复用 `MonsterLootBinding` 扩 `owner_kind`、寻路以怪物高度图 A\* 为主、
关系价优先模板价、NPC 身份由服务端从交互目标解析、交付校验先对账后上线、
D5 在第 4 步现场验证"NPC 有没有第二条权威通路"后如实记录。

### 10.2 需要你点头才做的事（不擅自执行）

- 执行 SQL `021`/`022`（与怪物侧 `019`/`020` 同样只生成）。
- 改 `Horizon.Game.Message` 消息契约与两份 DLL 清单（会牵动 `FlaxToolsRefreshListTests`）。
- 若 D5 选 b，需重启 Silo/网关（服务进程顺序与清理有既定要求，见记忆 [Orleans服务进程启动顺序]）。

---

## 11. 进度表（2026-10-25 首轮）

### 11.1 已落地，且有真跑证据

| 层 | 产物 | 证据 |
|---|---|---|
| 规格 | `docs/WOW_NPC_BEHAVIOR_REVERSE_ANALYSIS.md`（§14 证据分级 + 5 条待读） | 外部核对过指示器语义、`npc_vendor`/`creature_template` 字段、npcflag 位图口径 |
| 设计 | 本文（§1 是 A1–A14 交付物对照，完成审计只认这张表） | 决策四问已拍板（§10.1） |
| 内核 | `Horizon.Game.Sim/Npcs/`：`NpcServiceCore`（位图→可见入口 + 死位拒绝）、`NpcQuestIndicatorCore`（四态 + 数字前缀 + 坏行计数）、`NpcReactionCore`（立场→色/可否打/给不给经验/引不经守卫，三态覆盖）、`NpcCommerceCore`（覆盖→模板→拒绝、库存与补货时钟、TryBuy 四档）、`NpcTrainerCore`（游戏门槛码原样透传）、`NpcCraftCore`（逐件缺料 + 空配方拒绝 + 设施门 + 曲线表强制显式）、`NpcKillCore`（来源分派，**经验逐字委托怪物内核**）、`NpcScheduleCore`（周期换算/跨零点/全天兜底/覆盖普查）、`NpcGossipCore`（等级可见性 + 动作↔服务位成对校验）、`NpcContent`（装配点 + `DescribeInstall` 回执） | `dotnet test --filter NpcEditorKernel` → **47 通过 0 失败**；与怪物侧合跑 **133 通过 0 失败** |
| 数据 | `Horizon.Model/GameModel/NpcTemplateEntity.cs`（24 列主表）+ `NpcWorldEntity.cs`（11 张关系/世界表）+ 两个 DbContext 各登记 11 个 DbSet | `dotnet build Horizon.Model` 0 错误 |
| 接缝 | `NpcTemplateCatalog.Get/TryGetRegistered/RegisteredTemplates/TryFindByName` 改成"先查装配内容，再退硬编码三行"；签名与调用点（`QuestStore.cs:73`、`QuestManagerGrain.cs:556`、`QuestHandler.cs:238`）不变 | 编译通过；**未装配==现状的等价性合同测试还没写**（见 11.3） |

### 11.2 本轮被测试抓出的真缺陷

`NpcScheduleCore` 最初用同一个 `>=` 卡作息档的起止秒。作息终点是**开区间**，
所以 `to_seconds == 周期长度`（"做到日终"）是完全合法的数据，却被判成越界坏行并**静默跳过**。
症状是最后一段作息永远不生效、而所有数值门照样全绿。现已由 `NpcScheduleSlot.FitsInCycle`
把"起点必须 < 周期、终点允许 == 周期"这一条定死，并配了两条断言（合法日终档命中 + 起点越界仍报坏行）。

### 11.3 已补上（第二轮）

| 层 | 产物 | 证据 |
|---|---|---|
| 迁移 | `Horizon.Entities/Migrations/Games/20261026160704_AddNpcEditorTables.cs` + Designer + 快照 | `dotnet ef migrations add` 生成后**已剔除并行会话的 11 张 Pet 表**（它们当时还没有自己的迁移，被差分一并卷入；剔除有脚本 `Tools/_strip_pet_from_npc_migration.py`，带"Pet 清干净 / NPC 一根不少 / 实体数恰好少 11"三条断言） |
| SQL | `scripts/sql/021_npc_editor_tables.sql`（500 行、**11 张 Npc 表**，无 Pet）+ `022_seed_npc_templates.sql`（3 条存量 + 2 条样板 + 6 处自检） | 021 由 `dotnet ef migrations script` 生成；022 用 `SET PARSEONLY ON` 过语法（**未执行**，退出码 0）；列名离线对账 `Tools/_verify_npc_seed_columns.py` → 11 表 / 7 张被引用 / **70 个 (表,列) 全部存在** |
| 装配器 | `Horizon.Game.Core/Sim/Server/NpcContentSnapshot.cs`：快照 DTO 族 + `NpcContentLoader.Compile/LoadFile` + 回执 `Describe()` | 编译 0 错；被 CLI 的 `validate/save/export-content` 直接复用 |
| CLI | `Horizon.NpcCli`（help/catalog/get/validate/save/publish/retire/preview-indicator/vendor/train/craft/xp/schedule/gossip/export-content） | **真跑**：good 草稿 → `problems=0`；坏草稿 → 精确 10 个码（reaction/ai_tier/stock/ trainer/recipe×3/gossip bit/questlink/schedule gap）；七个 preview 全部回执正常；`preview-xp` PVP 大号杀小号 → `GrayConcession`、敌对同级 → 226 |
| 插件 | `HundunWorld/Plugins/NpcEditor`（flaxproj + 两模块 + `Tools▸NPC 编辑器` + 6 面板窗口 + CLI 桥），已登记进 `HundunWorld.flaxproj` 与 `HundunWorld.slnx`（两文件 JSON/XML 校验通过） | `_verify.sh` 离线 csc：**rt 0 错、ed 0 错**（这道门当场拦下 `UTF8Encoding` 在编辑器脚本域不可用 → 已改 `Encoding.UTF8`） |
| 测试 | `NpcEditorKernelTests` 47 + `NpcEditorContractTests` 7（目录未装配等价、装配值优先、草稿往返、坏样本十码、STJ 绑定、gossip 的 0 语义） | `dotnet test --filter NpcEditor` → **54 通过 0 失败** |

### 11.4 接下来（第三轮已做前两项）

| 项 | 状态 |
|---|---|
| **DsHost 接线** | ✅ `--npc-content=<path>` 装入并打印回执；`DsHostRunner` 在装完刷点后调 `ApplyNpcContentSpawns()`。**实跑证据**：`NPC 内容已装配：npc_content_loaded rev=3 templates=2 spawns=2 … problems=0` + `NPC 刷点：已生成 1（敌对型跳过 1，分片不符 0，模板缺失 0）`；不给开关时一行 NPC 日志都没有（行为与之前逐字相同） |
| **客户端名牌立场色** | ✅ 新增 `Source/Game/Selection/NpcStanceCatalog.cs`（按 npcId/modelKey 查立场，内置 1/2/3 三条兜底）+ `NameplateUI` 对 `SelectableKind.Npc` 走绿/黄/红（与怪物让分色正交）。数据由 `Horizon.NpcCli export-visuals --out` 生成到 `Data/Npcs/client_visuals.json`；**库不可用时自动兜底成硬编码三条，并把原因写进回执**（实测：021 未执行时 `source=catalog_fallback` + SqlException 原文）。客户脚本门 `verify-client-scripts.sh` → 478 源 0 错；契约测试钉住两侧键名与立场编号（0/1/2） |
| 视觉验收 | ⚠️ **尚未目视**：绿/黄/红只做了编译与查表断言，没有 PIE 截图。按项目纪律这只能算"逻辑已通、观感未验" |

### 11.5 第四轮：钱包（商人/训练/制作共同的地基）

| 层 | 产物 | 证据 |
|---|---|---|
| 算术内核 | `Horizon.Game.Sim/Economy/WalletCore.cs`：`Spend/Earn` + `WalletReject`（BadAmount/InsufficientFunds/Overflow/UnknownCurrency），**拒绝时余额分毫不动**；加钱越界拒而不钳位 | `WalletCoreTests` **7 通过 0 失败**（含 5000 步随机序列：余额恒在 [0, MaxBalance]、成功操作的变化量与金额严格相等、拒绝=零副作用） |
| 权威钱包 | `Horizon.Orleans.Interface/ICurrencyGrain.cs`（`GetBalance/Spend/Add` + `WalletMutation` 回执）+ `Horizon.Orleans.Grains/CurrencyGrain.cs`（`GameStore` 持久化，`CurrencyState` 存余额 + 累计收支） | `dotnet build Horizon.Orleans.Grains` 0 错误；Grain 只做"读状态→调内核→落状态→通知监控"，失败路径连 `WriteStateAsync` 都不调 |
| 经济监控接线 | 每次金币变动通知既有的 `IEconomyMonitorGrain.RecordCurrencyMint/Burn`（该接口此前**全仓零调用方**）；通知失败只记警告，不让已落库的扣款变成"调用方以为失败" | 代码路径 + 注释口径；未连集群实跑 |

**仍未完成（顺序即优先级）**：
1. **商人链的客户端与端到端**：服务端已全通（见 §11.6），但 `MenuShopPage` 仍是纯 mock、没人发 `ShopOpen/Buy/Sell`；
   端到端实跑还需要起 Silo + Gateway（起共享服务要先取得点头）。
2. **头顶 `!` / `?` 指示器**：Identity 追加服务摘要（动消息契约 + 两份 DLL 清单）。
3. **训练师与制作**：NPC 权威学习通路（D9，学费走 `ICurrencyGrain`）+ 修 `CraftingGrain` 两处假实现（D10）。
4. **交付 NPC 校验**（先对账存量任务，D11）。
5. **敌对 NPC 真生成**（属性 + 掉落 owner 扩展 + 贡献账本三件套）。
6. **PVP 阵营口径（决策 D13，需你拍板）**。

### 11.6 第五轮：商人服务端全链

| 层 | 产物 | 证据 |
|---|---|---|
| 库存记账内核 | `Horizon.Game.Sim/Npcs/NpcShopLedgerCore.cs`：存"已买数 + 补货锚点"（不存剩余量——剩余是时间的函数），跨补货边界自动重置；失败零副作用 | `NpcShopLedgerTests` **5 绿**，含 2000 步随机售卖（剩余恒在 [0, 上限]、失败不改状态） |
| 库存权威 | `INpcShopGrain`/`NpcShopGrain`（按 NPC 分键，`GameStore` 持久化）；`TakeAsync` 原子占用、`ReturnAsync` 显式冲正（补偿路径不能被"负数=坏请求"挡死） | `dotnet build Horizon.Orleans.Grains` 0 错 |
| 消息契约 | `Horizon.Game.Message/Network/ShopMessages.cs` 六条（开窗/买/卖 各 req+resp），union 标签 250–255，`ServiceType.Trade` | `ShopMessageContractTests` **6 绿**：MemoryPack 往返、**以 `MessageUnion` 走真实线上路径**能回到具体类型（证明标签注册生效）、标签互异且落在 250–255 |
| 处理器 | `Horizon.Game.Core/Handlers/ShopHandler.cs`：身份由 `QuestInteractionAuthority` 三重核对（存在/可交互/够近），不采信客户端自报 npcId；买=查库存→扣钱→占库存→进背包，三步各有补偿；卖=先扣物后给钱（钱进不去就退物） | `dotnet build Horizon.Game.Core` 0 错；**未连集群实跑** |
| 网关装配 | `Horizon.Game.Gateway` 启动时按 `--npc-content=<path>` 或 `NPC_CONTENT_PATH` 装入 NPC 内容并打回执；没配时商人请求如实回 `not_a_vendor` | `dotnet build Horizon.Gateway` 0 错 |

**合并门禁**：`NpcEditor* + WalletCore + NpcShopLedger + ShopMessageContract` → **75 通过 0 失败**。

**客户端管道（同轮补）**：`Source/Game/Network/MessageHandlers/ShopMessageHandler.cs`（按报文类型再分派，
三条回执各一个事件）+ `Source/Game/Network/ShopClientService.cs`（单例，`Open/Buy/Sell` + 货架缓存 +
`Revision` 脏标记 + `LastNotice/LastReason`；**不预扣钱、不自行判"买得起吗"**）。
`verify-client-scripts.sh` → **480 源 0 错**（不需要注册：`NetworkManager` 按反射装配处理器）。
**页面仍未接**：`MenuShopPage` 的卡片在 `BuildContentArea` 里一次性建好并按下标绑 `_products`，
换成服务端数据需要"重建卡片 + 重排"，这一步单独做（下轮），避免半套渲染。

### 11.7 第七轮：商店页接入（闭环已通到"能点"）

| 环节 | 实现 | 说明 |
|---|---|---|
| 入口 | `WorldSelectionService.TryInteractSelected`：对**商人型 NPC** 交互时 `ShopClientService.RequestOpen(entityId)` + `MainUIManager.NavigateToInkPage(nav-social-shop)`（新增公开导航方法） | 判据 `NpcStanceCatalog.IsVendor` 来自导出服务位图，与服务端 `NpcServiceCore` 同源；**只决定弹不弹窗** |
| 绑定 | `MenuShopPage.Update` 取走 `TakeRequestedVendor()` → `OpenVendor`（路由复用实例时同样生效） | 覆盖"首次打开"与"复用旧页面"两条路 |
| 渲染 | `ApplyServerShelf`：服务端货架 → `ShopProduct`（名字/品质/字形取自 `ItemCatalog`）→ **重建卡片**（旧卡解绑并 Dispose）→ `SelectTab + ApplyLayout` | 卡片在构造期读死数据，只改 `_products` 不会刷新任何字 |
| 成交 | `OnConfirmPurchase` 在商人模式下只发 `Buy(entityId, slot, qty)`，不本地扣钱；回执经 `Revision` 回到 `_actionHintLabel` | 本地余额由回执里的 `GoldAfter` 覆盖 |
| 服务位导出 | `NpcVisualsExport` 增 `services` 键（协议键表同步），客户端 `NpcStanceCatalog` 解析并新增 `IsVendor`；兜底三条服务位与 022 种子同值（121/7/9） | 契约测试断言协议键集合与三条兜底的服务位 |

**门禁**：客户脚本 **480 源 0 错**；`NpcEditor* + WalletCore + NpcShopLedger + ShopMessageContract` → **75 通过 0 失败**。
**未做**：端到端实跑（需 Silo + Gateway + 客户端，起共享服务要先取得点头）与目视验收。

### 11.8 第八轮：训练师收费（技能学习走 NPC）

| 环节 | 实现 |
|---|---|
| 请求 | `LearnSkillRequest` 追加 `EntityId`（MemoryPackOrder 5）：<b>0 = 书/直接学（老路不变）</b>；非 0 = 正在交互的 NPC 实体 |
| 解析 | `SkillHandler.ResolveTrainerAsync`：`QuestInteractionAuthority` 三重核对（本 shard 存在 / 可交互 / 够近）→ `NpcContent.TryGetConfig` 要求 `Trainer` 位 → `NpcContent.TrainerRows` 里必须确有这一式的 `Skill` 行 |
| 收费 | 学费走 `ICurrencyGrain.SpendAsync`（来源标签 `npc_trainer:<npcId>:<skillId>`）；教学材料走 `ConsumeByTemplateAsync` |
| 补偿 | **先扣后学，学失败原路退回**（金币 `AddAsync` + 材料 `AddItemAsync`）；门槛失败是常规路径，不是异常 |
| 门槛 | 仍然只由 `SkillGrain.CheckLearnGates` 判 —— NPC 层不重写等级/境界/前置，理由见 §6.2 |

**证据**：`dotnet build Horizon.Game.Core` 0 错；75 绿不变。
**客户端（同轮补）**：`TrainerClientService`（软绑定实体 id）；`NpcStanceCatalog.IsTrainer`（位 1<<4，服务位来自导出表）；
交互训练师 → 绑定 + `NavigateToInkPage(nav-skill-tree)`；`SkillTreePage` 学习时带上该实体；**学习失败自动解绑**
（失败多半是"人不在跟前"，服务端在扣费前就按距离拒了，所以不会扣钱）。
客户脚本门 **481 源 0 错**。
**未做**：训练师窗口（列出"教什么、多少钱"）与收费提示横幅——现在玩家只在结果提示里看到成败。

### 11.9 全量回归的状态（2026-10-26）

**绿色回执（本目标改动落地后、并行会话下一次编辑之前）**：`dotnet test` → **3588 通过 / 0 失败 / 8 跳过（3596）**。

**当前绿色回执**（NPC 路线接入后重跑）：**3601 通过 / 0 失败 / 8 跳过（3609）**——
并行会话的巡逻用例已由对方修好，本条取代下面的历史记录。

**历史：一次中间态**（制作 handler 落地、对方巡逻改动未落定时）：**3591 通过 / 2 失败 / 8 跳过（3601）**，
两条失败均为 **`MonsterEditorKernelTests` 的巡逻用例**——
`同一条巡逻线在v1与v2上给出逐格相同的走法`、`默认档的怪也沿画好的巡逻线走`，
失败信息分别是"600 tick 只到了出生点附近：这条线根本没被读"与"没给线它却走到了 (6,0)"。
这两条正属于**并行会话正在做的"v1 沿线路走"**（同轮其文件 `MonsterAiCore`/`DsWorldSession` 多次处于半改状态），
**不在本目标路径**，按纪律未触碰。NPC 侧子集（`NpcEditor* + WalletCore + NpcShopLedger + ShopMessageContract + CraftMessageContract + CraftingGrainGuard`）
同轮 **78 通过 0 失败**。

> 结论：**本目标的改动没有引入失败**；全量门在对方巡逻工作落定后需要再跑一次确认（届时本节应只剩一行绿色）。
> 8 条 skipped 属既有，明细需 `-v n` 单跑。

**最新一次全量（NPC 作息接入后）：3595 通过 / 7 失败 / 8 跳过（3610）——7 条全部是并行会话的怪物侧在途改动：**

| 失败 | 直接证据 | 归属 |
|---|---|---|
| `GameplayGoldenTraceTests` 6 条 | 断言原文：`轨迹 AiVersion=6 ≠ MonsterAiCore.Version=7——轨迹已过期，须重生成` | `MonsterAiCore.Version` 被并行会话从 6 升到 7，金轨迹 fixture 未同步 |
| `MonsterEditorContractTests.脱战与起手…` | `宿主给 cfg.FollowType 赋值 1 处（应当恰好两处…）` | 他们把两处赋值合并/改写了一处，契约计数未同步 |

本目标当轮改的是 `NpcScheduleCore.RouteAt` 与 `ResolvePatrolRoute` 的 NPC 分支（怪物路径逐字不变），
单跑 `Schedule_RouteAt`、`NpcRouteTests`、`NpcIdleWander` 全绿。
**结论：NPC 侧无回归；全量门需等对方把金轨迹/契约更新完再跑一次。**

**第十三轮补记**：敌对型 NPC 改为**走怪物管线真生成**（`ScriptedKind.Monster`），名字/等级在怪物目录查不到时
回退到 NPC 目录（`ResolveMonsterTemplate` / `ResolveTemplateLevel`）；`ApplyNpcContentSpawns` 的计数拆成
"总生成数"与"其中敌对型"两个，日志措辞同步（原来写着"跳过"，实际已生成）。
实测：`NPC 刷点：已生成 2（其中敌对型 1；分片不符 0；模板缺失 0）`。
为了让回退分支可断言，`Horizon.Game.Core` 对测试程序集开了 `InternalsVisibleTo`（只开内部静态解析，不扩大公共面）。

### 11.10 制作链设计与决策（D10 的落地口径）

**现状复核（已核实）**：`ICraftingGrain` **全仓零调用方**——没有 handler、没有客户端请求，连键口径都没人用过；
`LearnRecipeAsync` 当场捏一个 `SuccessRate=1.0` 的配方、`CheckMaterialsAsync` 不看背包直接 `true`。

**决定（按"每个权威只放一处"分工）**：

| 环节 | 归属 | 理由 |
|---|---|---|
| 配方定义（材料/产物/工费/设施/难度曲线） | **Gateway handler**（`NpcContent.Recipe`，内容已装配） | Grain 不查库/content 依赖最小；与商店/训练师同一取数路径 |
| 配方是否已学、制作历史 | `CraftingGrain`（`GameStore` 持久化，键用 `GrainKeys.ForEntity(characterId)`） | 已有的 `GrainKeys` 把角色 id 映到 Guid，与 `SkillGrain` 同一口径 |
| 材料是否齐、逐件缺多少 | `NpcCraftCore.CheckMaterials`（纯函数） | 已交付并有暴力参照对拍；**Grain 里没有背包访问权**，不许"返回 true" |
| 扣材料 / 收工费 / 出成品 | handler 调 `IInventoryGrain` + `ICurrencyGrain` | 与商店同一条成交流程；失败按顺序补偿 |
| 成功率 | **本工程没有"制作技能点"这一权威输入** → 一期口径：`skill_req` 只作为**门槛**（不满足则拒），材料齐即可做 | 在代码里编一个"成功率随技能涨"的曲线就是凭空造数值；要做得先有制作技能数据源，属二期 |

**接口改动（无调用方，安全）**：
`LearnRecipeAsync(int)` → `LearnRecipeAsync(CraftingRecipe definition)`（沿用已有 DTO：`RequiredMaterials`/`RequiredGold`/`RequiredLevel` 都在）；
`CheckMaterialsAsync(int)` → `CheckMaterialsAsync(int recipeId, Dictionary<long,int> held)`（返回逐件缺料回执，不再是 bool）；
`CraftItemAsync(int)` 保留但**只做"已学 + 材料/工费判定 + 记录历史"**，扣减与发货由 handler 执行（Grain 碰不到背包）。

### 11.11 制作链落地进度

| 环节 | 状态 |
|---|---|
| Grain 真判据（学/查料/制作） | ✅ §11.10；`CraftingGrainGuardTests` 双钉（旧串消失 + 新判据存在） |
| 报文 | ✅ **复用** `CraftingRequest/Response`（union 标签 75/76，号段已到 255 不再新增）；请求追加 `EntityId`(6)/`Action`(7)，回执追加 `Recipes`(7) |
| 处理器 | ✅ `CraftHandler`：`Action=1` 学配方（`CraftTrainer` 位 + 内容里有该配方）、`Action=2` 清单（教的 ∪ 该设施能做的，去重+按 id 排序）、`Action=0` 制作（设施位 → 背包快照 → Grain 判 → 扣料→工费→发成品，逐步补偿） |
| 契约测试 | ✅ `CraftMessageContractTests`：`EntityId/Action` 与 `Recipes` 都走 **union 真实线上路径**往返 |
| 客户端服务 | ✅ `CraftMessageHandler` + `CraftClientService`（清单/学/做 + `Revision` 脏标记）；`NpcStanceCatalog.IsCraftTrainer/IsCraftStation`；交互训练师/设施 → 拉清单并打开制作页 |
| 制作页 UI | ✅ 卡片改为**按服务端清单重建**（旧卡解绑并 Dispose 后重建，`OpenForVendor` + `TakeRequestedVendor` 复用页面实例）；"制作"区从装饰标签变成**真按钮**（新增最小 `ClickTarget`，因为 `ContainerControl` 没有 `Clicked`），选中配方后显示工费并按配方 id 发请求；未接设施时状态行明确写"演示数据 · 未连接制作设施" |

**门禁**：`Horizon.Game.Core` 0 错；客户脚本 **483 源 0 错**；全量 **3594 通过 0 失败 / 8 跳过**（§11.9）。
**仍未验**：制作页的目视（卡片换数据后的排版、按钮点击后的回执）没有截图验证；端到端需起 Silo+Gateway。

### 11.14 友好 NPC 的可见行为（踱步/移速）

- `CreateScriptedPoint` 增加两个可选覆盖（`wanderRadiusOverride` / `moveSpeedOverride`），**旧调用点逐字不变**；
  NPC 分支在"清零战斗参数"之后再应用覆盖。
- `ApplyNpcContentSpawns` 按内容翻译：`ai_bits.Wanders` → 6 米踱步圈（常量 `NpcIdleWanderRadiusMeters`），
  `speed_walk` → 移速。站桩档仍给 0。
- 证据：`NpcIdleWanderTests` 起一个真会话、跑 300 拍，从**快照坐标**断言
  "会走的那只离出生点 >0.3m、站桩那只 <0.05m"（首版只跑 120 拍/2 秒，短于 `IdleToPatrolSeconds=3s`，
  测成"从没起身"——已改为 5 秒并在注释里写明原因）。
- **路线已接（同轮）**：生成时把刷点 `RouteId`（且线路 ≥2 点）记进 `_scriptPointRouteId`，
  `ResolveRouteId` 先查它；取线收敛到 `ResolveRouteObject`（怪物内容 → NPC 内容，结构逐字段同款）。
  `NpcRouteTests`：NPC 在 30 米线路上 25 秒内走到 >10 米（踱步半径只有 6 米，越界只能来自读线）。
- **作息已接（同轮）**：`NpcScheduleCore.RouteAt(slots, worldSeconds, cycle, fallback)` 是唯一的"当前该走哪条线"判据
  （命中且档里给了路线 → 用它；没命中/未配/route≤0 → 兜底）；宿主每 tick 用 `_tick / TicksPerSecond` 换算世界秒后问它。
  非 NPC 走 `TryGetConfig` 失败分支，行为逐字不变。单测覆盖命中/档内无路线/空档/未配/跨零点五种出口。
- 仍未接：**姿态（坐/工作/持械）不随作息切换**——已核实 Identity 只在 `Spawn` 下发（增量帧不带 Identity），
  这条要么加"身份增量"消息、要么由客户端按导出的作息表自行推算**服务端世界时间**；两条都需要先定口径，属架构决策。
  路线段仍按直线连（真导航图是 fixture）。

### 11.13 NPC 掉落（D4：扩 owner，不另立掉落表）

| 层 | 改动 |
|---|---|
| 内容 | `NpcContentSnapshot.LootBindings`（**复用** `MonsterLootBindingSnapshotDto`）→ `NpcContentBundle.LootBindings`（按 npc_id 索引）→ `NpcContent.LootBindings(npcId)` 访问器；回执多一项 `loot_bindings=N` |
| 装配校验 | 绑定挂在未登记的 NPC 上 → `loot_binding_unknown_npc:<id>`（"配了掉落但没人会掉"必须显形） |
| 运行期 | `DsWorldSession` 掷落点：怪物内容查不到时退到 `NpcContent.LootBindings(templateId)` |
| 导出 | `Horizon.NpcCli export-content` 从 `MonsterLootBinding/MonsterDropEntry` 读 owner=npc_id 的行（同表、不同 owner），条目按 `seq` 升序 |
| 种子 | 022 追加 500001 的样板掉落（750003 60%×1–2、91001 15%×1、5–25 铜）+ 一条自检 |
| 证据 | 实跑回执 `… schedule_rows=1 loot_bindings=1 problems=0`；`NpcLootContentTests` 2 绿；全量 **3599 通过 0 失败** |

**编辑闭环（同轮补）**：`NpcDraft.LootBindings`（复用怪物 DTO）→ `ToSnapshot` 把 owner 归一成草稿主体 NPC →
`NpcStore.Upsert` 整包替换（先按 owner 删条目再删绑定，避免孤儿条目撞主键）→ 插件新增**「掉落」面板**（一行绑定的掉率/金币区间 + 4 件逐件条目）。
跨脚本域键名用**插件形状的草稿 JSON** 真跑过 CLI：`validate -` → `loot_bindings=1 problems=0`；插件 `_verify.sh` 两模块 0 错。
另加 `preview-drop -`（读 stdin 草稿，用内核解析口径给**逐件精确命中率**与期望件数；实跑 6000/1500 → `1.05 件/杀`），
以及契约测试 `PluginDraftModel_UsesSameLootKeysAsCli`：插件草稿模型与 CLI DTO 的掉落字段名逐字一致 + 必须 camelCase。

**仍未做**：① "打死敌对 NPC → 掉落地板"的端到端（需要客户端/战斗，且 021/022 未执行）；② 条目级条件/任务掉落列未建（当前固定"继承绑定"）。

### 11.12 头顶任务符号 `!` / `?`（客户端合成，无协议改动）

| 环节 | 实现 |
|---|---|
| 缺口补齐 | 交互任务发布者 NPC 时按**真实 giverEntityId** 调 `QuestClientService.RequestList`——此前客户端从不带这个参数，服务端算好 offer 也没人问 |
| 合成 | `Source/Game/Selection/NpcQuestMark.cs`：把**两份已裁决事实**（该 NPC 的 `QuestOfferInfo[]` + 身上的 `QuestSummary[]`）合成三态，优先级与 `NpcQuestIndicatorCore` 一致（可交付 > 可接 > 进行中），并给数字前缀 |
| 判交付 | **看身上任务的逐条目标是否全部完成**，不看 `AlreadyFinished`（那是"做过、不可再接"）；没有逐条目标的旧数据按"未完成"处理，宁可灰 `?` 不谎报可交付 |
| 渲染 | `NameplateWidget` 新增 `_questMark` 标签（名牌右上、板子 `ClipChildren=false`），金=可接/可交付、灰=进行中；非 NPC 或清单不属于该 NPC 时不显示 |
| 服务位 | `NpcStanceCatalog.IsQuestGiver`（位 1<<1），与服务端同源 |

**门禁**：客户脚本 **484 源 0 错**。
**已知边界（不是缺陷）**：符号只出现在**刚交互过的那一个 NPC** 上——客户端没有"每个 NPC 一份摘要"的下发面；
要让"远远看见一排 `!`"成立，需要 Identity 追加服务摘要（AOI 级下发），属下一步。
**未目视**：符号的位置/颜色/数字前缀没有截图验证。

---

### 11.15 数据入库 + 身份增量（2026-10-27）

**021/022 真执行了，并修掉两个"只有真跑才会露头"的缺陷**：

| 缺陷 | 症状 | 修法 |
|---|---|---|
| `npc_id` 被 EF 约定建成 IDENTITY | 种子插入报 544；更糟的是 `NpcStore` 插入时显式 `Id` 会被静默丢弃、改成库自增值——"给 NPC 编号"这个前提直接失效 | 实体加 `DatabaseGenerated(None)`；迁移/Designer/Snapshot/021 同步去掉 IDENTITY；空表重建（已在盘上核对 `is_identity=0`） |
| `USING (VALUES …) AS s (...)` 别名清单缺基类列，INSERT 却引用 `s.IsValid/s.GameId/...` | Msg 207"列名无效"报在**别人的行号**上，看起来像表结构不对 | 六张子表 INSERT 的基类列改字面量；子表主键仍是 IDENTITY，按工程既有惯例用 `SET IDENTITY_INSERT` 包住 |

**种子内容（全部通过脚本内 7 组自检）**：模板 5（1/2/3 逐字对齐 `NpcTemplateCatalog`，+400001 商人样板、500001 敌对样板）、货架 5、教学 3、配方 1、传授 1、对话 7、作息 5、刷点 5、路线 1（4 节点）。自检 #4/#5 一度真报红：`7500xx` 是不存在的"特殊物品"号段（016 未执行），`810101` 不是技能号——改成库里实有的 700006/700011/700002 与 1001/1002 后才全绿。**这类红必须当成数据缺陷改掉，不能把自检调松**。

**身份增量（姿态 + 服务位）**：`MessageUnion` 的 tag 0–255 已用尽、132 是永久墓碑（复用会让旧客户端按旧 schema 误解析），所以**不新开消息**，改走已有的每快照 `EntityStateAuthComponent`：

| 环节 | 实现 |
|---|---|
| 契约 | 追加 `NpcStandState` / `NpcServiceFlags`（`-1` = 未下发；`0` = **确知**站立/无服务） |
| 服务端 | `DsWorldSession.ResolveNpcIdentityDelta`：姿态走 `NpcScheduleCore.SlotAt`（时段未命中 = 站立）、服务位取内容模板；非内容 NPC 一律 `(-1,-1)` |
| 客户端 | `SelectableEntityTag` 加 `NpcServiceFlags`；同步系统按变化写回（不变则不取脚本组件）；目标框把姿态显示成 `NPC · 做工/坐/持械` |
| 交互判定 | `NpcStanceCatalog.HasServiceOrFallback`：**服务端位图优先，未下发才退回导出目录**——运营新加的服务立刻生效，而不是等下一次导表 |
| 全局 `!`/`?` | `QuestListResponseMessage` 追加 `GiverEntityId`（union tag 不变）；客户端按发布者缓存清单；任务 NPC 出现时各拉一次；接/交后 `RefreshAllGivers` 刷新。头顶符号从此按**这只 NPC 自己的**清单算，不再依赖"刚交互过" |
| 门禁 | `NpcIdentityDeltaTests`：t=10s 姿态=做工(2)、服务位=121，t=700s 姿态=站立(0)，山贼为 `(0, 0)`（0 与 −1 分得开）；NPC 子集 110 绿；客户脚本离线 csc **0 错** |

**已知边界**：① 姿态目前只到"目标框一行字"，没有坐/打铁动画集（内核口径本来就是"没有动作集按站立画"）；② 全局符号受 `QuestInteractionAuthority` 的 800 单位（≈8m）采信半径限制——远处 NPC 拿不到清单就不画符号，与 WoW"走近才看见"一致但半径偏小。
**未目视**：以上三条都还没有截图证据。

---

### 11.16 默认装载 + D13 阵营矩阵（2026-10-27）

**默认装载（让"配好了却看不见"不再可能）**：`--npc-content` / `NPC_CONTENT_PATH` 都没配时，DsHost 与 Gateway 会向上探查 `Data/Npcs/db_content.json`（`NpcContentLoader.ResolveDefaultPath()`，两处共用一份实现）。找不到才走原来的"没有 NPC 内容"分支，且照常念回执。门禁 `NpcContentDefaultPathTests` 两条：临时目录只认存在的文件；**生产探查必须真的找到仓库里那份导出**（否则导到别处就会静默退化成"一个 NPC 也没有"）。

**D13 阵营（复用 `CharacterEntity.faction`）**：

| 环节 | 实现 |
|---|---|
| 内核 | `Sim/Factions/FactionCore`：**只有一条规则**——正派 ↔ 邪派 互敌；同阵营／任一方未入阵营（0）／越界脏数据 一律不敌对。回执带 `FactionReject` 四档理由（0 不当成一个可参战阵营） |
| 权威链 | `CharacterSyncProfile.Faction` → `HandshakePacket.Faction`（网关在 `StampAuthoritativeProfileAsync` 用 Grain 档案覆盖）→ `DsPlayer.Faction`；客户端自报值一律被冲掉 |
| 战斗入口 | `DsWorldSession.SubmitCombatAction`：目标是玩家时**只**问矩阵。敌对阵营回 `CombatOutcome.PvpUnsupported`（新档 9，与"打不了"分开），同/未入阵营回 `Invalid` |
| 击杀经验 | `NpcKillCore.SourceOfPvp`：敌对阵营 → `PvpPlayer` 来源（走同一条让分曲线）；否则 `Unknown` = 0 经验并带矩阵理由 |
| 门禁 | `FactionCoreTests` 9 组矩阵 + 理由可分辨 + `SourceOfPvp`；`DsPvpFactionGateTests` 2 条在**真实战斗入口**上断言（只测矩阵会让它变成没人调用的纯函数）。合计 13 绿 |

**仍未做**：① PvP 的伤害／死亡／荣誉结算（所以敌对阵营现在只到"认得出，但声称未实现"）；② 建角 UI 没有选阵营入口——`faction` 今天由库里那一列给（默认 0），要等角色创建流程接上。

---

### 11.17 训练师窗口（2026-10-27）

**问题**：学技能的通路本来就通（扣费/门槛都在服务端），但玩家看不到**这位前辈教什么、多少钱**——
点开训练师只是"打开一张全门派技能树"，选错式只会在服务端被拒，且拒绝里没有"是谁不教"。

| 环节 | 实现 |
|---|---|
| 数据 | `export-visuals` 给每个 NPC 增 `trainerRows`（skillId/kind/gold/itemId/itemCount/seq），键并入 `ClientKeys`（契约测试逐字钉住） |
| 客户端 | `NpcStanceCatalog` 解析 `trainerRows` → `GetTrainerRows(npcId)` / `TryGetTrainerRow(npcId, skillId, out row)`；未导表时内置铁匠三条（1001/1002/900001，与 022 同值） |
| 语境 | `TrainerClientService` 加 `NpcId`，交互时 `Bind(entityId, templateId)` |
| 显示 | 技能页在进入训练师语境时念一次"师承：裂金斩（200金）、…"；点「习得」前先查这位教不教，不教当场说明，教则先报学费再发请求 |
| 权威 | 只是**显示**：能不能学、收多少钱仍由 `SkillHandler` 按内容表判——导出表可能与库里最新一版不同，两处判断各有其位 |

**门禁**：`NpcEditorContractTests` 12 绿（含提交进版本库的 5 条导出表、服务位 121/7/9/9/0、敌对 500001 立场 2、铁匠三条教学行价格）；客户脚本离线 csc 0 错。
**未目视**：这条只有编译与契约证据。

---

### 11.18 交付 NPC 校验（D11，2026-10-27）

**问题**：`QuestTemplate.turn_in_kind / turn_in_template_id` 两列从建列起就没有读者——玩家可以在任意地点把任务交掉，
奖励照发、进度照清，没有任何异常；这两列成了"写了没人读"的死列。

| 环节 | 实现 |
|---|---|
| 内核 | `Sim/Quests/QuestTurnInCore`：`ResolveRequired`（0=沿用发布者）+ `Evaluate` 五档（不限制/缺实体/认不出/类别不符/不是指定对象） |
| 目录 | `QuestCatalogEntry` 追加 `TurnInKind/TurnInTemplateId`（Id 14/15），`QuestManagerGrain` 从模板填充 |
| 协议 | `CompleteQuestRequest` 追加 `EntityId`（union tag 不变）；客户端提交时带**当前选中的 NPC** 实体 id |
| 校验 | `QuestHandler.ValidateTurnInAsync`：服务端用 `QuestInteractionAuthority`（注册表+分片+800 单位）解析实体，再与目录里的交付者比对；不通过回**各自不同的话**（缺实体/认不出/类别不对/不是这一位） |
| 失败姿态 | 取目录异常**按拒绝处理**——放行会让"该到指定 NPC 交"静默变成随处可交 |

**门禁**：`QuestTurnInCoreTests` 6 绿（4 组回退表 + 五档拒绝 + **接线检查**：请求体真有 `EntityId`、处理器真的调内核与"按拒绝处理"、客户端真的填 `EntityId = entityId`）。
**已知边界**：客户端只在"当前选中是 NPC"时带对象；裸交互（未选中）会被判成"需要到指定交付者处交付"——这是有意的严格侧。

---

### 11.19 任务链图（A6 的最后一环，2026-10-27）

| 环节 | 实现 |
|---|---|
| 内核 | `Sim/Quests/QuestChainCore`：前置边 + Kahn 拓扑（同层按 questId 升序，**两次构建逐条相同**）；未知前置与成环**只报不吞**（`unknown_prerequisite:` / `cycle:`），成环节点仍按 id 排进末尾 |
| 读库 | `NpcStore.LoadQuestChain`：链接表优先；为空时**退到模板反查**（`giver_kind=1 && giver_template_id=npc` 或 `turn_in_template_id=npc`）——链接表由发布流程生成，存量任务没人回填过，只信它会让老任务链整片消失。并沿 `prerequisite_quests` 把**传递前置**一起收进来（否则链头消失） |
| CLI | `quest-chain <npcId>` → `{source, nodes[拓扑序], edges, roots, topologicalOrder, problems}`；连库失败如实回错 |
| 编辑器 | 第 8 页签「任务链」：刷新后逐行显示 `#id 名称 发布 NPC#x 交付 NPC#y 前置[…]`，问题逐条列出 |
| 真跑 | 库里 NPC 2 → `chain_nodes=6 chain_edges=5 chain_roots=5 problems=0`：90001–90005 五条入门 → 90006「五行异闻」 |
| 门禁 | `QuestChainCoreTests` 7 绿（线性/分支/未知前置/成环/重复 id/空输入 + **接线检查**：CLI 动词与编辑器页签都在）；插件离线 csc 两模块 0 错 |

**边界**：交付者显示为"沿用发布者"时不画具体 NPC 号（模板里就是 0，客户端口径同 §11.18）；链图不带任务文本/目标，只是拓扑。

---

### 11.20 PvP 结算（D13 的下半场，2026-10-27）

**问题**：§11.16 只做到"认得出敌对阵营，但声称未实现"——玩家互殴仍然打不动，
于是"死亡贡献经验（PVP 时有效）"这条原话只在敌对 NPC 上成立。

| 环节 | 实现 |
|---|---|
| 合法性 | `DsWorldSession.ResolvePlayerVsPlayer`：**只**问 `FactionCore.Evaluate`；非敌对回 `Invalid`（与"打空气"同表现，玩家这边本来就该一样） |
| 结算 | 走与打怪**同一个** `CombatCore.Resolve`（同一套闪避/暴击/扇区/射程）；另写一套会让"砍怪"和"砍人"出现两条公式 |
| 表现 | `DamagePacket` 发**双方**（打怪那条只发受击者，因为打怪者已有回执；PvP 两边都是真人）；死亡 `DeathPacket` 同 |
| 死亡 | 复用玩家复活排定（`RespawnAtTick`）+ `PlayerDeath` 业务事件；经验发 `KillXp`（`PvpPlayer` 来源，同一条让分曲线，所以"大号杀小号不给"与"打灰名怪"同判据） |
| 计数 | `PvpRejectedFactionCount` / `PvpDamageDealtCount` / `PvpKillXpGranted`——三件事分开能看到 |
| 清理 | 删掉本轮临时引入的 `CombatOutcome.PvpUnsupported`（从未发布，不留死枚举值） |
| 门禁 | `DsPvpFactionGateTests` 3 条：敌对打到**真掉血、真死亡事件、荣誉经验 >0**；同/未入阵营 `Invalid` 且被拒计数 +1；正例里被拒计数必须为 0（证明计数器不是恒真） |

**踩坑（写进测试注释）**：战斗掷骰是 `(攻击者, 目标, tick, salt)` 的纯函数。第一版测试在同一 tick 连打 400 次，
第一下撞上 5% 闪避后 400 次全部闪避——**"没伤害"不是代码错，是夹具没推进 tick**。

---

### 11.21 NPC 身份的其余四格（头衔/外观键/体型/档位，2026-10-27）

**问题**：`BuildMonsterIdentity` 只认**怪物**内容包。内容 NPC 走的是 `else if (template.IsWorldBoss)` 那条兜底，
于是编辑器里配的头衔、`model_key`、体型缩放、精英档**全部静默失效**——而名字却是对的：只错一半，最难看出来。

| 环节 | 实现 |
|---|---|
| 服务端 | `BuildMonsterIdentity` 增加 `NpcContent.TryGetConfig` 分支：`ModelKey / ModelScale / Rank / Title` 全部取自 NPC 目录；怪物内容优先（现有行为不变） |
| 客户端 | 无需改动：`FlaxActorSyncSystem` 已把 `identity.Title` 搬进 `CharacterAttributesComponent.Title`，名牌第二行因此显示"铁匠/城门/行商" |
| 门禁 | `SpawnIdentity_CarriesNpcTitleModelScaleAndRank`：装 400001（头衔「货郎」/`npc_peddler`/1.15/精英档），从**快照 Spawn 身份包**逐个字段断言 |

**仍未做**：`model_key → prefab` 分流（缺 NPC 美术与目录；现在仍用占位人形）。

---

### 11.22 NPC 对话菜单（修掉"多服务抢窗口"，2026-10-27）

**问题**：交互层按服务位**各自**开窗。一个既有货架、又教艺、又有炉砧的 NPC（比如 022 种子里的铁匠 121），
会连发三次导航，**最后打开的那个赢**——玩家看到的是"铁匠只能买东西"，另外两个入口没有任何提示，
而且没有任何报错能指向这件事。

| 环节 | 实现 |
|---|---|
| 数据 | `export-visuals` 每只 NPC 增 `gossipOptions`（kind/arg/seq/min/maxLevel，按 group/seq 排好），键并入 `ClientKeys` |
| 客户端目录 | `NpcStanceCatalog.GetGossipOptions(npcId)`；未导表时内置铁匠 0/1/3、行商 0/1（与 022 同值） |
| 请求 | `NpcGossipService.Begin(...)` 一次性请求（点的是谁、能做什么、玩家等级），页面构造时 `Take()` 读走 |
| 页面 | 新 Ink 页 `NpcGossipPage`（`nav-npc-gossip`，Modal）：按种类合成文字（看看货架/请教武艺/借用炉砧/复命交差/闲谈），等级门槛只做灰显；点哪项就发哪项的请求并导航到原页面 |
| 交互 | `TryInteractSelected`：有菜单的 NPC 先开菜单；无菜单的（任务 NPC 2、敌对 500001）维持"直接打开唯一相关页"的旧语义 |
| 权威 | 菜单只决定**入口**；价钱/门槛/库存/配方仍由各自服务端通路裁决（页面底部一行字如实写明） |

**门禁**：契约测试 12 绿（含提交进版本库那份表的 `gossipOptions`：铁匠 0/1/3、行商 0/1、任务与敌对 NPC 为空）；客户脚本离线 csc **0 错**（新增文件不在旧 response 里，需显式追加——这正是"陈旧引用清单会把报错算到你头上"的现场）。
**未目视**：菜单的排版/点击尚未截图验证。

---

### 11.23 NPC 刷新时间接了（2026-10-27）

**问题**：`NpcSpawnPoint.respawn_seconds` 在编辑器里有输入框、种子里有值（60/30），但 `ApplyNpcContentSpawns`
调 `CreateScriptedPoint` 时**没把这一列传进去**——脚本生成点一律用默认 15 秒。运营把 30 秒改成 5 分钟，
游戏里纹丝不动，两边都不报错。

| 环节 | 实现 |
|---|---|
| 服务端 | `CreateScriptedPoint` 增 `respawnSecondsOverride`（默认 −1 = 未配）；`>0` 用它，`==0` 回落到 `SpawnPointConfig.Default`（与表里"0 = 用全局默认曲线"同义），TTL 生成物仍然不允许复活 |
| 传参 | `ApplyNpcContentSpawns` 的友好型与敌对型两处都传 `point.RespawnSeconds` |
| 门禁 | `NpcRespawnTests` 2 条：配 2 秒 → 实测 **19 拍**（10 TPS ≈ 2 秒）；配 0（默认曲线）→ **149 拍**（≈15 秒）。两条一起才证明这一列真的被读——只测 2 秒的话，碰巧默认值接近就白测 |

---

### 11.24 敌对 NPC 的 AI 档位真的影响战斗（2026-10-27）

**问题**：9 个 AI 能力位里只有 `Wanders` 有运行期读者（`CallsReinforcement` 只在装配时校验）。敌对 NPC
走怪物管线时用的全是怪物点位的默认值（仇恨 10 米、脱战 30 米、游荡 6 米），所以**站桩山贼与精英山贼行为一模一样**——
编辑器里选哪一档只改了一个没人读的数字。

| 环节 | 实现 |
|---|---|
| 索敌 | `DefendsSelf`/`Pursues` 都关 → `AggroRadius=0`（被打才反应，不主动开战）；任一开 → 仇恨半径取 `alert_radius_meters`（0 则 10） |
| 脱战 | `Pursues` 开 → 脱战半径 = 仇恨 × 2.5，下限 30 米 |
| 游荡 | 敌对型也吃 `Wanders`：开 → 半径 6 米 + `speed_walk`；关 → 0（不再按怪物默认 6 米乱走） |
| 传参 | `CreateScriptedPoint` 增 `aggroRadiusOverride/leashRadiusOverride`（−1 = 不覆盖），移动覆盖从"Npc 专属分支"提到"两种 kind 都生效" |
| 门禁 | `NpcAiTierRuntimeTests`：同场两只敌对 NPC（Static / Elite），5 秒后 **Elite 进入 Attack 且目标=玩家**，**Static 无目标且位移 0m**；与 `NpcIdleWanderTests`（友好型踱步 3.15m / 站桩 0m）、`NpcRouteTests`、`NpcRespawnTests` 同轮 5 条全绿 |

**仍未接的能力位**：`FacePlayer`（转身面向玩家）、`Casts`（NPC 内容没有技能表，敌对型靠怪物内容）、
`FleesAtLowHealth`（怪物 AI 没有逃跑态）、`SpeaksGossip`（台词文本表未建）。这些位目前在编辑器里可选但运行期无表现，属已知缺口。

---

### 11.25 NPC 转身面向玩家（FacePlayer，2026-10-27）

| 环节 | 实现 |
|---|---|
| 服务端 | `TickMonsters` 尾部对 `ScriptedKind.Npc` 且带 `FacePlayer` 位的实体调 `ApplyNpcFacing`：在 `alert_radius_meters`（上限 20 米，未配则 8 米）内取**最近存活玩家**，写 `Yaw = atan2(dy,dx)`；同距离按玩家实体 id 升序（可重放） |
| 边界 | **只写朝向**：位置/状态/威胁一律不碰——"转身看着你"与"进入战斗"是两件事，合起来会让友善 NPC 一靠近就把玩家当敌人；敌对型不在此列（朝向由追击决策给，覆盖会与 AI 打架） |
| 门禁 | `FacePlayerTier_TurnsTowardPlayer_StaticDoesNot`：Aware 档 NPC 面向北侧玩家 → `yaw=1.571`（π/2）；Static 档保持 `0`；两者位移都 <0.05m（转身不等于挪窝） |

**剩余无运行期表现的能力位**：`Casts`（NPC 内容没有技能表）、`FleesAtLowHealth`（怪物 AI 无逃跑态）、`SpeaksGossip`（台词文本表未建）。

---

### 11.26 NPC 击杀→掉落端到端（DS 侧，2026-10-27）

怪物 019 已对 `Game` 库执行（并行会话），022 的掉落段终于不再被守卫跳过；重跑后 **绑定 1 行 + 条目 2 行**落库，
`export-content` 回执从 `loot_bindings=0` 变为 **`loot_bindings=1`**（库 → 快照这条路径自带证据）。

| 环节 | 实现/证据 |
|---|---|
| 修掉的种子缺陷 | 掉落两条 INSERT 各有**一个多余的字面量**（这两张表没有 `enabled` 列，第一版按 NPC 子表的"enabled+IsValid"双 1 写）；守卫期间整段被跳过所以一直没露头，表一建就报 Msg 110 |
| DS 断言 | `NpcKillLootTests`：装 500001 敌对 + 必触绑定（青竹 100% / 5~25 铜），玩家打死 → `kill=1 loot=1 money=1 items=[700002]`，金币落在 [5,25] |

**仍未做（客户端侧）**：尸体上的地板物品与拾取窗口（客户端只收到 `Loot`/`KillMoney` 跨层事件，入包由业务层负责；地表现与拾取 UI 未接）。

---

### 11.27 号段闸门（A1 的最后一个缺口，2026-10-27）

**问题**：§12 A1 一直挂着"把 150000 登记成 NPC 没有拒绝码"。放行的话它会一路正常跑起来，
直到有人按号段做统计/分组才发现它落在无人认领的洞里。

| 环节 | 实现 |
|---|---|
| 内核 | `Sim/Npcs/NpcIdSegmentCore`：`Legacy 1–3` / `Service 400000–499999` / `Hostile 500000–599999`，其余 `OutOfRange`；边界常量与 `NameOf` 都只在这里 |
| 装载 | `NpcContentLoader.Build`：号段外的模板回 `npc_id_out_of_segment:<id>` **且不进内容包**（否则刷点仍可引用一个被拒的模板）；另加 `npc_segment_stance_mismatch:<id>:<reaction>`——敌对号段配成友好会"永远打不到"，服务号段配成敌对会"能砍掉服务者" |
| 门禁 | `NpcIdSegmentTests`：11 个边界值 + 号段外模板被拒且内容包为空 + 两类立场矛盾；NPC 子集 89 绿；**真库导出仍 `problems=0`**（1/2/3 legacy、400001 服务、500001 敌对） |

---

### 11.28 保存链路：路线持久化 + 两个"永远保存不了/发布了却不在"的缺陷（2026-10-27）

| 问题 | 症状 | 修法 |
|---|---|---|
| 草稿**从不携带版本号** | `LoadDraft` 没填 `ContentVersion`，所有草稿都是 0、库里是 1 → 编辑器"加载→保存"**永远**报"内容版本冲突"，回执看起来像别人先改了 | `LoadDraft` 写 `draft.ContentVersion = GetContentVersion(npcId)` |
| **路线根本不落库** | `Upsert` 只写刷点/货架/教学/对话/作息/掉落，`draft.Routes` 被静默忽略——编辑器里画的巡街线保存后消失 | 按草稿里的 `PathId` 覆盖：存在就改、不存在用 `SET IDENTITY_INSERT` 分配下一个号再插（与 022 种子同法）；节点按路径整包替换；`WrittenRows` 逐表计数 |
| **发布不蔓延到路线** | 刷点/作息引用的路线仍是 `publish_state=0`，导出时整条路线缺失 → 装配器 `spawn_unknown_route` → "发布了 NPC，世界里却没有他"，而每张表看起来都对 | `SetPublishState` 收集本 NPC 刷点 ∪ 作息引用的路线一起改发布态，回执里报 `NpcWaypointPath` 行数 |
| 草稿只收刷点引用的路线 | 只在某时段走的路线在编辑器里显示为空，保存一次就把作息切路线的数据补丢 | `FromSnapshotForNpc` 同时收 `ScheduleRows.RouteId` |

**真跑往返（NPC 3）**：`get 3`（version=1，含 1 刷点 / 1 路线 4 节点 / 1 作息）→ 原样 `save --expect-version 1` →
`writtenRows` 含 `NpcWaypointPath=1, NpcWaypointNode=4`；库里 vendor=2 / gossip=2 / schedule=1 / spawn=1 / nodes=4 全在；
保存即下线（既定语义）后 `publish 3` → 回执 `NpcWaypointPath=1`；导出恢复 **templates=5 spawns=5 routes=1 problems=0**。
**门禁**：契约测试 15 绿（含"作息引用路线要进草稿""草稿带版本号""发布蔓延到路线"三条钉子）。

**仍未做**：编辑器插件的草稿模型还没有 `Routes` 字段与「刷点·路线」面板——CLI/库这条链已通，缺的是插件 UI；在此之前从编辑器保存不会破坏已有路线（只增改不清扫）。

---

### 11.29 插件「刷点·路线」面板（2026-10-27）

CLI/库这条链在 §11.28 已通，缺的只剩插件 UI。本轮补上：

| 改动 | 说明 |
|---|---|
| 草稿模型 | `NpcDraftModel.Routes` + `NpcRouteModel`/`NpcRoutePointModel`（字段名与 CLI 的 `NpcRouteSnapshot` 逐字一致，camelCase 序列化后直接被 `NpcDraft.FromJson` 认出来） |
| 第 9 页签 | 刷点（X/Y/Z/朝向/路线/同时存活/刷新秒）+ 路线（编号 0=新建、名称、走法、是否走图、点停留）+ 6 行路线点（**序号留空 = 该行不存在**，0 是合法序号） |
| 写回规则 | 刷点与路线都**只在真的填过时才覆盖**：打开一个没有刷点的 NPC 看一眼就保存，不该凭空在原点长出一个刷点 |
| 门禁 | 契约测试 16 绿：新增一条把插件的 camelCase 路线 JSON **喂给 CLI 草稿**，再走 `Compile` 断言不再报 `spawn_unknown_route`；插件离线 csc 两模块 0 错 |

**边界**：一行只编辑"第一组"刷点/路线（NPC 多名刷点/多路线仍走 CLI/导入）；面板不画预览，坐标是数字输入。

---

### 11.30 刷点的等级覆盖与朝向档（2026-10-27）

**问题**：`NpcSpawnPoint.level_override` 与 `facing_mode` 两列在库里、编辑器里都有，运行期**没有读者**——
配"这一点固定 42 级"或"面向玩家出生"完全没有效果。

| 列 | 实现 |
|---|---|
| `level_override` | `CreateScriptedPoint` 记 `_scriptPointLevelOverride[spawnPointId]`，`ResolveEffectiveLevel` 在模板/区间之前先查它（**一点定终身，不掷区间**，与怪物点位的口径一致）；只在 `> 0` 时才下发，否则保留等级区间掷骰 |
| `facing_mode` | `ResolveNpcSpawnYaw`：0 = 用配置 `yaw`；1 = **确定性哈希**角度（同刷点重启后同朝向，截图/重放才有基线）；2 = 面向最近存活玩家（同距离按实体 id 升序），没有玩家时退回配置 `yaw` |
| 门禁 | `NpcSpawnExtrasTests` 4 条：`level=42`、固定朝向 `yaw=1`、面向玩家 `yaw=1.571`、随机档两次会话都 `1.677`；真库导出仍 `problems=0` |

---

### 11.31 随包产物装进世界（服务器侧端到端，2026-10-27）

前面所有用例都用**内存快照**搭场景，证明不了"导出的那份文件本身能用"。本轮直接拿
`HundunWorld/Data/Npcs/db_content.json`（DsHost/Gateway 默认装载的同一份）跑一遍：

| 观测 | 实测 |
|---|---|
| 装载回执 | `rev=3 templates=5 spawns=5 routes=1 vendor_rows=5 trainer_rows=3 recipes=1 gossip_rows=7 schedule_rows=5 loot_bindings=1 problems=0` |
| 生成 | `spawned=5 hostile=1 shard=0 unknown=0`（敌对山贼走怪物管线，计"总生成"与"其中敌对型"） |
| 身份 | 铁匠 `svc=121 posture=2`（白天打铁）/ 守门人 `svc=7 posture=3`（持械）/ 行商 `svc=9 posture=0` / 货郎 `svc=9 posture=0`；名字与头衔与种子逐字一致 |
| 敌对 | 500001 以 `EntityType=Monster` 出场，`Level=32`、名字「山贼头目」/头衔「山贼」 |

门禁 `NpcShippedContentTests` 1 条全绿。它同时钉住：**改库 → 重新导出 → 世界跟着变**这条运维链路，
以及"随包文件损坏/缺段"会在 CI 里立刻红，而不是等玩家进游戏发现少了人。

---

### 11.32 五只 NPC 的库往返逐格对账（2026-10-27）

**工具**：`Tools/npc-roundtrip.py` —— 对每个 NPC 跑 `get → save（原样） → publish → get`，逐格比较两份草稿
（忽略乐观锁 `contentVersion` 与发布态），差异直接打印表名 + 字段路径 + 前后值。

**它抓到的真缺陷**：每只 NPC 的 `spawnPoints[0].spawnPointId` **每次保存都换号**（1→7、2→8、3→9、4→10、5→11）。
根因是刷点"删光重插"，而 `spawn_point_id` 是 IDENTITY——号段/日志/对账表都按它指认"哪一点"，
编辑器里点开同一只 NPC 两次看到的编号还不一样。

**修法**：整包替换时**保住草稿带来的编号**（`SET IDENTITY_INSERT … ON` 显式写回；没给号的分配下一个）。

**结果**：`1 / 2 / 3 / 400001 / 500001` 五只全部 **逐格一致**，`writtenRows` 逐表可见
（如 3 号：Vendor 2 / Gossip 2 / Schedule 1 / Spawn 1 / Path 1 / Node 4；500001：LootBinding+Entry 3）。
之后重新导出 → `rev=7 templates=5 spawns=5 routes=1 loot_bindings=1 problems=0`，随包产物用例仍绿。

**门禁**：契约测试 18 绿（新增"刷点保存保号"的源码钉子）；脚本可随时复跑（需本地 `Game` 库）。

---

### 11.33 配方落库 + 插件配方编辑（2026-10-27）

**问题**：`NpcStore.Upsert` **从来没写过** `NpcCraftRecipe` / `NpcRecipeTaught`。编辑器里改的产物、材料、
成功率曲线保存后**全部消失**，而 `validate` 照绿（它只看草稿），运行期继续用库里的旧行。

| 环节 | 实现 |
|---|---|
| 落库 | 按 `recipe_id` 覆盖（与路线同纪律：配方是全局表，只增改不清扫）；新建用 `SET IDENTITY_INSERT` 分配号；`materials_json` 与两列曲线 JSON **按"由易到难"原序**写入；同时 upsert `NpcRecipeTaught`（学费取草稿里 `service_kind=2` 那一行，缺省用配方工费）；`WrittenRows` 回 `NpcCraftRecipe` / `NpcRecipeTaught` |
| 插件 | 草稿模型补 `Recipes`（`NpcRecipeModel`/`NpcMaterialModel`）；「教学·配方」页加第一条配方的编辑（产物/工费/设施/技能要求、3 行材料、4 档成功率与技能差上限） |
| 真跑 | NPC 1 往返：`WrittenRows` 含 `NpcCraftRecipe=1, NpcRecipeTaught=1`；库内 `900001` 的材料 `[{"item":700011,"count":2}]`、曲线 `[10000,6000,3000,1000]`、发布态 1、教学行学费 800 全部原样 |
| 门禁 | 契约测试 18 绿（新增"插件配方 camelCase → CLI 草稿"往返）；插件 csc 两模块 0 错 |

**边界**：插件一次编辑**第一条**配方；多配方仍走 CLI/导入。配方不参与"保存即下线"的发布开关（保存即发布）。

---

### 11.34 随包内容跑满一整天（作息→姿态→路线，2026-10-27）

用随包导出的 `db_content.json` 以 2 拍/秒跑满 1200 秒周期（2400 拍），把三张表拧在一起验：

| 观测 | 实测 |
|---|---|
| 铁匠（作息 0–600 做工 / 600–1200 站立） | 白天 `posture=2`、夜里 `posture=0` |
| 守门人（全天持械档） | 白天/夜里都 `posture=3` |
| 行商（刷点挂巡街路线、速度 1.6） | 一天内沿方框走动，离出生点最远 **5m** |

`NpcShippedScheduleTests` 1 条。它对应的玩家症状是"到点不换姿势 / 行商站着不动"——
任何一张表（作息/模板/路线）单独改坏时，单表测试仍是绿的，只有把三张拧起来跑一天才看得见。

---

### 11.35 四跳流水线（一条命令验收，2026-10-27）

`Tools/npc-pipeline.py`：**CLI 草稿 → 库 → 导出 → 世界**一条命令跑完，退出码即结论。

| 步 | 做什么 | 本次实测（§11.41 之后） |
|---|---|---|
| 1/3 往返 | 每只 NPC `get → save（原样） → publish → get`，逐格 diff（忽略版本/发布态） | 1/2/3/400001/500001 **全部逐格一致** |
| 2/3 导出 | `export-content` + `export-visuals` 两份随包产物一起写回 | `rev=11 templates=5 spawns=5 routes=1 vendor 5 trainer 3 recipes 1 gossip 7 schedule 5 loot 1 problems=0`；立场表 5 条 |
| 3/3 世界 | 跑 `NpcShippedContentTests` + `NpcShippedScheduleTests` + `NpcEditorContractTests`（**内置 `-p:BaseOutputPath=bin/VerifyPipeline/`**：宿主在跑时默认 Debug 输出会被锁） | **30 通过**：5 只在位、铁匠换姿态、行商走路线、产物与内容逐字节同源 |

失败即停：往返有差异就不导出（避免把坏数据推进随包文件），导出失败/世界用例红都回非零。
这条把之前散在 §11.28–§11.34 的手工验收收成可复跑的一步——换台机器、换个人接手都能照跑。

---

### 11.36 AI 覆盖的编辑器入口（2026-10-27）

**问题**：`ai_bits_force_on/off` 两列在内核里有完整语义（强制开/强制关、同位冲突要报），但编辑器只能选档位——
"这一只例外"表达不出来；而档位是预设按钮，一改档位就把整组位一起换掉。

| 环节 | 实现 |
|---|---|
| 插件 | 「基础·外观」页加 9 行"强制开 / 强制关"下拉（与 `NpcAiBits` 同位值）；`NpcAiBitsUi` 常量表与内核逐值对齐 |
| 往返 | 草稿模型本来就有 `AiBitsForceOn/Off`，此前只是没有 UI；现在读入/写回都走这两列 |
| 门禁 | 契约测试 19 绿：① Static 档 + 强制开 `FollowsPath` → 展开结果确实带上该位；② 同位既开又关 → 报 `ai_force_conflict:400001`；③ 插件常量表与内核枚举逐值相等 |

**边界**：`Casts`/`FleesAtLowHealth` 两格在运行期仍无行为消费者（§11.25 末段已记），勾上只影响一致性校验与回执，不会让 NPC 真的施法/逃跑。

**输入侧互斥（2026-11-01）**：`ai_force_conflict` 只该出现在"库里已有 / 外部导入"的草稿上，不该由编辑器自己的两列下拉造出来。
某一边改成「是」时，同行的另一边立刻回「否」；改成「否」不动另一边。回填草稿时**不镜像**——已冲突的行要原样显示、由 `validate` 报出来，不在打开时静默改掉一半。
规则住在 `NpcDraftModel.cs` 的 `NpcAiOverrideEditing`（纯 C#，测试工程直接链接该文件），`NpcEditorContractTests` 断言 4 种输入 × 2 个改动侧都不可能产出冲突，并把结果喂回内核验证被勾的位仍生效。

---

### 11.37 全量门的失败归因（2026-10-27）

本轮全量跑出现两类红，逐一归因（都不是 NPC 侧改动）：

| 红 | 证据 | 结论 |
|---|---|---|
| `WuxingBossLootTests.Live_…` | 单跑 11/11 绿；同一次改动的后续全量跑也绿 | "Live/实时"用例顺序抖动，观察中 |
| `ScriptedSpawnConfigurationBindingTests` ×7（**并行会话新加**） | **单跑 7/7 绿**；`--filter "FullyQualifiedName!~ScriptedSpawnConfigurationBindingTests"` 全量 **3685 通过 / 0 失败 / 8 跳过** | 该类的用例会临时改**进程级环境变量** `ScriptedSpawn__*`，在并行集合下与同进程其它用例互踩；属对方在途工作，只报不改 |

**NPC 侧全量回执（排除该在途类）**：`3685 通过 / 0 失败 / 8 跳过`。

---

### 11.38 配方行显示真名（补掉 A2 的最后一格，2026-10-27）

**问题**：训练师窗口的配方行只能显示 `配方#900001`——客户端没有配方目录，编号对玩家没有意义。

| 环节 | 实现 |
|---|---|
| 导出 | `trainerRows` 增 `label`：`service_kind=2` 时按 `recipe_id` 从配方表取 `name`，技能行留空串 |
| 客户端 | `NpcStanceCatalog.TrainerRow.Label` 解析该键；技能页「师承」行与习得前提示按 `Label` 显示，空时才退回 `配方#id` |
| 真跑 | 重新 `export-visuals`：NPC 1 第三行 `label=锻造·玄铁重铠`（与 022 种子 `NpcCraftRecipe.name` 逐字一致） |
| 门禁 | 契约测试 20 绿：`ClientKeys` 含 `label`、读侧逐键取用、随包表第三行 label 等于种子里的配方名、技能行为空串；**另加合成快照直接跑 `Build`**（代码回归时旧表仍绿） |

**边界**：`label` 只是展示；学费、能否学、配方是否真存在仍由服务端裁决（与 `trainerRows` 同一纪律）。

---

### 11.39 「新建 NPC」：发号入口（补掉 A1 的最后一格，2026-10-27）

**问题**：号段闸门有了、编号列也有了，但**没有任何入口能发一个新号**——编辑器只能按编号加载已有 NPC，
新建只能手写 SQL 或手填一个号（手填撞号时保存链路只表现为覆盖：乐观锁管的是版本，不管"这个号本来就有人"）。

| 环节 | 实现 |
|---|---|
| 内核 | `NpcIdSegmentCore.NextFree(used, segment)`：号段**已用最大值 + 1**，**不复用空号**（编号会进任务/掉落/日志，回收后历史记录会指到另一只）；用尽回 0，越界号段回 0；`RangeOf` 是边界唯一出处 |
| CLI | `allocate-id --segment service|hostile`（只读库，**不写任何行**）；legacy 1–3 明确拒绝分配 |
| 插件 | 工具条第二行「新建服务」「新建敌对」：先要号，再摆空白草稿（服务=友好 + 档 2，敌对=红名 + 档 5，与号段一致）；**不自动落库**，保存仍走 `save`（未发布态） |
| 门禁 | 内核 7 条：空段起点、最大+1 不回填、跨号段只在本段计数、用尽回 0、legacy 不外溢、空列表、连续分配全落在段内；契约 2 条：CLI 动词 + 两个号段映射 + 插件两个按钮与同一动词；**空白草稿过服务端校验**（服务/敌对两种，走与 DsHost 同一个 `NpcContentLoader.Compile`） |
| 真跑 | 真库只读回执：`service → 400002`、`hostile → 500002`、`legacy → 拒绝`；两种空白草稿 `validate -` 都是 `problems=0` |

**边界**：一次只新建一条；名/模型键/服务位仍是人工填。号段用尽（各 10 万）会回执"已用尽"，不静默回 0 号。

---

### 11.40 NPC 外观分流（A4 的代码侧收口，2026-10-27）

**问题**：友好型 NPC 的客户端渲染**永远**走"远程玩家占位 prefab + 补挂人形身体"；
库里明明有 `prefab_guid` 列、编辑器也有那一格，但导出不带、客户端不读——
运营填了 GUID 世界里的铁匠还是同一个占位人形，而且逐只的 `scale` 对 NPC 也不生效（只有怪物吃）。

| 环节 | 实现 |
|---|---|
| 导出 | `export-visuals` 每条 NPC 增 `prefab`（模板 `prefab_guid` 原样；空串 = 没配） |
| 客户端目录 | `NpcStanceCatalog.TryGetPrefab`：**编号优先、模型键兜底**（同模型键的两只各配各的美术时，按键回退会把后加载的样子套到前一只） |
| 渲染 | `CreateRemoteEntityActor` 在 `kind==Npc` 且 GUID 可解析时用它加载 prefab，**跳过人形身体兜底**；**加载失败先回退占位人形再谈其他**（贴错 GUID 不该让 NPC 隐形）；NPC 也吃 `identity.ModelScale`；Actor 命名 `Npc_x` |
| 门禁 | 契约：`ClientKeys` 含 `prefab` + 读侧取用；合成快照导出 GUID 原样、未配为空串；随包五只 `prefab==""`；NPC 子集 **140 绿**；客户端离线 csc **0 错** |

**边界**：**当前没有任何 NPC 美术资产**，五只仍渲占位人形——这一格剩下的只是"谁来做模型"，
配好 GUID 后不需要再改代码。敌对 NPC 仍走怪物外观目录（它本来就是怪物管线）。

---

### 11.41 产物同源门（导出器改了不重导＝红灯，2026-10-27）

**问题**：`client_visuals.json` 是导出器的产物，而此前的断言只抽查"几条、字段对不对"。
导出器改了格式却没重导时，**旧表仍然逐项通过**——玩家读到的是旧格式（键还在，值已经过期），
这类漂移不会在任何地方显形，直到客户端某天按新键取值全空。

| 环节 | 实现 |
|---|---|
| 门禁 | `ClientVisuals_IsExactExportOfShippedContent`：反序列化随包 `db_content.json` → `NpcVisualsExport.Build` → 与随包 `client_visuals.json` **逐字节相等**（只归一换行）。内容事实与代码产物只能有一份 |
| 反证 | 把表里 `"services": 121` 改成 122 ⇒ 该用例红；`cp` 还原后复跑绿（一条自复原命令跑完） |
| 流水线 | `Tools/npc-pipeline.py` 第 2 步现在**两份产物一起导**（内容 + 立场表），第 3 步加跑契约类；本次 `PIPELINE OK`，26 通过 |

**边界**：这条只锁"产物 ↔ 随包内容"；库与随包内容的漂移由流水线第 1/2 步（往返 + 重新导出）负责。

---

### 11.42 残血逃跑落地（A5 的 `FleesAtLowHealth`，2026-10-27）

**问题**：这一位从接入起就只在编辑器里可选——运行期没有任何消费者。玩家看到的是"精英山贼和站桩山贼一样，
血到底照样对砍"，而档位表写着它会逃。

| 环节 | 实现 |
|---|---|
| 内核 | `NpcFleeCore`：阈值 **25%**（`HpPercentThreshold`）；活着、上限已知、`hp*100 ≤ maxHp*25`（用 long 乘，int 相乘会溢出成负数把满血判成残血）；能力位 + 残血两条同时成立才逃 |
| DS | `TickMonsters` 里对**立场为敌对**的 NPC：处于追击/攻击且残血 → 本 tick 改成**朝远离当前威胁的方向跑**（速度 取 `MoveSpeed` 或 `WalkSpeed`）、`WantsAttack=false`、朝向朝逃跑方向；目标不在场或速度为 0 时按原样打（没有方向可逃时"逃"只会站着挨打）；`NpcFleeStartedCount` 按**进入次数**记（不按 tick） |
| 边界 | 不按 `ScriptedKind` 筛（敌对 NPC 走怪物管线）；撞号风险由"立场必须敌对"挡掉；逃跑途中不做 FacePlayer 转身（否则看起来像倒着走） |
| 门禁 | `NpcFleeTests` 6 条：阈值边界（25% 逃 / 26% 不逃 / 小数字按比例）、尸体与未知上限、溢出、能力位、常量值；**同夹具正对照**——一只 `Defensive` 档的敌对 NPC 同血量同战斗，不跑且继续造成伤害；NPC 子集 **147 绿** |

**并行冲突**：首次接线（14:15）被并行会话整文件还原两次，按纪律撤回并删内核；14:33 之后该文件与
`MonsterAiCore/MonsterAiRuntime/MonsterAiGate` 均无写入，本轮重接后在 **14:57 落盘、观察 7 分钟无覆盖**。

**边界（剩余）**：`Casts`（NPC 内容没有技能表）与 `SpeaksGossip`（台词文本表未建）两格仍无运行期表现。
**审计更正（2026-10-27）**：`CallsReinforcement` 此前没被列进缺口，但它对 NPC 只有**装载期校验**；
当轮已补运行期读者（§11.54：被打的敌对 NPC 唤醒警戒半径内的同族），现归入"已消费"。

---

### 11.43 多行内容不再被整表替换（A14 的数据丢失修复，2026-10-27）

**问题**：面板只显示**第一条**刷点/路线/配方，保存回写却把整份列表换成"界面上那一条"。
一个 NPC 若有多个刷点（两个营地）、多条巡街线或多张配方，**保存一次就静默删掉其余几条**——
库里少行、没有任何报错，玩家看到的是"另一个营地的人没了"，而编辑器里从没见过那一条。

| 环节 | 实现 |
|---|---|
| 模型 | `NpcDraftModel.ReplaceSpawn(index, spawn)`：替换第 index 条（越界 = 追加），其余原样；`UpsertRoute(route)` 按 `PathId` 命中替换、0/未命中追加；`UpsertRecipe(recipe)` 按 `RecipeId` 同理；三者都自补 null 容器 |
| 面板 | `CommitSpawnAndRoute` / `CommitRecipeRow` 改走这三个入口；**老的 `_draft.X = new List<...> { 一条 }` 写法不许回来** |
| 门禁 | 模型是纯 C#，直接链进测试工程编译：5 条行为用例（替换保留其余、越界/0 追加、null 容器）；另 1 条源码守卫（三条老写法用正则断言"不存在"）+ 插件 `_verify.sh` 两模块 0 错；NPC 子集 **153 绿** |

**边界**：界面仍然只编辑第一条（多行的**切换/新增**还没做），但现在"看一眼就保存"不会再毁掉其余行。

---

### 11.44 经验衰减曲线的形状门（A12 原话"随玩家等级反比例衰减"，2026-10-27）

**问题**：既有用例只钉两端——"大号杀小号 = 灰名断供"与"低等级观察者有经验"。
**中间段没人管**：让分曲线的方向写反（经验随玩家等级上涨）或接错表时，两端照样绿。

| 环节 | 实现 |
|---|---|
| 门禁 | `NpcKillXpDecayTests` 2 条：①同一只 30 级敌对 NPC，玩家等级从 30 逐级升到灰名边界，**经验不回升**且首→末确实下降（≥3 个可比较点，防夹具退化）；到边界那一级归零且理由是 `GrayConcession`（不是"没配"）；②PvP 与敌对 NPC 在**同等级观察者**下同数（同一条数值链，只有"来源给不给"这一层不同），PvP 高等级观察者经验更低 |
| 期望值来源 | **不写死等级表**：灰名边界由 `MonsterDifficultyCore.GrayLevel` 现算——表换了这条仍按同一份数据源判对错 |
| 结果 | NPC 子集 **155 绿** |

---

### 11.45 逐件掉落条件三路存活（A11 ②，2026-10-27）

**问题**：`MonsterDropEntry.condition_kind/quest_id` 两列**已经存在**，写侧也照草稿落库；
但 NPC 侧**读库时写死 `-1/0`**、插件保存时也写死 `-1`。于是：
库里配的"任务持有者才掉 / 同组互斥"在编辑器里打开一次、保存一次就变"人人可掉 / 逐件独立"，
而草稿往返对账两边都读到默认值——**任何既有测试都看不出来**（怪物侧早有这条守卫，NPC 侧缺）。

| 环节 | 实现 |
|---|---|
| 读库 | `NpcStore` 逐件行改读 `e.ConditionKind / e.QuestId`（删掉"条目级条件暂未建表"的过期注释） |
| 插件 | `CommitLootRows` 以库里那一份为**底座**（人数区间按旧值带过；组号按物品号带回），只覆盖面板真露出的几格；**逐件与绑定级都给了"条件"下拉 + "任务 id"文本框**（`NpcLootConditionUi`：逐件索引 0=继承(-1)、绑定级没有继承档即索引=库值，值域与库列同号） |
| 门禁 | 契约 1 条：三条入口的源码守卫（读侧 `= e.ConditionKind`、写侧不被夹、插件 `keep?.`/`previous?.` 兜底 + 下拉换算走 `NpcLootConditionUi`）+ 禁止"未建表"注释回来；**模型映射 1 条**（两面互为逆、越界/未知回继承、默认必须是继承）；NPC 子集 **157 绿**，插件两模块 0 错 |
| 真跑（含回滚） | NPC 500001：`get → 改 entry0 conditionKind=1/questId=1001 → save → get` **原样读回** → 用原始草稿 `save` 回写 + `publish` → `get` 与原始草稿逐格一致（忽略版本/发布态）；随后跑四跳流水线：往返 5 只逐格一致、`rev=14`、27 条测试通过 |

**边界**：运行期是否按条件出账由 DS 既有通路负责；下拉只到"稀有度门槛"这一档（更细的参数仍走 CLI/SQL）。

---

### 11.46 多行内容的行切换（A14 的最后一格，2026-10-27）

**问题**：§11.43 让保存不再删其余行，但面板**只能看/改第一行**——多营地的刷点、第二条巡街线、
第二张配方在界面上永远够不着（只能回 CLI/JSON）。

| 环节 | 实现 |
|---|---|
| 行选择 | 刷点/路线/配方各加"行号 + ◀ ▶ 新增"：`NpcRowIndex.Clamp/Step`（**count 本身是合法值 = 新增档**；负数回 0，超界夹回 count）；行尾标签显示 `共 N 行（当前 i）` |
| 落盘 | 刷点 `ReplaceSpawn(hadSpawn ? spawnIndex : -1, …)`；路线/配方仍按 `PathId/RecipeId` 走 `UpsertRoute/UpsertRecipe`（Apply 已把该行编号填进编号框，命中即替换、0 即追加） |
| 门禁 | `NpcRowIndex` 4 条断言（count 可作索引、负数回 0、超界夹回、Step 两端停得住且能停在新增档）；契约守卫加 `ReplaceSpawn(hadSpawn ? spawnIndex : -1` 与两处 `NpcRowIndex.*`；插件两模块 0 错；NPC 子集 **158 绿** |

**边界**：一次仍只显示一行（没有列表控件）；"删除某行"还没有入口（把该行的物品/坐标清空不会删行——
没有删除语义就宁可留着，避免"看一眼就删"）。

---

### 11.47 任务链面板补上描述与目标（A6 的"链图不含任务文本/目标"，2026-10-27）

**问题**：链图只显示 `#questId 名字 发布/交付/前置`——运营看得见"链怎么连"，看不见"这一环让人干什么"
（目标 JSON 在 `QuestTemplate.objectives` 列里，链面板从不读）。

| 环节 | 实现 |
|---|---|
| CLI | `LoadQuestChain` 多返回一份 `Briefs`（名字/描述/objectives JSON 原样）；目标的解析走**权威映射器** `QuestWireMapper.ObjectivesFromTemplateJson`，文本由 `QuestChainText.ObjectiveLine` 拼成成品行（"×N" 规则一份，≤1 不带 ×） |
| 输出 | `quest-chain` 每个节点多 `description` 与 `objectives[]`（**已经拼好的字符串**，插件不重复拼） |
| 插件 | 链框每节点多两行"描述：…""目标：a、b" |
| 门禁 | `QuestChainTextTests` 3 条（×N 边界、缺描述兜底、裁剪）；契约守卫加 `QuestWireMapper`/`QuestChainText.ObjectiveLine` 与插件两处文案键；NPC 子集 **161 绿** |
| 真跑 | `quest-chain 2` 实测：五条入门 + 五行异闻都带回描述与 `['猎杀… ×5']`、`['击杀… ×3']` 目标行 |

**边界**：目标只显示"做什么、几次"；目标对象 id 的翻译仍由客户端目录负责（映射器有意不谎报对象名）。

---

### 11.48 路线试算（A10 的可验收性，2026-10-27）

**问题**：编辑器能画路线，但**看不出这条路走起来像什么**——一个循环多远、多久、会不会有"两点贴在一起"
（出发即到点、在原地抖）或"一段 80 米直线穿山"。这些只有等运营进游戏盯着看才发现，而那是目视阶段的事。

| 环节 | 实现 |
|---|---|
| 内核 | `NpcRoutePreviewCore.Preview(points, mode, pause, walkSpeed)`：**顺序与停留交给巡逻内核**（`MonsterPatrolCore.Advance`：宿主视角瞬移到位、停留按内核自己的计时推到翻出为止），演算层只量距离与报问题。周期判据用**内核状态**（点下标 + 方向）重现，而不是"回到 0 号点"——往返模式永远回不到那个组合 |
| 问题清单 | `route_needs_two_points` / `leg_too_short`（阈值与内核 `ArriveDistance` **同值**）/ `leg_too_long`（60 米直线连段）/ `random_mode_order_unpredictable`（随机模式不给假顺序）/ `walk_speed_not_set`（速度 ≤0 时循环秒数回 −1，不编时间）/ `route_not_closed` |
| CLI | `preview-route - [--route-index <n>] [--speed <m/s>]`：点按 `seq` 排、走法与停留取草稿、速度优先 `--speed` 否则模板 `SpeedWalk` |
| 插件 | 「刷点·路线」页加「路线试算」按钮（CLI 现算，面板只显示回执） |
| 门禁 | 7 条内核用例：往返=来回两段（20m/10s）、循环=三角周长（12m/8s）、停留按每个到点计一次并进循环秒数（2s×2 到点 ⇒ 14s）、没速度不给假时间、点太少不成立、短/长段点名、随机模式点名；契约：CLI 动词 + 插件动词 + **阈值同尺**（`ShortLegMetres == MonsterPatrolCore.ArriveDistance`）；NPC 子集 **166 绿** |
| 真跑 | `preview-route` 对 NPC 3（随包四方针路线）：`循环 24 米 / 15 秒（停 0；问题 0）closed=1`；`--speed 0` ⇒ `cycleSeconds=-1 + walk_speed_not_set`；坏下标 ⇒ 明确拒绝 |

**边界**：`cycleSeconds` 是"走 + 停"的估时（直线连段、不含起停加速）；真导航图（段间绕障）由怪物侧的 NavMesh 切片负责。

---

### 11.49 全量回执：3787 通过 / 0 失败 / 8 跳过（2026-10-27，接任务发布者校验后）

本轮 NPC 侧连续落地的十件事（发号新建、预制体分流、产物同源门、残血逃跑、逐件+绑定级条件、行切换、链条描述与目标、路线试算、制作/教学执行面同数+真收学费、接任务发布者校验）
一起进全量跑——**整仓 3795 条用例、3787 绿、0 红、8 跳过**（跳过的是既有 SKIP 的交互应用用例）。
做法：并行会话的 Silo/Gateway/DsHost 在跑、Debug 输出被锁，用
`dotnet test … -p:BaseOutputPath=bin/VerifyNpc/` 旁路构建（该目录在 .gitignore 覆盖内），不杀对方进程、也不动他们的运行实例。
**注意**：旁路目录不能同时给两个测试进程用（同目录并发会互相覆盖），并行跑第二条命令要换一个输出目录。

---

### 11.50 目视窗口的被动取证（2026-10-27，只读）

并行会话起了 Silo/Gateway/DsHost 并跑起 PIE；我全程**只读**（sim_status / scene_hierarchy / ui_dump），未动编辑器与进程。观测到：

| 观测 | 实测 |
|---|---|
| 运行态 | `isPlayMode=true`，RootScene + Login + **World** 三场景都加载 |
| 世界对象 | World 场景 **52 个 Actor**：`Monster_*` 19、`RemotePlayer_*` 4、`NPC_*` 2（后者是场景布景，中文名）、POI/Foliage/车马等布景 |
| 名牌宿主 | `ContainerControl` 下 **每实体一块 `Panel 160x58`**（名称行 + "名字 Lv.N" + 血条），当前可见 1 块（`赵问剑 Lv.1`），其余 hidden（`墨麒麟 Lv.12`/`石甲狰 Lv.18`/`冥羽鸩 Lv.26`/`银爪狸 Lv.8`/`青藤蟒 Lv.21`）——与怪物会话的 11 只种子怪对得上 |
| **未观测到友好 NPC 名牌** | 快照这一刻没有 `铸剑师/守门人/行商/货郎` 名牌；且友好 NPC 的 Actor 名仍是 **`RemotePlayer_*`** 前缀 |
| 由此得到的装配结论 | 我的客户端改动（`Npc_` 改名、NPC 预制体分流、NPC 体型缩放）**不在这次 PIE 的游戏装配里**——本轮对方只重编了 `GameEditorTarget`（编辑期 DLL），PIE 吃的是更早的 `GameTarget` 运行期脚本（与 [[flax-flax-build]] 的"运行期脚本 DLL 不换"同因）。要让这批客户端改动在 PIE 里生效，需要对方**重编 GameTarget 并重启 PIE** |

**这轮取到的价值**：证明"名牌宿主 + 世界实体 + 三场景加载"这条目视通路在现环境是通的，且给我方留下可复用的只读取证配方
（`sim_status → scene_hierarchy（按前缀分组+坐标与内容刷点对坐标）→ ui_dump（名牌文本，写到 UTF-8 文件再读，避免 GBK 控制台乱码））。

**补记（同日，第二次只读取证——友好 NPC 名牌全部命中）**：用 `ui_dump(maxRows=400)` 取到 270 行控件树，名牌文本里出现
**随包五只 NPC 全部五只**（标题行 + "名字 Lv.N"）：

| 名牌第二行 | 名牌第一行（头衔） |
|---|---|
| `铸剑师 Lv.12` | `外门弟子` |
| `守门人 Lv.18` | `外门弟子` |
| `游方商队 Lv.26` | `外门弟子` |
| `杂货商 Lv.30` | `内门弟子` |
| `山贼头目 Lv.32` | （敌对，无头衔行） |

同时 `Monster_*`/`RemotePlayer_*`（友好 NPC 在本装配下的 Actor 名）**两次采样间坐标变化**（行商 8.17→12.64），
证明它们是运行期驱动而非场景摆放。**这就是 A13"玩家视觉的实际表现"的第一份运行态证据**：名字/头衔/等级来自 NPC 内容，
名牌宿主在真实客户端里为五只 NPC 各画了一块。仍未取证：像素截图、对话菜单/商店/训练/制作的**点击**链路（需要新 GameTarget 装配与可操作窗口）。

---

### 11.51 制作/教学的执行面与清单面同数 + 学费真收（A8/A9，2026-10-27）

**问题**（两条都已实证，症状都属"面板说得清、服务端不认"）：
1. 清单只列"**这位 NPC 教的**配方（他的教学行）"和"**绑定到这位设施**的配方（`StationNpcId == 他`）"，
   而**动作**只查"有没有训练师位 / 设施位"——站到任意训练师面前能学全服任意配方、站在任意设施前能做不属于它的东西。
2. 教学行上的**学费/材料**从未被收取：训练师窗口标价 800 金，服务端实际免费。

| 环节 | 实现 |
|---|---|
| 内核 | `NpcCraftCore.FindTaughtRecipeRow`（清单与动作**同一份行查找**；`TaughtByTrainer` 成为它的判空形式，两函数关系有断言）；`StationIdentityOk(recipeStationNpcId, interactingNpcId)`（未绑设施 ≤0 随处可做，绑了必须对号） |
| 动作 | 学配方：先按教学行校验"这位教不教"，再**先扣金币/材料 → 学 → 失败原路退回**（与技能分支同一条纪律，退不掉留 Error）；做东西：绑了设施时必须 `resolve.NpcId == recipe.StationNpcId` |
| 拒绝语 | "这位前辈不教这张配方" / "这里不是做这件东西的设施" / "金币不足，学不了这张图纸" / "缺少教学材料" |
| 门禁 | `NpcCraftAuthorityTests` 4 条：行查找（命中带收费、技能行不算配方、null/空/坏 id）、两函数关系一致、设施对号五例、**源码守卫**（两判据必须被调、`SpendAsync(..., row.CostGold` 必须在、`RefundRecipeLearnAsync` 必须存在且真的 `AddAsync` 退回）；NPC 子集 **171 绿**、Craft 相关 **63 绿** |

**边界**：材料退回只覆盖"学失败"这一路；跨进程部分失败（钱退了、材料没退）只留 Error 日志，与既有制作回滚同口径。

**补记（同日，执行面）**：上面那条源码守卫已升级为**行为断言**——`CraftLearnGateHandlerTests` 用
真 `NpcContent.Install`（合成内容：模板 1 带 `CraftTrainer` 位、教学行 `900001/800 金`，另有同库的 `900002`）
+ 真 `DsEntityRegistry` + 假集群跑 `RouteHandlerAsync`，四格：**不教的配方** → 回"不教"且
`SpendAsync`/`LearnRecipeAsync` 各 0 次；**教的配方** → `SpendAsync(Gold, 800)` 与学各恰一次、无退回；
**已经学会了**（Grain 返回 false）→ 先扣 800 再 `AddAsync(Gold, 800)` 退回一次；**没有训练师位** → 回"不教配方"且不扣费。
另一处口径：`NpcContent` 是进程级静态，新类必须并入 `monster-content` 这个**禁用并行**的集合——
第一版没并，并行跑的"对话记好感"用例读到合成快照，"找不到这个 NPC"/好感零记录两盏红灯全是它导致的。
并入后 NPC 子集 **201 绿**。

---

### 11.52 接任务的发布者校验（A6 执行面，2026-10-27）

**问题**（与 §11.51 同形）：可接列表**按发布者过滤**（`QuestListRequest.GiverEntityId` → 服务端核对 giver 实体再出清单），
而 `AcceptQuestRequest` 只带 `questId`——直连一条消息就能在任何地方接走高等级任务（等级门槛也只是在列表那次校验里生效一次）。

| 环节 | 实现 |
|---|---|
| 协议 | `AcceptQuestRequest` **追加** `EntityId`（`MemoryPackOrder(4)/Id(4)`，与 `CompleteQuestRequest.EntityId` 同一套追加法） |
| 判据 | `QuestGiverCore`（Sim.Quests）：身份对号**复用 `QuestTurnInCore.Evaluate`**（同一种"要谁就是谁"的形状）；四类拒绝各有接取侧文案（"这个任务需要在发布者处接取…"），不借用交付措辞 |
| 服务端 | `HandleAcceptQuestAsync` → `ValidateGiverAsync`：先从任务目录取 `GiverKind/GiverTemplateId`，`TryResolveGiver`（本分片 + 交互距离）解析实体，再判身份；**目录读失败按拒绝处理**（宁可拒绝也不放走"任意地点接任务"），目录里没有该任务时放行给 Grain 拒（"任务不存在"的权威在那边） |
| 客户端 | `QuestClientService.Accept(questId, giverEntityId)` + `GetOffersWithGiver()`（按 (发布者, 任务号) 排序）；`MenuQuestsPage` 的条目带 `GiverEntityId`，接取时一并上报 |
| 门禁 | `QuestGiverGateTests` 6 条：未指定发布者不限制、四类拒绝分得开、正确发布者放行、四类文案互不相同、**报文往返**（`EntityId` 经 MessageUnion/MemoryPack 原样到达）、源码守卫；**`QuestAcceptGateHandlerTests` 6 条执行面**（真 `DsEntityRegistry` + 假集群跑 `RouteHandlerAsync`）：不带实体/站在别的 NPC 面前/实体认不出/目录读失败 **四种都 Success=false 且 `AcceptFromTemplateAsync` 一次都没调**，正确发布者放行一次，`giver_template_id=0` 的自由任务不受影响；客户端离线 csc **0 错** |

**边界**：`giver_kind != 1`（物件/区域发布者）仍按"不限制"处理——那类发布者没有实体注册表身份，等有玩法再补。

**补记（同日）：链确实能跨 NPC** —— `QuestChainNpcHopTests` 用一条合成链（NPC1 发布→NPC2 交付→NPC2 再发布→NPC3 交付）钉住：
拓扑序 `[90001,90002]`、唯一一条边 `90001→90002`、每个节点上的发布/交付 NPC 号原样保留、根只有第一环。
对应原话里的"任务链跨几点 NPC"这一档（此前只有单 NPC 的随包链做证据）。

---

### 11.53 只配了预制体也能进客户端表（A4 的耦合修复，2026-10-27）

**问题**：导出时"模型键为空就整行丢掉"。而**新建 NPC 的模型键默认为空**——运营在编辑器里配好预制体 GUID 后，
这一行依然进不了表（客户端按编号认人，模型键只是分组），表现是"库里明明配了美术，世界里还是占位人形"，而表里查无此人。

| 环节 | 实现 |
|---|---|
| 判据 | 跳过条件收窄为**两样都没有**：`ModelKey` 与 `PrefabGuid` 同时为空才丢；只有 GUID 就足够让客户端认出这一只 |
| 客户端 | `NpcStanceCatalog.Add` 本来就只在模型键非空时才登记 `ByModelKey`——按编号查不受影响（无需改） |
| 门禁 | 契约用例扩容：合成快照里"NpcId=9、模型键空、GUID 有"必须进表且带回 GUID；"两样都空"才被跳过；随包表仍是 5 条且逐字节同源 |

---

### 11.54 求援落地（A5 `CallsReinforcement`，2026-10-27）

**问题**：这一位对 NPC **只有装载期校验**（勾了位就必须配警戒半径），运行期没有读者——
玩家打哨兵时旁边的同伙站着看。审计上一轮刚把这条更正进缺口表，这一轮补上实现。

| 环节 | 实现 |
|---|---|
| 宿主 | 在**唯一伤害写点** `ApplyDamageToMonster` 之后调 `TryWakeNpcReinforcements`：被打中且没死时，把**同模板、活着、无目标**、且未在复活宽限/回位免疫窗内的敌对 NPC（`AlertRadiusMeters` 内）的 `ThreatTargetId` 钉到攻击者 |
| 为什么在宿主 | v1 AI 是单槽、没有社交仇恨入口；这里只做一次跨实体唤醒，之后走的就是"先挨一下"的同一条通路，不新增状态位、不改内核判据 |
| 回执 | `NpcReinforcementWokenCount`（勾了位却恒 0 = 位没生效，比"看起来没人来"更早暴露） |
| 门禁 | `NpcReinforcementTests` 1 条：三只同模板（2m / **21m** / 50m）——受害者被打中后，21m 那只（**在自己仇恨半径 20 之外**，否则会自己索敌，测的就是"被叫醒"）目标变为攻击者、进 Chase 并向玩家移动；50m 那只目标仍为 0、不入战；计数恰为 1。NPC 子集 **174 绿** |

**坑（夹具教训）**：第一版把同伙放在 5m，它**自己就先索敌了**（仇恨半径 20），于是"目标=玩家"根本不是求援的证据；
第二版还把"写入目标是这一拍、位移从下一拍开始"当成失败——补三拍再断言位移。

---

### 11.55 商人（开窗/买/卖）的执行面（A7，2026-10-27）

**问题**：A7 一直只有"服务端全链 + 客户端接真数据"的清单面证据，`ShopHandler` 的**编排顺序与补偿**
（钱→库存→背包、背包满退回、卖出先扣物后给钱）没有行为断言；库存内核单独测过，但"谁在什么时候调谁"没人钉。

| 环节 | 实现/断言 |
|---|---|
| 夹具 | 真 `NpcContent.Install`（模板 1 带 `Vendor` 位；货架行 slot 0 / 物品 750001 / 买入覆盖 100 / 收购覆盖 40 / 库存 5 / 补货 60s）+ 真 `DsEntityRegistry` + 假集群（钱包/库存/背包/角色位置）跑 `RouteHandlerAsync` |
| 开窗 | 价与剩余都来自服务端货架行：`Ok`、`NpcId=1`、`Items[0]` 的买/卖价 100/40、剩余 5、非无限；金币取自钱包 |
| 买 | 扣 `SpendAsync(Gold, 300, "npc_buy:1:750001")` 恰一次 → `TakeAsync(750001,3,5,60)` 恰一次 → `AddItemAsync(750001,3)` 恰一次；回执 `TotalCost=300`、`GoldAfter=扣后余额`；成功路径**不**调 `ReturnAsync` |
| 背包满 | `AddItemAsync=false` → `ReturnAsync(750001,3,5,60)` 一次 + `AddAsync(Gold,300,"npc_buy_addfail:…")` 一次；回执 `Reason=inventory_full` 且 `GoldAfter` 回到扣款前 |
| 卖 | 先 `ConsumeByTemplateAsync(角色,物品,2)` 恰一次 → `AddAsync(Gold,80,"npc_sell:1:750001")` 恰一次；`UnitPrice=40`；卖出**不**动货架（`TakeAsync` 0 次） |
| 拒绝 + 正对照 | 同一夹具只翻 `Vendor` 位：无位 → `not_a_vendor` 且钱包/库存一次都没调；勾位 → `Ok` 且清单 1 条 |
| 回执 | `ShopHandlerExecutionTests` **5 绿**；NPC 子集 **201 绿**（含前一条 §11.51 补记） |

**边界**：客户端窗口的点击链路与目视仍未验（需运行态 PIE 吃新 `GameTarget`，§11.50 同一条前置）。

---

### 11.56 技能学习的执行面（A8 的另一条通道，2026-10-27）

**问题**：§11.51 补的是**配方**教学（`CraftHandler`），而"技能学习"原话对应的 `SkillHandler.LearnSkill`
只有读代码才知道它确实按训练师行收学费/材料——没有行为断言。两条通道共用纪律但实现是两份，不能互相代言。

| 环节 | 实现/断言 |
|---|---|
| 夹具 | 真 `NpcContent.Install`（模板 1 带 `Trainer` 位；教学行 `1001 / Skill / 500 金`，可另配材料 700011×2）+ 真 `DsEntityRegistry` + 假集群（钱包/背包/`ISkillGrain`/角色位置）跑 `RouteHandlerAsync` |
| 条件 | 学费 500 由 `npc_trainer:1:1001` 标签扣；门槛与清单都走服务端内容，客户端只报实体 id + 技能 id |
| 学不教的 | 报"这位训练师不教这一式"，`SpendAsync`/`LearnSkillAsync` 各 0 次 |
| 无训练师位 | 报"这位 NPC 不教技能"，不扣费 |
| 学教的 | `SpendAsync(Gold,500)` 恰一次、`LearnSkillAsync(角色,1001)` 恰一次、无退款 |
| 门槛不过 | Grain 回失败 → 先扣后 `AddAsync(Gold,500,"npc_trainer_refund:1001")` 退回一次，失败原因原文下行 |
| 材料不齐 | `ConsumeByTemplateAsync` 拒绝 → 退学费、**不调用学习**，报"缺少教学材料" |
| 钱不够 | `SpendAsync` 拒绝（InsufficientFunds）→ 报"金币不足"，不落库、无退款 |
| 回执 | `SkillLearnGateHandlerTests` **6 绿**；合并子集（NPC + 任务/制作/商店/技能执行面）**212 绿** |

**边界**：同样只到"请求→回执"，训练师窗口的点击链与目视未验；插件与内容两侧没有新增字段（教学行早已导出）。

---

### 11.57 制作动作的执行面（A9 的 `Action=0`，2026-10-27）

**问题**：§11.51 补的是学配方的**执行面**，而"做东西"是同一处理器的另一分支，四条失败路径各有补偿：
材料不足退已扣料、工费不够退料、成品放不下退料+退费、合成判定失败无副作用。此前只有内核与源码守卫。

| 环节 | 实现/断言 |
|---|---|
| 夹具 | 真 `NpcContent.Install`（铸剑师=教学+设施、丹炉=仅设施；配方 A 绑铸剑师、工费 50、料 X×2+Y×1；配方 B 不绑设施）+ 真 `DsEntityRegistry`（两实体在原点）+ 假集群（背包快照 X=5/Y=3、钱包、`ICraftingGrain`） |
| 站错设施 | 丹炉前做铸剑师的配方 → "这里不是做这件东西的设施"，`CraftItemAsync`/扣料/扣款各 0 次 |
| 站对设施 | `CraftItemAsync` 恰一次且**持有量来自背包快照**（断言 `held[X]=5, held[Y]=3`）；料 X×2、Y×1 各扣一次；`Spend(Gold,50,"npc_craft:900101")` 一次；成品恰发一次 |
| 材料不足 | 第二样扣不动 → 把已扣的 X×2 退回一次、不扣工费、不发成品 |
| 工费不够 | `Spend` 拒绝 → X×2、Y×1 都退回、不发成品 |
| 成品放不下 | 成品 `AddItemAsync` 失败 → 料两样退回 + `AddAsync(Gold,50,"npc_craft_refund:900101")` 一次 |
| 判定失败 | `CraftItemAsync` 回失败 → 料与钱一次都不动，失败原文下行 |
| 未绑设施 | 没有任何"教配方"位的丹炉前也能做（≤0 = 随处可做，但仍需交互对象） |
| 回执 | `CraftActionHandlerTests` **7 绿** |

**边界**：同 §11.51——客户端制作页的点击链与目视未验。

---

### 11.58 `SpeaksGossip` 全链落地（**不依赖 023 先执行**，2026-10-27）

**问题**：023 的 `text` 列一直按 §13.1 的"SQL 与实体同批"纪律挂着——因为把列写进 EF 实体/迁移后，
**未执行的库**上的任何查询都会因缺列被打挂。可用户口径是"SQL 只生成不执行"，于是这条能力永远停摆。

**改法**：**不碰 EF 实体与迁移**，用"列存在性守卫 + 参数化原始 SQL"把整条链一次接通：

| 环节 | 实现 |
|---|---|
| 读 | `NpcStore.HasGossipTextColumn()`（`COL_LENGTH`，进程内缓存一次）→ 有列才 `SELECT npc_id/group_id/seq/text` 回填 `NpcGossipRowDto.Text`；**没列整段跳过**并把 `gossip_text_column_absent` 记进 `snapshot.Skipped` |
| 写 | 保存是"删光重插"，所以必须在 `SaveChanges` **之后**按 `(npc_id, group_id, seq)` 回写 `text`；没列时 `WrittenRows["NpcGossipText"] = -1`（**−1 ≠ 0**：一个是"库还没这列"，一个是"写了 0 行"）；超 200 字截断并记 `gossip_text_too_long` 问题，不让整包保存失败 |
| 校验 | 装配期 `NpcContentLoader` 报 `gossip_text_too_long:{npc}:{seq}`；宽度常量只有一处（`NpcContentSnapshot.GossipTextMaxLength`） |
| 导出 | `gossipOptions` **只在非空时**写 `text` 键——没配台词的随包产物与上一版逐字节相同（同源门不必重导）；客户端读不到键就退回按种类合成 |
| 客户端 | `NpcStanceCatalog.GossipOption.Text` ← `"text"`；`NpcGossipPage` 配了台词就显示台词（仅文本项点击时把全文弹出来），没配仍显示"看看货架/请教武艺/…" |
| 编辑器 | 「对话·作息」每行下排加"台词"输入框（`NpcGossipRowModel.Text`），加载/保存都走同一格——**漏保存这一格就是每次保存清空台词** |
| CLI | `preview-gossip` 每项多回 `text`，能看到玩家实际会读到哪句话 |

**回执（023 未执行，走的是"缺席"路径）**：合并子集 **221 绿**（新增 2 条契约：非空才写键、200/201 边界）；
四跳流水线 **PIPELINE OK / 32 绿 / rev=24**——5 只往返逐格一致说明"守卫读 + 守卫写"没有破坏保存；
客户端离线 csc **0 错**；插件两模块 0 错。

**必须如实标注的边界**：`text` 的**有列路径**（读回真台词、保存后不清空、导出带 text、UI 显示）今天**跑不到**，
因为 023 还没执行。执行后按 §13.1 的"落地后五步"验一遍再改成 ✅。

---

### 11.59 服务入口按数据可见（`NpcServiceCore` 第一次有运行期消费者，2026-10-27）

**问题**：`NpcServiceCore.Evaluate/Resolve/VisibleMask` 这套"位图 → 玩家可见入口"的判据写了很久，
**除测试与装配期的未知位检查外没有任何消费者**。于是"开了商人位、货架却是空的"这种 NPC，
客户端菜单里仍有"看看货架"，点下去服务端只会回 `not_a_vendor`——**点了没反应，日志里全是正常拒绝**。
训练位无教学行、配方位无配方同理。

| 环节 | 实现 |
|---|---|
| 判据 | `NpcServiceCore.PruneDataEmpty(flags, vendorRows, skillRows, recipeRows)`：只摘**三个数据位**（商人/技能训练/配方训练）；任务位取决于"这个玩家"（运行时才知道）、设施与对话是物理/壳层入口，都不动 |
| 运行期 | `NpcContentLoader.Compile` 先按 NPC 计数（商人行、`ServiceKind=1/2` 的教学行各算各的）再写进 `NpcRuntimeConfig.ServiceFlags`——身份下发给客户端的位图就是已清洗的那份 |
| 客户端 | `NpcVisualsExport` 用**同一个函数**算 `services`：导出表与运行期不会一边有入口一边没有 |
| 回执 | `receipt.PrunedServiceBits`（计**位**，一只 NPC 可摘两位）+ `PrunedServices` 明细 `service_bit_pruned:{npc}:0x{mask}`；`Describe()` 多一段 `svc_pruned=N`（ASCII 自证） |
| 门禁 | `NpcServiceAvailabilityTests` 4 条：三数据位全空 → 只剩任务/设施/对话；有数据 → 原样；**按 NPC 各算各的**（甲的位被摘、乙保留，回执计数=2）；导出用同一判据（甲的 9→1、乙仍 9） |
| 夹具修正 | `NpcIdentityDeltaTests` 的铁匠原来只写 `ServiceFlags=121` 而没有货架/教学行——那是"位图逐快照下发"的用例，不是数据清洗；补齐最小数据后位图仍是 121 |

**回执**：NPC 子集 **225 绿**；四跳流水线 **PIPELINE OK / 32 绿 / rev=28 / svc_pruned=0**（随包五只的三个数据位都有行，
导出表逐字节不变，同源门照绿）。

**边界**：任务入口的可见性仍是**按玩家**的（`NoQuestForThisNpc`），不在装配期判——它要玩家的任务快照，
由客户端头顶 `!`/`?` 与任务页数据各自承担。

---

### 11.60 头顶任务符号：客户端语义测试 + 交付指示的已知错位（2026-10-27）

**背景**：`NpcQuestIndicatorCore`（内核）只有 CLI 预览在调，客户端另有一份 `NpcQuestMark`，**零测试**。

| 环节 | 内容 |
|---|---|
| 测试接线 | 把纯 C# 的 `Source/Game/Selection/NpcQuestMark.cs` 链进测试工程（与 `NpcDraftModel` 同法），不依赖 Flax |
| 语义门禁 | `NpcQuestMarkTests` **9 条**：可交付 > 可接 > 进行中、同类多条带条数、已交过不可重复不提示、已接但身上没这条任务不提示、清单空不画、空目标列表按未完成、**与内核逐组合等价**（9 种双任务组合，符号与条数都对） |
| 已验证的输入域差异 | 客户端只拿服务端**过滤后**的清单（等级/前置不满足已被 `GetOfferedQuestsAsync` 剔掉），所以内核的 `LockedOffer`（灰 `!`）在客户端不可达；"已交过且不可重复"客户端按不提示处理 |

**查实但**未**修的缺口（写清楚前置，避免下次半改）**：交付指示会**错位**。
- 现状：清单按 **giver** 过滤（`filterByGiver` 比 `GiverKind/GiverTemplateId`），而客户端对"已接且目标完成"的项一律画黄 `?`；
  `QuestOfferInfo`/`QuestOfferSummary` 都**不带交付者身份**。
- 后果：交付 NPC ≠ 发布 NPC 的任务，会在**发布者**头上亮黄 `?`（点进去交不了，服务端按 §11.18 拒），
  而真正的交付 NPC 头上**什么都不显示**（它的清单里根本没有这条）。
- 要修必须三件同批：① `QuestOfferInfo` + `QuestOfferSummary` 增交付者身份（Grain 接口类型变更 ⇒ **需重启 Silo**）；
  ② 清单在该 NPC 是交付者时也带出"可交/进行中"，而不只按 giver 过滤；③ 客户端按 `TurnInIsHere` 决定黄/灰。
  只做③会让玩家"哪儿都看不到问号"，比现状更糟——所以本轮**只记录不改**。

---

### 11.61 摘位必须让作者当场看见（§11.59 的作者侧收口，2026-10-27）

**问题**：§11.59 的清洗只体现在 `Describe()` 的 `svc_pruned=N` 里。编辑器状态栏仍说"校验通过"，
保存也不报错——作者勾着商人位、货架为空，要到世界里才发现入口没了。这正是"配了没反应"的典型。

| 环节 | 实现 |
|---|---|
| 判据文案 | 新 `NpcValidateReport`：有问题优先报问题；否则有摘位 → "校验通过（N 个服务位因内容为空将被摘除）"；否则"校验通过" |
| CLI | `validate` 的 data 增 `prunedBits` / `prunedServices`，message 用上面那份文案（CLI 与编辑器不再各写一句） |
| 编辑器 | 校验与**保存前**都把逐条 `service_bit_pruned:{npc}:0x{mask}` 打进回执区；状态栏直接用 CLI 的 message |
| 顺带修的边界 | 计数口径对齐"真正进包的行"：`ItemId<=0` 的坏货架行**不算数据**（装配会跳过它）。不修的话，一条坏行会让位保留、窗口照旧空的——清洗在边界上失效 |
| 门禁 | `NpcServiceAvailabilityTests` 再 +3：摘位被念出（4 位 / 2 条明细 / 文案逐字）、干净时就是"校验通过"、有问题时优先报问题；插件两模块 **0 错** |

**回执**：NPC 子集 **237 绿**；四跳流水线 **PIPELINE OK / 32 绿 / rev=32 / svc_pruned=0**。

---

### 11.62 两处"编辑器能配、运行期没人读"的收口（2026-10-27）

用同一个审计法（对 `NpcRuntimeConfig` 字段逐个查生产代码读取方）又抓到两格**零读者**：

| 字段 | 症状 | 修复 |
|---|---|---|
| `KillCreditNpcId`（`kill_credit_npc_id`，WoW `KillCredit1/2`） | 编辑器有输入框、库里有列、快照与运行期配置都在，但**没有任何读取方**：任务"击杀 N 只 X"与五行域好感永远记在被杀者头上，配了转移等于没配 | `HandleMonsterDeath` 里算 `creditTemplateId`（仅当被杀者属 NPC 内容且目标号 > 0 且不等于自身），只改 **`Kill` 事件**的身份；掉落/金币属于尸体，仍用被杀模板。任务推进与好感都读这一个字段，因此一处改动同时生效 |
| `LevelVariance`（`level_variance`） | 编辑器有"等级浮动"、库里有列，运行期从不读：所有 NPC 永远基准级 | `NpcSpawnSnapshot.ResolveLevel` 在无刷点覆盖时按 `LevelBase ± LevelVariance` 用 `MonsterLevelCore.Roll` 掷一次；种子用**刷点号+编号**（不用 tick）——等级牌玩家可见，同一点复活后等级不能跳。刷点调用点仅在"有覆盖 **或** 有浮动"时才传值，零浮动路径逐字不变 |

**门禁**：`NpcKillLootTests` +1（击杀事件换成 credit 号，`Loot`/`KillMoney` 仍是原模板）+ 夹具重构成 `RunKill`；
`NpcSpawnExtrasTests` +3（40 个刷点全在 [25,35] 且至少两种取值、同点两次同值；实际生成等级等于逐点掷出的值且 != 基准；浮动 0 时等于基准）。
子集 **241 绿**。

**边界**：`KillCreditNpcId` 只影响**击杀事件身份**（任务击杀 + 好感），不改经验曲线（经验按被杀者等级/档位算）；这是刻意口径，写在这里防止下一个人把它当漏项。

---

### 11.63 台词组（`gossip_group_id`）：模板那一格第一次有读者（2026-10-27）

**问题**：模板有 `gossip_group_id`、每个选项有 `group_id`，但**导出里一个都没有**——客户端把这位 NPC 的
所有组一次性摆出来，"白天/夜里两套台词"会同时出现；模板那一格全仓零读者。

| 环节 | 实现 |
|---|---|
| 导出 | NPC 行增 `gossipGroupId`（默认组，0=不筛）；每个选项增 `group`（0=通用项）。随包表已重导 |
| 客户端 | `GossipOption.GroupId` / `Entry.GossipGroupId` 解析；`GetGossipOptions` 交给新纯函数 `NpcGossipMenu.IndicesToShow`：活跃组 ≤0 不过滤（旧表行为逐字不变）、组 0 任何组都显示、其余只留活跃组，**原序保持** |
| 门禁 | `NpcGossipMenuTests` 3 条（不过滤 / 活跃组+通用项且保序 / 零匹配回空）；契约 1 条（两端都导出、用同一函数筛出 `{0,2}`）；`ClientKeys` 对账清单同步；客户端离线 csc **0 错** |
| 回执 | 子集 **245 绿**；四跳流水线 **PIPELINE OK / 33 绿**（随包表带上新键并通过同源门） |

**边界（下一片）**：**作息时段切换台词组**还没通——要修必须把"当前组"随每个快照下发
（在 `EntityStateAuthComponent` 追加 `NpcGossipGroup`，服务端按 `NpcScheduleCore` 的当前档算），
并让客户端选择层读它。**该组件是网络契约**：追加字段要求客户端与 DS/Gateway **同批重启**，
否则旧客户端读新包会失败；本片刻意不碰它，静态默认组已经可用。

---

### 11.64 速度可编辑 + 逃跑按奔跑速度（`speed_run` 的最后一位读者，2026-10-27）

**问题**：库里两列 `speed_walk` / `speed_run`，运行期只读前者（生成时的移动速度）；
编辑器**连输入框都没有**（模型有字段，面板没格子），作者只能改库。`speed_run` 更惨：全仓零读者。

| 环节 | 实现 |
|---|---|
| 编辑器 | 「基础·外观」加 **行走 m/s / 奔跑 m/s** 两个输入框；空框保留原值（不把"清空"当成"回默认"） |
| 运行期 | 残血逃跑改用 **`SpeedRun`**（WoW 脱战跑得比走得快）；没配才退回生成时的移动速度 |
| 门禁 | `NpcFleeTests` +1：同档同残血的两只，跑速 8 的位移必须 > 跑速 4 的 1.5 倍；`NpcDraftModelMergeTests` +1：`speedWalk/speedRun` 在草稿 JSON 里往返；插件两模块 **0 错** |
| 连带修正 | `NpcRespawnTests` 的精英夹具自带 `FleesAtLowHealth`，现在会按跑速跑出攻击距离——那个用例的主题是重生计时，夹具里显式 `AiBitsForceOff=FleesAtLowHealth` |

**回执**：NPC 子集 **247 绿**。

**边界**：追击（Chase）仍用生成时的移动速度（`speed_walk`），`speed_run` 目前只服务反击逃跑这一条路；
把追击也切到跑速会让所有敌对 NPC 的移动手感与既有夹具一起变，需要单独一轮回归。

---

### 11.65 头顶符号收口：灰 `!` 可达 + 交付指示按 NPC 对号（§11.60 的缺口修复，2026-10-27）

`NpcQuestMarkTests` 当时记了两个"输入域"缺口，本轮都用**只读已有任务目录**的方式补上，**不改 Grain 接口**：

| 缺口 | 修法 |
|---|---|
| 条件不足（灰 `!`）在客户端不可达：可接清单已被 Grain 按等级/前置过滤，客户端看不到被挡下的任务 | 网关读 `IQuestManagerGrain.GetCatalogAsync()`（已有方法），用纯函数 `QuestIndicatorOfferCore.CountLocked` 复算"这位发布者当前接不了"的条数（等级档 / 前置档分开计），只回**条数**不回名字——观感对齐 WoW，又不泄露未解锁内容。`QuestListResponse.LockedOfferCount` 追加 `Order 6` |
| 交付指示错位：客户端把"发布者清单里已接"当成交付，发布者头上亮黄 `?`，点进去却被交付者校验拒 | 网关用同一份目录给每条身上任务算 `TurnInHere`（交付者留空沿用发布者），`QuestSummary.TurnInHere` 追加 `Order 9`；客户端按**回包发布者**缓存"交付者就是我"的任务号集合，`?` 只画在交付者头上 |

**客户端为什么不能直接读全局清单上的 `TurnInHere`**：`_quests` 是全局的，而 `TurnInHere` 是"相对某个发布者"的结论；
直接复用会把 A 的交付结论画到 B 头上。`QuestClientService` 因此按 giver 缓存 `_turnInByGiver` 与 `_lockedByGiver`，
`NpcQuestMark.Resolve` 新增两个入参（条件不足条数、交付者任务号集合），并新增 `Locked` 态（灰 `!`，优先级在"进行中"之后）；
名牌颜色对 `InProgress`/`Locked` 统一走灰色。

**门禁**：`QuestIndicatorOfferCore` 5 条（等级/前置分档、已接已交下线他人不计、前置补齐后不计、无发布者不猜、交付者继承与显式）；
报文往返 1 条（两个新字段经 MessageUnion/MemoryPack）；`QuestIndicatorHandlerTests` 3 条（清单回包真的填了两个事实、
不带发布者/发布者认不出时保持 0/false 且不调用目录）；`NpcQuestMarkTests` 重写为 12 条（含"交付者在别处不画"、
"交付者面前清单为空也能画"、灰 `!` 与优先级、与内核逐组合等价）。客户端离线 csc **0 错**。

**回执**：NPC 子集 **260 绿**；整仓 **3861 通过 / 1 失败 / 8 跳过**——唯一红是 `MonsterEditorContractTests.导出回执数得出技能挂载也数得出真能施法的怪`：该文件跑前 2 分钟刚被并行会话改写、单跑同样红（他们在做"施法怪计数"），与本次改动无关，按纪律只报不改。

**边界**：两个追加字段对旧客户端是**忽略即兼容**（旧端不读，行为不变）；新客户端配旧网关时字段恒 0/false，
退回到修复前的表现而不是出错。

---

### 11.66 追击也用奔跑速度（`speed_run` 的第二个读者，2026-10-27）

**问题**：§11.64 只把 `speed_run` 接到"残血逃跑"；**追击/回位仍按走速**——玩家看到"山贼冲刺与散步同速"，
而 WoW 的生物追击用的是跑速。`RunSpeed` 这一格在别处也不存在（`MonsterAiConfig`/`SpawnPointConfig` 只有 Move/Walk）。

| 环节 | 实现 |
|---|---|
| 配置 | `MonsterAiConfig.RunSpeed` / `SpawnPointConfig.RunSpeed`：**0 = 没配、退回走速**（与 `WalkSpeed` 同一条"缺省逐字不变"纪律）；`ToAiConfig` 映射 |
| 判据 | `MonsterAiConfig.ChaseSpeed(cfg)`：先判 `MoveSpeed > 0`（站桩档的"不会动"语义不能被一格跑速改掉），再取跑速或走速 |
| 内核 | v1 追击与 Leash 回位都改用 `ChaseSpeed`；巡逻/游荡仍用 WalkSpeed |
| 宿主 | `CreateScriptedPoint` 增 `runSpeedOverride`；**只有内容 NPC 的刷点**传 `config.SpeedRun`，GM 指令与怪物模板逐字不变 |
| 客户端 | 快照速度投影（两处 `speed`）Chase/Leash 用 `ChaseSpeed`，否则客户端按走速外推会看到位置回拉 |
| 门禁 | `NpcChaseRunSpeedTests` 2 条：会话级（跑速 8 vs 1.6，同距离同时刻，接近量 > 2 倍）+ 内核边界（0 退回走速、`MoveSpeed=0` 仍为 0） |

**回执**：NPC 子集 **262 绿**；整仓 **3864 通过 / 0 失败 / 8 跳过**（3872）。

**注意**：装配器把 `speed_run <= 0` **归一成默认 4**（`NpcContentLoader`），所以"库里没填"其实是 4 而不是"退回走速"；
因此**随包敌对 NPC 现在追击速度 = 4 m/s**（此前是走速 1.6）。这是本片的有意行为变化，编辑器里可以改；
"0 = 退回走速"只在**内核层**成立（对没有内容包的老调用方）。

---

### 11.67 服务入口预览（编辑器回答"玩家会看到什么"，2026-10-27）

**问题**：作者勾的是**位图**，玩家看到的是**入口**。中间那一跳受内容（货架/教学行为空）与玩家态（等级、任务关系）影响；
`NpcServiceCore.Resolve` 写好后一直只有"数据摘位"在用，编辑器无从回答"我勾了商人，玩家为什么看不到"。

| 环节 | 实现 |
|---|---|
| 内核 | 新 `NpcServicePreviewCore.Build(bundle, npcId, level, NpcQuestCounts)`：计数（商人行 / `ServiceKind=1/2` 教学行）→ 逐条 `Resolve`，返回声明位图、生效位图与每条理由 |
| 诊断要读**声明值** | `NpcRuntimeConfig.DeclaredServiceFlags`（装配时写入）：拿已清洗的 `ServiceFlags` 求值会把"内容为空"压成 `BitOff`，把要回答的理由全抹平 |
| CLI | `preview-services - [--player <lvl>] [--turnin n] [--offer n] [--progress n] [--locked n]`，回 `{declared, effective, visible, quests, items[{bit,visible,reason}]}` |
| 编辑器 | 「服务与交互」页加「入口预览」按钮（用基础页的等级当玩家等级），回执逐条落日志 |
| 真库回执 | NPC 1（声明 121，玩家 10 级）→ 可见 5（货架/技能/配方训练/设施/对话），任务两格 `BitOff`；NPC 2 不带任务关系 → 任务两格 `NoQuestForThisNpc`、可见 1；加 `--turnin 1` → 任务两格可见、可见 3 |
| 门禁 | `NpcServicePreviewTests` 3 条（内容为空的理由逐条、有内容+任务关系全可见、目录里没有这位时不猜）；插件两模块 0 错 |

**回执**：NPC 子集 **265 绿**；整仓 **3867 通过 / 0 失败 / 8 跳过**（3875）。

**边界**：任务关系是**参数**不是查询——预览不连 Grain；CLI 的四个计数由调用方给，编辑器默认全 0（任务两格会显示"没有任务关系"）。

---

### 11.68 刷点行删除（A14 最后一条编辑器边界，2026-10-27）

**问题**：审计一直挂着"多行没有删除行入口"——作者能切行、能新增，却没法删掉一条刷点（只能手改 JSON）。
之前不做是因为路线/配方**跨 NPC 共享**，删一条会牵动别人；但**刷点不是共享的**：保存本来就是"按 NPC 整组替换刷点"。

| 环节 | 实现 |
|---|---|
| 模型 | `NpcDraftModel.RemoveSpawn(index)`：越界返回 false 且不动数据；删空是合法状态（NPC 还没摆进世界） |
| 编辑器 | 「刷点·路线」页加「删除本行」按钮：先夹行号，**尾行=新增档没有可删的**；删后行号夹回、整页重刷 |
| 为什么路线/配方没有 | 路线按 `PathId` 合并、配方在全局表共享；删除属另一套退役流程，不能由"保存这一只 NPC"顺手做 |
| 门禁 | `NpcDraftModelMergeTests` +1（只删被点中的行、其余原序、越界不动、删空后仍拒删）；插件两模块 0 错 |

**回执**：NPC 子集 **266 绿**；整仓 **3868 通过 / 0 失败 / 8 跳过**（3876）。

**边界**：按钮没有目视（编辑器未起）；删除只在"保存"后落库，与其它行的语义一致。

---

### 11.69 `Casts` 代码全链落地，由 `024` 打开（§13.2 的执行侧，2026-10-27）

**这一轮只写"表存在之后就能跑"的那一半代码**，不动 schema（024 仍未执行，故生产行为零变化）。

| 环节 | 落地内容 |
|---|---|
| 内容契约 | `NpcSkillMountDto`（20 个字段，逐列镜像 024）+ `NpcContentSnapshot.SkillMounts`；装配回执 `skill_mounts=N`，坏行四类各念一条（`skill_mount_unknown_npc` / `_bad_skill` / `_bad_chance` / `_bad_slot`），**坏行不静默修好** |
| 内容装载 | `NpcContent` 持 `SkillMounts` 字典 + `static SkillMounts(npcId)`（按 `SlotSeq` 稳定排序）；挂载顺序即编辑器的行顺序 |
| 读库 | `NpcStore.ReadSkillMounts/WriteSkillMounts` 走**表存在守卫**（同 §11.58 的 `COL_LENGTH` 口径）：024 没执行时整段跳过、不报错、回执里写 0 行；保存按 `npc_id` 整组替换（NPC 自己一列，不与怪物共用 id 空间，§13.2 的证伪已排除复用） |
| 编辑器 | 「技能挂载」四行（战斗/非战斗/残血/受控）+ 技能/几率/吟唱/冷却/射程五列；`SlotType` 夹到 0–3，`SlotSeq` 取行序；未在这页填的技能不丢（按 `SkillId` 保留原行） |
| 运行期 | `DsWorldSession.ResolveMonsterCast` 先认 `MonsterContent`，再回退 `NpcContent.SkillMounts(templateId)`——**同一个伤害/效果出口**，不另抄一份结算；v1 宿主上（敌对 NPC 走的就是 v1）在 `NpcFleeing` 之后追加施法适配：`MonsterCastingCore.Advance` 决策 → 需停下就把本拍位移清零 → `FinishesCast` 时调 `ResolveMonsterCast` |
| 客户端 | 施法条复用怪物侧快照字段（`CastTotalSeconds/CastElapsedSeconds/CastSkillId`），NPC 无需新增网络字段 |

**为什么不是"接进 v2"**：§13.2 的推荐 (a) 会让勾了 `Casts` 的 NPC 整体换一套 AI（flee/leash/求援三组夹具钉的是 v1）。
改用 v1 适配 + 复用 `MonsterCastingCore` 内核，行为面零分叉，且不需要改 `TryDecideMonsterV2` 的唯一内容入口。

**测试（`NpcCastingTests`，4 条，全真跑）**：
1. 装配回执：有挂载 ⇒ `skill_mounts=1`；坏几率行（20000 bps）⇒ `skill_mount_bad_chance:500010:1001:20000` 且不进表；
2. 无挂载 ⇒ `skill_mounts=0`（位是能力、数据是内容，不报错）；
3. 施法者第一发命中距离 **> 3m**（远距离起手，且有 `Casting.ActiveMountIndex >= 0` 的采样）；
4. 正对照：同一只关掉 `Casts` ⇒ 全程零施法、第一发命中 **< 3m**（贴脸平砍）。

**回执**：`NpcCastingTests` 4/4；`NpcDraftModelMergeTests` 11/11（含挂载 20 字段 JSON 往返、删行、速度往返）；
插件 `_verify.sh` `[rt] exit=0 errors=0` / `[ed] exit=0 errors=0`；
`python Tools/npc-pipeline.py` → `npc_content_loaded rev=36 templates=5 ... skill_mounts=0 svc_pruned=0 problems=0` + 33 条 + `PIPELINE OK`；
NPC 子集 **271 通过 / 0 失败**；整仓 **3878 通过 / 0 失败 / 8 跳过**（3886）。

**边界**：024 未执行 ⇒ 库上没有这张表，保存挂载不会落库、生产内容 `skill_mounts=0`；
编辑器四行没有目视（编辑器未起）；真起手的伤害量走 `ResolveMonsterCast`，与怪物侧同一条公式，未单独像素验收。

### 11.70 晚进场的补发：NPC 施法条不能只有时长没有技能身份（§11.69 的验收第④步，2026-10-27）

**问题**：补发 Spawn 的吟唱字段原来只问 `MonsterContent.TryGetConfig(...)?.Skills`。NPC 敌对走怪物管线、
没有 `MonsterTemplate` 行 ⇒ 晚进场的人看到的是一根**时长/进度都对、技能 id 恒 0** 的条：
客户端描边（系别）与"可打断"提示全丢，服务端与日志一切正常——只错一半最难发现。

**修法**：把挂载解析收成唯一出口 `ResolveCastMounts(templateId)`（怪物优先、NPC 回退），
**结算口与补发口都问它**。这也顺手保证两条通路不会各写一份判据。

| 判据 | 做法 |
|---|---|
| 反向对照 | 同内容同挂载、NPC 在 9000 米外全程不施法 ⇒ 补发 `CastTotalSeconds<=0` 且 `CastSkillId==0`（证明"恒 0"不是断言写死） |
| 正例 | 起手后新人进场 ⇒ `CastTotalSeconds=2.00`、`Elapsed>0 且 < 总时长`、`CastSkillId=1001`、`CastSchoolMask=2`、`CastInterruptible=1` |
| 夹具 | 补发可能落在**会话专属**快照（`InitialSpawnSnapshots`）而非共享分组：扫描必须两条都看，否则用例会以"补发通路没走通"假红 |
| 证伪 | 把补发口退回"只认怪物内容"重跑 ⇒ 红，打印 `total=2.00 elapsed=0.20 skill=0 school=0 interruptible=0`；恢复后绿 |

**回执**：`NpcCastingTests` **5/5**；NPC 子集 **272 通过 / 0 失败**；整仓 **3881 通过 / 0 失败 / 8 跳过**（3889）。
**边界**：只覆盖"晚进场补发"这一条；起手事件本身（`MonsterCastStart` 的系别/可打断 payload）沿用怪物侧既有通路，未另测。

### 11.71 技能挂载：面板看不见的行不能在保存时消失（2026-10-27）

**问题**：编辑器「技能挂载」只有 4 行，而 `NpcStore` 保存是**按 npc_id 整组替换**（先删光再重插）。
`CommitSkillMountRows` 只按 4 个可见行重建列表 ⇒ 库里挂了 5 条技能的 NPC，打开后**只改个名字再保存**，
第 5 条就被静默删掉（保存成功、库里少行，作者从没见过那一条）。与 §11.43/§11.68 同族的"整表替换吃掉多行"缺陷。

| 环节 | 实现 |
|---|---|
| 判据 | 合并逻辑从窗口类提到模型侧 `NpcSkillMountEditing.MergeSkillMounts(existing, visible, visibleRowCount)`——窗口类不在测试工程里，留在那里的逻辑只能靠肉眼 |
| 隐藏行 | 只认为**下标 ≥ 可见行数**的现有行是隐藏行：原样带回、`SlotSeq` 从可见行数起顺延（不与被见行撞号），面板没显示的字段（`DamageScaleBps`/`StatusId` 等）保持原值 |
| 删除语义（正对照） | 可见区里**清空技能列**仍是作者主动删除：它的下标在可见区内，不会被当作隐藏行复活 |
| 作者可见 | 面板底部一行 `另有 N 条未显示（保存不改动）`，N=0 时不显示 |
| 门禁 | `NpcDraftModelMergeTests` +3：5 条保尾（含隐藏列与 SlotSeq）、清空可见行真删且尾巴仍在、新 NPC 只有可见行；**证伪**：把回带循环改成空转 ⇒ 2 条红（第三条本就不该红），恢复后绿 |

**回执**：模型测试 **14/14**；插件 `[rt] exit=0 errors=0` / `[ed] exit=0 errors=0`；
NPC 子集 **275 通过 / 0 失败**；整仓 **3884 通过 / 0 失败 / 8 跳过**（3892）。

**边界**：面板仍未提供"删除第 5 条"的入口（须把它挪进可见四行再清空，或走 CLI）；未目视。

**同族第二处（同一轮收口）：掉落面板**。`CommitLootRows` 也只按可见 4 行重建，且把 `LootBindings` 收成"只有第 0 份"——
一条 5 件的掉落表保存一次少一件；库里有多份绑定时其余几份整份消失。<br>
修法同样提到模型侧：`NpcLootEditing.AppendHiddenEntries`（第 5 件起原样带回、`Seq` 顺延、面板没有的 `GroupId/ConditionKind/QuestId` 保持原值）+
`NpcLootEditing.MergeBindings`（只编第 0 份，其余绑定原样带回；第 0 份被清空时才整份消失）；
面板底部加 `另有 N 件 / M 份绑定未显示（保存不改动）`。
门禁 `NpcDraftModelMergeTests` +3（保尾、清空可见件真删且尾巴仍在、多份绑定保尾/清空首份不牵连），
**证伪**：回带循环空转 ⇒ 2 条红，恢复后绿。
**回执**：模型测试 **17/17**；NPC 子集 **278 通过 / 0 失败**；整仓 **3887 通过 / 0 失败 / 8 跳过**（3895）。

**第三处（同轮普查，一次收口）**：同一形状的面板还有货架/教学/台词/作息/任务链——都用固定行数逐格重建。
随包**单只** NPC 的最大行数（量自 `HundunWorld/Data/Npcs/db_content.json`：货架 2 / 教学 3 / 台词 3 / 任务 0 /
路线 4 点 / 配方材料 1）仍在面板行数（4/4/4/3/6/3）之内，所以这一处目前是**潜伏缺陷**，作者一旦配到第 5 行就会丢。
（此前把流水线回执的 `vendor_rows=5`、`gossip_rows=7` 误当成单只行数——那是**全体合计**，按"数字要带出处"已更正。）
抽出通用合并 `NpcRowListEditing.AppendHidden(edited, existing, visibleRowCount, key, setSeq)`：
业务键 = 商人物品号 / 技能号 / (组号,序) / 起始秒 / 任务号；`setSeq` 为空时保留原序号（商人槽位是稳定键）。
`NpcDraftModelMergeTests` +3（货架保尾且槽位不动、台词跨组保尾、教学清空可见行真删），模型测试 **20/20**；插件两模块 0 错；
整仓 **3893 通过 / 0 失败 / 8 跳过**（3901）。

**第四处（路线/配方的两个独立缺陷）**：
1. **新建行每次提交都追加一条**：`UpsertRoute/UpsertRecipe` 只认业务号（`PathId/RecipeId`），而新建的行在落库前业务号是 0——
   保存、服务入口预览、校验、路线试算**每一次都会提交草稿**，于是点三次就多三条一模一样的巡街线/配方。
   新增 `UpsertRouteAt(index, route)` / `UpsertRecipeAt(index, recipe)`：正在编辑的那一行按**面板行号**就地替换，与有没有业务号无关；
   行号等于行数（新增档）且无业务号时才追加。
2. **子列表超行被截断**：路线点面板 6 个（长线会更多）、配方材料面板 3 格（4 种材料就丢一种）。
   两者都复用 `NpcRowListEditing.AppendHidden`（路线点按 `Seq`、材料按物品号，序号不动）。

门禁 `NpcDraftModelMergeTests` +5（新路线/新配方重复提交只产生一条、按行号只改选中那条、8 点保尾、4 种材料保尾）；
**证伪**：`UpsertRouteAt` 的行号分支改成永不命中 ⇒ 2 条红，恢复后绿。
既有源码守卫 `NpcEditorContractTests.PluginMergesMultiRowContentInsteadOfReplacing` 同步改为断言新接口（并把"按行号替换"也钉进去），未放宽。
**回执**：模型测试 **25/25**；NPC 子集 **286 通过 / 0 失败**；整仓 **3898 通过 / 0 失败 / 8 跳过**（3906）。

**第五处（自纠：去重会吃掉合法的重复行）**：上面的合并最初按"业务键去重"，而业务键相同**未必是重复**——
同一技能可以挂两个时机（战斗 + 残血）、同一物品可以在两个掉落组各掉一次、同一任务可以既发布又交付。
按业务键去重会把这些合法配置静默吃掉（和要修的缺陷同形，只是方向相反）。
改为**只看下标**：可见区按面板逐格重建，隐藏行 = 下标 ≥ 可见行数的原行，一律带回；业务键只用来判"这行是不是空的"。
门禁 `NpcDraftModelMergeTests` +3（同技能两时机保留、同物品两组保留、同技能"教+卖书"保留）。
**回执**：模型测试 **28/28**；NPC 子集 **289 通过 / 0 失败**；整仓 **3902 通过 / 0 失败 / 8 跳过**（3910）。

**第六处（闸门：面板行数必须装得下随包单只最大行数）**：保尾只保证"不删"，作者仍**看不见也改不了**第 N+1 行。
新增数据驱动闸门 `NpcEditorContractTests.PanelRowCountsCoverShippedPerNpcMaximum`：
从 `HundunWorld/Data/Npcs/db_content.json` **按 npcId 分组数最大值**（货架/教学/台词/任务），
路线按单条点数、配方按单张材料数，逐项断言窗口常量 `>=` 该值；并断言量出来的数 > 0（防夹具退化）。
今天实测：货架 2 / 教学 3 / 台词 3 / 任务 0 / 路线 4 点 / 材料 1，面板 4/4/4/3/6/3 全部装得下——**当前是潜伏缺陷，不再是活跃缺陷**。
**证伪**：把 `VendorRows` 临时改成 1 ⇒ 红并打印 `VendorRows=1 装不下随包最大 2 行`；恢复后绿。
**面向将来的守卫**：`PluginMergesMultiRowContentInsteadOfReplacing` 从"只挡刷点/路线/配方"扩到**全部多行面板**：
六个集合（货架/教学/台词/作息/任务链/技能挂载）都禁止 `_draft.X = new List<…>` 直接覆盖，
并要求窗口里真的出现 10 处保尾/合并调用（7×`NpcRowListEditing.AppendHidden` + 挂载 `MergeSkillMounts` + 掉落 `AppendHiddenEntries`/`MergeBindings`）——
少接一处，就等于那个面板又退回"整组替换"。
**回执**：契约+模型 **32/32**；整仓 **3908 通过 / 0 失败 / 8 跳过**（3916）。

### 11.72 `024` 的三份清单同构闸门（执行前就能验，2026-10-27）

表还没执行，列名对不上只会**执行那天**在运行时炸。新增离线契约 `NpcEditorContractTests.SkillMountSchemaInsertAndReadListsAgree`：

| 清单 | 来源 | 断言 |
|---|---|---|
| 建表列 | `scripts/sql/024_npc_skill_mount.sql` 的 `CREATE TABLE` 段 | 解析到 ≥26 列（含 5 个基类列），否则判"解析退化" |
| 写入列 | `NpcStore.cs` 的 `INSERT INTO [{table}] (...)` 段 | 恰好 25 列；`mount_id`（IDENTITY）**不得出现** |
| 回读列 | 同一文件的 `SELECT … FROM [Game_HunduShijie_NpcSkillMount]` | 恰好 20 列（读回不需要行身份） |
| DTO | `NpcSkillMountDto` 的属性（CamelCase → snake_case） | 20 个字段**全部**同时出现在上面三份清单里；`mount_id` 不得出现 |

**证伪**：把 SQL 里的 `[emergency_hp_percent]` 临时改成 `[emergency_hp_pct]` ⇒ 红并打印 `Not found: "emergency_hp_percent"`；恢复后绿。
**踩坑**：`SELECT [npc_id] AS NpcId` 会先命中同文件的**台词**读取块（同列名）——锚点必须用技能挂载独有的 `[skill_id] AS SkillId`；
解析正则也要认 CamelCase 基类列，否则 21 列被当成 26。
**回执**：契约+模型 **31/31**；整仓 **3906 通过 / 0 失败 / 8 跳过**（3914）。
（另：同一时段整仓曾出现 2 条红——`DsWorldScaleTests` 的 p99 25.40ms/上限 25ms、`MonsterAuthoredContentTests` 的一条；
两条**单独复跑均绿**，属并行构建下的计时/共享夹具噪声，非本切片改动，如实记录不掩盖。）

### 11.73 `023` 的列宽/守卫/截断同源闸门（同批，2026-10-27）

台词列也没执行，最容易出的错是"代码许 200 字、库列只有 100"：保存当场截断（或整包失败），而离线测试全绿。
新增 `NpcEditorContractTests.GossipTextSchemaMatchesGuardAndLimit`：

| 断言 | 依据 |
|---|---|
| 023 的 `ADD [text] nvarchar(N)` 能解析、列名是 `text`、N>0 | SQL 脚本 |
| 脚本带 `IF COL_LENGTH(...) IS NULL` 幂等守卫 | 同文件 |
| `NpcStore` 的列存在守卫是同一对字面量（表名 + `text`） | `COL_LENGTH(N'Game_HunduShijie_NpcGossipOption', N'text')` |
| 台词上限**只有一份**：`NpcGossipTextMaxLength` 必须别名到 `NpcContentSnapshot.GossipTextMaxLength`，不许第二份字面量 | 源码逐字 |
| `N == NpcContentSnapshot.GossipTextMaxLength == NpcStore.NpcGossipTextMaxLength` | 反射 + 常量 |
| 读走 `[seq] AS Seq, [text] AS Text`、写走 `SET [text] = {0}`（保存是删光重插，正文必须跟回去） | 源码逐字 |

**证伪**：把 `023` 的列宽临时改成 100 ⇒ 红并打印 `Expected: 100 / Actual: 200`；恢复后绿。
**踩坑**：我一开始断言 `SELECT [text] AS Text`，实际语句是同行四列的列表——**锚点要抄原文**，不能照语义猜。
**回执**：契约+模型 **32/32**；整仓 **3908 通过 / 0 失败 / 8 跳过**（3916）。

### 11.74 施法挂载的两个"配置旋钮"第一次有运行期断言（2026-10-27）

`ChanceBps` 与 `DamageScaleBps` 是编辑器里仅有的"这条挂载怎么放、打多疼"两个旋钮，此前只有装配层校验（0..10000 越界报问题），
**没有一条断言证明它们真的进了运行期**——装配把它们丢掉时全部用例仍绿。

| 字段 | 内核语义（读自 `MonsterCastingCore`） | 断言 |
|---|---|---|
| `ChanceBps = 0` | `ChanceBasisPoints > 0` 才参与（第 245 行）⇒ **0 = 永不释放**，不是 100% | 概率填 0 的同挂载 NPC 全程零起手 |
| `DamageScaleBps` | 装配 `DamageScale = bps / 10000f`（`NpcContentSnapshot.cs:664`） | 同夹具、同目标：10000 ⇒ 首击 **15**；20000 ⇒ 首击 **30**（实测，断言钉**精确 2 倍**） |
| `TargetSelect=4 / TargetCount / AoeRadius` | `SelectTargets` 的 `AoeAroundThreat` 以**起手锁定的圆心**、按 `TargetCount` 截断（`MonsterCastingCore.cs:498` 的 `count < cap`） | 两人相距 4 米、半径 8：目标数 **1** ⇒ 旁观者 **0** 伤害（正对照）；目标数 **2** ⇒ 旁观者 **45** |
| `MinRange` | `MountReady`：`MinRange > 0 && dist < MinRange` ⇒ 不 cast（`MonsterCastingCore.cs:243`） | 同夹具两跑：不限射程 ⇒ 起手；`MinRange=25`（全程都在 25 米内）⇒ **永不起手**，但近战照样打（伤害 5，证明夹具没空转） |
| `SlotType`（时机） | `IsCandidate` 的 switch（`MonsterCastingCore.cs:221-236`）：战斗位要求 `inCombat`；非战斗位要求 `!inCombat`；残血位要求 `inCombat && hpPercent <= EmergencyHpPercent` | 战斗位 ⇒ 起手；非战斗位（敌对 NPC 一生成就接敌）⇒ **永不起手**，但近战照样打；残血位阈值 99：**满血 40 拍零起手**，玩家打中一下（`damaged=True`）后 60 拍内起手 |
| `Interruptible` | 宿主把打断请求写进 `monster.InterruptRequested`，内核按此位决定取消还是照放；**打断后会按 1.5 秒退避重试** | 起手后发一次打断请求、再跑两拍：可打断 ⇒ **已不在吟唱**；不可打断 ⇒ **仍在吟唱** |
| `AllowMovement` | 宿主在 `ShouldHalt` 时把本拍位移清零（`DsWorldSession` 的 v1 施法适配） | 起手窗口内 X 位移：不许移动 ⇒ **0.00 米**；允许移动 ⇒ **3.60 米** |

**已知悬空格（刻意钉住现状，不让它静默）**：`SlotType=3`（受控时）在内核里**只判"在战斗中"**——全仓没有"怪物是否被控"的输入源
（`Horizon.Game.Sim/Monsters` 与 `DsCombatTypes` 均无 stun/CC 状态）。也就是说**受控位当前等价于战斗位**。
处理：①编辑器下拉标签改为 `受控(≈战斗)`；②新增用例 `受控位当前等价于战斗位` 钉住现状——将来真接上 CC 判据时它会红，提醒改用例；
③真正落地需要怪物侧一套 CC/光环状态切片（与怪物编辑器的"状态"格同批），不在本轮范围。

夹具给出伤害是确定值（同一 tick、同一距离、无随机），所以不用"更高"这种会放过 1.1 倍的弱判据；
`Run()` 同时返回首击伤害，供后续挂载字段继续复用。

**踩到的夹具陷阱（记下来免得重犯）**：AOE 用例第一版观察窗 200 tick，主目标被打死后 NPC **转火旁观者近战**，
于是"目标数 1"的正对照里旁观者也掉了 20——读数被污染，会把正确实现判成缺陷。
改法：窗口截到**主目标死亡**那一刻（`Hp <= 0` 即 break），之前的掉血才与"这一发打几个"有关。
**回执**：`NpcCastingTests` **14/14**（时机三位 + 打断正反 + 位移正反 + 概率/倍率/AOE/射程）；插件两模块 0 错。整仓最近一次全量 **3912 / 0 失败 / 8 跳过**（3920）。

### 11.75 执行 `023`/`024` 与真库验收（2026-10-27，用户批准后）

**执行**（`cmd //c "sqlcmd -S localhost -E -d Game -f 65001 -i …"`）：
- `023_npc_gossip_text.sql` → `023 OK`；`COL_LENGTH('…NpcGossipOption','text')=400`（nvarchar(200)）；
- `024_npc_skill_mount.sql` → `024 OK`；`OBJECT_ID=1483152329`，`sys.columns` 26 列（与 §11.72 闸门的下限一致）。

**真库往返**（NPC 400001，先 `get` 存档再改）：
| 步 | 回执 |
|---|---|
| 填 1 条挂载 + 1 条台词 → `save` | `NpcSkillMount:1`、`NpcGossipText:2`（**不再是 −1**） |
| `get` 读回 | 挂载 20 字段逐格一致；台词 `LENGTH=8` 且 `eq=1`（草稿与库内值逐字相同） |
| `export-content --drafts` | `skill_mounts=1`（默认只导已发布，400001 是未发布 NPC，必须 `--drafts`） |
| 已发布 NPC 1 临时填台词 → `export-visuals` | `gossipOptions[0].text` 逐字相同；随后清空、重新发布、导出恢复为空 |
| 还原 400001（清空挂载/台词后 `save`） | 库里 `mounts=0`、`nonempty_texts=0`、`published=5`、`gossip_rows=7` |

**本轮抓到的两个真问题**：
1. **清空挂载保存后旧行不删**：`WriteSkillMounts` 原来 `if (rows.Count == 0) return 0;` 排在整组 `DELETE` 之前——
   作者在面板上清空全部挂载再保存，库里旧行永远留着（与"删掉的刷怪点仍在库里"同族）。真库实测：`save` 回执 0 行、库里仍有 1 行。
   修法：`HasTable` 守卫后**先删后判空**；`NpcEditorContractTests.SkillMountSchemaInsertAndReadListsAgree` 增加"删除必须在空列表分支之前"的源码判据。
   （第一版判据被我自己写在方法里的注释 `rows.Count == 0` 误伤——判据锚点要与注释措辞分开，改注释后绿。）
2. **保存会把已发布 NPC 打回草稿**（本轮验收实测）→ **2026-10-27 已拍板并实现：保存保持发布态**。
   `NpcStore.Save` 不再写 `now.PublishState = 0`；已有路线也不再被顺手打回草稿（新建模板/新建路线仍从草稿开始，`publish` 依旧是显式动作）。
   契约 `SaveKeepsPublishState` 钉住（禁止 `now.PublishState = 0;`、`path.PublishState = 0;` 只允许出现在"新建路线"一处）。
   **真库验证**：NPC 2 保存前后 `publish_state` 都是 1；流水线复跑 `rev=51 … PIPELINE OK`。
3. **编辑器的作息提交把台词组写死 0**：`CommitUiToDraft` 里 `GossipGroupId = 0`，而这一列面板没露——
   任何一次保存都会把"白天/夜里两套菜单"清掉（库里随包值 1 就是这样变成 0 的，本轮漂移里那条即此）。
   修法：按"起始秒"认同一档、沿用 `priorSchedule?.GossipGroupId ?? 0`；契约新增 `ScheduleCommitKeepsGossipGroup`（且禁止 `GossipGroupId = 0,` 老写法回来）。
   **注意**：只修了"以后不再丢"；库里已被清成 0 的那一条**没有**擅自改回（随包文件仍记 1），留待内容归属方决定以哪边为准。

**未跑流水线（`Tools/npc-pipeline.py`）的原因**：导出与随包产物存在**并行会话留下的漂移**——
`lootBindings` 库 4 条（500001/1/2/3）vs 随包 1 条；`templates.serviceFlags` 库 127 vs 随包 121；
`scheduleRows.gossipGroupId` 库 0 vs 随包 1。这些都**在我们本轮保存之前**就是库里的值（`npc1_before.json` 可证），
跑流水线会把它们写进 `db_content.json`/`client_visuals.json`（其中 gossipGroupId 1→0 是**内容损失**），故本轮不改随包产物，
留待与并行会话核对后再跑。**同源契约与 NPC 子集仍绿**（产物文件未被本轮改动）。

**回执**：NPC 子集 **305 通过 / 0 失败**（含新增的"先删后判空"与"掉落只认敌对"守卫）；`023`/`024` 均已入库。

### 11.76 以随包/种子为准修数据 + 重跑流水线（2026-10-27）

**数据修复**（都是"库被改成与随包/种子不一致"，逐条按出处回填）：
| 项 | 库现值 → 回填 | 依据 |
|---|---|---|
| `Game_HunduShijie_NpcScheduleSlot.gossip_group_id`（npc 1 两行） | 0 → **1** | 随包 `db_content.json`（两条作息都是 1）；`@@ROWCOUNT=2`，复核 1/1 |
| `Game_HunduShijie_NpcTemplate.service_flags`（npc 1） | 127 → **121** | `scripts/sql/022_seed_npc_templates.sql` 种子行（同时是 `NpcVisuals_…` 契约的期望值） |

**发现并修复的内容污染（id 空间共用）**：掉落绑定与怪物共用 `MonsterLootBinding`，owner 放 npc_id（D4）。
怪物种子占了同一批整数 id ⇒ 友好 NPC（铁匠/守门人/行商）的导出里带上了 6/6/5 件"掉落"，而保存友好 NPC 又会按同一
`monster_id` 删掉怪物那几行。修法两向都守：
- **读**：`LoadSnapshot` 先算 `hostileIds`（`Reaction == NpcStance.Hostile`），只导敌对 NPC 的绑定；
- **写**：`canWriteLoot = t.Reaction == Hostile`，友好 NPC 保存时既不删也不插（怪物行原样不动）。
契约 `NpcLootOnlyForHostileNpcs` 钉住这两处。实验中的探针行按 `binding_id` 精确删除并核对：monster 1–11 + 500001 共 12 条、孤儿条目 0。

**流水线复跑**（`python Tools/npc-pipeline.py`）：
- 第 1 跳（get→save→publish→get 逐格）：5 只全部一致；
- 第 2 跳：`rev=49 templates=5 spawns=5 … gossip_rows=7 schedule_rows=5 loot_bindings=1 skill_mounts=0 problems=0`；
  `client_visuals.json` 5 条；作息台词组 1/1、2、3、4；服务位 121/7/9/9/0；
- 第 3 跳（随包世界用例）：全绿，**`PIPELINE OK`**。

### 11.77 PIE 像素验收（2026-10-27，编辑器在跑、未重启）

**取证方式**（按 `flax-pie-runtime-diagnostics` 流程）：`sim_status` 确认不在 PIE → 建自测标记 `auto-login.on` + `enter-world.on`
→ `sim_play(mode=game)` → 25 秒后 `sim_status` 显示 `World.scene` 已加载 → 取证 → `sim_stop` → **删标记**（收尾已清，`%APPDATA%/HundunWorld` 无 `.on`）。

| 证据 | 内容 |
|---|---|
| 3D 画面（`game_screenshot`） | 玩家与 5 只 NPC 的占位人形（美术未做，已知）；GUI 不在其中 |
| 窗口截图（`.compile-temp/window_capture.ps1`，PrintWindow 含 GUI） | 右侧多块**绿色名牌 + 血条**、灰色 `杂役`/`赵问剑 Lv.1`、**品红 `?`**、右上**黄色 `!`**；HUD（罗盘、技能栏、聊天、单位框）正常 |
| 运行时控件树（`ui_tree includeRuntime`） | 3 块画布；`160×16 + 160×18` 的名牌标签对 **12 组**，全部 `visible:true`，绝对位置集中在 (632–741, 136–157) 一带 |
| Actor 坐标（`actor_find "Npc_"`） | 5 只 NPC 在 **(6..18, 0, −2..4)** 一簇（与随包刷点一致） |

**结论**：名牌/立场色/头顶符号在真实 PIE 里**可见且随内容渲染**——A13 的像素这一半补上了。
**新发现（内容/布局，不是代码）**：5 只 NPC 的刷点挤在约 12 米半径内，名牌在出生视角**完全重叠、文字不可读**，
玩家也很难点中具体某一只。建议把刷点拉开到彼此 4–6 米以上（改内容即可，不动代码）。
**验证上限**：本轮没有驱动游戏内 UI 点击（`ui_*` 系列点不到运行时 Ink 控件，MCP 也代替不了登录以外的操作）；
"对话→商店/训练/制作/交任务"的点击链仍需人工走一遍。

### 11.78 完整门禁（2026-11-01）

| 门 | 命令 | 回执 |
|---|---|---|
| 整仓 | `dotnet test … -p:BaseOutputPath=bin/VerifyGate/` | **4073 通过 / 1 失败 / 8 跳过**（4082）；唯一红 `TcpTransport_5Clients…` 平均 **20.999ms/tick** 顶 16.7ms，**隔离复跑 1/1 绿**（负载噪声，非 NPC） |
| 插件两模块 | `bash HundunWorld/Plugins/NpcEditor/_verify.sh` | `[rt] exit=0 errors=0` / `[ed] exit=0 errors=0` / `VERIFY DONE` |
| 内容四跳 | `python Tools/npc-pipeline.py` | `rev=53 … loot_bindings=1 skill_mounts=0 problems=0` → **`PIPELINE OK`**（exit=0） |
| 发号 | `allocate-id --segment service/hostile` | `400002` / `500002` |
| 库结构 | `OBJECT_ID` / `COL_LENGTH` / `publish_state` 计数 | 挂载表在、`text` 列 **400 字节**、已发布 **5** 只、挂载行 0（测试后已还原） |

**说明**：唯一红是既有的计时阈值测试（同一测试在 10-27、11-01 多轮全量里偶发，隔离恒绿）；本轮未改任何与其相关的代码。

### 11.79 拉开 NPC 刷点（§11.77 发现的落实，2026-11-01）

**动机**：§11.77 的像素证据显示 5 只 NPC 挤在 12×10 米内，名牌完全重叠、文字不可读、也难以点选。

**改动**（改库 → `publish` → 流水线导出，回退快照在 `bin/npc-accept/spawn_before/<id>.json`）：

| NPC | 旧 (x, 深度) | 新 (x, 深度) |
|---|---|---|
| 1 铸剑师 | (6, 0) | **(4, 0)** |
| 2 守门人 | (10, 2) | **(12, 10)** |
| 3 游方商队 | (8, -2) | **(8, -6)**（贴住 1 号路线 8..13 × −5..2） |
| 400001 杂货商 | (12, 4) | **(20, 8)** |
| 500001 山贼头目（敌对） | (18, -6) | **(26, -10)**（远离商队，6 m → 19 m） |

**效果（离线核算）**：最近一对间距 **2.0 → 7.2 米**，最远 24.4 米；流水线 `rev=58 … PIPELINE OK`。
**副作用（如实记录）**：`publish` 会按任务表重建"发布/交付"倒排索引，随包 `questLinks` 由 0 变 **2**——这是发布流程的既有行为，不是刷点改动带来的。

**运行态未验（阻塞）**：PIE 里 NPC 仍在旧坐标——客户端的数据来自**改动前启动**的 `Silo/DsHost/Game.Gateway`
（`Horizon.Orleans.Silo.exe` 9776 / `Horizon.DsHost.exe` 18036 / `Horizon.Game.Gateway.exe` 29180）。
这些进程不是我启动的，未擅自重启；**需要重启这套栈（或授权）后重抓一次名牌像素**。
在此之前随时可用 `spawn_before` 快照回退。

**重启与复验（用户授权"重启全部四个进程"后）**：
- 优雅停止（`taskkill` 不带 `/F`，四个进程都自行退出）后按序重启：
  Silo **7236**（等 `:30000` LISTENING）→ DsHost **25776** → Game.Gateway **16676**（`:7789` LISTENING）→ IM.Gateway **30004**；
- Gateway 日志：`NPC 内容已装配：npc_content_loaded rev=58 … quest_links=2 … problems=0`；
- PIE 复验（自测标记 → `sim_play(game)` → 取证 → `sim_stop` → 删标记）：
  `actor_find "Npc_"` 的位置已从旧的 **(6/10/16/18)** 变为 **(4 / 8.4 / 18 / 26)**，证明**新刷点已进入运行态**；
  运行时树仍有 12 组名牌标签且全部可见，但**出生视角下仍叠在一起**——相机基本沿 NPC 排列方向看，深度上的拉开在屏幕上退化成同一条线；
- 结论：刷点在**世界坐标上已生效**；若要屏幕上也可读，需要沿"相机横向轴"再拉开（或加顶部偏移阶梯/美术高度差），这是下一步内容调优。

### 11.80 `catalog` 改读库：新发布的 NPC 在编辑器里"看得到编号、读不到数据"（2026-11-01）

**现象**：新加的 `400002 游方医仙`（`publish_state=1`）在 NPC 编辑器和任务编辑器里都读不到。
**复现**：`get 400002` 有完整数据；`catalog` 只回 **3 只 legacy**（1/2/3），`400001/400002/500001` 全缺。

**根因**：`catalog` 取的是 `NpcTemplateCatalog.RegisteredTemplates()` —— **硬编码 1/2/3 + 本进程已装配内容**的并集。
CLI 每次都是新进程、从不装配内容 ⇒ 新建/发布的 NPC 永远进不了列表；两个编辑器（NPC 与任务）都以 `catalog` 为列表源，所以一起"读不到"。

**修法**：
- `NpcStore.ListTemplates()`：直接从库读（含草稿），返回 `npcId/name/title/modelKey/wuxingDomain/publishState`；
- `catalog` 先查库，**库不可用时才退回静态表**并把原因写进 `warnings`（避免"库连不上"与"没有 NPC"同形）；
- 返回里新增 `publishState`（客户端忽略即兼容）；
- **任务编辑器是另一半**：它跑的是 `Horizon.QuestCli catalog`（`QuestEditor.QuestCli` 起子进程），
  `catalog.npcs` 同样取自静态表，且 `QuestStore.Npc(templateId)` 也用静态判存在 ⇒ 新 NPC 在下拉里消失、校验报 Missing。
  改法：`QuestStore.AllNpcTemplates()`（读库）+ `Npc()` 改查 `NpcIds()`（库 id 集合，进程内缓存一次）；`RunCatalog` 的 `npcs` 改为库数据并带 `publishState`。

**验证**：
- `Horizon.NpcCli catalog` → **6 只**（1/2/3、400001、400002、500001），`warnings` 为空；`get 400002` 数据完整；
- `Horizon.QuestCli catalog` → `npcs` **6 只**，含 `400002`（`publishState=1`）。
**防回归**：
① 契约 `CatalogReadsFromStore` + `QuestEditorCatalogReadsNpcsFromStore`（禁止退回只读静态表）；
② 流水线第 2 步新增**两处目录对账**：`catalog` 与 `QuestCli npcs` 都必须覆盖随包每一只 ——
本次输出 `随包 6 只全部在 catalog 中（目录共 6 只）` 与 `任务编辑器目录对账：随包 6 只全部在 QuestCli npcs 中（共 6 只）`。
**顺带发现的随包契约变更**：400002 已发布但**没有刷点**（`spawns=5`）。原先"5 只模板"的断言随之更新为
`templates=6 / spawns=5`（允许"先发布后摆点"），并补上它在立场表里的样貌：`services=71`（声明 127 被剪到 71，`svc_pruned=3`）。

**回执**：两个 CLI 目录均 6 只；流水线 `rev=64 templates=6 spawns=5 … quest_links=4 svc_pruned=3 problems=0` → **`PIPELINE OK`**；
随包世界/契约 **42/42**；NPC 子集 **309/309**。
**编辑器侧无需重编**：NPC 编辑器每次现起 `Horizon.NpcCli.exe`、任务编辑器现起 `Horizon.QuestCli.exe`（已重编）；
若窗口里仍显示旧列表，点一次「刷新目录」即可。

### 11.81 补 400002 刷点 + 修复"接不了/交不了任务"（2026-11-01）

**① 补刷点**：`400002 游方医仙` 原先已发布但没有刷点（`spawns=5`）。新增 `spawn_point_id=12`，位置 `(0, 10)`、yaw 180、`maxAlive=1`、60 秒刷新，
保存后 `publish 400002` 把刷点一起发布（刷点行 `publish_state` 从 0 → 1）。

**② 任务入口的真实缺口（两层）**：
1. **全仓没有任何 `TurnInQuest` 对话选项**（`gossip_rows` 里 actionKind 只有 0/1/3）⇒ 玩家点开 NPC 菜单**看不到"交付任务"**；
2. **NPC 1 挂着任务 10001 的发布/交付链接，但服务位 121 里没有 `QuestGiver(2)`/`QuestTurnIn(4)`**；
   400002 挂着 90006 的链接，但它的刷点/对话/任务定义三方也对不上（90006 的发布者写的是 NPC 2）。

**修法（内容 + 判据）**：
| 项 | 改动 |
|---|---|
| NPC 1（铸剑师） | 服务位 `121 → 127`（补 QuestGiver+QuestTurnIn）；组 1 加 `seq3 action=4`（交付） |
| NPC 2（守门人） | 组 2 补 `talk(0)` + `turnin(4)` 两行（原先一行对话都没有，菜单是空的） |
| 400002（医者） | `gossipGroupId 0 → 5`；组 5 加 `talk` + `turnin`；**刷点 12** 见上 |
| 任务 90006 | 发布者 `npc2 → 400002`（与 NPC 编辑器里的链接一致；交付默认沿用发布者），`publish` 后 `contentVersion=2` |
| 编译期判据 | `NpcContentSnapshot` 编译时校验：链接 `role=1` 必须有 `QuestGiver` 位、`role=2` 必须有 `QuestTurnIn` 位，否则报 `questlink_giver_without_service` / `questlink_turnin_without_service` |

**门禁**：`NpcServiceAvailabilityTests.任务链接缺少对应服务位要报出来`（缺位报、位齐不报）；
随包契约：`gossipKinds(1)=[0,1,3,4]`、`gossipKinds(2)=[0,4]`、服务位 `1→127 / 400002→127`、`spawns=6 / spawned=6`。

**回执**：流水线 `rev=69 templates=6 spawns=6 … gossip_rows=12 quest_links=4 problems=0` → **`PIPELINE OK`**；随包/契约 **43/43**；
服务栈已重启（Silo 3216 / DsHost 11416 / Game.Gateway 22524 / IM.Gateway 31304），Gateway 日志确认 `rev=69`。
**待人工确认**：游戏内点 NPC → 交付选项/接取面板；任务 90006 仍带前置 90001–90005，未完成时只会显示"条件不足"。

### 11.82 商人货架"打不开 / 买不了"：消息路由根因（2026-11-01）

**现象**：NPC 卖的货在售卖 UI 里找不到、也买不了。
**取证（网关日志）**：`无 handler 匹配消息。MessageType=Shop, ServiceType=0`（11:54、12:51 两次，两个客户端）。
**根因**：`ShopMessages.cs` 六条消息都把服务类型写成了**错名属性** `public ServiceType Service {...} = Trade`；
`INetworkMessage` 的 `ServiceType` 实际映射到 **`MessageUnion` 基类**那个（默认 0）。
网关路由按 **Body 的 `Type`/`ServiceType`** 匹配处理器（`CanHandle`），于是 Shop 请求的 ServiceType=0 ≠ `ShopHandler` 的 Trade ⇒ **整类请求被丢弃**，
客户端自然"打开商店没反应、货架空"。原契约测试只断言了 `[NetworkMessage]` 特性，没断言实例字段，所以一直是绿的。

**修法**：
- 六条消息各加构造器把**基类**值设对：`base.Type = MessageType.Shop; base.ServiceType = ServiceType.Trade;`（不改序列化布局，历史影子字段保留）；
- 契约测试补"实例的 `INetworkMessage.Type/ServiceType`"断言（**特性对 ≠ 路由对**）。

**验证**：`ShopMessageContractTests` + `ShopHandlerExecutionTests` **12/12**；Gateway/DsHost 已重编重启
（`Horizon.Game.Message.dll` 13:19；Gateway PID 29304、DsHost 2788）。
**客户端**：PIE 运行时加载的是 `HundunWorld/Binaries/GameEditorTarget/.../Horizon.Game.Message.dll`，已用仓库新 DLL 覆盖（旧件备份 `.verify-bak`）；
**已在跑的 PIE 仍持旧程序集，必须重启 PIE 才生效**。

**经济系统关键环节普查（同日）**：
| 环节 | 现状 |
|---|---|
| 钱包 | `WalletCore` 拒 0 金额/余额不足/溢出/未登记货币；`CurrencyGrain` 落库 |
| 记账 | 每次变动上报 `IEconomyMonitorGrain` 的 mint/burn；但该 Grain **只在内存**（Silo 重启清零）、**无 CLI/UI 入口**读 `GetReportAsync` |
| 商人 | 价格 `覆盖 > 模板价 > 拒绝`；库存 `0=不限 / >0 定量+补货`；买/卖失败有补偿 |
| 物品流通 | 背包/制作消耗/掉落产出/卖给商人回收；**卖出的货不回货架**（直接销毁，无记录） |
| 通货平衡 | 无"当前货币总量/通胀率"计算面；金币产出/消耗是静态数值，没有按等级曲线配平 |
| 玩家间交易 | `RecordTradeAsync` 存在但**零调用**（无交易/拍卖） |
| 内容闸门 | 物品表 `buy_price=0` 全靠商人 override；没有"价格>0、sell≤buy、StockMax 合理"的保存校验 |
| 掉落闭环 | 地面拾取（怪物侧）未做，物品入包路径还不完整 |

### 11.83 经济 P0：账本可读、价格有闸门、开买卖可对账（2026-11-01）

**① 账本入口（新 CLI）**：`Horizon.EconomyCli`（`report [--top N] [--currency 1]` / `character <id>`）。
数据源是 `CurrencyEntity`（`CurrencyGrain` 用 GameStore 持久化的同一份：余额 / 累计获得 / 累计消耗），
**不读内存里的 `IEconomyMonitorGrain`**（那是异常检测，Silo 重启清零，不能当账本）。
每次 `report` 都做两类对账并打印 ASCII 自证行：
`economy_report currency=1 accounts=… supply=… earned=… spent=… net=… negative_accounts=… rows_off_book=… supply_matches_ledger=…`；
判据：**每行 余额 == earned − spent**，且 **总供给 == 总净额**。今天真库 `accounts=0`（还没有玩家赚过钱），如实返回 0。

**② 价格/库存闸门**（内容侧，保存与校验都挡）：
- 编译期结构码：`vendor_bad_item` / `vendor_bad_stock:{…}:{值}` / `vendor_bad_restock:…` / `vendor_bad_price:…` / `vendor_sell_above_buy:{sell}>{buy}`（两头都给正价时）；
- 真库码（`validate`/`save`，问 `GameEntityContext.ItemTemplates`）：`vendor_unknown_item` / `vendor_no_buy_price`（无 override 且模板价 ≤0）/ `vendor_sell_above_buy`（解析后的价）；
- 真库实测：正常 NPC `problemCount=0`；把一条 700002 的覆盖价清 0 → `vendor_no_buy_price:400001:700002`。

**③ 开/买/卖逐笔日志**（`ShopHandler`，网关日志可直接对账）：
`[SHOP] open char=… npc=… items=… gold=…`、
`[SHOP] buy char=… npc=… item=… qty=… unit=… total=… gold=Before->After remaining=…`、
`[SHOP] buy_reject … reason=out_of_stock|not_enough_gold …`、
`[SHOP] sell char=… … gold=Before->After`、`[SHOP] sell_reject … reason=not_enough_items`。

**门禁/回执**：`NpcServiceAvailabilityTests`+`ShopMessageContractTests`+`ShopHandlerExecutionTests` **21/21**；
流水线 `rev=71 … vendor_rows=12 … problems=0` → **`PIPELINE OK`**；服务栈按序重启（Silo 20316 / DsHost 28756 / Game.Gateway 20740 / IM.Gateway 21812），
（期间发现上一轮 Silo 已静默退出、Gateway 在重试，已一并拉起。）

**未做（P1/P2）**：逐笔流水表（现在只有"累计 + 每笔日志"）、货币总量周期快照/通胀率、
出售回货架口径、玩家交易/拍卖、按等级曲线配平、地面拾取。

### 11.84 经济 P1：流水表 + 周期快照/通胀（2026-11-01）

**① 逐笔流水表**：`scripts/sql/025_economy_ledger.sql`（已执行）建
`Game_HunduShijie_EconomyLedger`（id/IsValid/character_id/currency_type/delta/balance_after/reason/created_at + 基类列，两个索引）。
实体 `EconomyLedgerEntity`（`[EntityStorage("Game")]`，自动进 `IDataContext` 注册）；
`CurrencyGrain.ApplyAsync` 在**钱包落库之后**追加一行：产出为 `+amount`、消耗为 `-amount`，带 `balance_after` 与 `reason`
（`npc_buy:…` / `npc_sell:…` / `quest_reward:…`）。写流水失败**不回滚钱包**、但记 `LogError`（"少一笔"必须可查）；
上下文可空（单测/降级装配仍能跑）。真库探针插入/读取/删除已验证（探针行已清，`left_rows=0`）。

**② 周期快照 + 通胀**：`EconomySnapshotService`（Silo 托管）首拍延后 1 分钟、之后每 15 分钟，
把金币的 `supply/earned/spent/accounts/rows_off_book` 追加到 `Game_HunduShijie_EconomySnapshot`。
真库已落首拍：`id=1 currency=1 supply=0 earned=0 spent=0 accounts=0 rows_off_book=0`（当前还没人赚过钱）。
`EconomyCli report` 增加 `snapshot_age_min=` 与 `inflation_pct=`（**只有一拍时回 `na`**，不拿 0 冒充通胀）。

**③ 出售回货架口径（拍板，不改行为）**：卖给商人的物品**销毁、不回货架**——理由是"回货架"会和 `stock_max` 的
"限量+补货"语义打架，且同价买回会形成无风险套利。金币一侧记 `mint`（流水 `npc_sell:…`），物品一侧是 sink；
若要回购窗口（WoW 式"最近卖出"），需要新表 + 协议字段，另立切片。

**④ 仍未做（阻塞已钉清楚）**：
| 项 | 阻塞 |
|---|---|
| 玩家交易 / 拍卖 | 消息 union tag 已耗尽（商店占 250–255），客户端也没有交易 UI；需要"退役 tag 或扩既有报文"的协议切片 |
| 地面拾取 | 属怪物侧掉落窗口切片（同一 tag 约束），不在 NPC 编辑器的代码面 |
| 按等级曲线配平 | 需要产品给目标（每级金币产出/消耗、时长基线）；当前掉落/奖励都是静态数值，先有目标才能配 |

**门禁/回执**：经济相关用例 **28/28**、NPC+商店+钱包大子集 **288/288**；
四进程重启（Silo 19632 / DsHost 31680 / Game.Gateway 26252 / IM.Gateway 22172）；
`EconomyCli report` 实测 `snapshot_age_min=1.8 inflation_pct=na`。
**注意**：新表走 SQL 建表 + 属性映射，**没有加 EF 迁移**（避免卷进并行会话实体）；将来若要 `dotnet ef` 生成迁移，
需把这两张表一并纳入快照，否则快照会缺表。

### 11.85 经济表的 EF 迁移（2026-11-01，§11.84 的收尾）

**做法（因为当前模型有大量并行漂移，不能直接用 `migrations add` 的产物）**：
1. `migrations add` 的差分里同时卷进了 **PetInstance 的在途列变更**（`owner_user_id → owner_character_id`）以及大量未迁移实体的建表；
2. 因此把目标快照定为 **HEAD 快照 + 两张经济表**（手工插入两个 `modelBuilder.Entity` 块，3 个 `HasIndex`），
   **宠物等在途实体保持"待迁移"**（责任归各自会话）；
3. 迁移文件**手写** `20261101070000_AddEconomyLedgerAndSnapshot`：Up 仅 2 个 `CreateTable` + 3 个 `CreateIndex`，Down 仅 2 个 `DropTable`；
   Designer 由新快照**同源转换**（同一份 model body，只换类头），不是另写一份。
   生成/收尾脚本：`Tools/_finalize_economy_migration.py`（含"经济块逐字进入 Designer""PetInstance 不得进入快照""迁移只碰经济表"三条断言）。

**落地**：`dotnet ef database update` 在本仓库**走不通**——设计时工厂 `GameEntityContextDes.OnConfiguring` 直接读 `repository.json` 里
**未展开的** `${DB_GAME_CONNECTION_STRING}`（`--connection` 也会触发旧迁移重放，实测撞 `IX_User_AccountName` 重复键后中止，库未变更）。
改用与仓库既有习惯一致的**建表脚本 + 迁移历史基线**：
① 执行 `scripts/sql/025_economy_ledger.sql` 建表；② 把索引名改成 EF 迁移里声明的名字
（`IX_Game_HunduShijie_EconomyLedger_CharacterId_CreatedAt` 等）；③ 往 `__EFMigrationsHistory` 写入
`20261101070000_AddEconomyLedgerAndSnapshot / 10.0.8`（幂等 IF NOT EXISTS）。

**验证**：
- `dotnet ef migrations script 20261026160704_AddNpcEditorTables 20261101070000_AddEconomyLedgerAndSnapshot` 输出**只有**两张经济表 + 三个索引；
- `migrations list` 中该迁移**无 `(Pending)`**；`__EFMigrationsHistory` 首行为它；
- 四进程重启后 `EconomySnapshotService` 再次落行（`id=1 … accounts=0`），`EconomyCli report` 读得到 `snapshot_age_min`；
- 经济相关用例 **28/28**。

**遗留（值得单独修）**：设计时连接串占位符没有展开，导致本仓库无法用 `dotnet ef database update` 直接迁移——
正确修法是让 `GameEntityContextDes` 用 `EnvironmentPlaceholderResolver` 展开 `repository.json`，这是独立小切面。

**已修（2026-11-01 续）**：新增 `Horizon.Entities/DesignTimeConnection.cs`——把 `repository.json` 里的 `${VAR}`
按"环境变量 → 就近 `.env`（cwd/仓库根/Silo·Gateway 的 bin）"展开；三个设计时工厂入口（两个上下文的 `OnConfiguring`/`CreateDbContext`）
统一走它。**验证**：`dotnet ef migrations list --context GameEntityContextDes` 不再报"无法判定已应用状态"，本迁移无 `(Pending)`。
`dotnet ef database update` 现在会明确拒绝——`PendingModelChangesWarning`（宠物等并行实体尚未迁移），
这是 EF 10 的安全行为，不再出现"重放旧迁移"；经济表通过 025 + 历史基线落地。
**打断用例的第二个坑**：第一版还断言"可打断 ⇒ 之后不该在近战距离外掉血"——实测 `damageAtRange=True@4.4m` 是**打断后按 1.5 秒退避重试**又放了一发，
不是"没打断"。判据收窄到"请求后两拍还在不在吟唱"，并把这个机制写进注释，避免下次又把它当缺陷。
（上一轮加 AOE 用例时整仓曾 2 红——`DSRegistryMaintenanceHostedServiceTests.AliveCount…` 与 `MultiClientSyncPerformanceTests.LongRunning_StaticEntities…`，
两条都不在 NPC 切片、单跑全绿，属并发/静态注册表噪声；本轮全量复跑已全绿。）
两条红都**不在 NPC 切片**且**单独复跑均绿**：`DSRegistryMaintenanceHostedServiceTests.AliveCount_TracksNewRegistrations`（10/10，静态注册表被并发测试抬高到 2）、
`MultiClientSyncPerformanceTests.LongRunning_StaticEntities_RemainRegisteredAndReceiveHeartbeat`（1/1）；
与 §11.72 记录的同类噪声一致，如实保留而不是抹掉。

---

## 12. 完成度审计（2026-10-26，对着 §1 的 A1–A14 逐条）

| # | 原话 | 状态 | 证据 | 缺口 |
|---|---|---|---|---|
| A1 | 编号 | ✅ | `NpcTemplate.npc_id` + 号段（旧 1/2/3 / 400000 服务 / 500000 敌对）；`NpcCli catalog` 输出 `segment`；**号段闸门**（§11.27：号段外拒绝装载 + 号段与立场矛盾拒绝，11 个边界值断言）；**发号入口**（§11.39：`allocate-id` + 编辑器「新建服务/敌对」，最大+1 不复用空号） | 无 |
| A2 | 制作技能 | ✅ | `NpcTrainerEntry` + 训练师收费通路（服务端 `SkillHandler` 三分支 + 客户端 `TrainerClientService`）；**§11.17 训练师窗口**（导出 `trainerRows` → 技能页念师承、点习得前判"这位教不教"并报学费）；**库往返逐格对账**覆盖教学行（§11.32）；配方行显示配方真名（§11.38） | 未目视 |
| A3 | 行为 | ✅ | `NpcBehaviorCore`（0–7 档 + 增援全序）；敌对型走怪物管线真生成；友好型按 `ai_bits.Wanders` 真踱步（`NpcIdleWanderTests` 快照坐标断言）；**路线**（`NpcRouteTests` 30 米线路 >10m）、**作息切路线**、**姿态随作息增量下发**（§11.15）、**刷新时间由内容表给**（§11.23：配 2s→19 拍，配 0→149 拍）、**等级覆盖与朝向档**（§11.30：level=42 / yaw 0、1、2 三档实测） | 速度列可编辑，逃跑（§11.64）与追击/回位（§11.66）都按跑速 | 姿态只到目标框一行字（没有坐/打铁动画集） |
| A4 | 外观 | ◑ | 模板外观列 + 客户端立场色 + `export-visuals`（stance/services/prefab）；**身份包带 `model_key`/体型/档位/头衔**（§11.21）；**NPC 逐只预制体分流**（§11.40：编号优先解析、跳过人形兜底、NPC 也吃缩放）；**只配 GUID 也能进表**（§11.53：模型键只是分组，不是身份） | **没有 NPC 美术资产**（五只仍占位人形；指认 GUID 即生效，不需改代码） |
| A5 | AI 智能程度 | ✅(七格) | 档位内核 + CLI；运行期读者：`Wanders`、`DefendsSelf/Pursues`（§11.24）、**`FacePlayer`**（§11.25）、**`FleesAtLowHealth`**（§11.42）、**`CallsReinforcement`**（§11.54）、**`Casts`**（§11.69/§11.74：v1 适配 + 复用内核，`NpcCastingTests` 14 条含正反夹具） | `Casts` 与 `SpeaksGossip` 的表/列**均已执行并真库验收**（§11.75）——不再是缺口；受控位仍等价战斗位（全仓无 CC 输入源，标签与用例已钉住） |
| A6 | 发布任务（含链） | ◑→✅ | giver/turn-in 反查表；头顶 `!`/`?` 按每只 NPC 的清单（§11.15）；交付 NPC 校验（§11.18）；**接取发布者校验**（§11.52：报文带实体、身份对号、四类拒绝；**执行面 6 条**：四类拒绝都不落库、正确发布者放行、自由发布者不受影响）；**编辑器任务链图**（§11.19）+ **描述与目标行**（§11.47）；**客户端符号语义 9 条**（§11.60：与内核逐组合等价） | 全局指示器受 8m **交互**采信半径限制（显示不受限）；`giver_kind` 非 NPC 的发布者暂不限制；**交付指示已按 NPC 对号、灰 `!` 可达**（§11.65：网关从已有目录算 `TurnInHere`/`LockedOfferCount`，不改 Grain 接口；客户端按 giver 缓存） |
| A7 | 售卖物品 | ◑→✅(服务端执行面) | 服务端全链（`ShopHandler` + 库存 Grain + 钱包）+ `MenuShopPage` 接真数据；**执行面 5 条**（§11.55：开窗价/剩余来自货架、买按"钱→库存→背包"并回执服务端价、背包满钱与库存双退、卖先扣物后给钱且不动货架、非商人位零副作用 + 同夹具正对照） | 客户端点击链与目视未验 |
| A8 | 技能学习 | ◑→✅(展示+双通道执行面) | NPC 收费通路（先扣后退）+ **训练师窗口**（教什么/多少钱，见 §11.17）；**学配方只能学这位教的、并按教学行收学费/材料、失败原路退回**（§11.51）；**学技能六格执行面**（§11.56：不教/无位/扣费落库/门槛退款/材料不齐退款/钱不够） | 未目视 |
| A9 | 制作装备/物品 | ✅(数据+服务端+双执行面) | `CraftingGrain` 真判据 + `CraftHandler`（学/清单/做）+ 制作页重建卡片与真按钮；**配方落库与插件编辑**（§11.33：真跑往返，材料/曲线原序、教学行学费 800）；**设施身份对号**（§11.51）；**制作动作 7 格执行面**（§11.57：站错设施零副作用、站对按快照扣料收工费发成品、材料/工费/背包三条失败各自补偿、判定失败无副作用、未绑设施随处可做） | 客户端点击与目视未验；配方/路线/刷点均可切行编辑（§11.46），刷点可删（§11.68）；路线/配方无删除入口（跨 NPC 共享，属退役流程） |
| A10 | 自动寻路 | ✅(路线+作息+可编辑+试算) | NPC 沿内容路线走（`NpcRouteTests` 30m >10m；**随包内容一整天实测行商离点 5m**，§11.34）；作息按时段切路线；路线可经 CLI/库/插件编辑并落库（§11.28/§11.29）；**路线试算**（§11.48：循环米数/秒数/五类问题，顺序与停留问巡逻内核、阈值与它同尺） | **真导航图来源未接**（32×32 fixture，段间直线；怪物侧 NavMesh 切片在做） |
| A11 | 掉落与几率 | ✅(服务器侧) | NPC 掉落绑定随内容装配（复用怪物两表，owner 放 npc_id）；019 执行后 022 掉落段真正落库（**绑定 1 + 条目 2**，`export-content` → `loot_bindings=1`）；**击杀端到端**：`NpcKillLootTests` 实测 `kill=1 loot=1 money=1 items=[700002]`；**条件三路存活 + 两级面板可配**（§11.45：读库/写库/插件保存；逐件与绑定级下拉，含真库改条件再回滚的对账） | 客户端的**地表面**与拾取窗口未接（只到跨层事件；怪物侧切片在做） |
| A12 | 贡献经验（PVP/敌对，随等级衰减） | ◑→✅ | `NpcKillCore` 分派 + 复用 `MonsterXpCore`；敌对 NPC 真生成（`HostileNpcIdentityTests`）；D13 阵营矩阵在战斗入口被查询；**PvP 真结算**（§11.20：伤害/死亡/复活/荣誉经验，3 条运行期断言）；**衰减曲线的形状门**（§11.44：逐级不回升 + 灰名边界现算，PvP 同链同数）；**击杀归属转移**（§11.62：`kill_credit_npc_id` 首次有读者，任务击杀与域好感记到 credit 目标，掉落/金币仍属尸体）；**等级浮动**（§11.62：基准±浮动按刷点掷，同点稳定） | 建角 UI 无选阵营入口（`faction` 由库列给，默认 0）；未目视 |
| A13 | 玩家视觉的实际表现 | ✅(像素+名牌已取证；点击链未验) | 立场色、`!`/`?`、对话菜单、商店、制作、技能页、姿态行、头衔行均已接；**运行态像素证据**（§11.77，2026-10-27）：窗口截图里绿色名牌+血条、灰色副标题、品红 `?`、黄色 `!` 同时可见，运行时树 12 组名牌标签全部 `visible:true`；`actor_find` 5 只 NPC 在 (6..18, 0, −2..4) | **点击链路**（对话→商店/训练/制作/交任务）仍未验；5 只 NPC 刷点过近导致名牌重叠（内容问题，建议拉开 4–6 米） |
| A14 | 编辑器插件 | ✅ | `Plugins/NpcEditor` **九面板**（掉落、任务链、刷点·路线）+ **AI 覆盖逐位勾选**（§11.36）+ 配方编辑 + **新建发号**（§11.39）+ **多行按条合并**（§11.43）+ **行切换/新增**（§11.46）+ **刷点行删除**（§11.68）+ **逐件掉落条件输入**（§11.45）+ **服务入口预览**（§11.67）+ **技能挂载四行（保尾不丢行，§11.71）** + CLI 桥（20 个动词）+ `_verify.sh` 两模块 0 错 | 未目视（编辑器未起）；路线/配方无删除入口（跨 NPC 共享，属退役流程）；技能挂载第 5 条之后无法在面板删除（§11.71 边界） |

**横切缺口（比单条更关键，2026-10-27 更新）**：
1. **目视已破零，但只到"名牌/身份"这一层** —— 21901 桥在线、并行会话跑起 PIE（三场景加载），
   只读 `ui_dump` 取到五只随包 NPC 的名牌（名字/头衔/等级，§11.50 补记），友好 NPC 的 Actor 坐标在两次采样间变化（路线在走）。
   仍缺：像素截图、点击链路（对话菜单→商店/训练/制作/交任务），以及**新 `GameTarget` 装配**（当前 PIE 用的是旧运行期脚本，
   我的 `Npc_` 命名/预制体分流/体型缩放不在画面里）。
2. **服务器侧端到端已通**（§11.31：随包产物 → 世界 5 只在位），**客户端侧端到端**（商店/制作/训练真点一遍）仍未做；
3. **掉落的地表面/拾取窗口未做** —— 服务器侧的掷骰与跨层事件已通（§11.26），客户端只负责入包，地上没有可点的那一份；
   新消息受 union tag 0–255 用尽 + 132 墓碑的硬约束，要做得复用既有实体通道或退役某个 tag；
4. **建角选阵营**与开场文案"入门由你日后自行抉择"一致地留到玩法层，D13 的 `faction` 目前由库列给（默认 0）——
   PvP 判据已就绪，但"谁入正派/邪派"还没有玩法入口；
5. **`FleesAtLowHealth` 已落地（§11.42）** —— 首次接线被并行会话整文件还原两次，按纪律撤回；
   对方在 14:33 后停写，本轮重接后在 14:57 落盘并观察 7 分钟无覆盖，判据/行为/正对照都进了测试。
   教训：**先看目标文件与同族文件最近写入时间**，再决定"改还是等"；写完不要立刻宣称完成，先观察再记账。

### 12.1 复跑清单（每条轴一条命令 + 应看到的回执）

| 轴 | 命令 | 应看到 |
|---|---|---|
| NPC 全部用例 | `dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj -p:BaseOutputPath=bin/VerifyNpc/ --filter "FullyQualifiedName~Npc|FullyQualifiedName~QuestAcceptGateHandlerTests|FullyQualifiedName~CraftLearnGateHandlerTests|FullyQualifiedName~CraftActionHandlerTests|FullyQualifiedName~ShopHandlerExecutionTests|FullyQualifiedName~SkillLearnGateHandlerTests"` | **289 通过 / 0 失败**（含任务/学配方/做东西/商店/技能五条执行面 + 台词正文两条 + 服务入口可见性七条 + 入口预览三条 + 头顶任务符号十二条 + 击杀归属/等级浮动四条 + 台词组四条 + 速度四条 + 指示器服务端九条 + 刷点删除一条 + **NPC 施法五条**（含晚进场补发）+ **编辑器合并十四条**；宿主在跑也能跑：旁路输出，不动进程） |
| 整仓回归 | 同上去掉 `--filter` | 2026-11-01 完整门禁：**4073 通过 / 1 失败 / 8 跳过**（4082）。唯一红是 `TcpTransportSyncTests.TcpTransport_5Clients_SyncStableUnderRealNetwork`（平均 **20.999ms/tick** 顶 16.7ms 帧预算），**隔离复跑 1/1 绿** ⇒ 构建期负载噪声，不在 NPC 切片。数字随并行会话增减，**别让两条测试命令共用同一个旁路目录** |
| 内容四跳 | `python Tools/npc-pipeline.py` | 2026-11-01 门禁：往返 5 只逐格一致 → `rev=53 templates=5 spawns=5 vendor_rows=12 trainer_rows=3 recipes=1 gossip_rows=7 schedule_rows=5 loot_bindings=1 skill_mounts=0 problems=0` → 随包世界用例全绿 → `PIPELINE OK` |
| 发号 | `Horizon.NpcCli allocate-id --segment service|hostile` | `400002` / `500002`；`legacy` 明确拒绝（§11.39） |
| 产物同源 | 契约类里的 `ClientVisuals_IsExactExportOfShippedContent` | 两份随包产物逐字节相等（只有换行归一）；改一格即红（§11.41） |
| 路线试算 | `Horizon.NpcCli preview-route - --route-index 0 < HundunWorld/Data/Npcs/dev_draft_peddler.json` | 2026-10-27 实测：`循环 35 米 / 26.9 秒 / closed=1 / 问题 0`（2 点、停留 5 秒）；`--speed 0` ⇒ `cycleSeconds=-1`（§11.48 那一轮的回执，口径未改） |
| 链条文本 | `Horizon.NpcCli quest-chain 2` | 六个节点都带 `description` 与 `objectives`（如 `击杀… ×3`，§11.47） |
| 插件编译 | `bash HundunWorld/Plugins/NpcEditor/_verify.sh` | `[rt] exit=0 errors=0` / `[ed] exit=0 errors=0` / `VERIFY DONE` |
| 客户端编译 | `dotnet exec <sdk>/Roslyn/bincore/csc.dll @…/GameTarget/…/Development/Game.CSharp.response` | 0 error（response 里缺新文件时先补路径；**别把 `Game.Build.cs` 当编译单元塞进去**） |
| 目视/像素 | 自测标记 `auto-login.on` + `enter-world.on` → `sim_play(mode=game)` → `game_screenshot`（仅 3D）+ `.compile-temp/window_capture.ps1`（PrintWindow 含 GUI）→ `ui_tree includeRuntime` → **收尾删标记** | 5 只 NPC 名牌/血条/`?`/`!` 同屏（§11.77）；刷点过近导致名牌重叠。点 PIE 前确认 `sim_status.isPlayMode`，别把"没进 PIE"当页面坏 |

### 12.2 剩余缺口与归属（2026-10-27 审计时点）

| 缺口 | 现状 | 归属/前置 |
|---|---|---|
| 掉落**地表面/拾取窗口** | 服务器侧掷骰 + 跨层事件已通（§11.26/§11.45）；地面可点的那一份没有 | 怪物侧切片（"掉落按 AC 尸体拾取窗口形状做"，消息 union tag 已耗尽，要复用实体通道或退役 tag） |
| `Casts`（NPC 施法） | ✅ **已执行 024 并真库验收**（§11.75）：表 26 列、挂载保存/读回逐格一致、`export-content --drafts` 回执 `skill_mounts=1`、`NpcCastingTests` 14 条 | 无（编辑器四行未目视） |
| `SpeaksGossip`（台词） | ✅ **已执行 023 并真库验收**（§11.75）：`text` 列宽 400 字节、保存不再回 −1、台词逐字往返、`export-visuals` 出现 `text` | 无（编辑器台词框未目视） |
| **保存会把已发布 NPC 打回草稿** | 本轮验收时实测：`save` 后 `publish_state=0`，NPC 从发布导出里消失（已用 `publish` 恢复） | 需要产品口径：保存保持发布态，或编辑器显式提示"保存后需重新发布"（**行为未改**） |

> 注：`Casts`/`SpeaksGossip` 的随包产物**尚未**重新导出——库与随包文件存在并行会话留下的漂移（§11.75 末段），跑流水线会把 `scheduleRows.gossipGroupId` 的 1 覆盖成 0。待与并行会话核对后再跑。
| `CallsReinforcement`（求援） | ✅ 已落地（§11.54） | 被打唤醒同族；测试含"自己仇恨半径外的同伙才会被叫醒"的正反两侧 |
| 交付指示错位（黄 `?` 亮在发布者头上） | ✅ 已修（§11.65）：网关从已有目录算 `TurnInHere`/`LockedOfferCount`，客户端按 giver 缓存交付者集合；灰 `!` 同时可达 | 不改 Grain 接口；两个追加字段对旧客户端"忽略即兼容" |
| 作息换台词组（白天/夜里两套菜单） | 静态默认组已生效（§11.63）；**按作息切换**还没通 | 要把"当前组"随每个快照下发：`EntityStateAuthComponent` 追加 `NpcGossipGroup`（网络契约），客户端与 DS/Gateway **同批重启**；本机有并行会话在跑 PIE，未获批前不碰 |
| `FollowsPath` 位 | 位本身无运行期读者——**设计如此**：路线由刷点的 `route_id` 列驱动（随包行商是 Wanderer 档，不含这一位，仍沿路线走） | 文档口径；若要改成"位控制路线"，须同时改随包档位/夹具，属单独一轮 |
| 建角/入派选阵营 | D13 阵营矩阵与 PvP 结算已通；`faction` 由库列给 | 需要玩法入口（新消息 tag 已耗尽 → 只能扩既有报文） |
| NPC 美术资产 | 代码侧分流已通（GUID 即生效） | 美术/资产生产 |
| 视觉点击链路 | 像素已取证（§11.77：名牌/血条/`?`/`!` 同屏可见，运行时树 12 组名牌可见）；名牌重叠源于刷点过近 | 点击链（对话→商店/训练/制作/交任务）需人工走一遍（MCP 驱动不了运行时 Ink 控件）；建议同时把 5 个刷点拉开 4–6 米 |
| 真导航图 | 服务器侧直线连段 + 路线试算（§11.48） | 怪物侧 NavMesh 切片；NPC 复用即可 |

**结论**：上述每一项都不是"代码还没写"，而是**外部输入/协议前置/并行会话切片**；在它们落地前，本目标的完成度只能停在"除上述项外全部可用并有回执"。

### 12.3 完成度审计复跑（2026-10-27，对着原话逐条取新证据）

| 原话条款 | 交付物 / 入口 | 本轮复核证据 | 状态 |
|---|---|---|---|
| 为 NPC 编号 | `NpcIdSegmentCore` + `allocate-id` | 真库只读真跑：service → `400002`、hostile → `500002`；legacy 段明确拒绝 | ✅ |
| 制定技能（教/学） | `NpcTrainerEntry` + `SkillHandler` / `CraftHandler` | 学技能执行面 6 条、学配方执行面 4 条（扣费/退款/材料/门槛） | ✅ |
| 制定技能（NPC 自身施法 `Casts`） | `NpcSkillMountDto` + `NpcContent.SkillMounts` + v1 施法适配（§11.69）；补发口与结算口同一挂载出口（§11.70） | **024 已执行**：表 26 列；填/读 20 字段逐格一致；`export-content --drafts` = `skill_mounts=1`；`NpcCastingTests` **14 条**（概率/倍率/AOE/射程/时机三位/打断/位移/晚进场） | ✅ |
| 行为 | Wanders / 路线 / 作息 / 逃跑 / 求援 / 面向 | `NpcIdleWanderTests`、`NpcRouteTests`、`NpcShippedScheduleTests`（一整天 2400 拍）、`NpcFleeTests`、`NpcReinforcementTests`、`NpcAiTierRuntimeTests` | ✅ |
| 外观 | ModelKey / PrefabGUID / Scale / Rank / Title | `export-visuals` + `NpcStanceCatalog.TryGetPrefab` + 契约（GUID 即生效）；随包五只仍占位人形 | ✅(代码) / 美术缺 |
| AI 智能程度 | 9 个能力位 | **7 位有运行期读者**（Wanders/DefendsSelf/Pursues/FacePlayer/Flees/CallsReinforcement/Casts）；`FollowsPath` 由刷点路线列驱动、`SpeaksGossip` 由台词行驱动（位本身装饰）；`Casts` 内容源与执行链见 §11.69（表待执行） | ✅(代码) |
| 发布任务（含任务链跨 NPC） | `QuestHandler` 四类拒绝 + 链图 + 指示器 | 发布者/交付者校验 6+6 条；`quest-chain 2` 真跑 = 6 节点 / 5 边 / 5 根 / 0 问题；指示器服务端 9 条 + 客户端 12 条（与内核逐组合等价） | ✅ |
| 售卖物品 | `ShopHandler` + 库存 Grain + 钱包 | 执行面 5 条（价格/库存/背包失败双退/卖先扣物）；库存内核 5 条；报文契约 | ✅(服务端) |
| 制作装备/物品 | `CraftHandler` + `CraftingGrain` | 学配方 4 条 + 制作动作 7 条（设施对号、四条补偿） | ✅ |
| 自动寻路（参照 Flax/NarrativePro） | 内容路线 + 作息 + 试算 | `NpcRouteTests` 30m 线路；`preview-route` 真跑（2026-10-27 复审，夹具 `dev_draft_peddler.json`）= 循环 35 米 / 26.9 秒 / closed / 问题 0 | ✅(路线) / 真 navmesh 缺 |
| 掉落物品及几率 | 绑定 + 逐件条件 + 击杀事件 | `NpcKillLootTests` 端到端 `kill=1 loot=1 money=1`；条件三路（读库/写库/插件）；击杀归属转移 §11.62 | ✅(服务端) / 地面拾取缺 |
| 死亡贡献经验（敌对/PVP，等级反比） | `NpcKillCore` + 让分曲线 | 衰减形状门（逐级不回升 + 灰名边界现算）、PvP 真结算 3 条、`KillCredit` 执行面 1 条 | ✅ |
| 玩家视觉的实际表现 | 名牌/立场/头衔/姿态/菜单/指示器 | 运行态名牌树证据（§11.50，五只随包 NPC 全命中）；入口可见性/台词组/符号语义有离线门；**像素与点击链未验** | ◑ |
| 编辑器插件 | 9 面板 + 20 个 CLI 动词 + 预览 + 发号 + 多行合并/切行/删除 + 入口预览 | `_verify.sh` 两模块 0 错；契约/Kernel/模型测试 30+；本次新增入口预览与刷点删除 | ✅(代码) / 未目视 |

**审计结论**：逐条状态——**7 条完全落地**（编号、技能教/学、行为、发布任务含链、制作、经验、AI 位代码侧含 `Casts`）；
**4 条代码侧落地但外部缺一块**（外观缺美术、售卖缺点击目视、寻路缺真导航图、掉落缺地面拾取）；
**1 条未完成**（玩家视觉的**点击链路**——像素已于 §11.77 取证；`Casts` 的库表已执行并验收，§11.75）；`受控位`无 CC 输入源（已钉住现状）；**编辑器**代码侧 ✅、面板未目视。
剩余缺口**全部是外部前置**（SQL 授权、023/024 执行、网络契约同批重启、怪物侧切片、美术与 PIE 重建），
因此本目标**不能标记完成**；下一次可推进的前提是：①用户批准 `024`（NPC 技能挂载表）与/或执行 `023`；
②并行会话交出 `GameTarget` 并重启 PIE；③怪物侧 NavMesh/地面拾取切片落地。

### 12.4 复审回执（2026-10-27，全部当场重跑，不沿用旧数）

| 检查 | 命令 | 今天看到的 |
|---|---|---|
| 发号 | `allocate-id --segment service/hostile/legacy` | `400002` / `500002` / legacy 明确拒绝（exit=1） |
| 任务链 | `quest-chain 2` | `chain_nodes=6 chain_edges=5 chain_roots=5 chain_problems=0`，每个节点带描述与目标行 |
| 路线试算 | `preview-route - --route-index 0 < HundunWorld/Data/Npcs/dev_draft_peddler.json` | `循环 35 米 / 26.9 秒 / closed=true / warnings 0` |
| 服务入口预览 | `preview-services - --player 30 < 同一草稿` | `可见入口 5 个（生效位图 0x79）`，逐位带 `visible/reason` |
| 草稿校验 | `validate - < 同一草稿` | `problemCount=0`，回执含 `skill_mounts=0 svc_pruned=0 problems=0` |
| 执行前 schema | `SkillMountSchemaInsertAndReadListsAgree` / `GossipTextSchemaMatchesGuardAndLimit` | 两条绿（§11.72/§11.73），证伪各红过一次 |
| 编辑器保尾 | `NpcDraftModelMergeTests` 28 条 + 源码守卫 | 绿；守卫覆盖 6 个集合 + 10 处合并调用 |

**一并更正**：§12.1 的路线试算行原先引用 `draft3.json`——**盘上没有这个文件**，属于"数字没有出处"；
现改为仓库内真实夹具 `HundunWorld/Data/Npcs/dev_draft_peddler.json`，数字换成今天的实测值（35 米 / 26.9 秒）。
**结论不变**：逐条状态与 §12.3 相同，目标仍不满足完成条件（外部前置未解）。

---

## 13. 待执行的 schema 切片（已生成、未执行）

### 13.1 `SpeaksGossip`（台词文本）——**已执行 023，验收通过**

| 步 | 内容 | 现状 |
|---|---|---|
| 1 | `scripts/sql/023_npc_gossip_text.sql`：给 `Game_HunduShijie_NpcGossipOption` 加 `text nvarchar(200) NOT NULL DEFAULT ''`（带扩展属性与两条自检：列存在、现值无 NULL） | **已执行（2026-10-27，回执 `023 OK`）**；`COL_LENGTH=400` 字节 = nvarchar(200) |
| 2 | 实体 `Text` 列 → EF 迁移 + 模型快照 | **刻意不做**：改走 `COL_LENGTH` 守卫 + 参数化原始 SQL（见 §11.58）——EF 实体一旦加列，缺列库上的所有查询会一起挂 |
| 3 | `NpcStore` 读/写 → `NpcGossipRowDto.Text` → `export-visuals` 的 `gossipOptions` → `NpcStanceCatalog` → `NpcGossipPage` → 插件台词框 → `preview-gossip` | **已写**（§11.58）；**有列路径已实跑** |
| 4 | 判据：非空才写键、空串合成标签、200/201 边界、保存不清空台词 | 前三条已绿；**"保存不清空"已验**（见下） |

**执行后五步的实际回执（2026-10-27）**：
1. `sqlcmd -i scripts/sql/023_npc_gossip_text.sql` → `023 OK`，`COL_LENGTH('…NpcGossipOption','text') = 400`；
2. `get 400001` → `save -`：回执 `NpcGossipText: 2`（**不再是 −1**，守卫走"有列"分支）；
3. 填空 `验收台词·023` → `get` 读回**逐字相同**（草稿与库里 `LENGTH=8`、`[text]=N'验收台词·023'` 比较 `eq=1`）；
4. `export-visuals`：已发布 NPC 的 `gossipOptions[0].text` **逐字相同**（临时写在 NPC 1 上验证后已清空并重新发布）；`export-content` 回执 `problems=0`；
5. 流水线**本轮未跑**（原因见 §11.75：库与随包产物存在并行会话留下的漂移，跑它会把漂移写进随包文件）。

### 13.2 `Casts`（NPC 施法）——**已执行 024，验收通过**

1. **技能来源**：**(a) 新建 `NpcSkillMount`**；(b) 复用 `MonsterSkillMount`（owner 放 npc_id）——**2026-10-27 复核后不再推荐 (b)**：
   `Horizon.MonsterCli/MonsterStore.cs:446-447` 的 `Upsert` 对 `MonsterSkillMount` 是"按 `monster_id` 先删光再重插"，
   `:675` 的删除路径同样按 `monster_id` 整段清；NPC 与怪物在实体注册表里**共用同一整数 id 空间**——
   同一张表两方都按 `monster_id` 清理，谁保存谁就把对方那一份抹掉（与 §11.45 的"保存一次改掉条件"同形，
   而且这次是跨编辑器）。另 `MonsterStore` 导出会把**全表**按 monster_id 分组进怪物内容，npc_id 的行会混进去。
   结论：**必须新表**（或给现有表加 owner_kind 列并让两侧清理都带上它），两条都要 SQL 迁移。
   **已按"新表"生成脚本**：`scripts/sql/024_npc_skill_mount.sql`（`Game_HunduShijie_NpcSkillMount`，
   逐列镜像怪物侧挂载表 + 基类列，带扩展属性与四条自检；**未执行**）。
   本轮用 `SET PARSEONLY ON` 经 stdin 跑过：语法通过、且复核 `OBJECT_ID` 仍为 absent（确实没建表）。
2. **运行时**：(a) 把敌对 NPC 接进 **v2**（`TryDecideMonsterV2` 的唯一内容入口是 `MonsterContent.TryGetConfig`，
   需要为 NPC 合成/装配一份 config）；(b) 在 v1 上另写最小施法。**推荐 (a)**，但切换会让勾了 `Casts` 的 NPC
   整体换一套 AI，须用 flee/leash/求援三组夹具回归（这些目前钉的是 v1 通路）。
3. **缺数据时的口径**：勾了 `Casts` 但没配技能 = 只声明不报错（维持现状）还是装载期拒绝（更严）。推荐维持现状，与 `FleesAtLowHealth` 的"位是能力、数据是内容"一致。

**落地顺序（2026-10-27 更新：全部完成）**：
1. 执行 `024` → **已执行**（`024 OK`；`OBJECT_ID=1483152329`，26 列）；
2. ~~实体 `NpcSkillMountEntity` + 迁移/快照~~ → **刻意不做**：与 023 同口径，`HasTable` 守卫 + 参数化原始 SQL；
3. `NpcStore` 读写 + 导出 → **已写**（回执 `WrittenRows["NpcSkillMount"]`、导出 `skill_mounts=`）；
4. ~~v2 config 装配~~ → **v1 适配**（同一伤害出口，不换 AI 档）；
5. 编辑器「技能挂载」四行 → **已写**；6. 施法断言 → **`NpcCastingTests` 14 条**（见 §11.74）。

**执行后验收五步的实际回执（2026-10-27）**：
① 表存在、26 列、脚本四条自检无 THROW；
② `NpcStore` 填一条挂载（skill 1001）→ `get` 读回 **20 个字段逐格一致**（`WrittenRows["NpcSkillMount"]=1`）；
③ `export-content --drafts` 回执 `skill_mounts=1`（默认只导已发布内容，故需 `--drafts` 才看到未发布 NPC 的挂载）；
④ 快照字段与晚进场补发已由 `NpcCastingTests` 5/14 两条覆盖；
⑤ 勾 `Casts` 的敌对 NPC 真起手并造成伤害——离线夹具已证（近战距离外 15 点伤害）。
