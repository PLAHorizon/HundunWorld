# 控制状态层（D14）—— 方案与决策清单（切片 52，**只出方案，等点头再写码**）

> 目标：让怪物技能表里那两格 `status_id` + `status_duration_seconds` 第一次有消费者，
> 让玩家侧"打断"有技能以外的第二个来源（眩晕/沉默），并顺手把 `detect_mask` 那一位问清楚。
> **这一期可能涉及新表与写库 ⇒ 全部只生成不执行，等用户点头。**
>
> 证据基线（本轮量的）：怪物全谱 **982 通过 / 0 失败 / 1 跳过**、插件 rt+ed 各 0 错、`MonsterEditorKernelTests` 116/0。
> 本文里每个"今天是什么样"都带 `file:line`；写"我没找到 / 这份 checkout 里没有"的是如实结论，不是省略。

---

## 1. 为什么要做这一期（现状证据，逐条可复核）

### 1.1 那两格今天确实是死数据
- 写侧一路齐全：库列 `scripts/sql/019_monster_editor_tables.sql:300,351` → EF 实体
  `Horizon.Model/GameModel/MonsterTemplateEntity.cs:426-432` → 迁移
  `Horizon.Entities/Migrations/Games/20261025123917_AddMonsterEditorTables.cs:230`（+ Designer:3438、
  `GameEntityContextModelSnapshot.cs:3435`）→ `Horizon.MonsterCli/MonsterStore.cs:213-214,469-470,821-822`
  → 草稿键 `Horizon.MonsterCli/MonsterDraft.cs:189,207-208` → 插件镜像
  `HundunWorld/Plugins/MonsterEditor/Source/MonsterEditor/MonsterDraftModel.cs:54,233-234`。
- 装配也搬进内核了：`Horizon.Game.Core/Sim/Server/MonsterContentSnapshot.cs:392-393,413-414`
  （`MonsterSkillSnapshot.ToMount()`）→ `Horizon.Game.Sim/Monsters/MonsterCastingCore.cs:103,106` 上
  **字段存在、内核零读取**。
- 所以策划现在能做的 exactly 是：在库里填一个数字、在导出包里带一个数字、进游戏什么都不发生。
  校验器已如实念 Warn（`Horizon.MonsterCli/MonsterValidator.cs:318-320`，`status_id` 与时长**当一对**判）。

### 1.2 但"完全没有状态层"这句是错的 —— 本仓已经有三件半成品
这一条决定了方案的形状，所以先把它量清楚再谈设计：

| 既有物 | 位置 | 今天真的在跑吗 | 缺的是哪一段 |
| --- | --- | --- | --- |
| **状态"计划层"** `WuxingStatusEffects`（`StatusKind{ArmorDown,Vulnerability,DamageTakenDown,Stun,Root,Slow,Dot}` + `Plan(effectsJson)` 解 `stunSec/rootSec/slowPct/dotRatio`） | `Horizon.Game.Sim/Skills/WuxingStatusEffects.cs:21-155`（枚举 :40-57、计划 :79-113） | **只有测试在调**（`WuxingStatusEffectPlanTests.cs:27-158`） | "把计划倒进容器"那一句（它自己的头注释 :10-13 就写着缺这一句） |
| **服务端容器 + 聚合** `CombatState.ActiveEffects` / `EffectInfo`；写方 `CombatGrain.ApplyEffectAsync`，读方 `RefreshElementVectors`（普攻/技能/受击三条伤害路径每条都调） | `Horizon.Game.Message/Network/GrainStateModels.cs:25`（容器）、`:175-231`（`EffectInfo`，带五行三行载荷）；`Horizon.Orleans.Grains/CombatGrain.cs:637-704`（写）、`:970-1008`（聚合） | 容器与聚合**在跑**，但生产里几乎没人往里倒（`ApplyEffectAsync` 只有契约测试调过：`ElementalBuffAuthorityContractTests.cs:108`） | 上游喂它的那一句 |
| **客户端表现四件** `ControlState` 十档枚举、`ControlStateManager`、`SkillEffectSystem.SetControlState`、`BuffBarUI`（已挂 HUD） | `HundunWorld/Source/Game/Character/ControlStateManager.cs:243-255`（Stunned/Silenced/Rooted/Feared/Confused/Disarmed/Blinded/Taunted/Sleep/Frozen）、`Source/Game/Combat/Effects/SkillEffectSystem.cs:142,338`、`Source/Game/UI/GameMain/BuffBarUI.cs:14`、挂载点 `CombatHUDManager.cs:38-39,97,111` + 每帧喂数据 :311-326 | **画得出来但永远是空的**：`WuxingClientEffectHonestyTests.cs:54-57,61-90` 钉死"客户端不许本地施加"（`ApplyEffect/RemoveEffect` 生产零调用），而服务端还没发 | 服务端权威的那一跳 |

⇒ **这一期不是"从零造一个 aura 系统"，而是"把已有的三层接起来，并给怪物一条能配出来的入口"。**
把它当新功能从零建会造出**第四份清单**（本仓在 HP 权威、坐标换轴、掉落条件上各踩过同款）。

### 1.3 下发载体已经有位，只是没人写
`EntityStateBits`（`Horizon.Game.Message/Sync/Components/SyncComponents.cs:239-249`）声明了
`Dead=1<<0`、`Invincible=1<<1`、**`Stunned=1<<2`**、**`Hidden=1<<3`**、**`Frozen=1<<4`**、`Evading=1<<5`，
而注释 :224-225 自己写着"只有 `Dead` 与 `Evading` 两位真的被服务端写过；其余几位仍然没有载体"。
切片 18 已经证明这条路可用（`Evading` 一位打通了"回位中的怪打不到"整条玩家可见链：
写方 `DsWorldSession.MonsterStateBitsOf`（`Horizon.Game.Core/Sim/Server/DsWorldSession.cs:1343-1348`，
周期快照 :3582 与晚加入补发 :4160 两条通路都调它；客户端读方 `FlaxActorSyncSystem.cs:1113-1140` →
`SelectableEntityTag.cs:82`）。
⇒ **`Stunned`/`Frozen` 是"声明了没人写"的既有陷阱**（见记忆 [[schema-key-declared-but-never-applied]]）：
这一期要么真的写它们，要么把它们连同注释一起删掉；留着一个永远为 0 的位比没有位更坏。
注意 **`Silenced`/`Rooted`/`Slow` 没有位** —— 而 AC 证明这三档语义互不相同（见 §2.2）。

