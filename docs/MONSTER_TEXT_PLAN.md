# 怪物文本表与喊话执行端 —— 方案与决策清单（切片 30，**只出方案，等点头再写码**）

日期：2026-10-27。作者：Agent（跨轮目标"怪物编辑器"的第 30 期提案）。
状态：**提案，未落地**（**代码一行未动**）。本文的 AC 部分全部是本机源码量出来的（`C:/Works/AzerothCore/azerothcore-wotlk`），
不是转述；本作部分每条都标了"要动谁"和"判据长什么样"。
**2026-10-27 用户已答两条**：D15 = 走"插件草稿 + 内容快照文件"（不等数据库）；
D16 = `aggro_say_id` 与 `start_emote` **并成文本表里同一行的两列**（⇒ 存量草稿要做一次合成，别留两份真相）。
D17–D20（文案走不走实时包 / 聊天框还是头顶气泡 / 去重照不照抄 AC / 音效与 `BroadcastTextId`）仍未定，
其中 D17 与 D18 决定期 2、期 3 的形状，**开工前必须先答**。
同一天用户另外拍了一条方向性的：**掉落与金币要按 AC 的"尸体上那一份 + 拾取窗口"形状推进**，
那是比本期更大的一期，**方案文档已出**：`docs/MONSTER_CORPSE_LOOT_PLAN.md`（D21–D27 待答，
其中 D21"那份掉落列表住 DS 还是 Orleans"不定则后面六条都无意义）。
两期都要动 `HandleMonsterDeath` 那条出口，**先后顺序要一起定**，别先做文本再回来推翻掉落的上行形状。

---

## 1. 为什么要做这一期（现状证据）

`aggro_say_id`（怪物行为表的一格）今天的状态是**合法数据、零消费者**：
编辑器能填、库里能存、快照能装配、`MonsterBehaviorConfig`/`MonsterAiConfig` 里带着它一路到运行期，
然后**没有任何人读**——上一轮（切片 28）把这件事从"静默"改成"当场念一条 Warn"，
并在 `docs/MONSTER_EDITOR_PLUGIN_DESIGN.md` §11.2 里记明"缺的不是接线，是**本作没有怪物文本表**这个概念"。
同一族的还有 `start_emote`（起手动作，挂在客户端动画表上）与技能条的 `status_id`（挂在状态层 D14）。

按 AC 量完之后，这条缺口比上一轮记录的更具体：**喊话不是一格，是一整张表**，
而且表里已经带了我们要的另外三件事（概率、动作、音效、可见范围）。也就是说
`aggro_say_id` + `start_emote` 这两格在 AC 那边是**同一行的两列**——
将来决定怎么存时，要么把两格并回一行（AC 形状），要么各自有表并说清为什么（见 D16）。

## 2. AC 的权威形状（逐条带出处）

### 2.1 行的构成

`CreatureTextEntry`（`src/server/game/Texts/CreatureTextMgr.h:37-51`）：

| 列 | 类型 | 作用 |
|---|---|---|
| `entry` | uint32 | 怪物模板号（我们叫 `monster_id`） |
| `group` | uint8 | **组号**：同一个触发时机下的若干候选台词（AC 不规定组号的语义，由各脚本自己命名） |
| `id` | uint8 | 组内的行号，用于"这组里哪些已经说过"的去重 |
| `text` | string | 文本本体 |
| `type` | `ChatMsg` | 频道/颜色（say / yell / emote / boss emote / whisper / zone yell；对位枚举见 `CreatureData.h:414-424` 的 `ChatType`） |
| `lang` | `Language` | 语言（兽人语/亡灵语那种，客户端据此渲染字体与"听不懂"的效果） |
| `probability` | float | **组内加权**，不是"这条会不会说"的独立判定 |
| `emote` | `Emote` | 同一行自带一个动作 |
| `duration` | uint32 | 说这条的持续（`SendChat` 把它当返回值交给调用方） |
| `sound` | uint32 | 音效 id |
| `TextRange` | enum | 可见范围：`NORMAL/AREA/ZONE/MAP/WORLD`（`CreatureTextMgr.h:27-33`） |
| `BroadcastTextId` | uint32 | 客户端字符串表引用（非 0 时文本取自客户端那张表而不是这一列） |

