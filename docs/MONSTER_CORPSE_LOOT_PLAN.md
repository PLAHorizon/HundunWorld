# 尸体拾取窗口与掉落分配 —— 方案与决策清单（切片 31，**只出方案，等点头再写码**）

日期：2026-10-27。状态：**提案，代码一行未动、SQL 一条未生成**。
AC 部分全部是本机源码量出来的（`C:/Works/AzerothCore/azerothcore-wotlk`，WotLK 3.3.5）；
量不到的地方**明确写"未量到"**，不当已知用。
本作部分每条都标了"要动谁"。

触发这一期的原因是用户 2026-10-27 的一条拍板：**掉落与金币的最终形态按 AC 的"尸体上那一份 + 拾取窗口"推进**，
而不是维持"击杀瞬间直接进击杀者背包"，也不是"每人各掷一份"。

---

## 1. 为什么要改（本作现状，逐条有出处）

今天击杀一只怪的链路是这样的（全在 DS 侧一次算完）：

1. `HandleMonsterDeath` 里 `MonsterLootCore.Roll(bindings, killerCharacterId, templateId, _tick)` ——
   **掷骰的钥匙是击杀者一个人**；
2. 掷出来的东西立刻变成跨层事件：逐件 `Loot`、条件件 `LootConditional`、金币 `KillMoney`；
3. 网关那侧消费事件 → `IInventoryWriter.AddItemAsync` / `ICurrencyWriter.AddGoldAsync` → **直接进击杀者背包与钱包**；
4. 尸体本身另外存在：`MonsterCorpseCore.PlanExpiryTick` 把尸体窗口压到重生前一 tick，
   带 `StateBits.Dead`、名牌收起、打不到、选中会掉。

于是"尸体"在玩家眼里**只是视觉**：一具躺着的模型，右键它**什么列表都没有**。
这正是设计文档 §11.2-2 记的那句："corpse_seconds 现在买到的是看得见的一具尸体，不是能摸的尸体"。

**同轮顺手量到的一条静默缺陷**（本方案要一并说破，不是新造的）：
`SyncEventKind.Pickup = 6`（`Horizon.Game.Message/Sync/SyncPackets.cs:438`）**零生产者零消费者** ——
`grep -rn "SyncEventKind.Pickup"` 只命中枚举声明本身。这个档位在协议里躺着，
谁都可能以为"拾取已经有通道了"。本期落地时它要么被真的接上，要么在文档里写明"保留位、无人实现"，
**不许留成"看着像有"。**

## 2. AC 的权威形状（本机实测）

### 2.1 尸体上只有**一份**掉落，钱也是那一份里的一格

- `Unit.cpp:18050-18059`（击杀后生成掉落）：
  `Loot* loot = &creature->loot; loot->clear();` →
  `loot->FillLoot(lootid, LootTemplates_Creature, looter, false, false, creature->GetLootMode(), creature);` →
  `if (creature->GetLootMode()) loot->generateMoneyLoot(template->mingold, template->maxgold);`
- 也就是：**物品列表与金币都挂在怪物对象的 `loot` 成员上**，由击杀触发一次，
  不是每个参与者各掷一份；`FillLoot` 传的是 `looter`（谁开的），任务件那类才按请求者逐人判
  （`LootItemInSlot` 的 `qitem`/`ffaitem`/`conditem`）。
- 钱是**静态模板区间**（`creature_template.mingold/maxgold`，`ObjectMgr.cpp:654-655`），
  **没有等级公式**；`generateMoneyLoot`（`LootMgr.cpp:838-849`）的三条边界已在设计文档 §11.2-18 记过。
- 组队侧改的是"**谁能拿**"而不是"每人有一份"：`group->UpdateLooterGuid(creature)`
  维护 round-robin 的 looter 标签（`Unit.cpp:18061-18070`）。

### 2.2 拾取窗口的五个档位（`Loot/LootMgr.h`）

- `enum LootMethod`（`:56-62`）：`FREE_FOR_ALL=0 / ROUND_ROBIN=1 / MASTER_LOOT=2 / GROUP_LOOT=3 / NEED_BEFORE_GREED=4`
  —— 组自己存这一档（`Group.h:203/223/349` 的 `m_lootMethod`）。
- `enum LootSlotType`（`:112-118`，**每一格**给客户端的可见状态）：
  `ALLOW_LOOT=0 / ROLL_ONGOING=1（滚点进行中，不许拿）/ MASTER=2（只能由分配者给）/ LOCKED=3（红色显示，拿不了）/ OWNER=4（单人拾取，跳过绑定确认）`。
- 窗口容量是**客户端硬限制**：`#define MAX_NR_LOOT_ITEMS 18`（`LootMgr.h:51`，注释明写
  "the client cannot show more than 18 items in the loot window on 3.3.5a"）、
  `#define MAX_NR_QUEST_ITEMS 32`（`:53`）。**这条数字要进我们的判据**（超过 18 件时怎么裁，是策划要知道的事）。
- 拒绝理由是一份枚举（`LootError`，`:94`），其中与拾取窗口语义直接相关的四条：
  `DIDNT_KILL=0（你没权限）`、`TOO_FAR=4`、`BAD_FACING=5（必须面对尸体）`、`LOCKED=6（有人在翻了）`。
  **这四条正是"右键拾取"的全部前置判据清单**，本方案 §4 的闸门就是照着它摆的。
- `LootType`（`:76`）里怪物尸体是 `LOOT_CORPSE=1`，另有偷窃 `=2`／钓鱼 `=3`／分解 `=4`／剥皮 `=6`
  等**同一套窗口复用**。

### 2.3 期 0 已量到的部分（原判为"未量到"，本轮补上了）

- **那份 loot 什么时候没**：**在重生那一刻清**——`Creature.cpp:2052` 的重生成路径里
  `ResetPickPocketLootTime(); loot.clear(); SelectLevel();`（紧接 `setDeathState(JustRespawned)`），
  另有 `Creature.cpp:404` —— 在 `Creature::RemoveCorpse(bool setSpawnTime, bool skipVisibility)` 里
  （`loot.clear()` 与 `DestroyForVisiblePlayers()`、`RemoveAllAuras()` 同一段）。
  ⇒ AC 的准确规则是：**那份掉落活到"尸体被移除"与"重生"里先到那一个**，
  没有独立的"掉落过期计时器"——也就是**掉落列表与这只怪的"一次生命"同寿命**。
  ⇒ 这条对本方案的形状有决定性影响：**列表的失效判据 = 重生**，而本作已经有
  `MonsterCorpseCore.PlanExpiryTick` 把尸体窗口压到重生前一 tick，
  所以 D26 的"窗口长度"其实**不需要新常量**：列表跟着尸体窗口走，尸体没了列表也就没了。
- **尸体的可见时长不是模板列，是按稀有度查全局配置**：`Creature.cpp:1162-1178`
  依次取 `CONFIG_CORPSE_DECAY_RARE / ELITE / RAREELITE / WORLDBOSS / NORMAL`（精英+稀有还有一条 `× 2`），
  存进 `m_corpseDelay`。本作是**每模板一列 `corpse_seconds`**（更细、策划可控），
  这是一处**有意的偏离**，不是遗漏（AC 那两个数字的缺省值不需要抄：我们已经有模板级的 `corpse_seconds`，
  只要策划填得出数，行为就与 AC 同形）。
- **need/greed 的投票档位（AC 侧的形状，已量到位置）**：`enum RollVote : uint8`
  （`Groups/Group.h:48-56`）= `PASS=0 / NEED=1 / GREED=2 / DISENCHANT=3 / NOT_EMITED_YET=4 / NOT_VALID=5`；
  记票是 `typedef std::map<ObjectGuid, RollVote> PlayerVote`（`Group.h:156`），
  受理入口 `Group::CountRollVote(playerGUID, Guid, Choise)`（`Group.h:300`），
  团/raid 规模上限 `MAXGROUPSIZE`/`MAXRAIDSIZE 40`（`Group.h:44`）。
  ⇒ 对 D23 的直接含义：票种是**六种状态**（含 `NOT_EMITED_YET`/`NOT_VALID` 两个流程位），
  而"先收票、NEED 与 GREED 分两段结算"**这句已经在函数体里证实**（2026-10-27 补量，见下面 ①）。
  顺带一条自我更正：我上一版写的那三个函数名 `Group::DoRoll` / `HandleRollAccept` / `ConfirmLootRound`
  **在这份源码里根本不存在**（全库零命中），真实形状是 `Group::CountRollVote`（收票）+ `Roll::GroupLoot` /
  `Roll::NeedBeforeGreed`（分派）+ `Group::EndRoll`（超时/提前结束）——**照着不存在的名去读会读成一堵墙**，
  这也是"期 0 剩余测量"当时迟迟没开工的直接原因。