### 1.4 打断的第二上游，接口位已经留好了
`MonsterCastingCore.Advance(..., bool interruptSignal, ...)` 里
`interruptSignal && mount.Interruptible` → 打断 + 1.5 秒静默（`Horizon.Game.Sim/Monsters/MonsterCastingCore.cs:312-323`，
参数位 :272）。今天唯一调用点传的是"玩家踢了一脚"（`DsWorldSession.cs:2276-2280` 喂 `monster.InterruptRequested`，
上游 `CombatActionKind.Interrupt=4`）。
⇒ "眩晕/沉默打断施法"不需要改内核签名，只需要一个真把 `interruptSignal` 置起来的上游。

### 1.5 `detect_mask` 那一格与 AC 根本不是同一个形状
- 本仓：`Horizon.Game.Sim/Monsters/MonsterBehaviorCore.cs:175` 有字段、`:178`
  `CanDetectStealth => (DetectMask & 0x4) != 0`；装配写入 `MonsterContentSnapshot.cs:314,344`；
  **`CanDetectStealth` 全仓零调用方**（只有定义与登记注释 `MonsterValidator.cs:64,251-256`、
  例外登记 `MonsterEditorContractTests.cs:557,577`）——"零调用方"这句本轮重新量过，仍成立。
- AC：侦测**不是位掩码**。`WorldObject::CanDetectStealthOf`（`Entities/Object/Object.cpp:1981`）的公式在
  `:2018-2041`：`detectionValue = 30 + (level-1)*5 + m_stealthDetect.GetValue(type) − obj->m_stealth.GetValue(type)`，
  `range = value*0.3 + combatReach`；stealth 等级来自 aura effect 的 `GetAmount()`
  （`SPELL_AURA_MOD_STEALTH`=16，`Spells/Auras/SpellAuraEffects.cpp:1656-1667`），侦测值来自
  `MOD_STEALTH_DETECT`=17（`:1631-1642`）。AC 全树**没有叫 `canDetect` 的函数**。
⇒ **`detect_mask` 在 AC 里没有对应物**。把它"接上"之前要先决定：是把它当本作的**独立口径**
（位掩码 = 这只怪能看穿哪一类隐藏），还是借这一期把它改成 AC 那种连续值。这是 D32。

---

## 2. AC 的权威形状（逐条带出处，本轮直接读的源码）

根目录：`C:/Works/AzerothCore/azerothcore-wotlk/src/server/game/`。

### 2.1 aura 类型是**一张全局枚举表**，不是怪自己的配置
`Spells/Auras/SpellAuraDefines.h:61` 的 `enum AuraType`（顺序赋值，值都量到了）：
`CONFUSE=5`(:68)、**`MOD_STUN=12`**(:75)、`MOD_STEALTH=16`(:79)、`STEALTH_DETECT=17`(:80)、
`INVISIBILITY=18`、`INVISIBILITY_DETECT=19`、**`MOD_ROOT=26`**(:89)、**`MOD_SILENCE=27`**(:90)、
`MOD_INCREASE_SPEED=31`/`MOUNTED=32`/`DECREASE_SPEED=33`(:94-96)、
`_IMMUNITY_*` = EFFECT/STATE/SCHOOL/DAMAGE/DISPEL 37..41(:100-104)、`MECHANIC_IMMUNITY=77`(:140)、
`MOD_DISARM=67`(:130)、`MOD_MELEE_HASTE=138`(:201)、`MOD_DETECTED_RANGE=152`(:215)、
`MOD_STEALTH_LEVEL=154`(:217)、`MOD_DETECT_STEALTH=228`(:291)。
**这份 checkout 里没有** `SPELL_AURA_MOD_CONTROL`，也没有 `SPELL_AURA_MOD_SPEED_BONUS`（旧名）。

⇒ 对口径的直接含义：**"状态"在 AC 是全局字典里的一个号**（由 spell 的 effect 表驱动），
不是每只怪各自定义。这决定了我们 `status_id` 该指向"一张全局状态清单"，
而不是"这只怪自己的一个自定义状态"。（D28 的正题。）

### 2.2 三档控制走**两条不同的闸口**，不能合成一位
- **眩晕**：`AuraEffect::HandleAuraModStun`（`Spells/Auras/SpellAuraEffects.cpp:3528`）只做一件事——
  `SetControlled(apply, UNIT_STATE_STUNNED)`（`Entities/Unit/Unit.cpp:18296`）→
  `Unit::SetStunned(true)`（`Unit.cpp:18418`）。它同时封两条路：
  ① 移动：`SetStunned` → `SetRooted(true,true)` → `SendMoveRoot` 加 `MOVEMENTFLAG_ROOT` 并 `StopMoving`
  （`Unit.cpp:18464-18497`），判据是 `IsImmobilizedState() = HasUnitState(ROOT|STUNNED)`（`Unit.h:775`）；
  ② 施法：`SetStunned` 里 :18435 **直接 `CastStop()`**（`Unit.cpp:1187`，在读条被晕就是被打断），
  之后想再施法在 `Spell::CheckCasterAuras`（`Spells/Spell.cpp:6820`）见 `UNIT_FLAG_STUNNED`
  → `SPELL_FAILED_STUNNED`（:6863-6891）。
- **沉默**：只设 `UNIT_FLAG_SILENCED`（`SpellAuraEffects.cpp:3033-3050`），且**只中断
  `PreventionType == SPELL_PREVENTION_TYPE_SILENCE` 的那一条当前施法**（闸口 `Spell.cpp:6900` 按 spell 的
  PreventionType 逐条判）。
- **定身**：只有 ROOT → 只进 `IsImmobilizedState`，**不碰** `CastStop`、不碰 `STUNNED` flag。
- **（期 0 落地时补的一条，本文件以前的口径把它漏了）三档都不阻止自动近战**：
  `Unit::AttackerStateUpdate`（`Unit.cpp:2601-2606`）进门只查 `UNIT_FLAG_PACIFIED`，
  **没有** `IsStunned()` / `IsImmobilizedState()` 那一支。⇒ 本文 §5 期 0 那一格原先写的
  "眩晕中的怪这一拍不出手也不位移"里，**"不出手"是错的**（那是 pacified/缴械，不是眩晕）。
  落地按源码来：眩晕/定身拦腿、眩晕/沉默拦嘴（不许起手、施加那一刻打断在读的条），近战不受影响。

⇒ 三条玩家看得见的差别：眩晕=动不了也不能施法且**当前施法被打断**；沉默=能动能打但读不了条
（且能不能打断在读的那条取决于那条施法的预防类型）；定身=只能打不能走。
**所以状态位不能只有 `Stunned` 一位**（§1.3 那个缺 `Silenced` 的坑要正面处理）。

