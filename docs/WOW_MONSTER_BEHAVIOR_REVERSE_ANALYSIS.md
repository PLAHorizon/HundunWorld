# 魔兽世界怪物行为逆向分析与本工程落地规格

> 状态：v1 逆向分析（供 `docs/MONSTER_EDITOR_PLUGIN_DESIGN.md` 当规格输入）
> 方法：**只从玩家可观测的表现反推内部模型**——名牌颜色/血条/施法条/巡逻轨迹/脱战回位/掉落面板/经验数字，
> 每一项都对应一个可写进数据表的字段与一个可写进确定性内核的判据。
> 单位口径：本工程服务端为 **米、Z-up（水平面 = XY）**（见 `Horizon.Game.Sim/MonsterAiCore.cs` 的 `MonsterAiSelf` 注释）；
> 客户端 Flax 为 Y-up，跨端位置要换轴（见记忆 [跨坐标系3D位置同步需Y/Z轴交换]）。
> 外部参考：[Experience point - Warcraft Wiki](https://warcraft.wiki.gg/wiki/Experience_point)、
> [Mob difficulty colors - WoWWiki](https://wowwiki-archive.fandom.com/wiki/Mob_difficulty_colors)、
> TrinityCore `worldserver.conf` 的 CREATURE SETTINGS 段（`ThreatRadius` 默认 60、`Rate.Creature.Aggro` 默认 1、
> `CreatureFamilyFleeAssistanceRadius` 默认 30、`CreatureFamilyAssistanceRadius` 默认 10）。

---

## 0. 一句话结论

WoW 的怪物不是一套"AI"，而是 **四张正交的数据 + 一条结算链**：

| 层 | WoW 里叫什么 | 玩家看到什么 | 我们对应的位置 |
|---|---|---|---|
| 模板层 | creature template（编号 = entry） | 名字、等级、模型、种族/类型、是否精英 | `MonsterTemplateCatalog` → 新表 `Game_HunduShijie_MonsterTemplate` |
| 行为层 | AI flags + aggro/detect/meanlevel + path | 主动还是被动、追多远、巡逻路线、脱战回位 | `MonsterAiCore`（现为直线追击，需扩） |
| 技能层 | creature_spell + spell_school + chance/timer | 施法条、范围伤害、打断窗口、冷却 | 新增 `MonsterSkillCore` + 施法状态 |
| 结算层 | loot + xp + respawn | 掉落面板、经验飘字、复活节奏 | `LootTableGrain` / 新增击杀经验链 / `SpawnDirectorCore` |

**逆向下来的最重要一条**：这四层在 WoW 里彼此**不互相引用具体单位**，全靠"编号 + 概率 + 计时器"松耦合。
所以编辑器能做出来——因为存在一个"给编号填字段"的动作，而不需要写代码。

---

## 1. 怪物编号（entry）与模板分层

### 1.1 观测

- 同一只怪在任何玩家眼里长得一模一样、名字一样、等级一样 → 存在**模板**，实例只是模板的 spawn。
- 同一模板可以出现在多个位置且等级不同（如某怪在 A 处 Lv10、在 B 处显示 Lv12）→
  **等级可以属于 spawn 点，而不是模板**（WoW 用 `minlevel/maxlevel` + `difficulty` 三级模板变体：普通/英雄/史诗）。
- 精英怪 = **同模型 + 名字前缀（卫士/队长/领主）+ 金/黄名 + 龙形血条框 + 血量放大**，不是新模型。
  → 精英是**修饰符**，不是新条目。
- 世界 BOSS 有独立刷新窗口与全服通告 → 有 `spawn` 组与刷新时刻概念。

### 1.2 逆向出的模型

```
编号(entry)  ──1:1──  模板(名字/模型/种族/五行域/基础等级/基础属性系数)
     │
     ├──1:N──  精英变体(前缀, HpScale, DamageScale, XpScale, LootScale)
     ├──1:N──  刷怪点(位置, 等级覆盖, 数量, 刷新秒, 巡逻路线, leash)
     └──1:N──  技能挂载(技能id, 释放概率, 冷却, 目标数, 施法时间覆盖)
```

### 1.3 本工程落点

- 现状：`MonsterTemplateInfo(TemplateId, Name, Level, ModelKey, WuxingDomain, IsWorldBoss)`，id 1–11 硬编码在
  `Horizon.Game.Sim/MonsterTemplateCatalog.cs:54`。**数值（HP/攻击/速度）反而挂在刷怪点上**
  （`SpawnPointConfig.MaxHp/AttackPower/MoveSpeed`，`Horizon.Game.Sim/SpawnDirectorCore.cs:58`）。
- 这是 WoW 的对偶错位：WoW 把数值放模板（按等级公式），把位置放 spawn。
  → 目标：**模板持有"等级→属性"的公式与系数，刷怪点只保留位置/数量/刷新/路线/等级覆盖**。
- 编号规则必须显式（现在只有 1–11 且靠 `1 + (i % 5)` 轮换）。逆向 WoW 的"分段编号"落到我们这里：

| 号段 | 含义 | 依据 |
|---|---|---|
| 1 – 99999 | 普通野怪 | 沿用既有 1–11，不许改号（白名单与显示断言按它们写过，见 `MonsterTemplateCatalog.cs:49`） |
| 100000 – 199999 | 精英/稀有精英 | 与技能书 800000+、材料 700001+ 的"号段即身份"传统一致 |
| 200000 – 299999 | 世界 BOSS | 沿用 6–11 的 BOSS 标记，新号走新段 |
| 300000+ | 副本怪 | 预留 |

> 注意：这条号段规则**必须**与 `ScriptAuthorityCore.TryGetRegistered` 的白名单授权共存——白名单是"能不能生成"，号段是"生成的是哪一类"。

---

## 2. 等级、难度色与"反比例衰减"的经验

### 2.1 观测

选一个怪，名字/等级数字的颜色**只随"我"的等级变**，不随血量、位置、组队变。
换一个小号看同一只怪，颜色就变。→ 颜色是 **(怪等级 − 我等级)** 的纯函数。这就是"concession"（让分）表。

| 颜色 | 判据（怪等级 ML，我等级 CL） |
|---|---|
| 骷髅 | `ML ≥ CL + 10`（或等级未知/世界 BOSS） |
| 红 | `ML ≥ CL + 5` |
| 橙 | `CL + 3 ≤ ML ≤ CL + 4` |
| 黄 | `CL − 2 ≤ ML ≤ CL + 2` |
| 绿 | `GML < ML ≤ CL − 3` |
| 灰 | `ML ≤ GML`（击杀**零经验**） |

灰名等级 `GML`（让分门槛）：

```
CL ∈ [0, 5]   → GML = 0        （没有灰怪）
CL ∈ [6, 39]  → GML = CL − (floor(CL/10) + 5)
CL ∈ [40, 59] → GML = CL − (floor(CL/5)  + 1)
CL ≥ 60       → GML = CL − 9
```

### 2.2 观测：经验数字

- 同级怪：经验 ≈ `45 + 5 × CL`（旧世界）；外域 ≈ `235 + 5 × CL`。
- 打**高于**我的怪：`×(1 + 0.05 × (ML − CL))`，但 **差值封顶 4**（打高 10 级也只按高 4 级算）→ 上限 1.2 倍。
- 打**低于**我的怪：`×(1 − (CL − ML) / ZD)`，ZD = 零差值系数（下表）。到 `ML ≤ GML` 时系数为 0。
- 精英怪经验 ×2（普通精英），首领 ×4（团队本）。

| CL | 1–7 | 8–9 | 10–11 | 12–15 | 16–19 | 20–29 | 30–39 | 40–44 | 45–49 | 50–54 | 55–59 | 60+ |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ZD | 5 | 6 | 7 | 8 | 9 | 11 | 12 | 13 | 14 | 15 | 16 | 17 |

### 2.3 逆向要点：这是"反比例衰减"的确切形状

用户目标里写"随玩家等级变化反比例衰减"。WoW 的形状不是 `1/level`，而是**线性衰减到零再断供**：

```
XP(ML, CL) = BXP(CL)                       BXP(CL) = ExpBase + ExpPerLevel × CL
             × HighGapBonus(ML − CL)        = 1 + ExpUpRate × min(ML − CL, ExpUpCap)   [ML > CL]
             × LowGapDecay(ML − CL)         = max(0, 1 − (CL − ML) / ZD(CL))           [ML < CL]
             × (ML ≤ GML(CL) ? 0 : 1)
             × EliteScale × PartyScale × RestScale
```

这条链的每一段都必须**可编辑**（编辑器要能改 `ExpBase / ExpPerLevel / ExpUpRate / ExpUpCap / ZD 表 / GML 分段 / EliteScale`），
因为"衰减快慢"是唯一的平衡旋钮。

### 2.4 观测：组队与"谁拿到经验"

- 只要**在死前对怪造成过伤害/治疗/仇恨**（WoW 的 tag 窗口 ≈ 伤害后 10 秒内、且在怪 175 码内）就分到经验。
- 队伍经验按**等级加权**分配（等级高的人分得多），并且**人数越多总池越大**（WoW: 5 人队的总经验 > 单人，但人均少于单人）。
- 只有一人拿到"击杀"（最后一击）的名额，但经验是共享的 → 玩家视觉上"我摸到了就有"。

### 2.5 本工程落点

- 现状：**完全没有击杀经验链路**。`DsWorldSession.HandleMonsterDeath`（`Horizon.Game.Core/Sim/Server/DsWorldSession.cs:1025`）
  只发 `DeathPacket` + `DsGameplayEventKind.Kill`；`OrleansGameplayWiring` 的 `Kill` 分支只做任务计数与分域好感。
- 等级曲线已有：`Horizon.Orleans.Grains/ProgressionCurve.cs`（`MaxLevel=300`，`ExpToNext=100+25×(lv−1)`）。
  注意我们的曲线**到 300 级**，WoW 的 ZD/GML 分段只到 60+，需要外推一档（见设计文档 D7）。
- 需要一个"参与者集合"：现在 `Kill` 事件只带 `killerCharacterId` 一个值 → 无法分经验。

---

## 3. 感知与索敌（aggro）

### 3.1 观测

- 走进一个圈怪就动，圈的**半径因怪而异**（有些要贴脸，有些半个屏幕外就来）。
- 背后贴着它不一定会醒，正面对视更敏感 → **朝向锥**（`aggro` 半径 × 视野角度）。
- 潜行/隐身能穿过某些怪 → 有**检测类型**（视觉/听觉/野兽/人形/元素/龙/巨人……），且各怪不同。
- 有的怪会**叫同伴**（一只醒了整窝醒）→ `assistance` 半径（TrinityCore 默认 10 米同类求助；逃跑求助 30 米）。
- 有的怪是**被动**（必须打它才还手），有的**主动**（靠近就打）。玩家一眼能看出区别 → 行为标志位。

### 3.2 逆向出的判据

```
可否索敌(S, T) = T.可攻击
             && 距离2D(S,T) ≤ AggroRadius × AggroRate
             && 夹角(S.朝向 → T, S.Yaw) ≤ FacingCone/2   或 S.无朝向约束
             && !T.隐蔽(若 S 无隐蔽探测)
             && 状态 ∈ {Idle, Patrol}                    （已接战的不受半径约束）
             && 距离出生点 ≤ LeashRadius                  （超距只回家，不接新仇）
```

WoW 的"主动/被动"落地成一位标志；"会不会叫人"落地成一位标志 + 一个半径；"看不见潜行"落地成检测类型集合。

### 3.3 本工程落点

- 现状：`MonsterAiCore.SelectThreat`（`Horizon.Game.Sim/MonsterAiCore.cs:491`）只做**圆形半径 + 最近目标**，
  没有朝向锥、没有隐蔽探测、没有同类求援、没有被动/主动之分。
- **语义正确的一部分已经有了**：`ResolveThreat` 区分"获取新目标才受半径约束，已锁定不受"
  （`MonsterAiCore.cs:454`）——这条要保留，别在扩展时把半径约束回灌到 Chase。

---

## 4. 威胁表（仇恨）

### 4.1 观测

- 一个怪被多人打时，**打得多/打得晚的人会拉走目标**；治疗者会意外 OT。
- 嘲讽技能**必定**立刻成为目标 → 嘲讽是"目标当前仇恨 × 强制下限"而不是"加固定值"。
- 打不过时，队友摸一下怪就"换目标"了，且不会立刻换回来 → **威胁切换有滞回**（需要超过当前目标一定比例）。
- 脱战一段时间后仇恨表清空。

### 4.2 逆向出的数学

```
威胁贡献(actor) = 伤害 × 1.0(伤害类型系数) + 治疗 × HealThreatRate(≈0.5) + 技能自带仇恨
                 × 职业/技能仇恨修正（坦克姿态、减仇恨技能）
目标选择        = max(威胁)，但切换需 新威胁 > 当前目标威胁 × SmartThreatMult
                  · 对近战坦克：经典为 1.3（"要超过 T 的 30% 才 OT"）
                  · 对其他玩家：1.0（等于即 OT，"smart"）
Taunt(actor)    = 威胁[actor] := max(威胁) + ε，并锁定 TauntDuration 秒（期间不可被普通 OT 抢走）
衰减            = 超时（脱战/玩家离开半径/玩家死亡）→ 移除条目；空表 → 清战斗状态
```

关键：**威胁表是"每个玩家一条记录"，不是"当前目标"一个字段**。
`MonsterAiCore` 现在只有一个 `ThreatTargetId`（`MonsterAiCore.cs:143`），永远表达不出 OT。

（旁注：`Horizon.Orleans.Grains/Combat/MonsterGrain.cs` 里已经写过 `Dictionary<ulong,int> _threatTable` +
`GetHighestThreat`，但**全仓零引用**（`Horizon.Game.Gateway.Tests/DsWorldSessionTests.cs:20` 记录了这条）。
可用它当"我们早就想过"的对照，但权威实现要放进 `Horizon.Game.Sim` 的纯函数层——Orleans Grain 里的版本无法与 DS 对拍。）

---

## 5. 移动：巡逻、追击寻路、脱战回位

### 5.1 观测

- 怪沿**固定折线**来回走，在每个点**停顿**一小段，到终点反向或绕回第一点 → waypoint 路线 + 每点停留时间。
- 有的怪**原地转/踱步**不离开出生点 → wander 半径（无路线时的退化）。
- 追击时**直线冲过来**，但会绕开树和墙；有些怪会**爬墙/跳过沟**（跟随类型不同），有些到崖边就停住回头 →
  **follow type**（步行/飞行/游泳/不移动）+ 是否使用导航网格。
- 追出一定距离后**突然放弃、跑回原位、血条瞬间满**、并且"不再理你" → leash/evade：清威胁 + 回位 + 满血 + 免疫追击。
- 回位路上被打，会**再追**，但通常很快再次放弃（"二次 leash"）。
- 怪的移动速度分**待机速度 / 战斗/追击速度**两档（巡逻慢、追你快）。

### 5.2 逆向出的状态机（比现有 6 态多出的部分）

```
Idle ──(IdleToPatrolSeconds)──→ Patrol ──(发现目标)──→ Chase
Chase ──(进入攻击射程)──→ Attack ──(目标离开射程)──→ Chase
Chase/Attack ──(距出生点 > LeashRadius)──→ Evade(回位, 清仇恨, 免疫新仇恨)
Evade ──(距出生点 < ArriveRadius)──→ Idle, Hp := MaxHp
Attack ──(有技能且条件满足)──→ Casting ──(施法完成/被打断)──→ Attack
任意 ──(Hp ≤ 0)──→ Dead ──(RespawnAtTick)──→ Idle
```

新增的两块：**Casting**（施法占用移动，可能允许移动施法）与 **Evade ≠ Leash**（回位要带"清仇恨 + 满血 + 短期免疫再仇恨"，
现有 `MonsterAiState.Leash` 只做了"往出生点走"，`MonsterAiCore.cs:390`）。

### 5.3 寻路：Flax 原生 API 与 NarrativePro 的可用性（实测事实）

- Flax **有**可用的导航 API，且已被本工程验证过：
  `Plugins/NarrativePro/Source/NarrativePro/Navigation/NavigatorStatics.cs` 用了
  `FlaxEngine.Navigation.IsBuildingNavMesh / TestPath / FindPath / FindClosestPoint / FindRandomPointAroundCircle / FindDistanceToWall`；
  烘焙在 `Source/NarrativeProEditor/Navigation/NavMeshBaker.cs`（`BuildNavMesh`）。
- 客户端玩家点击移动已经在用：`HundunWorld/Source/Game/PlayerController.cs:1193`（`IsNavigationReady → TryFindPath → ResamplePath`，失败退化为直线）。
- 已知边界（`docs/NARRATIVE_PRO_MAP_NAVIGATION_DESIGN.md`）：托管 API **没有** `NavMeshQueryFilter`/区域代价入口（NarrativePro 用自己建的 `NavCostGraph` 绕开）；
  不暴露 dtNavMesh tile/polygon；平面口径 XZ vs XY 有历史不一致（G14）。
- **权威侧没有寻路**：`MonsterAiCore` 是纯函数、无 IO、不能直接调 Flax（它跑在 Orleans/DS 两端并要求位级可复现）。
  → 寻路必须在**服务端**做（否则客户端各算各的路径，多玩家会看到不同轨迹）。可行的架构是：
  **烘焙 NavMesh 离线导出为服务端可读的图（多边形/格子），服务端 AI 走图；客户端只做表现**。
  NarrativePro 的 `NavMeshBaker` + `NavCostGraph` 正好是"烘焙 + 图"两半，可以直接借鉴其数据格式。

### 5.4 落点结论

| 能力 | WoW 里怎么表现 | 服务端可复现做法 |
|---|---|---|
| waypoint 巡逻 | 沿折线走 + 停留 | 路线数据（点列 + 每点停留秒 + 循环/往返模式）存表，AI 内核消费索引与计时 |
| wander | 出生点半径内随机踱步 | 已有 `PatrolDirection`；改成"目标点 + 到点停留"，随机源由宿主注入种子 |
| 追击寻路 | 绕障碍 | 服务端 NavMesh 图 A*；`MonsterAiDecision` 从 `DeltaX/DeltaY` 升级为"路径点 + 沿边推进" |
| 回位寻路 | 跑回原点 | 同上（回位用同一条图查询，目标 = 出生点） |
| 不可寻路怪 | 站桩/飞行怪穿墙 | `FollowType` 字段决定"用不用图"，穿墙怪退回直线 `StepTowards` |
| leash | 突然放弃跑回 | `Evade` 状态 + 免疫窗口秒 |

---

## 6. 技能施法（怪物会放技能）

### 6.1 观测

- 怪头顶有**施法条**（有始有终，可被打断；打断后它**会重试**，通常换个目标或原地再来）。
- 施法条出现前怪会**先做起手动作/喊话** → 有"start spell"事件与表现。
- 同一只怪**不是每次都放**同一技能（火怪有时喷火有时近战）→ **概率选择**（chance）+ **计时器/冷却**。
- 技能有**最小/最大射程**与**目标数**（近战单体 vs 扇形 vs 范围 AOE 打多人）。
- 有**元素系**（火/冰/自然/暗影……）；被某系打过会短暂对该系减免 → 系别锁定（school lockout）。
- 玩家用**颜色与图标**分辨技能系别 → 视觉必须把 school 传下去。

### 6.2 逆向出的数据（每张"怪物技能"行的最小闭包）

```
monsterId, skillId, slotType(战斗/非战斗),
chance(0..100), castTimeMs, cooldownMs, minRange, maxRange,
targetSelect(当前目标/随机仇恨内/最近/血最低/aoe半径),
targetCount, schoolMask, interruptible(bool), mobility(站桩/可移动施法),
globalCooldownMs, requiresState(仅战斗/仅Idle/血量低于X%), 
startEmote/sayText, effectOverrides(伤害倍率/持续秒/状态id)
```

### 6.3 落点

- 服务端技能体系已存在但**只服务玩家**：`SkillTemplateEntity` → 表 `Game_HunduShijie_SkillTemplate`；
  运行时效果 `Horizon.Game.Sim/Skills/WuxingSkillEffects.cs`（含 `EffectRole` 诚实台账）；
  五行技能树 `scripts/skilltree/wuxing_skill_tree.json`。
- **怪物施法不存在**：`Horizon.Orleans.Interface/Combat/IMonsterGrain.cs` 里声明了 `MonsterAiAction.CastSkill`
  与 `MonsterAiDecision.SkillId`，但**没有任何生产者**；现役 `MonsterAiCore.MonsterAiDecision` 只有 `WantsAttack`。
- 好消息：技能 id/效果表已经齐了，怪物技能只需一张"挂载表" + 一个施法子状态机，不必重造技能系统。

---

## 7. 掉落

### 7.1 观测

- 尸体上**右键拾取**，一个窗口里若干格，每格**独立出现**（有时掉 3 件，有时 0 件）→ **逐件独立掷骰**，不是"抽一件"
  ——**这条观测只说对了一半**（本机权威源码纠出的错，见 §13）：普通杂物与金币确实是逐件独立，
  但 `creature_loot_template` 还有一个 `GroupID`：同一组里的行**互斥**，整组一次掷骰最多出一件，
  而"必出一件"就是靠组内把概率填 0 的那些成员（`EqualChanced`）兜底实现的。
  只看玩家视觉会得出"每件各掉各的"这个过头结论，于是"从 10 件里出 1 件"这种最常见的配法写不出来。。
- 金币单独一格，是个**区间**——"与等级相关"**这半句是观测加上去的，权威实现里没有等级公式**
  （钱是 `creature_template.mingold/maxgold` 两列静态区间，在击杀那一刻掷一次，`Unit.cpp:18059`；
  高等级怪掉得多是因为**模板里填的数就大**，见 §13 的金币那两行）。
- 任务物品**必掉且只给做了任务的人**（且"拾取绑定"）→ 条件掉落。
- 组队时**每个人各自掷**（个人掉落）或 N 人 need/greed → 掉落是"按参与者"而非"按击杀者"。
  **这条也要打折**：AC 的普通怪是"尸体上只有一份掉落 + 组队按 round-robin/need-greed 分配"，
  逐人判的只有任务件；"按参与者"真正成立的是**经验**那一侧（见 §13 的组队掉落那行与设计文档 §11.2-2/-18）。
- 稀有怪（稀有精英）有**必掉的一张额外表**与**更高刷新**；BOSS 有固定掉落表 + 团队人数修正。

### 7.2 落点（这里我们**已经有相当多**，别重造）

- 表装配：`Horizon.Orleans.Grains/World/LootTableGrain.cs`（`GetConfigAsync/RollLootAsync/PityThreshold`）；
  由 `Horizon.Orleans.Silo/Startup/WuxingLootTables.cs` 从物品行装配；
  `LootTableIdOf` 现在是**从"五行域"推导**的（`MonsterTemplateCatalog.cs:87`：普通怪=域号 1..5，BOSS=100+域，未定域=0 不掉）。
- DS 侧契约：`Horizon.Game.Core/Sim/Server/DsGrainContracts.cs`（`LootTableSnapshot`/`LootEntrySnapshot`/`IMonsterLootMapping.ResolveLootTableId`）。
- 物品定义与概率字段已在库：`Horizon.Model/GameModel/ItemTemplateEntity.cs` → 表 `Game_HunduShijie_ItemTemplate`，
  **已有 `drop_rate decimal(5,4)`**；号段：装备 10001+/20001+、材料 700001–700041、技能书 800000+skillId、外观（sql 018）。
- **缺口（逆向出来的关键差异）**：现在是"**一只怪 → 一张表**"，且表 ID 由**五行域**推导，
  不是由怪本身配置。WoW 是"一只怪可挂 **N 张表**，每张表配 数量区间 + 逐件概率 + 条件"。
  → 编辑器必须把"怪→表"从**推导**改成**配置**（同时保留 `LootTableIdOf` 作为兜底，
  否则 90001/天阶那条"BOSS→100+域"的顶端链路会静默断，`MonsterTemplateCatalog.cs:22` 的注释专门警告过这点）。
- 另一条：`HundunWorld/Source/Game/Combat/LootSystem.cs`（385 行，含 pity/luck/首杀）是**零引用孤儿代码**——
  不要以它为准，也不要顺手接上（那是另一件事）。

---

## 8. 死亡、尸体与刷新

### 8.1 观测

- 死亡 → 倒地动画 + 尸体停留一段时间（可被拾取/被复活技能交互），之后**消失**（不占位、不可再点）。
- 刷新：死后**固定秒数**在原出生点重新出现；**等级越高刷新越慢**（低级的怪几秒，高级/精英几十秒到几分钟，世界 BOSS 数小时）。
- 世界 BOSS 尸体**长时间不消失**（全服围观）且刷新时刻是全服事件。
- 组（pack）内一只死了不影响别只；但"linked"怪要一起死才算清。

### 8.2 落点

- 已有：`MonsterAiCore.ComputeRespawnTick`（`MonsterAiCore.cs:423`）、`SpawnDirectorCore` 的槽位重生模型
  （`SpawnSlotState.RespawnAtTick`、`SpawnPointConfig.RespawnDelaySeconds/MaxAlive/SlotSpacing`）。
- 缺：**刷新时长的等级曲线**（现在是一个常量秒）、**尸体停留秒**（dead → 从世界移除的第二段时间）、
  **同组/linked** 概念。

---

## 9. AI 智能程度（用户目标里的"AI 智能程度"到底指什么）

WoW 里没有一个"智能评分"字段，但玩家能明确感到"这怪笨/这怪精"。逆向成**四个可独立开关的能力位**，
"智能等级"就是它们的组合（编辑器里给一个 0–3 的档位下拉，映射到位组合，并允许覆盖单项）：

| 能力位 | 关闭（笨） | 开启（精） |
|---|---|---|
| `UseThreatTable` | 只咬第一个打它的人 / 最近的人 | 按威胁表选目标，可被 OT、可被嘲讽 |
| `UseSkills` | 只普攻 | 按概率/冷却施法、选 AOE 多目标、残血放控制 |
| `UseNavigation` | 直线冲（会被树挡住） | 走 NavMesh 图，会绕障碍、会回位寻路 |
| `UseAssist` | 各打各的 | 求援同类、逃跑搬救兵、共享仇恨 |
| （可选）`UseTactics` | 恒定贴身 | 保持距离（远程怪风筝）、寻找掩体、残血逃跑 |

对应玩家观感：Lv1 野猪 = 全关；精英法师怪 = 前四项开；世界 BOSS = 全开 + 脚本阶段。

---

## 10. 外观（编辑器要能配的"长什么样"）

### 10.1 观测

- 模型（同模板同模型；精英用**更大 Scale**，常见 1.0 / 1.2 / 1.5 三档）。
- 名牌：名字颜色 = 难度色；精英加"◆/龙框"、稀有精英紫名、世界 BOSS 骷髅 + 专属血条样式与头顶等级"?? "。
- 头顶等级数字（灰/绿/黄/橙/红/骷髅/??）。
- 血条颜色 = 难度色；精英为黄色 + 龙框。
- 待机姿态：站立 / 坐姿 / 卧姿 / 潜于水下（`UnitStandState`）；脱战后坐下回血。
- 出生/巡逻/战斗/施法/死亡各有动作槽；某些怪在**索敌时喊话/做表情**（可听声效是预警，玩家靠这个学"这种怪会叫什么"）。

### 10.2 落点

- 现状：`MonsterTemplateInfo.ModelKey` 存在但**客户端没接**——
  `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` 的 `MonsterPrefabPath/MonsterPrefabGuid` **是空串**，
  怪物目前**用玩家 prefab 占位**，只有名牌与选中环区分。`Content/Models/Monsters/guai/` 里只有 3 个 `.flax` 材质，无网格无 prefab。
- 名牌颜色/等级显示：`UI/GameMain/NameplateUI.cs`（需要接入"难度色"这条纯函数）。
- 图鉴页 `UI/Ink/Pages/Bestiary/MenuBestiaryPage.cs` 是**空壳、无数据绑定**——编辑器配出来的东西正好是它的数据源。

---

## 11. 逆向结论 → 必须新增/修改的东西（供设计文档细化）

**纯函数内核层（`Horizon.Game.Sim`，两端共用、必须确定性）**
1. `MonsterDifficulty`：难度色 + GML + ZD 的**单一事实源**（名牌颜色、经验衰减、编辑器预览都调它）。
2. `MonsterXpCore`：§2.3 的完整公式（含精英/组队/GML 断供）。
3. `ThreatTable`（值类型版，宿主持有 `ulong→long` 的稳定序列表）替换单槽 `ThreatTargetId`。
4. `MonsterCastingCore`：施法子状态（选技 → 起手 → 吟唱 → 结算/打断 → 冷却）。
5. `MonsterPathCore`：把"沿路径点推进"接进 `MonsterAiDecision`（图数据由宿主喂入，内核不做 IO）。
6. `MonsterEvade`：Leash 升级为带"清威胁 + 满血 + 免疫窗口"的 Evade。
7. `MonsterAiCore.Version` 递增 + 金轨迹对拍（`Horizon.Game.Sim/GameplayGoldenTrace.cs` 已在此纪律上）。

**数据/服务端**
8. 新表：怪物模板（含等级→属性公式）、精英变体、怪物技能挂载、怪物掉落表映射、刷怪点与巡逻路线、AI 能力位。
9. 击杀经验链：`Kill` 事件要带**参与者集合**（谁摸过），落到 `CharacterGrain.AddExpAsync`。

**客户端表现**
10. 怪物 prefab 分流（填 `MonsterPrefabPath`）、难度色名牌、施法条、血条颜色。

**编辑器（本目标的主体交付）**
11. `MonsterEditor` Flax 插件（照 `QuestEditor` 约定）+ 后端 CLI/HTTP 通道 + 预览（3D 外观 + AI 行为可视化沙盘）。

---

## 12. 未决问题（留给设计文档的决策清单）

见 `docs/MONSTER_EDITOR_PLUGIN_DESIGN.md` §"决策清单"。摘要：数据权威在哪（SQL vs Grain）、
属性是"表里存数字"还是"存公式按等级算"、寻路是否要在服务端建 NavMesh 图（这是**工程量最大**的一条）、
怪物技能是复用玩家技能表还是独立表、组队经验分配口径、编辑器与后端通道的形态（子进程 CLI vs HTTP）。

---

## 13. 权威代码对照（本机 AzerothCore，2026-10-26 补）

前面各节的口径来自观测与文档转述。这台机器上有 **WotLK 权威实现的源码**，可以直接把"观测到的表现"落到
具体判据与字面数字上。复查方法（不依赖本文的转述）：

```
文件：C:/Works/AzerothCore/azerothcore-wotlk/src/server/game/Entities/Creature/Creature.cpp
函数：Creature::CanCreatureAttack(Unit const* victim, bool skipDistCheck)   （约 2583 行起）
读点：2606（复活宽限）、2630（leash / 世界BOSS / 嘲讽 三条豁免）
```

| 规则（源码字面） | 玩家可见表现 | 本作落点 |
|---|---|---|
| `if (m_respawnedTime && now - m_respawnedTime < 5 && !IsEngagedBy(victim)) return false;` | 贴着刷新点站，刚冒出来的怪不会立刻咬人；**你先打它它才还手** | 内核 v10：`MonsterRuntimeState.NoInitiateUntilTick` + `RespawnGraceSeconds = 5`。只挡两条**发起**通路（主动索敌、被同族叫起），伤害写进威胁表那一路不挡——这就是 AC 那个 `IsEngagedBy` 例外。`m_respawnedTime` 在 `Creature::Respawn()` 里刷新，而 `if (m_respawnedTime && …)` 说明**首次生成的怪不生效** ⇒ 本作也只在"这个槽位死过"时装（`DsMonster.SpawnIsRespawn`），否则服务器一开全图的怪先聋 5 秒 |
| 这三条挂在 **`Creature` 本身**，与"这只怪会不会用威胁表"无关 | 玩家看见的表现对图上每一只怪都成立——包括最笨的那种 | **作用域补齐（2026-10-26）**：本作此前把三条只长在 v2（开了能力位）的通路上，而宿主在能力位全关时根本不进 v2 ⇒ 种子目录那批 `ai_profile=0` 的怪一条都没享受过。现在判据收进 `MonsterAiGate`（`LeashApplies`/`TauntHolds`/`ExceededLeash`/`InRespawnGrace`），v1 与 v2 **共用同一份**，v1 的三份输入落在 `MonsterAiSelf`（宽限截止、嘲讽对象与截止）与 `MonsterAiConfig.Rank`（装配层从内容配置带）。v1 那边还补了 AC 这条判据自带的例外：伤害当场把攻击者写进单槽仇恨，否则"不许主动起手"会退化成"打了它它也不还手"。`MonsterAiCore.Version` 1→2，而金轨迹逐帧重放一致 ⇒ 三格输入全缺省时表现与之前**逐字相同**（涨的是作用域，不是手感） |
| 脱战回家那段的免疫是**状态**不是"秒"：`CreatureAI::EnterEvadeMode`（`AI/CreatureAI.cpp:205-233`）进 evade 时 `AddUnitState(UNIT_STATE_EVADE)` + `MoveTargetedHome`，`SmartAI::MovementInform` 到达 OOC 点位才 `ClearUnitState`（`AI/SmartScripts/SmartAI.cpp:653-657`）；窗内**不吃仇恨**（`Entities/Unit/Unit.cpp:1092` 的 `!victim->HasUnitState(UNIT_STATE_EVADE)`、`:14765` 威胁表分支）。AC **没有** `evade_immunity_seconds` 这一格，也**没有**"到家刷满血"——血是脱战后的自然回血 | 玩家看得见的是："怪在跑回家那整段路上都不会被重新拉进战斗"；"到家后血是几秒内抬上去的，不是啪一下满" | **本作换了形状并已把作用域补齐到 v1/v2 两路**：免疫 = 到家后 `EvadeImmunitySeconds`（缺省 3 秒，唯一的数字主人是 `MonsterBehaviorConfig.EvadeImmunityDefaultSeconds`），血 = 到家一次刷满。**两处已知偏离**记在设计文档 §11.2 第 13 条：① ~~窗口起点应从"到家"挪到"进 Leash/Evade 那一 tick"以覆盖整段路~~ **本轮已落**（`MonsterAiGate.ReturningHome`：v1 的 `Leash` 与 v2 的 `Evade` 都算回位中，索敌／求援／嘲讽受理三处入口都问它；`MonsterAiCore.Version` 3→4）——顺带留了一条刻意的**宽度差**：清掉"已选中的目标"只跟回位（路上或免疫窗）走，**不跟复活宽限**走，因为 AC 那条判据本身写着 `!IsEngagedBy(victim)`；② "一次刷满"若要改成按秒回血，先得有人定"脱战回血速率归谁配"——那是全局战斗层的一格，不是怪物编辑器能自己拍的 |
| "evading 的怪不打人"**也**"打不到 evading 的怪"——两侧都有门，而宽度不同。攻击方那侧：`Unit::CanAttack`（`Unit.cpp:10474`，攻击者是 evading 的 creature 就 return false）与 `Creature::CanCreatureAttack`（`Creature.cpp:2598` 自己、`:2602` 对面）。受击方那侧走的是另一个谓词 `Creature::IsEvadingAttacks()`（`Creature.h:139`，定义是 `IsInEvadeMode()` 与 `CanNotReachTarget()` 取或），一共铺在**五处**：`Unit::CanAttack:10496`（攻击指令本身）、`RollMeleeOutcomeAgainst:2846`（已经在空中的那一刀不出结果）、`DealSpellDamage:1480`、`DealMeleeDamage:1867`（伤害落地那一步）、`SpellHitResult:3529/3602`（回 `SPELL_MISS_EVADE`，屏幕上那行"闪避"就是它）；`Spell::DoSpellHitOnUnit:2983` 对**有飞行时间的**技能再补一次。三条例外只有法术侧有：`SPELL_AURA_CONTROL_VEHICLE`、`SPELL_ATTR0_CU_IGNORE_EVADE`、`SPELL_ATTR1_AURA_STAYS_AFTER_COMBAT`。而"刚复活 5 秒"那一档（`:2606`）**只在攻击方那侧**——宽限期的怪是你打得它而它不还手，不是打不到 | 怪在跑回原位的那一整段路上既不参战也打不动；玩家对着它按技能不会"没伤害数字也没解释"，而是看见一句"打不到"。刚冒出来的怪则相反：能打，它只是不还手 | **已落（2026-10-26，作用域 v1+v2）**：判据主人 `MonsterAiGate.BlocksIncomingDamage(returningHome)`，**只认回位状态**，不含复活宽限、不含到家后的免疫窗（那条式子刻意比 `BlockedFromNewEnemy` 窄，两处各有反例钉着）。接线：`DsWorldSession.SubmitCombatAction` 在 `CombatCore.Resolve` **之前**拒（`CombatOutcome.Evading`，新增枚举值尾部追加），拒的是整手——不掉血、不记贡献、不写 v1 的单槽仇恨；同一个判据另写进下行状态位 `EntityStateBits.Evading`（`MonsterStateBitsOf` 是怪物掩码的唯一主人，周期快照与晚加入补发两条通路都调它）。客户端 `FlaxActorSyncSystem` 搬到 `SelectableEntityTag.Unattackable` → 名牌名字与血条转灰、目标框两按钮一起灰、技能栏按下去念"它正在跑回原位，这一下打不到"（且挡在 `StartCooldown` 之前，不白扣冷却） |
| `CanNotReachTarget()`（`IsEvadingAttacks` 的另一半，来自移动卡死检测） | "怪卡在石头里，玩家也打不到它" | **不落**：本作服务端没有"移动卡住"这个概念（没有路径生成器的失败回执可问），凭空造一个就是发明规则。AC 那半边的来源是 `PathGenerator`/`MovementGenerator` 的可达性判定 |
| 索敌半径**不是常量**：`Creature::GetAggroRadius`（`Entities/Creature/Creature.cpp:3225-3270`）在 `GetDetectionRange()` 之上做了四件事——按等级差 `aggroRadius -= (creatureLevel - playerLevel)`（**每级约 1 码**，等级差先截到 -25 ⇒ 上限 45 码）、叠 `SPELL_AURA_MOD_DETECT_RANGE` 与 `SPELL_AURA_MOD_DETECTED_RANGE` 两类光环、封顶 `MAX_AGGRO_RADIUS`、**保底 5 码**（宠物 10 码） | 高等级号路过怪群基本不会被秒拖；低等级号贴着同一群怪则一进门就被咬；给怪挂"侦测范围"类 buff 真的能改变它看得见多远 | **未落（本轮按 AC 原文读出来、量过出处，暂未动代码）**：本作的 `aggro_radius` 是一个不带等级修正的常数，两条 AI 通路都只比 `aggro_radius` 这个数。要做的是"半径 = 基准 ± 等级差，夹在 [5, 上限]"这一条式子，主人应当放在 `MonsterAiGate`（与宽限/嘲讽/世界 BOSS 同族），且**必须同时改两条通路**——只改 v2 就是"默认档的怪不吃等级差"那一类作用域洞。留作待办（设计文档 §11.2） |
| 怪物生成时的等级**不是常量**：`Creature::SelectLevel()`（`src/server/game/Entities/Creature/Creature.cpp:1478-1480`）先 `min = std::min(maxlevel, minlevel)`、`max = std::max(...)`（写反了不报错，按区间取用），再 `level = min == max ? min : urand(min, max)`（**含两端**）；`Respawn()` 里会重跑这段 ⇒ 同一个刷新点每次刷出来的等级可以不同 | 玩家看得见的是"同一片怪里这只 6 级那只 8 级"，以及"经验/血量随这只怪自己的等级走"，而不是整片营地一个模子 | **已落（2026-10-26）**：内容表的 `level_min`/`level_max` 两格以前**配了没人读**（消费者普查实测：运行期命中 0，只有 CLI 与插件在读写；等级解析只用 `level_base`）。现在判据住在 `MonsterLevelCore.Roll`（三条 AC 口径逐条对齐：归一化 / 退化区间不掷 / 闭区间），掷骰走本作唯一的确定性随机源 `CombatCore.Roll01`（键 = 实体 id + 生成那一拍 tick ⇒ 同 seed 同 tick 可重放、每次重生重掷，与 AC 同形）；优先序是"刷怪点显式点名 > 模板区间掷 > 无内容时目录等级"。**并且三个消费者从此一个权威**：生成时的初始血、战斗层的血量上限/攻击力、AI 投影都从切片 21 那一个出口按**这只怪自己的等级**算（以前点位那份按基准等级算 ⇒ "等级牌变了而血没变"）。020 的种子脚本把三格写成同一个数 ⇒ 对今天的内容**零变化**，金轨迹逐帧一致 |
| 本作内容表另有 `aggro_cone_degrees`（扇形）——**AC 没有这一格**（它是一整个球，见上一行） | 玩家看得见的是"从背后走近一只配了窄扇形的怪不会被咬" | **已落（2026-10-26，作用域 v1+v2）**：判据收进唯一主人 `MonsterAiGate.InAggroCone`（≤0 或 ≥360 都算全向；贴在脚下算看得见），v1 的索敌与 v2 的 `SelectByAggroRangeAndCone` 都只问它，v2 那份 inline 式子删掉。这一格是**本作的口径**，不是逆向结果——写清楚免得下一个读的人去 AC 里找它 |
| 巡逻段的移动速度**与追击分开**：`WaypointMovementGenerator::Initialize`（`src/server/game/Movement/MovementGenerators/WaypointMovementGenerator.cpp:205-220`）按路径点上的 `WAYPOINT_MOVE_TYPE_WALK / _RUN` 调 `init.SetWalk(true/false)`（枚举在 `Movement/Waypoints/WaypointMgr.h:29`，组队巡逻那一路 `CreatureGroups.cpp:395` 认同一格）；速度本身由 `Unit::GetSpeedRate(UnitMoveType)`（`Entities/Unit/Unit.h:1721`，按移动方式取倍率）给 | 玩家看得见的是"怪在自己那条巡逻道上走得很慢，一发现你立刻换成冲刺速度"——而且同一队巡逻怪跟着领队用同一种走/跑 | **已落（2026-10-26，作用域 v1+v2；粒度不同，记在这里免得被当成照抄）**：本作是**每只怪**一格 `walk_speed`（巡逻速度）+ 一格 `move_speed`（追击速度），不是 AC 的**每路径点**一格；式子只在 `MonsterPatrolCore.WanderSpeed(walk, move)` 有一份，v1 的无路线游荡与 v2 的同一支都问它（以前 v1 直接拿 `move_speed` ⇒ 种子内容 `walk=2.5 / move=4` 时默认档的怪**溜达比冲刺快**）。**没落的另一半**是"按点配走/跑"与 `SetWalk` 那层动画语义——那需要巡逻线带上每点的移动方式，属编辑器另一张表 |
| 本作内容表另有 `is_aggressive`（主动／被动怪）——**AC 这份树里读不到这一格**：`AGGRESSIVE` / `PASSIVE` 在 `Entities/Creature/` 与 `src/shared/` 的头文件里**零命中**；`creature_template.type_flags` 的注释写着 "enum CreatureTypeFlags mask values" 而那个枚举在本树里根本没有定义；`CREATURE_TYPE_FLAG_*` 真正被读的只有采集三件／驯服两件／`CAN_ASSIST`（`ScriptedEscortAI.cpp:78`） | 玩家看得见的是"营地旁边配成被动的怪不会先动手，但捅它一刀它就追你" | **已落（2026-10-26，作用域 v1+v2）**：以前只有 v2 两处 inline 读它 ⇒ 默认档（生产全部种子怪）配成被动也照样先咬人。现在判据住在 `MonsterAiGate.MayInitiateOnSight(isPassive)`（谁先动）与 `WipesThreatForPassive(isPassive, hasLockedTarget, calledByAlly)`（**被动不许抹掉已经选中的那位**，也不许抹掉被同族求援叫起来的——求援机制存在的意义就是叫那些不会自己索敌的同类），两路各问一次、v2 的 inline 合取式删掉。v1 那份配置用**反向旗标** `IsPassive`：bool 的零值必须等于既有行为，写成 `IsAggressive` 会让所有没带这一格的旧内容一夜之间变成"站着挨打不还手"（缺省＝主动 ⇒ 金轨迹逐帧不变） |
| `if (!isWorldBoss() && (lastLeashExtension + leashTimer > now \|\| HasTauntAura())) return true;`（注释原文 "this should include taunt auras"） | ① 世界 BOSS 被拉出几百米也不会半路回头满血；② 坦克把怪从队友身边拉开后，怪不会因为"离家太远"而跑回去 | 内核 v10：`exceededLeash = leashApplies && !tauntHolds && dist > leashRadius`；`leashApplies = Rank != WorldBoss`；`tauntHolds` 与那条嘲讽锁**同一个条件**（含"被钉的人还在场"），否则会露出"锁撤了、距离检查还被锁挡着"的木桩怪。校验器另外补一条 Warn：给世界 BOSS 配 `leash_radius` 是死配置 |
| `uint8 GetLeashTimer() { 11 + minlevel/10 - 2, 下限 11 }`（注释自己写着 "Formula is likely not quite correct, but better than flat timer"） | leash 不只有距离，还有一段时间窗：刚被拖打过/卡住的怪**推迟**回家 | **未落，且刻意不落**：AC 这段是**时间制**脱战，本作是**距离制**（`leash_radius` 一列、编辑器、库、内核都是它）。两种模型混用要先定口径（谁赢、编辑器那一格算什么），而这段代码的注释自己就不保证正确。留作 §12 的未决问题 |
| `struct AttackPosition { Position _pos; bool _taken; }`（`Unit.h:583`，全仓**只有声明没有使用**） | "多只怪围着你打而不叠在同一个点" | **不落**：AC 里没有服务端近战站位槽——那条表现是客户端避让/碰撞做的。据此造一套"攻击槽位"就是凭想象加规则，不属于逆向结果 |
| 组队分经验：`KillRewarder::_InitGroupData/_RewardGroup/_RewardXP` + `Formulas.h:xp_in_group_rate` | 组队打一只怪不会把经验放大五倍；带一个已经灰名的小号会**拖累全组**（其余人减半），小号自己拿 0；低级队员分到的更少 | 已落（`MonsterXpCore.ComputeGroupShares`，`Version` 1→2）：`rate = ≤2人 1.0 / 3人 1.166 / 4人 1.3 / ≥5人 1.4`，池子按 `pool = Base × Concession(最高的那个没灰名的队员)`，个人份额 `pool × rate × 自己等级 / 等级和`，有人灰名则 `÷2 + 1`（那个 +1 是 AC 防整除归零的写法，照抄）。**定基仍按决策 D3 用怪物等级**（AC 用玩家等级，理由见 §2.3 与 D3） |
| 谁能进分母：`Player::IsAtGroupRewardDistance`（`Player.cpp:12769`）+ `_RewardXP` 里那句 `player->IsAlive()` | 队友死了或跑远了就不该分走我的经验 | 已落：宿主过滤（在线 + 活着 + 距离 ≤ 74 米，`WorldConfig.cpp:181` 的 `MaxGroupXPDistance` 默认值），三类跳过各计数 |
| `_InitXP` 里 `if (ct->ModHealth ≤ 0.75) xp *= ModHealth`（"血薄的 NPC 给更少经验"）与 `ModExperience` 逐模板经验倍率 | 同等级但被削过血的怪经验更少；同为某档位的"剧情怪"能比同级普通怪多给几倍 | **已落（内核 v3）**：`MonsterXpConfig.XpModBps` / `HealthModBps` + 模板两列 `xp_mod_bps` / `health_mod_bps`。两段照抄 AC 的顺序与取整方式：<c>Formulas.cpp:90</c> 的 <c>xpMod *= ModExperience</c> 在**同一次取整之前**乘进去，<c>KillRewarder.cpp:136-138</c> 的 <c>_xp = uint32(_xp * ModHealth)</c> 在**已经取整的经验上再截一次**——合成一次四舍五入会在奇数档差 1 点。那条 `<= 0.75f` 的门槛也照抄：`7501..10000` 与不填一样、`>10000` 不会放大（校验器对"落在不生效那一段"给提示），而 `xp_mod_bps=0` 是**真的归零**、不是"没配"（缺省是 10000，旧库行/旧快照文件全都不受影响） |
| taunt 的实现形态：`SPELL_AURA_MOD_TAUNT` + `Unit::TauntApply/TauntFadeOut` + `DIMINISHING_LEVEL_TAUNT_IMMUNE` | 嘲讽是一段**有施法者对象的**状态（谁施的、什么时候到），而且存在"嘲讽免疫"这一类怪 | 内核 v9 的 `TauntTargetId + TauntUntilTick` 与它同形（只有截止时间没有对象，锁就无法执行）。**嘲讽免疫**这一格本作还没有列：它不是 rank 能表达的（AC 用 aura/diminishing 类别），要做要新开一列 ⇒ 与 `status_id`/`aggro_say_id`/`zone_id` 那几条一起等下一次迁移批次 |
| 掉落组：`LootGroup::Roll`（`Loot/LootMgr.cpp:1292-1327`）一个 `rand_chance()`[0,100) 按行序累减组内概率，谁把余额减成负数谁中奖；`chance >= 100.0f` **直接返回该件**；显式档全落空后才在 `EqualChanced`（组内 `chance==0` 的那些，`:1283-1289`）里**等概率抽一个**；两档都空则整组不掉 | "从这 10 件里必出 1 件"、"这两件互斥不会一起掉"、"某件配了 100% 之后排在它后面的组员再也不掉" | 已落（内核 `MonsterLootCore.RollGroup` + `MonsterDropSnapshot.GroupId` + 库列 `group_id`）：三条判据逐字照做，包括"组内 0 不是永不掉而是平分余额"和"100% 短路挡死后排"（后者额外数出 `GroupShadowedByGuaranteed`）。组 0 仍是原来的逐件独立掷，盐值与下标一个数都没动 |
| 组的次序与归属：`LootTemplate::Process`（`:1699-1768`）**先把所有未分组的行逐件独立掷完**，再逐组处理；组的成员次序 = 装载时的行序；`GroupId>0 && Reference>0` 的行被强行当成未分组条目（`:1562-1582`） | 同一件物品在"引用表"里和在外面掉法不同 | 已落前半（先逐件后组，组按组号升序、组内按 `(seq, itemId)`）；`Reference`（引用另一张模板）**本作没有这个概念**——我们的"一怪 N 表"已经覆盖了大部分需求，引用表等真正需要跨怪复用时再开 |
| 组配置校验：`LootGroup::Verify`（`:1497-1509`）对"组内合计 >101%"与"合计 ≥100% 却还挂着 `chance==0` 的成员"各打一条 `LOG_ERROR("sql.sql", ...)`，但**不改数值、不丢行**；`IsValid`（`:335-339`）另把 `groupid >= 128` 的整行丢弃、把"`chance==0 && groupid==0`"当"equal-chanced 却没分组"直接跳过 | 策划在库里配错组，服务器日志里有错误而游戏照掉不误 | 校验器**只提示不丢行**（不照抄 AC 的丢行：静默丢行等于"配了不掉"，是最难归因的表现）：合计 >100%、吃满还挂等概率档、后排被满概率件挡死、整组等概率、组外填 0 各念一条；组号负数/>127 才是硬错。AC 的 `RATE_DROP_ITEM_GROUP_AMOUNT` 见下一行（那一条本轮已落，只是换了宿主） |
| 组可以出多件：`LootGroup::Process(nonRefIterationsLeft)` 在一轮中签后**递归再掷同一组**，次数取自世界配置 `RATE_DROP_ITEM_GROUP_AMOUNT` 且只对最外层生效（`LootMgr.cpp:1439-1470`、`1711-1719`）；递归的停止条件是 `!item->needs_quest` | "同一组连出两件"、"任务物品不会一次出三份" | **已落，但换了宿主**：本作没有世界配置层，那个次数落在**表**上——绑定的 `min_count`/`max_count` = 这张表的互斥组重掷几轮（钳到 16 轮）。每轮用**各自的骰子**（轮次进盐值，否则每轮抽到同一件、"抽 3 件"会变成"这一件给 3 份"），抽中任务件即停止重掷；组 0 的逐件独立掷不受该区间影响（决策 D4），因此校验器会念"这张表没有互斥组却配了抽几件" |
| 条件件挂在**每一行**上（`LootStoreItem::needs_quest`，与 `QuestRequired` 列同源） | 尸体窗口里"任务物品只对做了任务的人出现"，而同组其他件照常 | **已落**：逐件条目新增 `condition_kind`/`quest_id`，`-1 = 继承所属绑定`（旧数据与旧快照文件里没这个键，反序列化落 -1 就退回加这一列之前的逐字行为；缺省若写成 0，0 恰好是"无条件"，所有任务物品会静默变成人人可拿）。解析规则只有一个主人 `MonsterLootCore.ResolveConditionKind/ResolveQuestId`，内核出账、校验器、命令行试算都调它；条件跟着组内胜者走（"保底随机一件、其中一件是任务物品"从此配得出来）。业务层那一跳本来就是逐件的（`DsGameplayEvent.QuestId` 取的是这一件解析后的 id），宿主侧零改动 |
| 金币的来源：`Unit.cpp:18059` 在**击杀那一刻**调 `loot->generateMoneyLoot(creature->GetCreatureTemplate()->mingold, ...->maxgold)`，那两格来自 `creature_template` 的第 40/41 列（`ObjectMgr.cpp:654-655`、`CreatureData.h:227-228`）——**没有任何按等级的公式**；`generateMoneyLoot`（`Loot/LootMgr.cpp:838-849`）三条边界：`maxAmount <= minAmount` 直接给 `maxAmount`、`(max-min) >= 32700` 时按 `>>8` 的粒度掷再 `<<8`、两种都要乘全局 `sWorld->getRate(RATE_DROP_MONEY)` | §7.1 原来记的是"金币单独一格，是个区间**且与等级相关**" | **观测那句只说对了一半**：区间确实存在，但"与等级相关"是**数据**（高等级模板自己填了更大的数），不是运行时按等级算出来的。所以本作把区间挂在掉落绑定上（静态两格）**已经是 AC 口径**，不该再加"按生效等级缩放"那一档——这条是本轮重量之后才写死的，我之前在缺口清单里凭推测写过"要做等级缩放"，现已撤回（设计文档 §11.2-18）。仍未落的是两条真差：全局掉落倍率（`RATE_DROP_MONEY` 的对位，本作只有逐模板/逐表的数值）与大跨度时的 **1/256 粒度** |
| 组队掉落**不是**"每人一份"：`Unit.cpp:18050-18059` 生成的是**尸体上的那一份** `creature->loot`，组队用的是 round-robin 的 looter 标签（`group->UpdateLooterGuid`）加 need/greed 分配；`FillLoot(..., bool personal, ..., uint16 lootMode)` 里那个 `LOOT_MODE_*`（`SharedDefines.h:43-47`）是**团队本难度位**（DEFAULT / HARD_MODE_1..4），不是"个人掉落"；真正逐人判的只有任务件（`LootItemInSlot` 的 `qitem`/`conditem`） | §7.1 原来记的是"组队时每个人各自掷（个人掉落）或 N 人 need/greed → 掉落是按参与者而不是按击杀者" | **这句把两种模式压成了一句**：AC 的普通怪是"尸体一份共享 + 组队分配规则"，不是"每个参与者各掷一份"；本作今天的"击杀即发到击杀者"两种都不是，真正缺的是**尸体上那一份掉落列表 + 右键拾取 + need/greed**（见 §7.2 与设计文档 §11.2-2 的拾取窗口）。**只有经验那一侧是真的"按参与者"**（`KillRewarder::_RewardGroup`），那条已经落了 |