- 期 0 那三条"未量到"的，本轮全部量到了（出处都是本机 AC 源码，路径相对 `azerothcore-wotlk/`）：
  ① **need/greed 的结算**：`src/server/game/Groups/Group.cpp:1436` 与 `:1500`——need 那一段先跑，
     greed 那一段的进入条件是 `roll->totalNeed == 0 && roll->totalGreed > 0`（作者注释特意写着
     "是 `if` 不是 `else if`，因为跨地图的人的计数可能在上面那段里被改动"）⇒ **greed 永远顶不掉 need**；
     段内比大小是 `uint8 randomN = urand(1, 100); if (maxresul < randomN)`（`:1456-1458`）——
     **严格小于** ⇒ 平点不换手，赢家是**遍历里先碰到的那个**，而遍历序是 `playerVote` 这个 map 的键序
     （`ObjectGuid` 升序，`Group.h:156-157`），**不是入队序也不是伤害序**。
     结算完对 `vote != NOT_VALID` 的每个人广播 `SMSG_LOOT_ROLL_WON`（`:1600-1607`），
     全员 pass 时发 `SMSG_LOOT_ALL_PASSED` 并把 `is_blocked` 复位（列表还在、人人可拾，`:946-963`）。
     **掷点窗口时长 60 秒是硬编码字面量**（`SendLootStartRoll(60000, …)` `:1075/:1081`、
     `creature->m_groupLootTimer = 60000` `:1227`，递减在 `Entities/Creature/Creature.cpp:693-707`），
     **没有配置项**；没投票的人按 pass 处理（`EndRoll` 的注释写着"who didn't vote … he will pass"）。
     对我上一版那句"不要自己发明一个点数大者得的简化版"的更正：**AC 就是点数大者得**，
     需要照抄的不是"比大小"而是那三样——need 段先跑、平点判据、以及投票资格（下面 ④）。
  ② **拾取距离阈值**：`LootError::LOOT_ERROR_TOO_FAR = 4`（`game/Loot/LootMgr.h:97`），
     判距四处（开尸/取物/取币/掷点）共用同一个式子
     `if (!lootAllowed || !creature->IsWithinDistInMap(_player, INTERACTION_DISTANCE))`
     （`game/Handlers/LootHandler.cpp:87`、`:166`、`:340`、`:386`），
     `INTERACTION_DISTANCE = 5.5f`（`game/Entities/Object/ObjectDefines.h:24`，同级还有 `CONTACT 0.5`/`ATTACK 5.0`）。
     **另有第二个半径**管"谁有资格进掷点名单"：`Player::IsAtLootRewardDistance`
     （`game/Entities/Player/Player.cpp:12751-12784`）用 `CONFIG_GROUP_XP_DISTANCE`＝`MaxGroupXPDistance`＝**74.0f**
     （`game/World/WorldConfig.cpp:181`），副本内直接放行——**74 米这一档本作已经有同一个数**
     （`MonsterXpCore.MaxGroupXPDistance`，切片"组队经验"那轮照 AC 落的），所以这两档不是新常量。
  ③ **尸体留多久**：`WorldConfig.cpp:392-396` 给默认值 `Corpse.Decay.NORMAL=60 / RARE=300 / ELITE=300 /
     RAREELITE=300 / WORLDBOSS=3600`（单位**秒**，不是毫秒——上一版我按"缺省毫秒数"去找，方向就错了），
     `src/server/apps/worldserver/worldserver.conf.dist:2941-2945` 是这五项的对外说明、`:2953` 是
     `Rate.Corpse.Decay.Looted = 0.5`（拾空后加速移除，实现在 `Creature.cpp:2964-2983`）；
     分级表在 `Creature::Create` 的 rank switch（`Creature.cpp:1159-1180`，野外世界 BOSS 取 `ELITE×2`=600）。
     **更正 §2.3 那句"本仓库里没有 worldserver.conf.dist"**：它在 `src/server/apps/worldserver/` 下，
     我那次 `find` 没命中是我搜错了目录（`data/sql` 下确实没有），不是文件不存在。
  ④ **谁能进名单**：`Group.cpp:987-1001` + `:1183-1194` 先按 `IsAtGroupRewardDistance` 与 `CanRollOnItem`
     （`proto->MaxCount` 上限、`item.AllowedForPlayer`）过滤，不合格者当场写成 `PASS` 并广播 autoPass——
     即"**没资格的人不是不能投票，而是被系统替他投了 pass**"，这个形状要照，否则 UI 会给一个点不动的按钮。
  这四条都属于期 3/期 4 的前置，**现在都有出处了**；仍然没量的是"金币在一份尸体上怎么分配"
  （AC 的 `money` 在 roll 通路里不进 `playerVote`，走的仍是 `Loot::Money` 直接按人数分——**这句是待量，不是结论**）。

## 3. 本作已有的件（别重造）

| 已有的 | 在哪 | 本期怎么用 |
|---|---|---|
| 交互槽通道（含"被抢占"语义） | `SyncEventKind.InteractStart/InteractEnd/InteractStolen`、`InteractionSyncPacket{SlotIdx, InteractableId, InteractorId, StateBits, ServerTick}`（`SyncPackets.cs:439-444, 712-740`） | 窗口的**每格状态**天然对位 `LootSlotType`；`InteractStolen` 对位 `LOCKED=6（有人在翻）` |
| 交互意图上行处理 | `SyncInteractionHandler.HandleInteractionIntent(interactorId, interactableId, slotIdx, intentType)` + `SessionInteractionRateLimiter` | 右键/拿取的上行走这里，**但它是 Orleans 侧**（见 §4 的架构冲突） |
| 尸体窗口与状态位 | `MonsterCorpseCore.PlanExpiryTick`、`MarkMonsterDead`、`StateBits.Dead`、`MonsterCorpseCount/MonsterCorpsesLingering`（DS 侧） | 尸体的存在期已经归 DS，掉落列表**要住在同一侧**才不用跨权威来回问 |
| 逐件/条件/金币事件 | `DsGameplayEventKind.Loot / LootConditional / KillMoney` + `IInventoryWriter/IQuestStateReader/ICurrencyWriter` | "东西最终进包"这一跳**已经通了**，本期只是把它从"击杀即发"改成"拿取时才发" |
| 掉落掷骰内核 | `MonsterLootCore.Roll`（组互斥、逐件独立、条件、金币） | **掷的时机**要从击杀挪到"第一次开尸"，掷的钥匙从击杀者改成"这只怪的这份掉落" |

## 4. 唯一真正的架构决定：那份掉落列表住在哪一侧

本作现在是**双权威**（设计文档 §8 D13）：怪物归 DS 世界，角色/背包/任务归 Orleans。
"尸体上的一份掉落"横跨两边——尸体是 DS 的实体，背包是 Orleans 的。三条路线：

**路线 A：掉落列表住 DS（推荐，与 AC 同形）**
- DS 在 `DsMonster` 上带一份 `LootSlots`（第一次被开时按 AC 那样掷一次并缓存；后续开同一尸体读同一份）；
- 上行加一种"开尸/拿取第 N 格"的意图（复用 `CombatActionPacket` 那条已通的 DS 上行通路，不新建通道）；
- DS 校验四条 AC 判据（是不是击杀参与者 / 距离 / 尸体还在不在 / 那一格是不是已经被拿），
  然后把"这一件归谁"发成**现有的** `Loot`/`KillMoney` 事件；
- Orleans 侧写入端**零改动**（背包、钱包、条件件判定都已经在）。
- 代价：DS 多一份运行态（要进快照吗？——**不要**，它是每具尸体的私有列表，客户端开尸时按需下发即可）；
  金轨迹要重录（下行会多一种事件）。

**路线 B：掉落列表住 Orleans 的交互槽**
- 复用现成的 `InteractionSyncPacket` + `IZoneShardGrain.GenerateInteractionSync`，客户端侧改动最小；
- 代价：DS 击杀时要把掷骰结果**搬到 Orleans** 才能建槽，"尸体还在 DS、列表在 Orleans"
  意味着尸体到期/重生时两侧要各清一次（这正是本项目最容易长出"两份清单"的形状）；
  而且掷骰钥匙要从 DS 的确定性里挪出来，**AC 的确定性口径会丢**。

**路线 C：混合——列表住 DS，窗口状态用现成的交互槽包下发**
- 拿 A 的住处 + B 的包（不新增包类型）。
- 代价：同一概念两套状态位编码（DS 的 `LootSlotType` 对位 Orleans 的 `StateBits`），要有一张映射表并配"两边同数"的判据。

**这条必须由用户选**，因为它决定后面所有期；文档给的建议是 **A**（最贴 AC、且不引入跨权威的重复清单），
如果只想要"玩家能右键摸一下"的最小改动，C 的代价最低但会留一张映射表。

## 5. 决策清单（D21–D27）

- **D21**：路线 A / B / C（§4）。**这条不定，后面六条都无意义。**
- **D22**：掷骰时机——"第一次开尸时掷并缓存"（AC 形状）还是"击杀时就掷好、只是不给"？
  前者省运行态但把 `Roll` 的钥匙从 `killerCharacterId` 换成"开尸者"，会改变确定性分布；
  后者保持现在全部内核判据不变，只是把"发"延后。**建议后者**（改动面小、金轨迹影响可控），但要如实记下"与 AC 不同"。
- **D23**：拿取权限档位先支持哪几档？AC 有 5 种 `LootMethod`，本作今天只有"击杀者独得"。
  最小可信集合是 `FREE_FOR_ALL + ROUND_ROBIN`（单人随便拿、组队轮流优先）；
  `MASTER_LOOT / GROUP_LOOT(need-greed) / NEED_BEFORE_GREED` 各是独立一期。
- **D24**：金币怎么进窗口？AC 是窗口的**独立一格**，"谁拿到那一格"由权限档决定。
  本作现在 `KillMoney` 直接给击杀者；改成窗口后，金币也要走"拿取"这一跳，
  否则会出现"物品要摸、钱自动到手"的怪手感。**建议一起改**（与 D21 的路线一起落）。
- **D25**：窗口上限。AC 客户端硬顶 18 件（+32 任务件）。本作客户端是自己写的，
  要不要照抄 18？以及"这只怪配了 25 件掉落"时是**拒绝装配**（校验器 Error）还是**截断**？
  **建议校验器拒收并说明上限**，别默默截（本项目"静默裁产物"已经记过账）。
- **D26**：只剩一条要定：**"窗口到期时没人拿完"要不要给可见反馈**。
  窗口长度这一半**已被实测回答**（§2.3）：AC 没有独立的掉落过期计时器，那份列表活到
  "尸体移除"与"重生"里先到那一个（`Creature.cpp:404`、`:2052`），而本作
  `MonsterCorpseCore.PlanExpiryTick` 已经把尸体窗口压到重生前一 tick ⇒ **不需要新常量**，
  列表跟着尸体窗口走就行。要不要在到期时给一句"某某东西没拿成，随尸体消失了"，是唯一的选择项
  （AC 那边是静默消失，没有任何提示）。