### 2.3 受击破控是有的，但它是"按伤害量掉进度"的 proc 机
`Unit.cpp:16660-16690`：CONFUSE / FEAR / STUN / ROOT 在受击时按伤害量扣减 aura 进度（不是"一挨打就醒"）。
另有 `m_procCharges`（`Spells/Auras/SpellAuras.h:257-266` 那组字段：
`m_maxDuration/m_duration/m_timeCla/m_procCharges/m_stackAmount`）。

### 2.4 生命周期是"每帧减时长 → 到期 → 反向解除"三点式
`Aura::Update`（`Spells/Auras/SpellAuras.cpp:740`）只做 `m_duration -= diff`；
真正的回收在 `Unit::_UpdateSpells`（`Unit.cpp:3885-3900`）：扫 `IsExpired()` →
`RemoveOwnedAura(i, AURA_REMOVE_BY_EXPIRE)`。
**旧名 `DEAULER_*` 叠层规则全树 0 命中**（已被删）；现在叠层走
`Aura::TryRefreshStackOrCreate`（`Spells/Spell.cpp:3080`）+ `MaxAffected`/`stackAmount`。

### 2.5 施加的主路径：技能 effect 的隐式目标 → ApplyAura
`Spell::DoAllEffectOnTarget`（`Spell.cpp:2513`，:2548 `HandleEffects`）→ `Spell::DoSpellHitOnUnit`（:2925）
→ **`Aura::TryRefreshStackOrCreate`（:3080，创建即带 duration 与 DR）** →
effect 函数表 `SpellEffects[6] = &Spell::EffectApplyAura`（`Spells/SpellEffects.cpp:77`）→
`EffectApplyAura`（:1308）→ `m_spellAura->_ApplyEffectForTargets(effIndex)`。
`Creature.cpp` / AI 里**不直接加控制 aura**；`ScriptedAura` 这个类不存在（只有 `SpellScript::AuraScript`）。

⇒ 直接含义：**在 AC 里"这式技能附带什么状态"是技能的属性，不是怪物挂载的属性。**
我们技能挂载上那两格 `status_id`/`status_duration_seconds` 是**偏离 AC 的自家设计**
（因为本作的挂载指的是 `SkillTemplate`，而那 91 行里带 `stunSec` 的行极少）。
这句话必须在方案里说白，否则"参考 WoW"会被拿这两格当根据。（D28 的第二半。）

### 2.6 递减（DR）的真实形状，以及它现在能不能不抄
旧名 `GetDampening` / `_dampeningEffects` / `DECREASING_AFFORTANCE_*` **全部不存在**。
现在是 `DiminishingGroup`（`SharedDefines.h:3496`，21 组 0..20，含 `STUN=14`、`CONTROLLED_STUN=4`、
`ROOT=13`、`SILENCE=16`、`TAUNT=18`）+ `DiminishingType{NONE,PLAYER,ALL}`（:3488；
组归类表 `Spells/SpellMgr.cpp:242`，**stun 系归 `DRTYPE_ALL` ⇒ 怪物也吃**）。
乘数 `DiminishingLevels`（`Unit.h:268`）= **1.0 / 0.5 / 0.25 / 0**（`Unit.cpp:15220-15232`），
taunt 特供 0.65/0.4225/0.274625/0（:15187-15202）；重置 **15000ms**（`Unit.cpp:15130`）；
计数 `IncrDiminishing`（:15142）封顶 `GetDiminishingReturnsMaxLevel`（`SpellMgr.cpp:261`）；
CC 总时长另被 10 秒截（`Spell.cpp:3107-3115` `LimitDuration`）。

⇒ AC 侧的"一期最小集"判断（来自上面的源码阅读）：**不能省的三条**是
①"单一状态位 + 两条独立闸口（移动门 / 施法门）"的形状；②"施加即打断"（apply 回调里同步 CastStop/StopMoving）；
③"到期单点回收"（每帧减 → expired → remove → 反向解除）。
**可以先不抄**：DR 的 21 组与 15s/10s 限制（先留一个 stun 组或整条关掉）、stealth 的连续值公式、
叠层 `MaxAffected`、受击破控的 proc-charge。

---

## 3. 本作的落点设计（六跳，与掉落/文本/巡逻同一条链）

```
① 定义源     状态是什么、封哪条路、能不能被驱 ⇒ 全局一份（D28 选一个落点）
② 内容       技能挂载两格 status_id + status_duration_seconds（列已在库里，不开新列）
③ 装配       MonsterSkillSnapshot.ToMount() 已经在搬这两格 → 只需把它们带到运行期判据（已完成一半）
④ 运行期     新内核纯函数 MonsterStatusCore（Horizon.Game.Sim/Monsters/）：施加/到期/查询/两条闸口 + 免疫
⑤ 下发       EntityStateBits 补位（D33）+ 一条 SyncEventKind（照 MonsterSay 那条边沿形状）
⑥ 客户端     读位 + ControlStateManager/BuffBarUI（表现端已齐全，缺的只是驱动）
```

④ 是要新写的唯一一层，其余五跳都有现成骨架。判据全部走本仓一贯的"唯一主人"纪律：
`MonsterStatusCore` 里回答"这一档封不封移动 / 封不封施法 / 打断不打断在读的那条 / 吃不吃受击破控"，
v1、v2、宿主、客户端（镜像表）都只问它。

### 3.1 计划层已有的东西直接复用，不另立
- `WuxingStatusEffects.StatusKind` 已经有 `Stun/Root/Slow/Dot` 四档，且 `Plan()` 已经把
  `stunSec/rootSec/slowPct/dotRatio` 解成 `PlannedStatus`（含"时长来自数据还是引擎常量"这个出处标记）。
  **玩家技能那条线继续走 JSON 键**（不动它，它是并行会话在用的文件）；
  怪物这一期新增的是"挂载上的号 → 同一个 `StatusKind` 词表"这一跳。
  ⇒ 状态种类**只有一份枚举**，两个入口（JSON 键 / 挂载号）都归到它。这是 D28 的默认建议。
- 容器：`EffectInfo` 是 Orleans grain state 的一部分且 `[MemoryPackable(SerializeLayout.Explicit)]`
  + `[GenerateSerializer]`（`GrainStateModels.cs:172-174`）⇒ **追加成员是跨版本事件**
  （见记忆 [[appending-serialized-parameters-needs-slot-order-tests]]：MemoryPack 追加成员会让旧读新直接抛）。
  所以"要不要复用 `EffectInfo` 当怪物状态的载体"是 D29，不是显然的。