装载时只做两类校验，各打一条 `LOG_ERROR("sql.sql", ...)`：`BroadcastTextId` 不存在/不匹配
（`CreatureTextMgr.cpp:148`）与 `TextRange` 越界（`:154`）。**注意：概率不是校验项**——
`probability` 在整个文件里只出现两次（装载 `:111` 与选择 `:236`），
所以"组内权重加起来不等于 100"不会被说；这与掉落那边**正好相反**
（`LootGroup::Verify` 对"组内合计 >101%"要打 `LOG_ERROR`），照抄时别把两套口径混在一起。

装载语句本身就是权威列清单（`src/server/database/Database/Implementation/WorldDatabase.cpp:29` 的
`WORLD_SEL_CREATURE_TEXT`）：

```sql
SELECT CreatureID, GroupID, ID, Text, Type, Language, Probability, Emote, Duration, Sound, BroadcastTextId, TextRange FROM creature_text
```

——表名 `creature_text`，12 列，除 `(CreatureID, GroupID, ID)` 这组键之外没有别的索引列。
**AC 不规定组号的语义**：各脚本自己给组号起名字（例：`scripts/EasternKingdoms/Scholomance/scholomance.h:38`
的 `enum TalkGroupIds`），所以"组 0 一定是进战斗喊话"这种话不能说，只能说"本作要自己定一张触发→组号的对照表"。

### 2.2 一次喊话是怎么挑出来的（`CreatureTextMgr::SendChat`，`CreatureTextMgr.cpp:199-268`）

触发方只给**组号**（`SendChat(source, textGroup, ...)`），其余按下面顺序决定：

1. 查这只怪的 `entry`；查不到 → `LOG_ERROR` 后**什么都不发生**（返回 0）。
2. 查这个 `group`；查不到 → 同样 `LOG_ERROR` 后静默返回。
3. **去重**：取这只怪在该组里"已经说过的 id 集合"（存在怪物对象上，`GetTextRepeatGroup`），
   把那些行从候选里剔掉。
4. **候选空了就整组重来**：若第 3 步把候选清空，则 `ClearTextRepeatGroup(group)` 后用全量组
   ——所以玩家听到的是"一轮内不重复"，而不是"说完就哑"。
5. **加权抽一条**：`SelectRandomWeightedContainerElement(tempGroup, t => t.probability)`。
6. **调用方的覆盖位**：`msgType == CHAT_MSG_ADDON` ⇒ 用行里的 `type`；`language == LANG_ADDON`
   ⇒ 用行里的 `lang`；`sound == 0` ⇒ 用行里的 `sound`；`range == TEXT_RANGE_NORMAL`
   ⇒ 用行里的 `TextRange`。也就是"行给缺省，事件可以顶掉"。
7. **两个副作用**：`sound` 非 0 → 单发一条声音包；`emote` 非 0 → `SendEmote(source, iter->emote)`
   （**喊话与做动作是同一条记录的两个字段**，不是一个事件的两个步骤）。
8. 广播文本包（`SendChatPacket(... finalType, target, range, teamId, gmOnly)`）——
   **在玩家那边这是聊天框里的一行**（带说话者名字与频道颜色），不是模型头顶的气泡。
9. `SetTextRepeatId(group, id)` 记下这条说过，返回 `duration`。

### 2.3 与本作现有机制的同形之处（这决定了实现难度）

- 第 3–5 步的形状 = 我们已经落地的 `MonsterLootCore` 互斥组抽取（`LootGroup::Roll` 那一族：
  一次加权抽一件），只是权重语义不同（掉落是累减余额、文本是纯加权）。
  **判据可以直接复用"组期望不逐件相加"那一族的写法。**