- **D27**：拾取的前置判据要不要照抄 AC 那四条（`DIDNT_KILL / TOO_FAR / BAD_FACING / LOCKED`）。
  其中 `TOO_FAR` 需要 DS 知道请求者位置（有），`BAD_FACING` 要客户端算朝向（**当前协议不带朝向位，要做要升包**），
  `LOCKED` 需要一个"正在翻"的占用态（DS 侧新状态）。**建议第一期只做前两条 + LOCKED，不做 BAD_FACING**。

## 6. 分期（每条判据的形状也一并给出，避免"实现了但没人证明它在动"）

| 期 | 内容 | 判据（都要真跑，不接受编译当证据） |
|---|---|---|
| 期 0（先量后写） | **四条全部量到（2026-10-27）**：need/greed 结算、`TOO_FAR`＝`INTERACTION_DISTANCE 5.5f`、`Corpse.Decay.*` 缺省（**60/300/300/300/3600 秒**，不是毫秒）、掷点资格过滤与 autoPass —— 见 §2.3 | 只出文档，不动代码；**这一期的前置已经清了**，卡着的只剩 D21/D23 两个口径 |
| 期 1 | DS 侧每具尸体一份掉落列表 + 开尸上行 + 四条判据里先做三条 | 内核：同一尸体两次开得到**同一份**列表（确定性）；宿主：没参与击杀的人拿不到、离得太远拿不到、正在被翻时拿不到（各配正向对照） |
| 期 2 | 客户端窗口 UI（格位、类型、金币一格）+ 拿取才发事件 | 端到端：击杀后**背包里没有**，开尸拿第 1 格后**恰好多那 1 件**；不拿满就到期 → 一件都没进包；金轨迹重录（需单独批准） |
| 期 3 | `ROUND_ROBIN`（组队轮流）与 `LootSlotType` 五档可见状态 | 逐档对照：ROLL_ONGOING/LOCKED 那格必须"看得见但拿不了"，且拿不动要计数可见 |
| 期 4 | `MASTER_LOOT` / need-greed（**AC 的结算已量到**：`Group.cpp:1436`/`:1500` need 段先跑、`urand(1,100)` 严格小于、平点先遍历者得、60 秒硬编码——见 §2.3 ①） | 前置已清；仍等 D21（列表归 DS 还是 Orleans）与 D23 拍板 |

## 7. 风险与前置

- **金轨迹**：任何下行包的变化都要重录 `Traces/gameplay_trace_600.txt`，那是共享资产，**单独批准**。
- **并行会话**：`Horizon.Game.Core/Handlers/*`（`ChatHandler`、`IPlayerPushService`）与
  `Horizon.Game.Sim` → `Horizon.Game.Sim.Contracts` 的搬迁正在进行；
  开工前逐个文件看 mtime 与归属，本期若要动 `SyncInteractionHandler.cs` 也先确认没人改。
- **SQL**：本方案**不需要新表**（掉落列表是运行态，不是配置）。要加的是内容侧的
  `LootMethod` 缺省档之类的模板列——那才需要迁移，届时仍按"只生成不执行"。
- **不要顺手做的事**：剥皮/钓鱼/分解等其它 `LootType` 共用窗口（AC 有，本作没有对应玩法），
  以及 `BAD_FACING`（要升包）。

## 8. 一句话版本

AC 的拾取是"尸体上一份列表、五档分配方法、每格五种可见状态、18 件硬顶、四条拒绝理由"；
本作的尸体现在**只是视觉**，东西在击杀那一刻就进了击杀者口袋，
而协议里躺着一个没人实现的 `Pickup` 档位。**要推进必须先定 D21（那份列表住 DS 还是 Orleans）**，
其余六条都在它后面。

---

## 2026-10-28 拍板（用户答复，两项）

- **D21（拾取列表权威分层）＝ Orleans 权威**：掉落列表落 Grain/持久层，DS 只转发拾取请求。
  与"尸体只活 N 秒"的时间窗要显式处理：窗口到期时由权威侧判定不可再拾取，DS 侧不得自行擦账。
- **D22/D23（窗口参数）＝ AzerothCore 默认值，但可逐怪配置**：
  缺省 尸体 60 秒、拾取交互距离 `INTERACTION_DISTANCE 5.5` 米、被拾空后的衰减速率沿用 AC 缺省；
  这三项进怪物内容作为**可配格**（编辑器可改），缺省即 AC 默认值。

**据此的切片顺序（尚未实现）**：① 数据面两列（`corpse_loot_interval_seconds` / `pickup_distance_meters`）→
② 协议：拾取请求/回执（尾部追加，带尸体实体 id 与槽位）→ ③ Orleans 侧权威列表与取出幂等 →
④ DS 转发与窗口到期判定 → ⑤ 客户端拾取入口 → ⑥ 判据：同一具尸体第二次拾取必须拒、窗口到期后拾取必须拒、
两个人同时拾取同一槽位只允许一个成功、参数缺省值等于 AC 默认值（跨层常量直接断言相等）。

### 步骤 0（2026-10-28 已落）：拾取判据与 AC 默认常量

`Horizon.Game.Sim/Monsters/MonsterCorpseCore.cs` 新增 `DefaultLootWindowSeconds = 60f`、
`DefaultPickupDistanceMeters = 5.5f` 与 `PickupDenial { None, Expired, TooFar }` + `CanPickUp(...)`。
边界口径写死两条：**到期那一拍就已过期**（`now >= expiry`，与收尸同 tick 不许竞争）、
**正好站在 5.5 米上算够得着**；传 0 距离阈值时退回缺省而不是"谁都够不着"。
判据 `尸体拾取的三种结果可区分而到期那一拍已经取不了`（含四类边界 + 两个 AC 常量断言 + 0 阈值退回）。
**反证**：把 `now >= expiry` 改成 `>` ⇒ 该用例在"到期那一拍"那格红；复原后复绿。
**注意**：这一步**还没有接线**（DS/Orleans 尚未调用它），所以 §12.3 的"掉落"仍是**部分**。

### 步骤 1（2026-10-29 已落）：内容列与六层名字同步

两列进实体：`loot_window_seconds`（float，缺省 60）、`pickup_distance_meters`（float，缺省 5.5），
Order 32/33（29/30 已被 `xp_mod_bps`/`health_mod_bps` 占用）。贯穿：实体 → `MonsterContentSnapshot`（含 `ToConfig`）→
`MonsterRuntimeConfig` → `MonsterDraft` → `MonsterStore`（读/写/快照三处）→ `MonsterDraft.KnownTopLevelKeys` →
插件 `MonsterDraftModel.KnownTopLevelKeys` → 面板两格（`拾取窗口秒` / `拾取距离米`，装载+提交各一处）→ 校验器。
**DDL 只生成不执行**：`scripts/sql/027_add_monster_loot_window_columns.sql`（幂等、缺省即 AC、带回执查询）与
`revert_027_*.sql`；EF 迁移**没有留**——`dotnet ef migrations add` 会把并行会话未提交的实体（items 表多列）一起吞进同一条迁移，
按 [[ef-migration-swallows-parallel-entities]] 弃用，改走项目惯用的手写 SQL。

**判据**：面板闸门 **122 列 / 开口 0**（新增两格进"面板格"105）；契约 `拾取窗口两格按拍板口径校验且缺省就是AC的数`
（缺省=AC 两个常量、距离 0 = Error、窗口 0 = 只 Warn、窗口超尸体停留 = 只 Warn）；
怪物筛 **1039 / 0 / 1 skipped**；`MonsterEditorContractTests` 57/57（两处 `KnownTopLevelKeys` 同步后）。

**踩到的两处旧坑（记下来别再犯）**：① `dotnet ef migrations remove` 要连库，设计期连接串不可用时只能手工撤；
② 我先把 `GameEntityContextModelSnapshot.cs` 用 `git restore` 还原到 HEAD，结果**抹掉了并行会话的模型更新**、
契约测试当场红（找不到 `Entity("...MonsterTemplateEntity"`）——正确做法是重新 `add` 一次把快照刷成当前模型、
再删掉那条迁移文件，只留快照。

### 步骤 2a（2026-10-29 已落）：权威侧取出幂等原语（未接 Grain）

`Horizon.Game.Sim/Monsters/MonsterLootPickupCore.cs`：`MaxSlots = 64`（位图宽度）、
`PickupOutcome { Taken, AlreadyTaken, InvalidSlot }`、`TryTake(ref ulong, int)`、`IsTaken`、`RemainingCount`、`IsEmpty`。
判据顺序固定：**先越界 → 再已取 → 最后置位**（反了就会把越界读成"已取/取到"，玩家看到的是点一下掉一件空气）。
判据 `同一具尸体的同一个槽只可能被取走一次`：首次 Taken 且置位、重复 AlreadyTaken 且位图不变、
−1 与 64 都 InvalidSlot 且位图不变、63 号槽可用、`RemainingCount` 在 totalSlots 超上限时钳到 64、
`IsEmpty` 的三种边界（只取 0 号 ⇒ totalSlots=1 取空 / totalSlots=2 未空 / totalSlots=0 空尸体算取空）。
**未接线**：Grain 还没调它；这一步只把"两人同槽只一人成功"变成可离线对拍的原语。
（写的时候自己算错一次：以为"取了 0 号和 63 号"就等于前两格都空了——夹具的算术错，产品是对的，已改正。）

### 步骤 2b（2026-10-29 已落）：DS 侧窗口字段与门槛