### 3.2 两条权威不能在这一期里偷偷合并
`§8 D13`（怪物归 DS、角色归 `CharacterGrain`）还没拍板，而 §1.2 那张表里"容器 + 聚合"住在
**Orleans 侧的 `CombatGrain`**，"怪物施法/近战/受击"住在 **DS 侧的 `DsWorldSession`**
（`CombatCore.Resolve` 四个调用点：`DsWorldSession.cs:1504` 玩家→怪、`:1582` 玩家→玩家、
`:3038` 怪施法结算、`:3393` 怪近战→玩家；`CombatRequest`/`CombatResolution` 两个结构体
`Horizon.Game.Sim/CombatCore.cs:43-137` **没有任何状态/控制字段**，Version=1 :166）。
⇒ 一期最保守的落法是**状态实例归 DS 世界**（怪物与玩家快照都在那儿），
`CombatGrain.ActiveEffects` 那一份只在"玩家自己的 buff"这条线上继续用；
两条容器的合并是 D13/D29 的事，不能靠这一期顺手做掉。

---

## 4. 决策清单（要用户点头的九条；每条给了建议但**不代替决定**）

**D28 · `status_id` 指向哪一份清单？**
- **a) 代码枚举（建议）**：`MonsterStatusKind` 住在 `Horizon.Game.Sim`，号表与
  `WuxingStatusEffects.StatusKind` 同一词表（1=Stun、2=Silence、3=Root、4=Slow、5=Dot…）。
  零迁移、零新表，`status_duration_seconds` 作为挂载级覆盖；
  校验器从此查"号在不在枚举里"（今天它是 Warn"没人读"，届时改成引用检查）。
- b) 新库表 `Game_HunduShijie_MonsterStatus`（id / 封移动 / 封施法 / 打断 / 可驱散 / 免疫组 / 时长缺省…）：
  配得出 AC 那种数据驱动，代价是**一张表 + 一次迁移 + 编辑器一个新面板 + 快照一个新 DTO**。
- c) 不用 `status_id`：改读挂载所指 `SkillTemplate.skill_effects`（`WuxingStatusEffects.Plan` 已经在等这个），
  两格降级成"覆盖"。**最贴 AC**（§2.5：状态是技能的属性），但今天那 91 行里带 `stunSec` 的行极少
  ⇒ 大部分技能式配不出控制，且这两格继续悬空。
- 补充事实：AC 里"这式带什么状态"确实是技能属性（§2.5），而 `status_id` 是**本作自家设计**。

**D29 · 状态实例的权威放哪？**
- **a) DS 世界内新容器（建议）**：`DsWorldSession` 里给每个实体挂一份小状态表，
  与 `DsMonster`/玩家快照同一生命周期；`EntityStateBits` 下发；到期由 tick 单点回收（§2.4 三点式）。
- b) 复用 `CombatGrain.ApplyEffectAsync` + `RefreshElementVectors`（§1.2 那张表里的容器）：
  少一个新容器，但**怪物不在 grain 里**（DS 权威），跨进程一跳且要吃 MemoryPack 追加成本。
- c) a + b 都要（怪物走 DS、玩家 buff 走 grain，两层各自一份）：最完整，也最容易长出第二份清单——
  选它的话必须同时定"谁赢"的口径与一条跨容器对账判据。

**D30 · 一期支持哪几档？**
- **a) 控制四档（建议）**：Stun / Silence / Root / Slow（已有的 Dot 与三个比例类继续走玩家技能那条线，不动）。
  依据 §2.2 的三条闸口差别 + §2.6 "不能省的三条"。
- b) 只做 Stun（最小）：沉默、定身、减速全部继续挂账；两格里 `status_id` 只有一个有效值。
- c) 客户端那十档全上（`ControlStateManager.cs:243-255`）：Feared/Confused/Disarmed/Blinded/Taunted/Sleep/Frozen
  都要真行为——其中 Feared/Confused 需要"强制移动/随机行动"这种**新的 AI 分支**，Sleep 需要受击醒来，
  Taunted 我们已经有独立实现（**不该并进 aura 层**，否则切片 16 那条嘲讽锁会长出第二份）。
  不建议一期做，但要做的话就是这一档。

**D31 · 递减（DR）与受击破控做不做？**
- **a) 一期都不做，但把接口位留好（建议）**：AC 的 DR 是 21 组 × 15s × 4 档（§2.6），
  PvE 单怪场景不接也不会出现玩家可见的病；而"没做"必须写进校验器/audit，
  否则"这只怪能无限晕人"会被读成配错。
- b) 只给 stun 一个组、只做 1.0/0.5/0.25/0 与 15s 重置（AC 的 stun 系是 `DRTYPE_ALL`，怪也吃）。
- c) 连受击破控（§2.3 按伤害量掉进度）一起做。

**D32 · `detect_mask` 怎么处理？**（它是 §11.4 例外清单里今天**唯一**那一格）
- **a) 定为本作独立口径（建议）**：位掩码 = "这只怪看穿哪一类隐藏"，一期**只把它接到
  "能不能进候选目标 / 能不能索敌"这一条闸口**（`MonsterAiTarget` 需要一个"他现在藏着吗"的位——
  今天没有），并**同时删掉 §11.4 的例外登记**；
  AC 的 `30 + 5×(level-1)` 连续值公式**明写不抄**（写进主人的注释里，不是静默省略）。
- b) 借这一期把 `detect_mask` 改成连续侦测值（对齐 AC §1.5）：要改列语义 ⇒ 一次迁移 + 编辑器 + 校验器取值范围。
- c) 继续挂账（不做）：例外清单那一行留着，`CanDetectStealth` 继续零调用方。
- ⚠ 注意：**a 也要求先有"谁能被隐藏"这一半**（没有隐身来源时它只有"永真"那一档），
  所以 a 的诚实分期是"接闸口 + 隐身来源先做空实现并如实念"。

**D33 · 控制状态怎么下发？**
- **a) 扩 `EntityStateBits`（建议，延续切片 18 那条已被证明可用的路）**：
  现有 `Stunned(1<<2)`/`Frozen(1<<4)` **真的开始被写**，另补 `Silenced(1<<6)`/`Rooted(1<<7)`
  （位是 `uint`，还有余量；**尾部追加不改既有位值 ⇒ 不升协议版**，切片 18 的 `Evading` 就是这么做的）。
  慢速/DoT 这类"数值类"不进位，走下面的事件档 + 客户端表。
- b) 新增一条 `SyncEventKind.MonsterStatus`（今天最后一档是 `MonsterSay=15`，`SyncPackets.cs:439-497`）
  带"施加/解除 + 状态号 + 剩余秒"：表达力强，但稳态每多一个状态就多一条边沿事件，且快照里的
  晚加入者要能补上（协议 v15 给施法条做过同款补发，可照抄）。