- 第 4 步"全说完就重来"= 我们做过的"重掷/轮次"类状态（组重掷），但要**每只怪各存一份**
  → 需要一个新怪物状态位容器（与 `MonsterCastingSelf` 同层次，不是全局）。
- 第 6 步"行给缺省、事件可顶"= 我们做过的哨兵缺省纪律（`ConditionInherit = -1` 那一族的写法）。

## 3. 本作的落点设计（六跳，与掉落/经验同一条链）

| 跳 | 要动的地方 | 说明 |
|---|---|---|
| ① 存 | 插件草稿 + `MonsterContentSnapshot` 新增一节 `monster_texts` | 见 D15：先只走草稿/快照这条，库表 023 **生成不执行** |
| ② 装配 | `MonsterContentLoader` → `MonsterContent` 按 (monster_id, group) 索引 | 与 `LootBindings` 完全同形；键清单要进"每一格都有消费者"那两道闸门 |
| ③ 内核 | 新 `MonsterTextCore.Pick(...)`：去重 → 空则整组重来 → 加权抽一条 → 返回行 | **唯一主人**；确定性盐值沿用 `(entityKey, group, tick)` 一族，绝不用 `RandomNumberGenerator` |
| ④ 宿主 | 在"这只怪从非战斗进入战斗"那一条边上发一次（v1 与 v2 两条 AI 通路**共用同一个出口**，与切片 21/22 同一条纪律） | 站点必须先量出来再写：`MonsterAiRuntime` 与宿主 tick 里各自有转换点，出现两份清单就等于又一种"到不了 v1" |
| ⑤ 下行 | 与施法条同一条通道（`Horizon.Game.Message.Sync` 那一族包，AOI 受众） | 见 D17：发 id 还是发整串 |
| ⑥ 客户端 | 聊天框一行（`ChatHandler`/战斗记录那侧）；`type` 决定颜色；`emote` 决定动作 | 见 D18。注意 `Horizon.Game.Core/Handlers/ChatHandler.cs` 与 `IPlayerPushService.cs` **是并行会话在改的文件**，动之前要确认归属 |

三条不变量（每一跳各配一条判据，不是写完再补）：
- **缺省等于今天**：`aggro_say_id = 0` 或没有文本行 ⇒ 一个包都不发，且金轨迹逐字不变（见 §6 风险）。
- **单一主人**：组选择/去重只在 `MonsterTextCore` 里，宿主与插件都只调它；
  镜像数学（预览面板）不许自己再抄一遍加权抽取（`preview-drop` 那一轮的教训）。
- **每一格都有读者**：新增的列全部进 `Tools/content-cell-matrix.py` 那把量尺，无读者的格子要么当场念、要么登记例外。

## 4. 决策清单（要用户点头的六条；每条给了建议但**不代替决定**）

**D15 — 文本存哪里？**
- a) 插件草稿 + 内容快照文件（**推荐**：与外观表同一路线，今天就能在游戏里看见，不碰库）
- b) 等 019/020 之后建 `monster_text` 库表，快照只作为导出产物
- c) 两边都做，以库为准
影响：a 最快但策划换机器要带文件；b/c 需要一条新迁移（本文按纪律**只生成不执行**）。

**D16 — `aggro_say_id` 与 `start_emote` 要不要并成 AC 那种"同一行两列"？**
- a) 并（AC 形状：一次喊话顺带做动作，配起来不会互相忘记）
- b) 不并（保留现有两格，`start_emote` 继续服务技能起手，文本行另带一个 emote 列）
影响：a 会让已有草稿字段迁移（存量怪的两格要合成行）；b 会留下"同一个动作两个来源"的口径问题。
**这条是本轮最贵的一条**，因为它决定后面几期要不要再改一次表形状。