`DsMonster` 增加 `CorpseLootExpireAtTick` 与 `PickupDistanceMeters`；死亡那一处用
`MonsterCorpseCore.PlanExpiryTick` 算窗口，并**与尸体到期取 min**（尸体先消失就无从拾取），
`RemoveMonsterFromWorld` 一并清零。新增 `DsWorldSession.CanLootCorpse(entityId, distance, out denial)`：
不在世界 / 不在尸体阶段 / 窗口过期 / 站太远 四种都分得开，且与客户端提示读同一个 `PickupDenial`。
**判据**：`尸体拾取窗口短于尸体停留时尸体还在但已经取不了`——夹具刻意让 窗口 5s < 尸体 30s < 刷新 120s，
断言两格差正好 1500 拍、5.5 米含边界 / 5.6 米 TooFar、推到窗口之后**尸体仍在**且拾取已 `Expired`。
**反证**：把 `Math.Min` 换成 `Math.Max` ⇒ `Expected: 1500 / Actual: 0`；复原后复绿。
怪物筛 **1041 / 0 / 1 skipped**。**仍未接**：协议与 Orleans 权威列表、客户端拾取入口。

## 2026-10-29 追加拍板（D23：入包时点）

**改为尸体拾取**：击杀只掷骰、**不再直接入包**；掉落挂在尸体上，玩家靠近取走才入包（与 AC 同形）。
代价已明确：既有"击杀即发"的多条用例与断言要改、客户端要拾取入口；**未拾取就随尸体消失**（AC 同样如此）。
落地顺序相应调整为：DS 在死亡那一拍把掷骰结果**扣在尸体上**（不再发 `Loot/LootConditional/KillMoney` 事件）→
Orleans 侧持权威列表（取出幂等）→ 客户端拾取请求 → 取走时才发入包事件 → 改动既有判据。
**无尸体（`corpse_seconds = 0`）的代码目录路径保持"击杀即发"**：那是"本功能之前"的逐字行为，金轨迹依赖它。

### 步骤 3（2026-10-29 已落）：DS 侧接入"尸体拾取"（默认安全关）

新增 `CorpseLootContracts.cs`：`ICorpseLootStore`（`RegisterAsync` / `RemoveAsync` / `TakeAsync`）、
`CorpseLootRegistration` / `CorpseLootSlotDto` / `CorpseLootTakeStatus`，以及**只给测试用的**
`InMemoryCorpseLootStore`（与 `MonsterCorpseLootState` 同一套判据，差别只在谁持锁）。
DS 构造器多一个可空参数 `corpseLootStore`：**null = 走旧行为（击杀即发）**——这是故意的安全门，
客户端拾取入口就绪前把掉落扣在尸体上会让玩家永远拿不到。
非 null 时：死亡那一拍把掷骰结果按"普通件 → 条件件 → 金币"登记给权威侧、**不再发** `Loot/KillMoney/LootConditional`；
尸体彻底离开世界（到期收尸 / 显式摘除 / 重生覆盖）时 `RemoveAsync` 释放登记；
新增 `TryLootCorpse(entityId, sessionId, slot, distance)`：DS 先过自己的门（`CanLootCorpse`），
再由权威侧裁决，**取走才**发对应入包事件（金币→`KillMoney`、无条件→`Loot`、条件件→`LootConditional`）。
计数：`MonsterCorpseLootRegistered` / `MonsterCorpseLootRegisterFailures` / `MonsterCorpseLootTaken`。
把死亡期限计算抽成 `ComputeCorpseExpiries(...)`（尸体停留 / 拾取窗口 / 距离一份式子，两处共用）。

**判据**：`接入权威存储后击杀不再直接入包而取走才发事件`——击杀期间 `Loot/KillMoney` 各 0 条、
登记 1 次；取物品槽发一条 `Loot`（555×2、记在取走者头上）、重复取 `AlreadyTaken` 且不再发；
金币槽取走才发 `KillMoney`=10；站 100 米 `TooFar`；尸体到期后登记释放（`store.Count=0`）且 DS 报 `Expired`、
直接问存储报 `UnknownCorpse`。
**反证**：把 `deferLootToCorpse` 写死 `false`（旧路径没关）⇒ `Expected: 0 / Actual: 1`；复原后复绿。
**顺带修**：金币链契约原来钉"`KillMoney` 只有一处"，现在事实是**两处**（旧路径 + 拾取）——
改成断言 2 并写清两条各有判据；不改松，只改准。
**两处测试自身的坑**：① 攻击朝向传反（`MathF.PI`）⇒ 怪一直不死，整条用例会在"没有掉落"上绿掉一大片，
已加"怪必须真的死了"的前置断言；② 尸体消失后 DS 报 `Expired`（与 `CanLootCorpse` 同源）才是对的，
`UnknownCorpse` 是权威侧内部语义，另在存储上单独断。
怪物筛 **1043 / 0 / 1 skipped**。**还没做**：真 Grain、上行事件接线、客户端拾取入口。

### 步骤 3b（2026-10-29 已落）：摘要三格

`Horizon.DsHost` 周期摘要新增 `尸体登记=` / `尸体取走=` / `尸体登记失败=`（`MonsterCorpseLootRegistered` /
`MonsterCorpseLootTaken` / `MonsterCorpseLootRegisterFailures`）；判据补进既有"三格都进了周期摘要"用例。
全量 `Horizon.Game.Gateway.Tests` **3984 / 0 / 8 skipped**。

### 下一步的接线图（已核过现状，避免下一轮重新摸）

- **DS 宿主（Horizon.DsHost）没有 Orleans 引用**（csproj 里没有 Orleans 包，只有 `IDsGameplayEventSender` 这条上行），
  所以"DS 转发拾取请求给权威侧"必须新增**上行请求/应答**（或由网关代发）。
- 现成的通路是单向的：`DsUplinkProtocol`（帧）+ `OrleansGameplayWiring`（事件→writer）。
  要在这里加一条带关联 id 的请求，等于协议尾部追加 + 网关 handler + 回执下行，和 v16/v17 那两轮同规模。
- 因此**下一段的顺序**：① 在 `DsUplinkProtocol` 尾部追加 `CorpseLootTakeRequest/Reply`（带 corpseEntityId/slot/version）；
  ② 网关侧新增 handler：把请求转成 `CorpseLootGrain.TakeAsync`；③ Grain 用 `MonsterCorpseLootState` 做状态（已就绪）；
  ④ DS 收到回执后走 `TryLootCorpse` 的入包事件；⑤ 客户端拾取入口与提示（编辑器空闲时做）。
- **开关**：DS 构造器的 `corpseLootStore` 仍为 null 时行为完全不变；接上适配器那一刻才切到"尸体拾取"。

### 步骤 4（2026-10-29 已落）：拾取三帧与上行收发口

`DsUplinkMessageKind` 尾部追加三档（数值钉死）：`CorpseLootRequest = 10`（网关→DS）、
`CorpseLootForward = 11`（DS→网关）、`CorpseLootResult = 12`（网关→DS）；对应 `DsCorpseLootRequest` /
`DsCorpseLootForward` / `DsCorpseLootResult` 三个 MemoryPack DTO。
`DsUplinkClient` 加：`CorpseLootRequestReceived` / `CorpseLootResultReceived` 两个事件（各带计数）、
`SendCorpseLootForwardAsync`（成功/丢弃两格计数——"点了没反应而日志一行没有"是所有静默丢弃里最难查的一种）。
**判据**：`尸体拾取三帧的编解码往返且种类号只是尾部追加`——三档数值等于 10/11/12（钉数值不钉名字：
插到中间会让新旧两端把请求当回执），三个 DTO 逐字段往返（含 `0xDEADBEEFCAFE` 这种高位实体 id）。
全量 `Horizon.Game.Gateway.Tests` **3985 / 0 / 8 skipped**。
**还没做**：DS 的入站处理（校验后转发）与回执落地成入包事件、网关侧 handler 与 Grain、客户端入口。

### 步骤 5（2026-10-29 已落）：DS 侧三段（登记 → 请求/转发 → 回执）+ 本地两道门

- 选项 `DeferCorpseLootToAuthority`（默认 false）：开启后击杀只登记、不发入包事件；**与"有没有本地存储"解耦**
  （本轮实测踩到：`RegisterCorpseLoot` 顶部那条"没有本地存储就 return"把上行副本也挡了，已改）。
- 上送盒：`DrainCorpseLootRegistrations()` / `DrainCorpseLootGone()`（宿主每 tick 取走发送）；尸体标志 `CorpseLootRegistered`
  决定收尸时是否发作废帧。
- 两段公开 API：`HandleCorpseLootRequest(request, out forward, out denial)`（玩家活着 + `CanLootCorpse` 两道门，
  不过就当场回同状态；过了记 pending 并产出 forward）、`HandleCorpseLootResult(result)`
  （只认 `Taken`，且同 `RequestId` 只认一次；`CorpseLootResultUnmatched` 计数）。
- 待回执请求约 10 秒过期（`ExpirePendingCorpseLoot`，tick 调用，`CorpseLootPendingExpired` 计数）。
- 发放抽成 `EmitCorpseLootGrant`，**本地存储路径与上行路径共用**（金币/无条件/条件件三档只有一处）。
- **判据**：`权威裁决走请求转发回执三段而本地只挡距离与窗口`——击杀期 0 条 Loot；登记帧 1 且槽序=普通件→金币；
  100 米当场拒（`TooFar`，不转发）；合法请求产出 forward（带 RequestId 与 DS tick）；回执 `Taken` 才发 Loot（555×2、记在发起人）；
  同一回执再到达不算数（`Unmatched=1`）；到期发作废帧。
  **反证**：把本地门短路成 `false &&` ⇒ 远距离请求也转发（`Expected: False / Actual: True`）；复原后复绿。
  全量 `Horizon.Game.Gateway.Tests` **3986 / 0 / 8 skipped**。