- c) a + b 都要（位管"能不能打 / 能不能动"，事件管"图标与剩余时间"）。**最完整**；
  `BuffBarUI` 要真画出东西就得走 b 或 c。
- 无论选哪个：**"声明了没人写"那三位（Stunned/Hidden/Frozen）这一期必须收口**——写它或删它。

**D34 · 客户端 buff 栏由谁驱动？**
`WuxingClientEffectHonestyTests.cs:61-90` 规定客户端只能被服务端驱动，而 `BuffBarUI` 今天永远画空。
- a) 这一期只做"控制态表现"（名牌 / 操作受限提示），**不动 buff 栏**（诚实：栏还是空的，注释写明等谁）。
- b) 一并把服务端发布者接上（协议 190 的 `EffectMessage`，`SkillMessages.cs:738`、
  `EffectType` 枚举 `GameEnums.cs:154`、入口 `SkillCastResponseHandler.cs:76-94,200-233`），
  buff 栏第一次有内容——**代价是这一期跨到玩家技能那条线**（并行会话在改 `SkillGrain`/`CombatGrain`）。

**D35 · 迁移批次与号段（写库要等点头）**
- 现状：`scripts/sql/` 最大编号 **025**（`025_seed_consumable_templates`）；
  怪物线用 019/020，NPC/宠物/物品并行线用 021/022/023/024，**已经撞过四次号**。
  新表建议 **026 起并在文首注明归属**（`021_pet_editor_tables.sql:837-839` 那条"故意不进
  `__EFMigrationsHistory`"的教训也要读：019:531 与 021_npc:495 会记账，回退脚本 `revert_019:40` 删该行）。
- D28 选 a ⇒ **本一期零新表**（枚举在代码里、两列已在 019 里）；选 b ⇒ 026 建表 + 027 种子 + 三件套
  （实体 / 迁移+Designer+Snapshot）+ 回退脚本，且**只生成不执行**。
- 另有一条早就挂着的批次建议（设计文档 §11.2 第 :1188 行）：`zone_id` 之类的缺列要攒一批一起迁，
  **别一列跑一次 SQL**。

**D36 · 玩家侧"被眩晕打断"的语义**
- **a) 照 AC 分两档（建议）**：眩晕 → 无条件打断在读的条（§2.2 `SetStunned:18435` 直接 `CastStop`）；
  沉默 → 只打断"预防类型=沉默"的那条（§2.2 `Spell.cpp:6900`）。本作没有 PreventionType 这一格，
  所以沉默这一半要么退化成"也打断"（与 AC 不同形，要写明），要么给玩家技能加一格（= 跨线，见 D34 b）。
- b) 两种都无条件打断（简单，但把沉默做成了小号眩晕，玩家会问"为什么沉默也打断"）。

---

## 5. 分期（点头后按这个顺序走，每期各自收口、各自有门禁数字）

| 期 | 内容 | 收口判据（都要真跑，不接受"编译过"） |
| --- | --- | --- |
| **期 0（零写库）** | `MonsterStatusKind` 枚举 + `MonsterStatusCore` 纯函数（施加/到期/两条闸口/打断）+ 挂载两格进内核 + 内核效果层用例 | "眩晕中的怪这一拍不出手也不位移""沉默中的怪读条被打断""同一夹具不填号 ⇒ 逐字不变（金轨迹护栏）"三档对照 + 唯一主人闸门 + 反证链。<br>**期 0 已落地（2026-10-28，切片 58）——落地时的实际形状与四处如实登记见 §8** |
| **期 1** | DS 侧状态容器 + 到期单点回收 + `EntityStateBits` 三位收口（写或删除）+ 摘要计数（受控中 / 到期数 / 被拒数） | 真 DS 一条：配了 `status_id=stun` 的怪在玩家身上打出眩晕后，**下一拍真的不动也不施法**，且 N 秒后自动恢复；反证只红自己那一格 |
| **期 2** | 下发与客户端：位 → `ControlStateManager` → 名牌 / 操作受限提示（buff 栏按 D34 决定） | 客户端闸门 0 错 + **第一次真像素**：一只"被眩晕的怪头顶状态提示且不动"的截图（要 PIE 窗口，按既有口径不抢占别人的编辑器/DS） |
| **期 3（可选）** | 状态免疫与"受击破控"；`detect_mask` 闸口（D32 a）；audit 新增"配了控制而那只怪免疫"这类普查 | 每条一个正反对照 |

---

## 6. 风险与前置

1. **并行会话的在途文件**：`Horizon.Game.Sim/Npcs|Pets`、`Horizon.Orleans.Grains/CombatGrain.cs`、
   `SkillGrain.cs`、`Source/Game/Game.Build.cs`、`WuxingClientEffectHonestyTests.cs` 都是别的线在改的。
   D29 选 b/c、D34 选 b 都会**直接进这些文件**——先协调再动手（本轮全量曾在别人的 `CS0104`/`CS0103` 上停过两次）。
2. **协议版本**：`EntityStateBits` 尾部追加位不升版（切片 18 有先例）；
   新增 `SyncEventKind` 档也不升版（`MonsterSay=15` 那次的口径）；
   但**给 `EffectInfo` 追加成员会**（MemoryPack 显式布局 + grain state 版本）⇒ D29 选 b/c 时要单独评估。
3. **金轨迹**：期 0/期 1 的纪律是"两格都填 0 ⇒ 逐字不变"（与切片 19/22/25/36 同一族）。
   `MonsterAiCore.Version` 若涨，头一行要同步，且重放必须逐帧一致。
4. **`NarrativePro` 的 GAS 是第二套效果系统**（`Plugins/NarrativePro/Source/NarrativePro/GAS/NarrativeAbilitySystemComponent.cs:205`
   的 `ActiveGameplayEffect`，未接战斗内核）⇒ 这一期**不许在它上面并行生长**，否则三个真相源。
5. **"打断"的既有口径别推翻**：踢一脚那条（`CombatActionKind.Interrupt=4`、三种受理拒因各计数、
   按 `ActiveMountIndex` 回内容包取同一条挂载的 `Interruptible`）今天已经是对的；
   新增的上游只是"另一个把 `interruptSignal` 置起来的人"。
6. **没做也不假装做了**：状态条 UI、可驱散 / 减益分类、`school_mask` 与免疫组的交叉、
   PVP 立场（D13）——这些都要显式留在文档里，不能被"控制层已落地"这句话覆盖。

---

## 7. 一句话版本

`status_id` 那两格差的不是一个读取端，而是**一份"状态是什么"的全局定义 + 一个权威容器 + 一条下发位**；
本仓已经有三层半成品（计划层 / grain 容器 / 客户端表现）在等这一下，
所以这一期的主要工作不是新写，而是**把三层接起来并当场决定谁是唯一主人**（D28 / D29）。