**D17 — 文案走不走实时包？**
- a) **发 (textId 或 (monsterId, group, id)，客户端查本地目录**（推荐：与 `MonsterVisualCatalog` 同形，
  广播包小，本地化改文案不用重启服务端）
- b) 包里带整串（改文案即时生效，但一次"世界BOSS 喊话 + 全服可见"就是每收件人一份字符串）
影响：a 需要客户端也装一份文本目录（多一次内容装载与一次键名对账）；b 简单但体积不可控。

**D18 — 玩家那边怎么呈现？**
- a) **聊天框一行**（AC 口径：say 普通字色、yell 红、boss emote 特殊——`type` 决定）
- b) 头顶气泡（更抓眼，但**不是** WoW 的表现；我们做这个是自创口径）
- c) a + b 都给（`type` 里留一档"气泡"，策划选）
影响：a 要接进聊天窗口那条链（注意并行会话文件归属）；b 要世界坐标投影，与名牌同一族（现成基础设施）。

**D19 — 去重与概率照抄 AC 吗？**
- a) **照抄**（组内说过的不再说，说完一轮整组重来；`probability` 是组内权重）
- b) 只加权、不去重（少一个每怪状态容器，但同一只怪会连着重复同一句）
影响：a 要在怪物运行态里存"每组说过的 id 集合"（新状态，需进快照/回放判据吗？——**不进**，
它是运行态而非存档，和金轨迹的关系见 §6）。

**D20 — `sound` 与 `BroadcastTextId` 本期要不要做？**
- 建议：**都不做，并在校验器里当场念**（本作没有音效表，也没有客户端字符串表；
  `BroadcastTextId` 的意义就是"文本从客户端表里取"，与 D17-a 是同一件事的两半）。
- 要做的代价：音频资源归属与一张 id 表，属另一期。

## 5. 分期（点头后按这个顺序走，每期各自收口）

1. **期 1（内核 + 装配 + 校验器）**：`MonsterTextCore.Pick` + 快照一节 + 闸门/量尺 +
   校验器把"`aggro_say_id` 没人读"改成"`aggro_say_id` 指向的组里一条都没有"（后者是真错，前者已过期）。
   这一期**零玩家可见变化**，但把"两份清单"的洞先堵上。
2. **期 2（宿主 + 下行）**：战斗进入那一条边发事件，v1/v2 共用出口；发一条下行包。
   判据形状照施法条那一族（"在场的人都收到、不在场的不收"）。
3. **期 3（客户端）**：聊天框一行 + 类型上色；`emote` 若 D16 选 a 就一并接。
4. **期 4（编辑器）**：文本面板（一怪 → N 组 → 每组 M 行，带概率/类型/动作/范围四列）。
   面板要先过"每一格都有消费者"那道反射闸门，别长出新的死配置。

## 6. 风险与前置

- **金轨迹**：期 2 一旦在下行里加包，`Traces/gameplay_trace_600.txt` 大概率需要重录。
  那是共享资产，**重录要单独批准**；期 1 不碰它（零行为变化）所以安全。
- **并行会话文件**：`ChatHandler.cs`、`IPlayerPushService.cs`、`Game.Build.cs` 在别的会话手里；
  期 3 开工前逐个看 mtime 与归属，必要时等对方收口。
- **不要顺手做的事**：`detect_mask`（要的是潜行系统）、`status_id`（要的是状态层 D14）——
  它们与本期的共同点只是"也没有消费者"，不共享任何代码，别混成一期。
- **本文的 AC 数字若与本作现有实现冲突**，以本文为准更新的是文档而不是偷偷改代码：
  上一轮我就是因为把"没有货币系统"这种旧记录当真，差点多造一整期（§11.2-1 的翻案）。

---

## 7. 一句话版本

AC 的怪物喊话是"（模板号, 组号）→ 一组带权重的行，说过的不再说、说完重来，行里顺带动作与音效，
在玩家那边是聊天框的一行字"；本作缺的正是这一张表，`aggro_say_id` 只是它露在外面的一格。
**要不要建、怎么存（D15/D16）、发出去长什么样（D17/D18），等用户点头，再按 §5 分期动代码。**