- **还没做**：网关侧 handler（把 Forward 转 Grain、把 Register/Gone 转 Grain 状态）、`CorpseLootGrain` 本体、
  客户端拾取入口；`DeferCorpseLootToAuthority` 在生产保持 false。

### 步骤 6（2026-10-29 已落）：权威 Grain + 网关映射（服务器半边闭环）

- `Horizon.Orleans.Interface/Corpse/ICorpseLootGrain`（键=尸体实体 id）+ `CorpseLootRegistrationWire` / `CorpseLootSlotWire` /
  `CorpseLootTakeReply`（`[GenerateSerializer]`/`[Id]`，这是**Grain 边界**那一份，与上行协议的同类名不共用）。
- `Horizon.Orleans.Grains/Corpse/CorpseLootGrain`：状态直接用 Sim 的 `MonsterCorpseLootState`（窗口/距离/幂等三条判据不在 Grain 里重写），
  纯内存、`RemoveAsync` 后 `DeactivateOnIdle`（尸体只活几十秒，持久化反而会让"重启后还挂着该没的尸体"更难清）。
- `Horizon.Game.Core/Sim/Server/CorpseLootUplinkHandler`：帧 → 存储 → 回执的映射（放在 Core 才能在不起集群时单测）；
  空登记拒收；`ForwardedCount` / `RegisterFailures` 计数。
  **距离传 0 而不是 MaxValue**：距离由 DS 按权威坐标量过，权威侧只剩窗口与幂等；初始版本传 MaxValue 被当场判 `TooFar`（实测红）。
- `Horizon.Game.Gateway/Services/CorpseLootGrainStore`：`ICorpseLootStore` 的 Orleans 实现（键按位转 `long`）。
- `DsUplinkServer` 三个 case：Register/Gone 单向、Forward 处理后把 `CorpseLootResult` **写回同一连接**（写失败要留日志）。
  DI：`ICorpseLootStore` + `CorpseLootUplinkHandler` 单例。
- **判据**：`网关处理器把登记转发回执串成一条链`（空登记拒收、Taken→555×2、重复→AlreadyTaken、
  金币槽→Money=10、作废后→UnknownCorpse、回执带原请求号）。全量 **3987 / 0 / 8 skipped**。
- **还没做**：DS 宿主把事件接进会话（订阅 + 每 tick 上送/转发）、游戏协议给客户端的请求/回执两帧、客户端拾取 UI；
  `DeferCorpseLootToAuthority` 生产仍为 false。

### 步骤 7（2026-10-29 已落）：DS 宿主接上上行链路（灰度默认关）

- `DsHostRunner`：两条入站队列（请求/回执，读循环线程只入队）+ `NotifyCorpseLootRequest/Result`；
  tick 尾部 `FlushCorpseLootAsync` 四段：**先**消费入站（请求 → 本地门 → 转发或当场拒并回同状态回执；回执 → 结账），
  **再**上送本 tick 产生的登记/作废帧（顺序有意：回执先结账，登记晚一拍发也不会漏）。
- 计数五格进周期摘要：`尸体上送` / `尸体作废` / `拾取转发` / `拾取当场拒` / `拾取入包`。
- `IDsCorpseLootSender` 四个发送口；`DsUplinkClient` 实现之（登记/作废/回执/转发，含丢弃计数）。
- 灰度：`DsHostOptions.DeferCorpseLootToAuthority` ← 环境变量 `CorpseLoot__Enabled=1`，**默认关**
  （客户端拾取入口与网关路由就绪前开启会让掉落拳在没人能取的地方）。
- 全量 `Horizon.Game.Gateway.Tests` **3987 / 0 / 8 skipped**。
- **只剩最后一段**：游戏协议给客户端的「拾取请求 / 结果」两帧 + 网关把 DS 的回执路由到发起会话 + 客户端拾取 UI；
  之后才能把 `CorpseLoot__Enabled` 打开。

### 步骤 8（2026-10-29 已落）：客户端两帧 + DS 客户端入口（距离在 DS 量）

- 游戏协议尾部追加：`SyncPacketKind.CorpseLootTakeRequest = 17` / `CorpseLootTakeResult = 18`，
  联合编号 `MemoryPackUnion(16/17)`；`CorpseLootTakeRequestPacket`（CharacterId/CorpseEntityId/SlotIndex）、
  `CorpseLootTakeResultPacket`（CorpseEntityId/SlotIndex/Status/ItemId/Count/Money）。
- 判据 `尸体拾取两包的编号是尾部追加且能往返`：编号钉死 + 两包经 `SyncPacket` 联合序列化逐字段往返。
- `DsWorldSession.HandleCorpseLootTakeFromClient`：**距离由 DS 量**（`_simWorld.GetEntity` 玩家坐标 vs 尸体坐标），
  再走与上行请求完全相同的两道门；受理 → 排进转发队列，拒绝 → 排进拒绝队列（回执带 `SessionId`，
  网关要凭它找连接）。`DsCorpseLootResult` 增加 `SessionId` 字段。
- `DsDataPlaneHost` 新增 `CorpseLootTakeRequestPacket` 分支（不受战斗动作节流约束，一点一次），
  计数 `CorpseLootRequestsSeen` / `CorpseLootRejected`；宿主 `FlushCorpseLootAsync` 增排空转发与拒绝。
- **判据**：`客户端拾取请求在DS侧量距离并把转发与当场拒分开排队`——受理产出 forward（会话号/尸体/本拍 tick）；
  尸体到期后当场拒且回执带会话号。
- 全量 `Horizon.Game.Gateway.Tests` **3989 / 0 / 8 skipped**。
- **还没做**：网关把客户端请求包转给 DS（`SyncPacketHandler` 分支 + `TryRouteToDsAsync`）、
  网关把 DS 的回执/权威结果转成 `CorpseLootTakeResultPacket` 发回客户端、客户端拾取 UI；灰度开关仍默认关。

### 步骤 9（2026-10-29 已落）：网关路由 + 结果直接下行（服务器全链闭合）

- `SyncCombatHandler.HandleCorpseLootTakeAsync`：**只有 DS 权威承载的会话才转**（没 DS 世界就没有"尸体"这件事），
  转成功后返回 null（结果由 DS 自己下行）；非 DS 会话回 `UnknownCorpse`，客户端当场拿到理由。
- `SyncPacketHandler` 分发新增该包的 case。
- DS 侧改为**直接下行结果**：`HandleCorpseLootResult` 成功时排一条 `CorpseLootTakeResultPacket` 给该会话；
  当场拒也排一条（带状态）。为此删除上一轮的"拒绝回执走上行"队列（避免两条路各发一份）。
  `DsCorpseLootResult` 仍保留（网关→DS 的权威回执用它），新增 `CorpseLootResultPacketsQueued` 计数。
- 判据更新：`客户端拾取请求在DS侧量距离…` 末段改为断言"拒绝也排了结果包"（`CorpseLootResultPacketsQueued >= 1`）。
- 全量 `Horizon.Game.Gateway.Tests` **3989 / 0 / 8 skipped**。
- **服务器全链已闭合**：客户端包 → 网关路由 → DS 数据面 → 本地两门 → 上行转发 → 网关映射 → Grain 裁决 →
  回执 → DS 入包事件 + 结果包下行。**只剩客户端拾取 UI**（Flax 侧代码；编辑器在跑，按纪律不动客户端源码）。

### 步骤 10（2026-10-29 已落）：Grain 的无集群判据

`CorpseLootGrain` 此前只有"编译通过"这一层证据。新增 `CorpseLootGrainTests`（直接 `new` Grain，不起集群）：
未登记 → `UnknownCorpse`（7）；取走 → Taken 且带槽内容；重复 → AlreadyTaken 且**不带**内容；太远/到期 → 4/3；
金币槽 → Money=10；越界 → InvalidSlot(5)；同 id 重新登记 → 掩码重置且新窗口生效。
**反证**：把"未登记"返回改成 `Taken` ⇒ `Expected 7 / Actual 1`；复原后复绿。全量 **3990 / 0 / 8 skipped**。

### 步骤 11（2026-10-29 已落）：进程内端到端（接缝判据）

新增 `拾取链从客户端入口到入包与结果包在进程内走通一遍`：`DsWorldSession`（灰度开）+ `InMemoryCorpseLootStore`
+ `CorpseLootUplinkHandler` 按真实顺序串起来——
① 击杀 → 登记帧 → 网关处理器 → 权威存储；② 客户端入口点槽 0 → 转发帧 → 处理器 → 回执；
③ DS 结账 → 一条 `Loot` 事件（555×2、记在发起人）+ 一条 `CorpseLootTakeResultPacket` 下行；
④ 再点一次 → `AlreadyTaken` → **不发包也不发事件**。
**反证**：把"非 Taken 也发放"的守卫短路 ⇒ ④ 立刻红（`Expected False / Actual True`）；复原后复绿。
全量 **3991 / 0 / 8 skipped**。这一条补上了"各段分开测都绿、接缝却错"的盲区。

### 客户端接入清单（2026-10-29 只读核对，代码未动——编辑器在跑）

| 环节 | 落点（已核实） | 要做的事 |
|---|---|---|
| 发包 | `Source/Game/Combat/CombatReporter.cs:108`（`ReportToDsWorld` 用 `GuardSyncSender.SendLocalAsync`） | 照抄成 `ReportCorpseLoot(corpseEntityId, slotIndex)`，发 `CorpseLootTakeRequestPacket`（`CharacterId`=本地角色） |
| 触发 | `Selection/WorldSelectionService.cs:205 TryInteractSelected()`（G 键，现有分支只认 Npc/Item/GatherNode） | 加 `Monster/尸体` 分支；**F 被攀爬占着**，别抢 |
| 收包 | `Network/MessageHandlers/SyncPacketMessageHandler.cs:105-175`（switch；新 kind 目前落 default） | 加 `case CorpseLootTakeResultPacket` → 事件；订阅点在 `HundunWorldGame.TrySubscribeSyncHandler:1025` |
| 提示 | `UI/UIAnimationSystem.cs:253 ShowLootToast(itemName, count, quality)` / `UIHelper.ShowToast:555` | 取到 → `ShowLootToast`；拒绝 → `ShowToast` 并**按状态给不同话术**（太远/过期/已取/越界） |
| 主线程 | `WorldSelectionService.cs:189 TryTakeInteractionResult` 的"轮询待处理结果"写法 | 网络回调只置标记，`OnUpdate` 里再碰 UI |