---

## 8. 期 0 落地记录（2026-10-28，切片 58；用户拍板"全 a"口径）

**落到了哪里（全部在 `Horizon.Game.Sim`，零写库、零迁移、零新表）**

- `Monsters/MonsterStatusCore.cs`（新）：`MonsterStatusKind`（0=None / 1=Stun / 2=Silence / 3=Root / 4=Slow）
  + `MonsterStatusEntry{Kind, ExpireAtTick}` + 纯函数 `Apply / Expire / Has / ActiveCount`
  + 三条闸口判据 `BlocksMovement / BlocksCasting / InterruptsCastOnApply`（唯一主人）。
  到期存的是**绝对 tick**而不是"剩余秒累减"——AC 每帧 `m_duration -= diff`，
  而本作 AI 是 tick 驱动且要求"同 tick 序列 → 同结果"，累减会把浮点舍入积进时长。
- `MonsterRuntimeState`：`Statuses / StatusCount`（**第一次挂状态才分配**，站桩小怪不背数组）
  + `ApplyStatus(kind, expireAtTick, nowTick)`（唯一的施加入口；顺带收走在读的吟唱并**留下一格待广播的打断**）
  + `ResetForRespawn` 里连状态与待发的打断一起清（否则"复活出来还是被晕着而头顶什么都没有"）。
- `MonsterAiRuntime.Advance`：拍首单点回收 + 拍尾**同一个出口**上两条闸口
  （位移清零 + 巡逻/路径游标与吟唱状态退回本拍入口；打断信号也从这个出口发出去）。
  与"投影只在一处"同一族理由：移动类分支有四五个，逐处守必然漏一处。
- `MonsterAiRuntime.Version` 11 → 12（这一族多了状态与闸口）。

**用例（`MonsterEditorKernelTests` 期 0 那一节，5 条，全绿）**

`控制状态的基线自己会走也会读条而没挂状态时逐字不变`、
`眩晕既不让它走也不让它读条而定身只拦腿沉默只拦嘴`、
`眩晕与沉默在施加那一刻打断在读的条而到期那一拍自己恢复`、
`状态表同档只刷新一条而填满之后再来同档条目数不变`、
`控制状态的词表与五行技能那一族同名而编号各归各`。
夹具刻意 `AllowMovement = true` + `leash = 200 米` + 目标在 40 米外 ⇒
"没动"只能来自闸口，不会来自"站桩施法该站住"或"走到 leash 边界回家"。

**四处如实登记（今天没做，不许被"控制层已落地"这句话覆盖）**

1. **Silence 是单边的**：玩家技能那一族（`WuxingStatusEffects.StatusKind`）
   只有 `ArmorDown/Vulnerability/DamageTakenDown/Stun/Root/Slow/Dot`，**没有沉默**
   ⇒ 沉默今天只能由怪物侧施加。这条由用例钉成显式清单（那一族补上沉默时用例会要求把清单收回）。
2. **沉默"打断在读的条"是刻意退化**：AC 只断 `PreventionType == SPELL_PREVENTION_TYPE_SILENCE`
   那一条（`Spell.cpp:6900`），本作**没有任何一格**记着预防类型 ⇒ 要么不做（沉默比 AC 更弱）、
   要么加一列（迁移）。今天取"也打断"（D36a 里写明的退化路线）。
3. **Slow 只登记不生效**：挂载上只有 `status_id` 与 `status_duration_seconds`，**没有幅度那一格**，
   所以随手写一个 50% 就是把猜测包装成配置；用例反向钉住"减速这一档不进任何闸口"。
4. **状态只装在 v2 那一条通路上**（表在 `MonsterRuntimeState` 里，v1 的怪 `Runtime == null`）
   ⇒ 期 1 要么把闸口上移到宿主那个两条通路汇合点，要么明确"控制状态只对开了能力位的怪生效"。
   生产那 11 只种子怪今天全是 `ai_profile=0`（走 v1）——**这一格不写清楚，期 1 就会做出
   "配了控制而生产怪一概不吃"的静默形状**（DR 未做、受击破控未做另计，见 §4 D31a）。

**门禁**：`Monster|Golden|Trace|Ds` 全组绿（期 0 净增 5 条内核用例）。

### 8.1 期 1a 落地（切片 59）：怪物的 `status_id` 第一次真的把玩家按住

- **施加路径**：`ResolveMonsterCast` 在命中结算之后把挂载的 `status_id / status_duration_seconds`
  施加到受害玩家（`MonsterStatusCore.TryFromId` 严格解析：不认识的号**拒收并计数**）。
  落点＝与伤害同一个条件（未命中/被闪避不挂控制）；"命中但零伤也要挂"需要"命中"信号，今天没有，不假装区分。
- **玩家状态表住在 DS**（D29a）：`DsPlayer.Statuses/StatusCount`，`TickPlayerStatuses` 是单点回收
  （玩家的"拍首"在宿主，因为玩家没有内核 tick）。
- **闸口**：`SubmitInput` 在被封腿时把**交给 sim 的那一份**换成"零移动 + 预测终点钉在当前位"
  （不动调用方的包；不然反作弊会把"声明跑了却走 0 米"当成瞬移纠偏）。
- **下发**：快照状态位收进唯一写口 `PlayerStateBitsOf`——`Stunned` 与新增的 `Rooted`（`1u << 6`，尾部追加不升协议版）
  第一次有写方；`Hidden`/`Frozen` 仍无来源，写口注释里留着它们的唯一登记处。
- **回执**：摘要新增 `受控玩家/状态施加/状态到期/状态被拒/输入被控`；另留 `PlayerStatusInterruptRequested`
  （DS 没有玩家施法权威 ⇒ 这一格只能是"请求了但没人执行"的证据）。
- **两条"会说谎的告警"被这次改动顶红，都是设计好的**：技能表死配置闸门的例外清单从三格缩到一格；
  校验器那句"状态号没有运行期消费者"改成**引用检查**（号不在词表 ⇒ Error；号在但没时长 ⇒ Warn 并点名 `.statusId`）。
- **仍未做**（每条都要单独立项，不许被"控制层已落地"覆盖）：玩家侧**打断读条**没有执行端（技能链在 grain 层）；
  怪物**自己**被控还缺内容来源（内核闸口已就位，没有"谁给怪上状态"）；`Slow` 没有幅度来源；
  DR 与受击破控按 D31a 未做；客户端表现（期 2）与 buffet 栏（D34a）未动。