**核对时发现的真缺口（服务器侧可先做）**：死亡那一拍 DS 会 `QueueEntityDescriptor(..., removed: true)`
（语义是"玩家面前可交互对象"），于是**尸体不在注册表里 ⇒ 客户端 G 键根本选不到它**。
所以客户端入口的前置是：让注册表能表达"尸体仍可交互"。两条可选路线——
① 给 `DsEntityDescriptor` 尾部追加一个 `CorpseInteractable` 位（旧客户端忽略未知尾部成员，前向兼容）；
② 新增一个 `ScriptedKind.Corpse`（语义更清楚，但客户端要认识这个 kind 才显示）。
**先做 ①**：服务端追加位 + 断言"死亡后仍发一条带该位的描述符、到期时随 Despawn 一起消失"；
客户端那一半等编辑器空闲再一起做。

### 步骤 12（2026-10-29 已落）：尸体"可交互但已死"的描述符位

`DsEntityDescriptor` / `DsEntityDescriptorDto` 尾部追加 `CorpseInteractable`（旧网关忽略未知成员）。
死亡那一拍按灰度决定形态：`DeferCorpseLootToAuthority && CorpseLootExpireAtTick>0` ⇒ 发 `removed:false + CorpseInteractable:true`
（尸体**留在注册表**，客户端 G 键才选得到）；否则照旧 `removed:true`（本功能之前的行为逐字不变）。
**判据**：`开了尸体拾取后尸体会带可交互位留在注册表而未开时照旧当场摘除`——同一夹具只差开关：
开 ⇒ `Removed=false / CorpseInteractable=true`；关 ⇒ `Removed=true / CorpseInteractable=false`。
**反证**：把条件写死 `false` ⇒ 开灰度那半报"死亡时把尸体从注册表摘除了"；复原后复绿。
**一处夹具坑**：描述符每拍出会话一次就被清空，必须在击杀循环里累计——"再补一拍"读不到死亡那条。
全量 **3992 / 0 / 8 skipped**（整 4000 条）。

### 客户端待粘贴代码（2026-10-29 依现有签名起草，**未编译验证**）

> 纪律：这几段**没有进仓库源码**（编辑器在跑）。落地时逐段粘贴、用编辑器自带构建 + PIE 验证，
> 任何一段编译不过都不要"改到能过"了事——那通常意味着我读错了签名。

**① 发包（`Source/Game/Combat/CombatReporter.cs`，照 `ReportToDsWorld` 的写法）**
```csharp
public static void ReportCorpseLoot(GuardSyncSender guardSender, ulong localCharacterId, ulong corpseEntityId, int slotIndex)
{
    if (guardSender is null || localCharacterId == 0UL) return;
    var packet = new Horizon.Game.Message.Sync.CorpseLootTakeRequestPacket
    {
        Kind = Horizon.Game.Message.Sync.SyncPacketKind.CorpseLootTakeRequest,
        CharacterId = localCharacterId,
        CorpseEntityId = corpseEntityId,
        SlotIndex = slotIndex,
    };
    _ = guardSender.SendLocalAsync(packet, localCharacterId);
}
```

**② 触发（`Selection/WorldSelectionService.TryInteractSelected`：在现有 kind 过滤里加一档）**
```csharp
// 尸体：Kind 仍是 Monster。可拾取与否读**快照状态位**（EntityStateBits.CorpseInteractable），
// 别读网关的实体描述符——那份东西客户端收不到（步骤 18）。
if (selected.Kind == SelectableKind.Monster && selected.CorpseInteractable)
{
    CombatReporter.ReportCorpseLoot(_guardSender, _localCharacterId, selected.EntityId, 0);
    return true;   // 槽号先固定 0：拾取窗口一期只取"第一个没被取走的槽"，多槽 UI 另起
}
```
其中 `SelectableEntityTag.CorpseInteractable` 是新增的一格，由 `FlaxActorSyncSystem` 每快照从
`EntityStateAuthComponent.IsCorpseInteractable` 搬下来（与 `IsDead` 同一处、同一口径）。

**③ 收包（`Network/MessageHandlers/SyncPacketMessageHandler.cs` 的 switch）**
```csharp
case Horizon.Game.Message.Sync.CorpseLootTakeResultPacket loot:
    CorpseLootResultReceived?.Invoke(loot);
    break;
```

**④ 提示（订阅处，拷 `WorldSelectionService` 的"回调只置标记、OnUpdate 再碰 UI"写法）**
```csharp
private Horizon.Game.Message.Sync.CorpseLootTakeResultPacket _pendingLootResult;
// OnUpdate:
if (_pendingLootResult is { } r)
{
    _pendingLootResult = null;
    if (r.Status == 1)
    {
        if (r.Money > 0) UIHelper.ShowToast($"获得 {r.Money} 铜", ToastType.Loot);
        else UIAnimationSystem.ShowLootToast(ItemNameOf(r.ItemId), r.Count, 0);
    }
    else
    {
        // 五种拒绝各说各的：太远/过期/已取/越界/不是你的——压成"拾取失败"等于把归因扔掉
        UIHelper.ShowToast(r.Status switch
        {
            2 => "已经被取走了",
            3 => "太晚了，尸体已经不能取",
            4 => "离得太远",
            5 => "这一格没有东西",
            8 => "这不是你打的怪，取不了",
            _ => "取不了",
        });
    }
}
```

**⑤ 窗口到期提示（`SyncEventKind.CorpseLootExpired = 16`；在现有事件 switch 里加一档）**
```csharp
case Horizon.Game.Message.Sync.SyncEventKind.CorpseLootExpired ev:
    // 服务端只在"窗口关了但还有东西没拿"时发，每人一次；IntValue = 没拿走的件数
    UIHelper.ShowToast($"尸体上还有 {ev.IntValue} 件没拿，已经不能取了");
    break;
```

**落地顺序与判据**：①+③ 先落地（能收能发，无 UI）→ 编辑器构建绿 + 抓包看请求/回执 → ②+④（G 键 + 提示）→
打开 `CorpseLoot__Enabled=1` → PIE 里杀一只、G 取、看背包与提示。**多槽拾取 UI**（列表、逐个取、全取）另起一期。

### 步骤 13（2026-10-29 已落）：「取下一件」（槽号 -1）

AC 的右键连取是"一件一件拿完"，而客户端不该自己跟踪槽号——槽序是只能往尾部追加的持久化契约，
把它复制到客户端迟早分叉。所以权威侧接受 `slotIndex < 0` = **取第一个没被取走的槽**，并在回执里**带回实际槽号**：
`MonsterCorpseLootState.TryTakeFirst` → `CorpseLootGrain.TakeAsync(-1)`（回执带 `SlotIndex`）→
`CorpseLootTakeResult.SlotIndex` → 网关处理器写进 `DsCorpseLootResult.SlotIndex` → DS 用它填结果包
（`result.SlotIndex >= 0 ? … : pending.SlotIndex`）。
取空时返回 `AlreadyTaken` 而不是 `Empty`——"取空了"与"本来没东西"对玩家是两句话。
**判据**：Grain 用例连取两次（0 号物品 → 1 号金币，槽号逐个正确），第三次 `AlreadyTaken`。全量 **3992 / 0 / 8 skipped**。

**步骤 13 的接缝判据（同轮补）**：进程内端到端用例扩展到「取下一件」——
点 0 号槽（物品）→ 再以 -1 连取（金币，回执与结果包的 `SlotIndex` 都是 1、Money=10）→
第三次 -1 得到 `AlreadyTaken` 且**不发包不发事件**。
**反证**：让 DS 忽略回执里的槽号（退回用请求时的 -1）⇒ 结果包槽号断言 `Expected 1 / Actual -1`；复原后复绿。
全量 **3992 / 0 / 8 skipped**。

**逐格往返脚本已跟上（同轮）**：`Tools/db-roundtrip-check.py` 的草稿模板补了 `lootWindowSeconds=33.5` /
`pickupDistanceMeters=7.25`（非缺省值）。**前提**：`scripts/sql/027_*.sql` 先执行；未执行时这两格会在 SAVE 报错——
那正是这条脚本该有的行为（新列没落库就不该"往返成功"）。今天**没有运行它**（库还没那两列），
所以"两格真的能往返"这一条仍待迁移执行后补跑。

## 2026-10-29 追加拍板（D24–D26：谁能取 / 掉几件 / 到期提示）

| 编号 | 问题 | 用户拍板 | 落地口径 |
| --- | --- | --- | --- |
| D24 | 尸体上的东西谁能取 | **击杀参与者**（AC `DIDNT_KILL`） | 名单在死亡那一刻冻结，DS 本地门与权威侧看**同一份** |
| D25 | 一具尸体最多掉几件 | **18 件 + 32 任务件** | 按桶分（AC `MAX_NR_LOOT_ITEMS` / `MAX_NR_QUEST_ITEMS`），金币槽另算 |
| D26 | 窗口到期没拿完要不要提示 | **给一条提示** | 每人一条、同一具尸体只发一次；拿空了不发 |

### 步骤 14（2026-10-29 已落）：D24–D26 三条口径

**D24 拾取权限**。新增 `MonsterCorpseCore.IsAllowedLooter(contributors, takerCharacterId)`：
名单空 = 不限制（环境击杀/自爆的怪没有任何玩家贡献，历史登记也没有这一格），
`takerCharacterId == 0`（认不出是谁）**不能**通过非空名单——漏传身份要表现成明确的拒绝，而不是悄悄放行。
- 名单来源：`DsMonster.Contributors`（既有经验账本，`MonsterContributors`）。
  死亡登记那一刻拷成 `DsMonster.CorpseLootContributors`，**本地门读它、登记帧也读它**：
  两处各读各的（一处活账本、一处网络快照）迟早分家。
- 判据落在 `MonsterCorpseLootState.TryTake/TryTakeFirst`（`LootTakeResult.NotYours`），
  顺序是 **空/越界 → 不是你的 → 过期 → 太远 → 已取 → 取走**：对没有资格的人回答"太远了"，
  等于告诉他走近一点就能拿——那是假话。
- 穿透三层：DS 本地门（`HandleCorpseLootRequest` / `TryLootCorpse`，计数 `CorpseLootRejectedNotYours`）→
  `DsCorpseLootRegister.Contributors`（尾部追加，老 DS 不发这格 ⇒ 空数组 ⇒ 语义不变）→
  `CorpseLootRegistrationWire.Contributors` → Grain 状态。转发帧带 **`TakerCharacterId`**（不是 `SessionId`）：
  用会话号代替角色号会把"这两个数恰好相等"这条约定变成权限本身。
- 拒绝的理由 `CorpseLootTakeStatus.NotYours = 8` 一路回给客户端（结果包 `Status=8`）。

**D25 掉件上限**。`MonsterLootCore.MaxNormalLootItems = 18` / `MaxQuestLootItems = 32`，
在 `EmitEntry` 入桶前拦（AC 在 `Loot::AddItem` 里就是"满了丢、只打一条日志"）。按**桶**分而不是合成一个总数：
任务件在客户端是另一个页签，合成总数会让 32 永远用不满。丢掉的件数记 `ItemsDroppedOverCapacity`，
非组条目另外记 `EntriesDroppedOverCapacity` 并**并入 `Accounted` 恒等式**（组条目没中签本来就落在
`GroupEntriesLost` 里，两边都记会让恒等式凑巧对上一个错数）。
校验器侧另给一条 `Warn`：候选条目本身就超过上限时（例如 30 条必掉）运行期只会掉 18 件——
那是"按配置数读结果会以为丢了 12 件"，不是错配。18+32+1（金币）= 51 ≤ 槽位位图 64，已断言。

**D26 到期提示**。`SyncEventKind.CorpseLootExpired = 16`（`SourceEntityId`=尸体、`TargetEntityId`=收件人、
`IntValue`=没拿走的件数）。触发点 `MaybeNotifyCorpseLootExpired` 在 `TickCorpses` 的**摘除之前**跑：
拾取窗口与尸体停留常常是同一拍到期（缺省都是 60 秒），放在摘除之后那条分支里就永远收不到提示。
三条判据都要成立才发：登记过、窗口真的关了、还有没取走的槽；收件人 = 击杀参与者 ∩ 本 DS 在线会话。
"还剩几件"由 DS 自己记（`CorpseLootSlotsTotal/Taken`，每次入包 +1）：权威侧的位图跨进程拿不到，
而窗口关的那一刻要当场决定发不发。

**判据**：
- `尸体只让击杀参与者取且别人的请求连转发都不发`——登记帧 `Contributors == [2001]`；旁观者 `NotYours`、
  不排转发、不发入包事件；击杀者同一格取得到。
- `一具尸体最多掉18件普通加32件任务件而超出的部分有计数`——25 条必掉普通 → 18 件 + 7 件丢弃、
  `Accounted == EntriesRolled`；40 条任务件 → 32 件 + 8 件丢弃，且普通桶不受影响。
- `窗口到期还没拿完只给一条提示而拿完了就不打扰`（正反两条）——1.5 秒窗口、5 秒尸体，
  提示在收尸之前到、`IntValue=1`、只来一次；取空之后同一份内容一条都不发。
- `CorpseLootGrainTests.只有击杀参与者能取且被拒时不扣槽`——名单外 `NotYours` 且**槽还在**
  （实现成"先扣再判"也会绿，而症状是别人路过一趟就把你的东西取掉了）；空名单谁都能取。

### 步骤 22（2026-10-30）：客户端补丁**已落地**（10 处，客户端源树编译通过）

用户点头「现在落」后：先查 6 个目标文件的 mtime（都在数小时前、没有并行会话正在写），再
`python Tools/apply-corpseloot-client-patch.py --apply` → **待应用 10 处、已写盘**；`--verify` **记号对账全部一致**；
`bash scripts/verify-client-scripts.sh` → **errors=0、sources=493**（离线客户端编译门）。
落地当天再加一道**源级闸门** `尸体拾取的客户端入口在源码里全程可查`（6 个文件、10 个记号、状态位只有一处定义）：
编译绿不代表这 10 处都还在——并行会话/回退/手改都可能只带走一处，而症状只是"按了 G 没反应"。
**反证**：把同步系统那句改成 `stateAuth.IsDead`（单 token 接错源）⇒ 闸门红；复原即绿。
落地内容与 §步骤 19 一致：`ReportCorpseLoot` 发包、选中标记的 `CorpseInteractable`、同步系统每快照搬位、
G 键尸体分支 + 回执/到期提示、`CorpseLootResultReceived` 事件与分派、`HundunWorldEvent` 事件入口扫 `CorpseLootExpired`。
**仍未做**：PIE 目视（提示位置、G 键是否与既有按键冲突、尸体高亮）——那必须跑起来才算。

**4 号怪口径（用户拍板）**：**保留 `Game` 现状** `aggroRadius=30 / leashRadius=60 / 13 点巡逻线`；
排练库那套 `10 / 30 / 无路线` 只算测试用小半径，不再作为候选值。`sync-rehearsal-content.py` 里那四条
「跳过（需人判）」自此有结论：**以 Game 为准、不同步**。

### 步骤 21（2026-10-30）：数据面 → 下行字节的整趟（客户端"按 G"那条路）

两段各自验过：会话入口（`HandleCorpseLootTakeFromClient`）与下行编码（`DsDownlinkEncoder`）。中间那段——
**客户端包真的进得了数据面、结果真的编得回下行字节**——没人串起来跑过；接缝上任一处错（包种、会话号、
编码器漏收这种包）在玩家那边都只是"按了 G 没反应"。
`客户端拾取包经数据面到下行结果包走完整一趟`：
① 客户端包先 `SyncPacketCodec.Encode/Decode` 走真线上形态 → `DsDataPlaneHost.Apply` →
`CorpseLootRequestsSeen=1 / CorpseLootRejected=0`；
② 会话排出一条转发（`SessionId=2001 / CorpseEntityId / TakerCharacterId=2001`）；
③ 权威回执按请求号结账；
④ `DsDownlinkEncoder.Encode(world.Tick())` 的帧里解回 `CorpseLootTakeResultPacket`
（`CorpseEntityId / SlotIndex=0 / Taken / 555×2`）。
**反证**：把数据面传给会话的 `CharacterId` 加 1（映射到不存在的会话）⇒ 用例红；复原即绿。

**全量并行下的一个坑（如实记）**：这条用例在整包跑里红过一次（`CorpseLootRejected=1`，单独跑永远绿）。
形状是「死亡那一刻内容包被别的测试类换掉 ⇒ 取不到 `corpseSeconds` ⇒ 没有尸体窗口 ⇒ 拾取门报 Expired」，
与「拾取门写错了」一模一样。已加两条前置断言把理由念出来：死亡时 `CorpseExpireAtTick > 0`；
被拒时打印 `gate / corpseExpire / lootExpire / registered`。下一次再红可直接归因，
不把进程级静态内容的并发问题算到这条链上。

### 步骤 20（2026-10-30）：拾取五帧在**真实 TCP** 上往返一次

之前两段各自绿：编解码往返（`DsUplinkProtocolTests`）与处理器直调（`CorpseLootUplinkHandler`）。
两段都不代表"DS 真正用的那个发送口把帧发出去了"——帧种写错、丢弃分支、计数器各写各的，都只会在真 socket 上露。
新增 `CorpseLootSocketIntegrationTests`：一个只做"收帧 + 对 Register/Heartbeat 回 ack + 对 Forward 回一条 Taken"的
最小网关（`TcpListener`），DS 侧用**真的** `DsUplinkClient` 连上去：
- ① `SendCorpseLootRegisterAsync` → 网关收到 kind=13，解回来 `CorpseEntityId=42 / ExpireTick=1200 /
  PickupDistanceMeters=5.5 / Slots[0].ItemId=555 / Contributors=[2001]`；
- ② `SendCorpseLootForwardAsync`（`SlotIndex=-1`、`TakerCharacterId=2001`）→ 网关收到 kind=11 且字段原样；
  网关回一条 `SlotIndex=3 / Status=Taken / ItemId=777 / Count=2` 的回执 → 客户端 `CorpseLootResultReceived` 拿到它；
- ③ `SendCorpseLootGoneAsync` → 网关收到 kind=14 且 `CorpseEntityId=42`；三个发送计数器各 1。
**等的是内容**（"这种帧出现了"），不是"计数器涨了"：计数涨一格可能来自别的帧。
**反证**：把 Forward 的帧种改成 Register ⇒ 用例 3 秒超时红；复原即绿。
**踩过的一脚**：`EncodeFrame(kind, object payload)` 装箱后 MemoryPack 按 `object` 找 provider，
报 `Object is not registered in this provider`——回执发送必须走泛型入参。

### 步骤 19（2026-10-30）：客户端拾取 UI 的补丁**已写好并编译验证**（编辑器占用期间的绕法）