- **补充（同一轮的收尾）**：受控期间的**技能类动作**（嘲讽/打断）现在会被拒——
  `SubmitCombatAction` 在解析目标与任何结算之前按 `PlayerCastingBlocked` 拒，并记 `CombatActionRejectedByStatus`；
  **普通近战照打**（AC 的 `AttackerStateUpdate` 只查 PACIFIED）。用例读的是同一格计数器在两次提交上的增量：
  眩晕中嘲讽 +1、普攻 +0，而含 `status_id=0` 的负对照两格都是 0（防"闸门恒真"）。
- **门禁**：`Monster|Golden|Trace|Ds` **997 / 0**；反证 `Tools/status-gates-falsify.py` PASS。

### 8.2 期 1b 落地（切片 60，协议 v16）：玩家技能第一次能把状态挂到**怪**身上

- 用户拍板"玩家技能给怪上状态（改协议）" ⇒ `CombatActionPacket` **末尾追加**
  `StatusId`（order 8）+ `StatusDurationSeconds`（order 9），`SyncProtocolVersion.Current` 15 → **16**，
  `MinimumSupported` 保持 5（沿用 v4/v10..v15 的追加纪律；协议往返用例改名并扩到这两格）。
- 宿主：`SubmitCombatAction` 在 `DealtDamage` 那一段里调 `ApplyStatusToMonster`——
  与"怪物打玩家"那条完全同形（落点同伤害：打空了不挂）。三个出口各自计数：
  `MonsterStatusAppliedCount` / `MonsterStatusRejectedCount`（号或时长非法）/
  `MonsterStatusUnsupportedCount`（**v1 的怪没有运行态、无处挂状态**——生产 11 只种子怪今天全走 v1，
  这一格涨就说明"配了控制而怪照旧追人"是覆盖边界而不是配错）。摘要新增三格。
- 用例 `玩家技能把眩晕挂到怪物身上而那只怪真的停住并在到期之后恢复`：
  施加 ≥1、受控窗口位移 < 1 米、到期后 ≥1 米；同一夹具换 `MonsterAiFlags.None`（v1）⇒ 施加 0、无运行态 ≥1。
  **这条用例自己红了三次，每次都是夹具的错、不是产品的错**（都写进注释）：
  ① 玩家站 +X 侧 ⇒ 默认朝向 +X 等于背对目标，一刀不落（这是本仓反复踩的坑）；
  ② 玩家贴着怪 ⇒ 它一到位就进 Attack（本来就不动），"停住"证明不了闸口 ⇒ 挪到 60 米让它整场都在赶路；
  ③ **每一刀都带状态 = 每秒重新晕一次**，整场都受控，"到期恢复"永远测不到 ⇒ 只让前两刀带。
- **仍未做**：`Slow` 的"幅度"那一格（用户已点头"加一列"，属迁移批次，未执行）；
  玩家侧读条打断仍在 grain 层；客户端表现（期 2）未动。
- **门禁**：`Monster|Golden|Trace|Ds` **998 / 0**；插件 rt+ed 各 0 错。

### 8.3 减速的数学先落地（切片 61，零写库）：强度那一格的内核与协议已就位，**数据来源仍缺**

- 用户拍板"加一列「幅度」"⇒ 内核先按那一列的语义写死规则，数据面留到**下一次迁移批次**一起做。
- `MonsterStatusEntry.MagnitudeBps`（基点，10000=100%；**0 = 没填 ⇒ 不生效**，与 AC 的"每式技能自带比例"同义）；
  `MonsterStatusCore.SpeedScale` 三条规则：**多条减速取最强那条**（AC `Unit::UpdateSpeed` 取最小语义；
  相乘是凭空发明的叠加规则）、上限 `MaxSlowMagnitudeBps = 9000`（10000 等于定身，两档会分不清）、0 不生效。
- `MonsterAiRuntime.Advance` 在两条闸口之后按比例缩放位移（`result.SlowedByStatus` 是效果层读数）——
  **减速不是闸口**：它只改速度，禁行动是定身/眩晕。
- 协议 v16 的 `CombatActionPacket` 同时带上 `StatusMagnitudeBps`（order 10）：玩家与怪物用**同一个**
  强度语义与**同一个**内核判据，不分成两套。
- 用例 `减速按强度缩放位移而强度没填时连速度也不改`：纯函数四条（1 / 0 不生效 / 5000→0.5 / 更强赢 7000→0.3 / 上限 50000→0.1）
  ＋效果层"整段减速后总位移 ≈ 基线的一半"（窗口覆盖全场；若只盖 120/300 拍，比值天然 0.8，
  那种期望会把正确实现读成错的——这一点写进注释）。
- **2026-10-28 收尾：这一列已经落库了**（用户点头"两个库都执行"）。`scripts/sql/026_add_monster_status_magnitude.sql`
  对 `Game_MonsterRT` 与 `Game` 各跑一次（幂等 `IF COL_LENGTH IS NULL`），`COL_LENGTH(...)=4` 两库实测；
  随后用**新装配**的 CLI 读了两库（`list` 12 / 11 行，无异常）——这一步是必要的：实体映射了新列，
  而库没这列时任何进程一读就是 `Invalid column name`。
  `status_magnitude_bps` 已接满六层：实体 `[Column]`+`[TableDescription]` → EF 模型快照 → 内容快照 DTO（含 `ToMount` 投影）
  → 草稿属性 + 草稿键 `statusMagnitudeBps`（CLI 与插件两份键表都加了，靠 `每一组同名清单的键集合逐字相同` 逼出来）
  → `MonsterSkillMount.StatusMagnitudeBps` → 内核 `SpeedScale`。新增闸门
  `状态强度这一格在实体快照模型草稿DTOCLI与内核五处同名且列名一致` 钉住六层 + 两个读写口 + DDL 文本。
  排练库真库往返：怪 3 的技能挂载写成 `statusId=4 / 5 秒 / 5000`，`save` 后 `get` 读回逐格相同。
  **同一轮补上面板那三格**（用户拍板那一列的初衷就是"编辑器那格不再是摆设"）：
  技能行的第四行加 `状态` 下拉（无/眩晕/沉默/定身/减速，**下标即 `status_id`**，中间不插翻译表）
  + `状态秒` + `强度bp`；行高 72 → 90（y+66 再 20 = 86，小于它就是最后一格被裁掉——那种"布局里看得见、控件摸不到"
  的洞用一个 `Height = 90f` 的针钉住）。提交与读回各一处，闸门 `面板技能行把控制状态三格真的摆出来并提交` 钉住。
  **仍缺**：DS 级"减速后位移真的减半"的效果层用例（要重新冻结夹具）；玩家读条打断仍在 grain 层。
  门禁：`Monster|Golden|Trace|Ds` **1002 / 0**；插件 rt+ed 各 0 错。