编辑器占着客户端源文件时不能改，但"补丁能不能编译"不等同于"要不要等编辑器"。做法：
1. `cp -r HundunWorld/Source/Game Tools/_clientpatch/Game`（拷贝树，不动被监视的源文件）；
2. `python Tools/apply-corpseloot-client-patch.py --root Tools/_clientpatch/Game --apply`
   —— 十处锚定插入（每条断言"锚点恰好出现一次"、带 `CORPSE-LOOT-PATCH` 记号所以幂等）：
   `CombatReporter.ReportCorpseLoot`（清客户端上报同一个出口）、`SelectableEntityTag.CorpseInteractable`（新增一格）、
   `FlaxActorSyncSystem`（每快照从 `EntityStateAuthComponent.IsCorpseInteractable` 搬到标记）、
   `WorldSelectionService`（G 键对尸体的分支 + 回执/到期提示的挂载点 + OnUpdate 弹提示）、
   `SyncPacketMessageHandler`（新事件 `CorpseLootResultReceived` + 分派 case）、`HundunWorldGame`（订阅回执 + 事件入口扫 `CorpseLootExpired`）；
3. `bash Tools/verify-client-patch.sh` —— 与 `scripts/verify-client-scripts.sh` **同一套响应文件与引用优先级**，
   只把源文件列表换成拷贝树 ⇒ **errors=0**。
4. `python Tools/apply-corpseloot-client-patch.py --root … --verify` —— 按**记号数逐文件对账**
   （`CombatReporter` 1、`SelectableEntityTag` 1、`FlaxActorSyncSystem` 1、`WorldSelectionService` 3、
   `SyncPacketMessageHandler` 2、`HundunWorldGame` 2）。**幂等"跳过 10 处"不等于补过**——
   文件可能是上一版补丁；未打补丁的真树上这条对账报 6 个文件记号 0、当场红。

**两处在写补丁时才发现的问题**：
- 可拾取位**不能**放进 `deadChanged/evadeChanged` 的守卫里：窗口到期那一刻两位都不翻转，位会停在旧值
  （尸体过期了还亮着）。改成每个快照无条件写一次（一个 bool 写入）。
- `NotifyCorpseLootExpired` 在上一版是**没有调用方的死代码**；这次在 `HundunWorldGame.OnEventReceived`
  里扫 `SyncEventKind.CorpseLootExpired` 接上（服务端每人只发一条，客户端只负责显示）。
**判据**：`errors=0`；反证把 `slotIndex` 实参删掉 ⇒ `error CS7036`、`errors=1`，恢复后复绿
（这条反证同时证明管线不是"没编到那些文件"）。
**它证明什么/不证明什么**：证明补丁对**当前客户端签名**可编译（类型、命名空间、事件签名都对得上）；
**不**证明运行期表现（提示文案位置、G 键抢了谁的键、尸体高亮）——那要 PIE 目视，仍等编辑器空闲。
**落地方式**：编辑器空闲后 `python Tools/apply-corpseloot-client-patch.py --apply`（默认只演练），
再跑一次 `scripts/verify-client-scripts.sh` 与 PIE。
> 上面"客户端待粘贴代码"那一节的 ①/③/④/⑤ 片段**已被本补丁取代**（同一个补丁里有更完整的版本，且过了编译）。

### 步骤 17（2026-10-30）：权威侧拒绝也要回执（同一槽两个人同时点）

`HandleCorpseLootResult` 以前只在 `Taken` 时给客户端发包：权威侧判定 `AlreadyTaken / Expired / TooFar / InvalidSlot / NotYours`
时**只记一个 false**。而"这一槽已经被别人取走"这件事**只有权威侧知道**（DS 本地门放行了它），
于是第二个人的屏幕上是"点了没反应"——他会以为没点中，继续反复点。
现在：非 `Taken` 也排一条同状态的 `CorpseLootTakeResultPacket`（槽号用回执/请求里的那个，物品与金额恒 0），
入包事件仍然不发，计数 `CorpseLootResultsRejected`，DS 摘要多一格 `拾取权威拒=`。
**判据**：`权威侧拒绝同一槽的第二个人时也要回一条结果包`——两个攻击者轮流砍死同一只怪（都要进参与者名单），
第一个人取 0 号槽 `Taken`；第二个人同一槽被权威判 `AlreadyTaken`，DS 必须回一条 `Status=2` 的结果包且不发 `Loot`。
**反证**：把那句 `QueueCorpseLootResultPacket(...)` 删掉 ⇒ 用例红；复原后复绿。
**两处自己的坏尺**（都记在这里）：① 以为"60 点攻击力两刀打死 100 血"，实测每刀约 15 血——写死刀数会让用例在"没有尸体"上空转，
改成轮流砍到死并断言真的死了；② 把第二个攻击者放在 3.2 米外，超出近战距离 → 它一刀都没打中、参与者名单只有 1 人，
用例红的理由与要测的东西无关。

### 步骤 18（2026-10-30）：尸体可拾取位进**快照**（客户端只看得见快照）

步骤 12 把"已死但可交互"写进了**网关的实体描述符**——那份东西只喂给网关的邻接/交互判定，
**客户端根本收不到**。所以只改描述符的话，玩家的屏幕上依旧点不到尸体（"尸体还在但点不了"）。
补法：`EntityStateBits.CorpseInteractable = 1u << 8`（尾部追加、不升协议版），由**唯一主人**
`MonsterStateBitsOf(monster, tick)` 计算（两条下行通路都调它）：`CorpseLootRegistered && CorpseLootExpireAtTick > tick`。
窗口一关就收位——尸体可能还在地上（尸体停留 ≥ 拾取窗口），那时它已经取不了了。
**判据**：`尸体可拾取位要进快照而不是只进网关描述符`——AOI 开/关各跑一遍：死亡那拍带位、
5 秒窗口过后尸体仍在但位已收、未开灰度（击杀即发）不带位。**反证**：删掉那句置位 ⇒ 用例红。
客户端侧的对应读法（`EntityStateAuthComponent.IsCorpseInteractable`）已加在组件上，
拾取入口按它决定 G 键是否可用（客户端代码仍待编辑器空闲时落地）。

**反证（`bash Tools/corpseloot-dm-falsify.sh`，每处只改一格、改完即复原）**：
> 2026-10-30 又补了一步：客户端**只看得见快照**、看不见网关那份实体描述符，所以"尸体可拾取"必须走
> `EntityStateBits.CorpseInteractable = 1<<8`（见步骤 18）；下面三条反证不受影响。

- F1 资格判据恒真 ⇒ 权限两条用例一起红（2/2）；F2 普通件上限 18→25 ⇒ 上限用例红（1/1）。
- F3 把提示挪进"尸体还在世上"那条 `else` 分支 ⇒ **第一次跑出来是绿的**。查下去是**等价变异体**：
  我那条提示用例配的是"窗口 1.5 秒 / 尸体 5 秒"，窗口先关的那些拍里尸体还没到期，`else` 照样会执行。
  于是补了一条**同拍到期**（1.5 / 1.5，也就是缺省 60/60 的形状）的用例，再跑 F3 才红（1 红 1 绿，
  绿的那条正是"窗口比尸体短"，与等价变异体的判断完全一致）。复原后 6 条全绿。

### 步骤 15（2026-10-30 已落）：真 Orleans 运行时下的拾取链（TestCluster）

进程内 `InMemoryCorpseLootStore` 与"上行三帧字节往返"各自绿，**证明不了**
`CorpseLootRegistrationWire.Contributors`（`[Id(4)]`）在 Orleans 编解码里真的过得去——
漏一个 Id 的症状是"名单到对岸变成空数组"，而空数组的语义恰好是**不限制**：权限门静默失效、零报错。
所以新增 `CorpseLootClusterTests`（复用既有 `TestCluster` 基建，走真序列化/路由/调度）：
- `真Orleans下参与名单与槽位一起过线而名单外的人取不到`：旁观者 `NotYours` 且不带槽内容；名单内取到 555×2；
  同槽 `AlreadyTaken`；`-1` 取下一件回执槽号 1 / Money 10；再取空 `AlreadyTaken`；未登记 `UnknownCorpse`。
- `真Orleans下上行三帧与作废都落到同一具Grain上`：登记帧（线上形态）→ 处理器 → Grain；转发帧回执**带原请求号**、
  名单外 `NotYours` 且 `ItemId=0`；作废帧之后同一具一律 `UnknownCorpse`。
**反证**：把 `CorpseLootGrainStore.RegisterAsync` 里那份名单映射改成空数组 ⇒ 两条一起红（2/2）；复原后复绿。

### 步骤 16（2026-10-30）：试算层也要吃 18 件上限（预览不许自己编数）

D25 把上限装在 `EmitEntry` 里之后，`ExpectedItemsPerKill` 仍然把 25 条必掉算成 25 件——
编辑器试算报"每次 25 件"、服务端只给 18 件，正是"预览与服务端各说一套"。
改法：期望值按 `MaxNormalLootItems` 截断（`ExpectedItemsPerKill`），另留
`ExpectedNormalItemsUncapped` 给交叉核对（逐条求和 vs 整表比的必须是**未截断**那一份，
否则超限配法会被误报成"两个入口分叉"）；`preview-drop` 多两格 `expectedItemsUncapped` / `cappedByLimit`，
headline 在截断时写明"候选期望 X 件，已按上限截断"。
**判据**：`期望件数被18件上限截断而原始期望仍留给交叉核对`（25 条 → 原始 25 / 截断 18；小表 0.5 不被压低）；
真库 `preview-drop --mob 1` 读数 `0.2 / 0.2 / capped=false / cross=一致`。
**反证**：把截断那行改回 `return uncapped;` ⇒ 新用例红（1/1）；复原后复绿。