### 8.4 减速落到 DS（切片 63）：怪物侧有效果层证据，玩家侧只能读回执

- **怪物侧（有效果层）**：玩家的动作包带 `statusId=Slow + 5000` 打到怪身上之后，同一段 60 拍窗口里
  它只走了恢复后的 ≈一半（比值实测落在 0.4..0.62），强度填 0 时比值≈1（负对照）。
  沿用期 1b 那条"怪物受控"的夹具（玩家 60 米外让它整场赶路、状态只随前两刀带）。
- **玩家侧（只有机制回执）**：`SubmitInput` 在减速时把 `MoveX/MoveY` 与**预测终点**按 `SpeedScale` 缩小
  （`MaxSpeed` 不缩：它是属性上限，缩了会让移动校验看到"上限突降又突涨"），并记 `InputSlowedByStatus`
  （进摘要）。但**它没有效果层用例**：本仓离线夹具里玩家的位置由 `SimWorld` 按输入推进，
  而测试直接构造的 `InputPacket` 不会真的让玩家走起来（多次尝试两窗口位移都是 0）。
  这是**夹具能力缺口**，如实写在这里，不假装测过；要补的话得先有一条"能把玩家驱动起来"的测试宿主。
- **门禁**：`Monster|Golden|Trace|Ds` **1003 / 0**；插件 rt+ed 各 0 错；`Horizon.DsHost` 构建过。

### 8.4b 曾经的"玩家被咬住就走不动"：**定性是读数错（轴映射），产品无缺陷**（切片 68）

切片 63/67 两次量到"玩家每拍位移恒为 0"，一度怀疑是产品缺陷（战斗中被咬住走不动）。切开看是**读错轴**：
快照把 ECS 的 (x, y=深度) 映射到组件的 `(X, Z)`，我按同名轴读 `MoveY` 的位移，分量恒为 0。
判据是探针里把 XYZ 三个分量一起打印：沿 MoveY 走时 `Transform.X=6` 不动、`Transform.Z` 从 6.07 涨到 12.73，
`Transform.Y` 恒为 0。换成沿 MoveX 走、读 `Transform.X` 之后，同一分世界一切正常。
⇒ 玩家侧减速的效果层用例终于成立：三段（减速前/减速中/到期后）以**同一世界的实时位置**推进，
减速期每拍≈减半、到期后回到原速，负对照（强度 0）三段同速。
**教训**：跨坐标系读位移时，"读到了 0"先怀疑轴映射（见记忆里那条 y/z 交换），别先怀疑产品。
相关：[[test-traps-that-mask-the-verdict]]、[[3d-y-z]]。

### 8.5 期 2a（切片 64）：服务端那两位终于有客户端读端了

- `EntityStateAuthComponent` 补 `IsStunned` / `IsRooted` 访问器（与 `EntityStateBits` 同名单一主人）；
  原来那句"只有 Dead 与 Evading 两位真的被写过"按事实改成四位。
- `LocalPlayerActorSyncSystem` 在**同一个权威入口**（读 `EntityStateAuthComponent` 那一处）把两位镜像进
  `ControlStateManager`：位在 ⇒ 用 **1.5 秒 TTL** 刷新（比 1Hz 快照心跳略长，心跳间隙不闪）；位没了 ⇒ 立刻撤。
- `PlayerController` 产生移动输入的那一处新增 `LocalPlayerMovementLocked`：眩晕/定身时 `MoveX/MoveY=0`
  且 `inputBits=0`（连跳/轻功一起清）。**只在客户端预测里先拦住**，服务端仍然照旧抹输入——
  两边认同一位才不会出现"滑出去一段再被拉回"。
- 两侧 id 同源：都取 `NetworkIdentityComponent.EntityId`（与 DS 的 `characterId == entityId` 口径一致），
  闸门 `客户端的控制状态位真的接到了输入闸口而不是只镜像了一份` 把这一条与"TTL 要长于心跳"一起钉住。
- **今天仍未做**：名牌/操作受限提示（`ControlState.Silenced` 也没有位可镜像——沉默今天只拦怪物的嘴，
  玩家侧的沉默没有下发载体）；buff 栏（D34a 定的"本期不动"）；**第一次真像素**（要 PIE，编辑器归另一个会话）。
- **一条工作方式的事实**：客户端脚本编译用的是仓库 `bin/Debug/net10.0` 里的 Horizon 装配，
  所以改了 `Horizon.Game.Message` 之后必须**先 `dotnet build` 到默认输出**，否则 `verify-client-scripts.sh`
  会报"新访问器不存在"（本轮就是这样红的，49 行源码一条没动）。
- **门禁**：`Monster|Golden|Trace|Ds` **1004 / 0**；`verify-client-scripts.sh` **489 源 0 错**。

### 8.6 期 2b-1（切片 65）：沉默那一位打通，技能栏真的会拒

- **位**：`EntityStateBits.Silenced = 1u << 7`（`Rooted` 占了 1<<6，与 D33a 里写的编号顺序不同——位是自家的，
  照实记在这里而不是硬套那张表）；`EntityStateAuthComponent.IsSilenced`。
- **服务端**：`PlayerStateBitsOf` 在玩家身上有 `Silence` 时点亮这一位（眩晕不叠它——`Stunned` 已经表达"连施法也不行"，
  客户端读 `Stunned` 时本来就同时拦两样）。
- **客户端**：镜像进 `ControlState.Silenced`；`ActionBarService.TryCastSkill` 在 **`StartCooldown` 之前**拒掉
  "被眩晕/被沉默"的施法请求并给人话原因（白吃一次冷却比一次明确拒绝更难归因，与本文件里"打不到就别发"同一理由）。
  服务端结算侧仍是最终裁决——这条只把"按下去没反应"变成人话，并把预测与权威对齐。
- **用例**：`三档状态各自点亮自己的位而眩晕连带定身`（同一条怪、同一玩家、同一窗口，只差挂哪一档）：
  眩晕 ⇒ `Stunned|Rooted` 且不带 `Silenced`；沉默 ⇒ 只 `Silenced`；定身 ⇒ 只 `Rooted`。
  闸门 `沉默这一位从DS一路走到技能栏的拒绝且挡在冷却之前` 顺着链路走一遍并比位置（拒绝早于 `StartCooldown`）。
- **仍未做**：名牌/操作受限提示与 buff 栏（D34a 本期不动）；真像素；玩家侧读条打断（grain 层）。
- **门禁**：`Monster|Golden|Trace|Ds` **1006 / 0**；插件 rt+ed 各 0 错；`verify-client-scripts.sh` **489 源 0 错**。
