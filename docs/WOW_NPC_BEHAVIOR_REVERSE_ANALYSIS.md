# 魔兽世界 NPC 行为逆向分析与本工程落地规格

> 状态：v1 逆向分析，供 `docs/NPC_EDITOR_PLUGIN_DESIGN.md` 当规格输入。
> 方法：**只从玩家可观测的表现反推内部模型**——头顶的 `!`/`?`、右键菜单里多出来的那一项、
> 商人窗口的槽位顺序、训练师里变灰的那一行、守卫的一句喊话、飞行路线的曲线、掉落面板的逐件概率、
> 击杀后的经验数字，每一项都要落到"一个可写进数据表的字段 + 一个可写进确定性内核的判据 + 一个客户端绘制点"。
> 姊妹篇：`docs/WOW_MONSTER_BEHAVIOR_REVERSE_ANALYSIS.md`（怪物侧已拍板的口径全部沿用，本文不重述、只写 NPC 增量）。
> 单位口径：服务端 **米、Z-up（水平面 = XY）**；客户端 Flax 为 Y-up，跨端位置要换轴（见记忆 [跨坐标系3D位置同步需Y/Z轴交换]）。

---

## 0. 一句话结论（决定了整个编辑器的形状）

**在 WoW 里 NPC 不是"怪的另一类对象"，而是同一个 creature 加了三样东西：**

| NPC 相对怪物多出来的层 | WoW 的载体 | 玩家看到什么 | 本工程的落点 |
|---|---|---|---|
| 服务位图 | `creature_template.npcflag`（位掩码） | 右键菜单出现"购买 / 训练 / 交任务 / 存仓库 / 设为炉石" | 新表列 `service_flags` + 内核 `NpcServiceCore` |
| 关系与阵营 | `faction` + `unit_flags` + `type_flags` | 名字颜色（绿/黄/红）、能不能打、打了会不会引来守卫 | `reaction` + `flags`，判定复用 `MonsterDifficultyCore` |
| 服务关系表 | `npc_vendor` / `npc_trainer` / `npc_class_trainer` / `gossip_menu*` / `quest_offer_relationship` | 窗口里的清单、价格、学不起的那一行 | 新增 4–6 张关系表 + `Horizon.NpcCli` |

而编号、等级、模型、速度、属性、AI 档位、掉落、经验这些**怪物已有的层，NPC 一模一样地复用**——
这就是本规格最重要的一条：**不要写第二套 NPC AI/寻路/掉落/经验**。怪物侧那套内核（`Horizon.Game.Sim/Monsters/`）
已经在盘上、有 36 条 MonsterEditor 测试与 3289 条全量绿当证据，NPC 只做增量与分派。

**第二条结论**：NPC 编辑器真正的产品价值在**"给编号挂服务"**这个动作上。
本工程今天做不到这件事——NPC 只是 `NpcTemplateCatalog.cs:37-42` 里写死的三行（铸剑师/守门人/游方商队），
没有表、没有 spawn 通路、客户端连模型都没有（见 §11）。所以第一阶段的目标不是"更多面板"，
而是**把 NPC 变成有编号、有 spawn、有服务位的真对象**。

---

## 1. NPC 编号（entry）与号段

### 1.1 观测

- `.npc info` 指着一个 NPC，第一行就是 `DBGuid` 与 `Entry`——**同名同型的 NPC 共享 Entry，实例只有 GUID**（模板/spawn 分离，与怪物侧同构）。
- 同一个 Entry 在不同副本难度下属性不同 → `difficulty_entry_1/2/3`：**同一次击杀可以有四个数字面孔**，玩家看到的是"名字一样、血量差十倍"。
- `KillCredit1/2`：打的是载具/召唤物，记的却是骑手的人头 → **击杀归属可以与被杀对象分离**（我们做"贡献经验"时必须留这个口子）。
- `subname` 显示在名字下方（`<商人>`、`<铁匠铺老板>`），`IconName` 是小地图图标键。

### 1.2 本工程现状（已核实）

- `Horizon.Game.Sim/NpcTemplateCatalog.cs:15` 的 `NpcTemplateInfo(TemplateId, Name, Title, ModelKey, WuxingDomain)`——**只有五个字段，没有等级、没有阵营、没有服务位**。
- `:37-42` 三行硬编码；`:45 Get()` 未登记时兜底成 `路人#{id}`（只服务显示）；`:66 TryGetRegistered()` 才是授权白名单。
- `:59 DomainOf()` 未登记返回 0 = **不记账**（fail closed），这条纪律要保持：NPC 授权侧绝不能走 `Get()` 的兜底。
- 号段传统（本工程既有约定，"号段即身份"）：技能书 `800000+技能id`、材料 `700001+`、怪物 `1–99999 普通 / 100000+ 精英 / 200000+ 世界BOSS / 300000+ 副本`（怪物侧规格 §1.3）。

### 1.3 号段提案（决策 D2）

| 号段 | 含义 | 理由 |
|---|---|---|
| `1 – 299999` | 怪物（现状） | 不动，已被白名单与显示断言钉住 |
| `300000+` | 副本怪（预留） | 不动 |
| **`400000 – 499999`** | **服务型 NPC**（铁匠/商人/训练师/任务/守卫/平民） | 与怪物号段完全不相交，`ScriptAuthority` 与任务校验可一次判类别；`4` 对齐"服务"这一层 |
| **`500000 – 599999`** | **敌对型 NPC**（可击杀、给经验、会引来增援） | 它们的行为档位由怪物内核承担，但身份是 NPC；分号段避免"能不能打"要靠读列猜 |

> 既有 1/2/3 号 NPC 是**必须保留的历史编号**（`QuestTemplateEntity.giver_template_id` 里已经写了它们）。
> 迁移口径见设计文档 §7：**旧小 id 继续有效，新登记走新号段**，两条并存但显示与授权各自记账。

---

## 2. 服务位图（npcflag）——NPC 的多态入口

### 2.1 结构（外部参考，AzerothCore/TrinityCore 同源惯例）

`creature_template.npcflag` 是位掩码，公开文档给出的取值：`1=Gossip（可对话）`、`2=Quest（任务）`、
`16=技能训练师`、`32=职业训练师`、`64=专业训练师`、`128=商人`、`256=军火商`、`512=食品商`、
`1024=毒药商`、`2048=药剂商`、`4096=修理`、`8192=飞行师`、`16384=灵魂医师`、`32768=灵魂使者`、`65536=旅馆老板`。
位可以相加（一个 NPC 同时是药剂商 + 飞行师 = `2048+8192`）。

### 2.2 观测

- 右键一个 NPC，菜单项**恰好等于它的位图**：没开 `商人` 就没有"购买"；开了 `Quest` 才有"交任务"。
- 同一个 NPC 的多项服务在**同一个对话面板里翻页**，不是弹多个窗。
- 服务项还会因**玩家**而异：旅店老板的"设为炉石"、灵魂医师只在死亡时出现、专业训练师按职业/已学等级过滤。
  → 位图是"能力"，**可见性还要过一层玩家态判定**。这条二阶段判定是编辑器和后端的分界线。

### 2.3 本工程落点

- 新列 `service_flags`（位掩码，本工程自己定义 bit，不照抄 WoW 的十进制值——照抄只会得到一份没人能核对的魔法数）。
- 内核 `NpcServiceCore`：**纯函数** `Resolve(templateFlags, PlayerContext) -> ServiceEntry[]`，
  输入服务端算出的玩家态（等级/职业/境界/生死/已学/好感），输出有序入口列表。**客户端只画结果，不自己判**。
  这条纪律的既有先例就是 §14.5 的教训：`SkillHandler.cs:184-203` 明确拒绝按客户端上报的 `LearnMethod` 选通道，
  理由写在注释里——"客户端只要声明一个 `LearnMethod=2` 就能白读一卷并不存在的书"。NPC 服务同理。

---

## 3. 阵营、反应与"能不能打"

### 3.1 观测

- 名字/血条颜色 = 反应档：红（敌对/痛恨）、黄（中立/不友好）、绿（友好/同盟）。**换阵营看同一个 NPC 颜色就变**，
  所以颜色是 `(NPC阵营组, 玩家阵营)` 的纯函数——与怪物侧"颜色是等级差的纯函数"是**两个正交的颜色轴**，
  客户端名牌要把两轴分开处理（怪物色 = 让分档，NPC 色 = 反应档）。
- 友方 NPC 对自己人不可攻击（技能直接无效并提示），对敌对阵营玩家**可击杀且给经验**——
  这就是目标原话"死亡贡献经验（PVP 时或敌对时有效）"的观测来源。
- 打了友方 NPC → 它自卫并**呼叫守卫**；守卫冲过来、喊一句话、脱离后跑回原位。
- 中立 NPC（`Neutral`）两侧都能打，打了谁就得罪谁。

### 3.2 落点

- `reaction`（本工程的档位：`Allied / Friendly / Neutral / Hostile`）+ `can_be_attacked_by_faction` 位；
- 敌对 NPC 的仇恨/追击/脱战**直接复用** `MonsterThreatCore`（威胁表 + 1.3 滞回）与 `MonsterBehaviorCore` 能力位；
- "呼叫增援"是新增能力位 `calls_reinforcement`，增援的选取规则（半径内、同阵营、空闲优先）要写成确定性内核函数，
  并把"谁呼了谁"记进会话账本——**同族拒绝分支要回同一套对账字段**（记忆纪律）。

---

## 4. 头顶任务指示器（最典型的"玩家视觉表现"）

### 4.1 观测（外部核对过）

| 图形 | 含义 | 判据（必须服务端算） |
|---|---|---|
| 黄色 `!` | 有**可接**任务 | 该 NPC 是某任务的 giver，且玩家过了接取门槛 |
| 黄色 `?` | 有**可交付**任务 | 玩家已接、目标已达成 |
| 灰色 `?` | 已接、**未完成**（等进度） | 玩家已接、目标未达成 |
| 灰色 `!` | 条件不足（等级不够 / 前置链没走完） | giver 匹配但门槛失败 |
| `2!` / `3?` | 同类多条（数字前缀） | 计数 |

（[warcraft.wiki.gg – Quests](https://warcraft.wiki.gg/wiki/Bonus_objective)、
[GAME Watch 早期专题](https://game.watch.impress.co.jp/docs/20050701/wow1.htm) 对灰色 `?` 的描述：
"まだクエストの目的が達成されてない報告待ち状态"——即"接了没完成"。）

### 4.2 逆向要点：**指示器不是一条数据，是一次四态求值的产物**

它同时需要：NPC→任务的反查（谁发布）、玩家→任务的状态（接没接、完成没完成）、门槛求值（等级/前置）。
WoW 把这三样在服务端合成一个"每 NPC 每玩家"的摘要下发，所以**距离越近才越精确**不是画质问题，是分帧预算问题。

### 4.3 本工程现状（已核实）

- 数据侧**够用**：`QuestTemplateEntity.cs` 有 `giver_kind:81`、`giver_template_id:86`、`turn_in_kind:101`、
  `turn_in_template_id:106`、`prerequisite_quests:111`（CSV）、`publish_state:120`。
  → **没有"后续任务"列**：链是"每条任务写自己的前置"，反查得出来，但没有 `successor` 直连，编辑器要显示链必须自己反查（做一张倒排索引，别在 UI 里现算）。
- 门槛求值**已存在**：`QuestHandler.cs:107-154` 按 giver 过滤 offer，`QuestManagerGrain.cs:224-272`（过滤）与 `:246`（前置）已实现，
  giver 合法性由 `QuestInteractionAuthority.TryResolveGiver`（`:125-135`）对着 **DS 实体注册表**判，
  注释明确"不许回退到全目录"——这条闸门必须保留（否则任何客户端报个 id 就能领任务）。
- **缺的是最后一公里**：
  1. 客户端**从不带 `giverEntityId` 请求 offer**（`CombatHudTraditionalPage.cs:2423` 是无参调用），所以"跟这个 NPC 说话能看到什么"在服务端算好了也没人问；
  2. `NameplateUI.cs:195 SetContent` 只画 头衔/名字/等级/血条，**没有任何指示器位**（全仓 grep `quest indicator|exclaim|感叹号|QuestIndicator` 世界空间零命中，只有 `GameMainUI.cs:665 AddQuestMarker` 的 minimap 边缘针）。
- 交付侧还有**一个真缺陷**：`turn_in_*` 存进了库、也进了 mapper，但 `CompleteQuestRequest` 只带 `CharacterId+QuestId`
  → **交付地点/交付 NPC 完全没被校验**。这是"声明了却没人读"的教科书案例（记忆 [声明了却没人写的键会静默失效]），
  NPC 编辑器一旦把交付位图当真数据用，就必须同时补这条校验，否则编辑器里配的"只能在 A 处交付"是句空话。

### 4.4 落点

- 内核 `NpcQuestIndicatorCore`：`Resolve(questSnapshot, playerState) -> {Kind, Count}` 四态 + 计数，纯函数，可被编辑器预览与客户端名牌**同一份消费**。
- 传输：走**服务端拉取 + 摘要缓存**，不做逐帧广播（每个 NPC 每玩家一个状态，N×M 会炸）。
  口径：NPC 进入玩家 AOI 时随 Identity 带一份 `service_summary`（含指示器），玩家任务状态变化时对该屏内的服务 NPC 定向刷新。

---

## 5. 售卖物品（商人）

### 5.1 结构（外部核对过）

`npc_vendor(entry, slot, item, maxcount, incrtime, extended_cost, Comment)`：
`slot` 是**窗口里的位置**（0 起，左上到右下）→ 玩家看到的顺序是数据，不是物品 id 顺序；
`maxcount = 0` 表示不限库存，正数是**库存上限**；`incrtime` 是补货秒数（与 `maxcount` 配对才有意义）；
`extended_cost` 是**非货币代价**（荣誉/徽章/代币）。修理、回购各自另有表。

### 5.2 观测

- 买入价与卖出价**不对称**（卖价远低于买价），且同一件物品在**不同商人处卖价可以不同** → 卖价不只是 `item.sell_price` 一列，而是 `商人 × 物品` 的关系值。
- 库存有限的商人卖完就没了，等 `incrtime` 补货 → **每个商人有物品级库存计时器**。
- 修理费随装备已损失耐久线性增长，满耐久时"修理全部"变灰。
- 玩家卖东西时，商人**只收白名单类别**（"这个物品不能卖到这里"）。

### 5.3 本工程现状（已核实，这一节是本规格最大的空白）

- 价格列在：`ItemTemplateEntity.cs:211 sell_price`、`:218 buy_price`。**服务端零消费者**
  （`grep -rn "SellPrice|BuyPrice"` 在 `Horizon.Game.Core` / `Horizon.Orleans.Grains` / `Horizon.Game.Sim` 全部为空）。
- `MessageType.Shop = 502` 存在（`Horizon.Game.Message/Enums/MessageType.cs:321`），**没有任何 handler**
  （`Horizon.Game.Core` / `Horizon.Orleans.Grains` 里对 `MessageType.Shop` 的引用只出现在 `bin/` 的 XML 文档里）。
- 钱包是**一张没人用的表**：`Horizon.Model/GameModel/CurrencyEntity.cs` 有 `character_id`、`currency_type`、`amount`、
  `total_earned`、`total_spent`——但 `grep CurrencyEntity|ICurrencyGrain` 在 grains/interface/core 里**零命中**。
  `ICharacterGrain` 也没有金币 API（`GmCommandGrain.cs:201` 的 `await characterGrain.AddGoldAsync(...)` 是**注释掉的**）。
  → **商人链的第一块地基是"钱从哪来、扣在哪、怎么对账"**，必须先决定用它还是另立。
- 背包侧**可用**：`IInventoryGrain.AddItemAsync(int,int,int)`（`Horizon.Orleans.Interface/IGameSystemGrains.cs:52`）、
  `RemoveItemAsync(long,int)`（`:60`）、`ConsumeByTemplateAsync(ulong,int,int)`（`:36`），且 `InventoryEquipmentTests.cs:217-258` 有测试。
- 客户端 `MenuShopPage.cs` 是**纯 mock**：商品硬编码在 `InitData()` `:926-996`，余额写死在 `:201-202`，
  `OnConfirmPurchase()` `:1577-1611` 只减本地整数，整个文件**没有任何网络调用**。

### 5.4 落点

- 新表 `NpcVendorEntry(npc_id, slot, item_id, price_override?, buy_price_override?, stock_max, restock_seconds, cost_kind, cost_amount, enabled, publish_state)`；
  价格留 `override` 空位 → 空则取 `ItemTemplate` 的列，**"有列没人读"就此闭环**（读它的是 `NpcCommerceCore`）。
- 内核 `NpcCommerceCore`：定价解析（关系值优先于模板值）、库存与补货的**确定性时钟**（用 tick/秒，不用 `DateTime.Now`，
  要能被 `DsReplayHarness` 重放）、买卖双侧的**拒绝理由带状态位**。
- 钱包：优先接 `CurrencyEntity`（表已在、字段够用、`total_earned/total_spent` 天然是对账口径），
  而不是新造一张——**但要显式记账，别学现在这样"字段声明了没人写"**。

---

## 6. 技能学习（训练师）

### 6.1 观测

- 训练师窗口把同一技能的多级（Rank）列成阶梯，**已学的最高级往上的那几行按"等级不足"变灰**，买不起的按钱变灰——
  两种灰的理由不同（鼠标提示也不同）。
- "你已学到当前等级可用的最高一级" 是硬墙，不是钱的问题。
- 前置技能未学 → 后面全部锁。
- 不同职业训练师卖的清单不同；同一训练师卖"专业配方"和"职业技能"是**两个不同的窗口**。

### 6.2 本工程现状（已核实，好消息）

- 门槛求值**很完整**：`SkillGrain.CheckLearnGates`（`:173`）已实现等级 `:177`、境界 `:182`、五行亲和 `:186`、
  前置 `:190`、演化源已学 `:200` / 已练满 `:207`、周天与道侣 `:216`、动态好感/材料/属性 `:222`，
  并有 `LearnSkillAsync:796`、`LearnFromBookAsync:249`、`CommitLearnAsync:296` 与 `:816-822` 的通道锁。
  测试面也足：`SkillLadderLiveTests:113/146`、`SkillEvolutionLiveTests:224`、`WuxingCounterLiveTests:119`、
  `WuxingMaterialGateLiveTests:149`、`WuxingChaosLiveTests:187`、`WuxingClientGateConsumerTests:97`。
- **缺的只有 NPC 这一头**：整条链没有任何 NPC 参数——学习来源是"书"或"直接学"，不是"某个训练师教你"。
  `SkillHandler.cs:184-203` 还**刻意**把客户端上报的 `LearnMethod` 丢掉（防白读书，注释在 `:192` 前后）。
  → NPC 训练师必须是**服务端权威**的：由服务端解析"眼前这个 NPC 教不教这一式"，绝不能加一条"客户端上报 npcId 就行"的通路。

### 6.3 落点

- 新表 `NpcTrainerEntry(npc_id, skill_id, rank?, cost_gold, cost_items_json?, req_level, req_realm, req_prerequisite_skill_id?, service_kind, enabled, publish_state)`；
  `service_kind` 区分 职业技能 / 专业配方 / 玩法教学（对应 WoW 的 16/32/64 三个位）。
- 内核 `NpcTrainerCore`：只做**"这个 NPC 能不能教这一式、要多少钱"**，学不学得下去**继续问 `SkillGrain.CheckLearnGates`**——
  两套判据叠一层，谁也不许改写谁。灰行的原因码必须由 `CheckLearnGates` 透传（别在 NPC 侧重新发明理由）。

---

## 7. 制作装备 / 物品（专业与设施）

### 7.1 观测

- 配方有三个来源：**训练师教**、**图纸/书学**、**掉落或任务奖**；学过的配方才出现在制作面板的可用清单里。
- 制作要**站在设施旁**（铁砧/炼金台/裁缝架），远处点"制作"会被拒；设施本身也是一个 NPC（有交互位图）。
- 成品品质与"技能点数 vs 配方难度"挂钩（经典服的可制作几率：橙/黄/绿/灰四档），同一配方不同人做结果不同。
- 材料不足时那一行变灰，且**缺哪样写在哪样上**（玩家能一眼看出缺 3 张皮）。

### 7.2 本工程现状（已核实，坏消息）

- `CraftingGrain.cs:57 LearnRecipeAsync(int)` 是**当场捏一个配方**塞进去，`SuccessRate = 1.0f`（`:69-76`），无成本、无校验；
  `CheckMaterialsAsync`（`:89-103`）**不看背包直接返回 true**。
- `ICraftingGrain` / `IWuxingAlchemyGrain`（`IGameSystemGrains.cs:275`/`:312`）**没有任何 handler 调用**——只有接口、实现与一个反射测试。
  真路径是假的：`ItemHandler.cs:459-481`、`CharacterHandler.cs:1539-1560` 直接回 `Success=true, ConsumedGold=1000, CraftedItems=[]`。
- **配方没有表**（`Horizon.Model/GameModel/` 里没有 recipe 实体），客户端 `CraftingPage.cs:327-338` 硬编码 8 条配方。
- 材料侧**有真东西**：`MaterialEntity.cs`、`MaterialSynthesisLogEntity.cs`、`ConsumeByTemplateAsync`（`IGameSystemGrains.cs:36`）。

### 7.3 落点

- 新表 `NpcCraftRecipe(id, name, product_item_id, materials_json, cost_gold, station_npc_id?, skill_req, quality_curve_json, learn_source_kind, publish_state)`
  + `NpcCraftStation`（设施就是"服务位图里有 Craft 的 NPC"，与 §2 复用同一套位图与同一套交互校验）。
- **`LearnRecipeAsync` 与 `CheckMaterialsAsync` 必须一起修**：只修 NPC 侧、留一个恒真的材料校验，
  等于给"无中生有"开第二条路。这一条要配**独立暴力参照**的对拍测试（记忆 [等价测试的参照要另写暴力版]）。

---

## 8. 外观

### 8.1 观测

- `modelid1..4`（四个模型槽随机/按阵营选）、`scale`（体型倍数，玩家一眼看出"这只大了三倍"）、
  `equipment_id`（手里拿什么）、`type`（人形/野兽/元素…决定能不能被剥皮）、`rank`（精英龙框、稀有金名）。
- **稀有（Rare）NPC 头顶带 `!!` 且不脱战**——是同模型换 Entry 的"修饰符条目"，不是新美术（与怪物侧 §1.1 同一结论）。

### 8.2 本工程现状（已核实）

- `NpcTemplateInfo.ModelKey` 有（`:10` 注释写明"当前客户端尚未接入 NPC 美术资源，仅作为稳定分流标识"）。
- `FlaxActorSyncSystem.cs:119` 只有 `MonsterPrefabPath`，**没有 `NpcPrefabPath`**；
  建 Actor 的特判只覆盖 Monster（`:482-510`、`:570`、`:575`、`:588-592`、`:617`），
  所以 NPC 现在**渲染成远程玩家的占位人形**（`:680` 把 wire Npc 映射成 `SelectableKind.Npc` 但没有模型分流）。
- **好消息**：怪物侧的外观链已经在盘上跑通（Identity v14 追加 5 字段 → ECS → `MonsterVisualCatalog` 分流 prefab，
  `Source/Game/Selection/MonsterVisualCatalog.cs`）。NPC 应接**同一条链的另一张表**，
  而不是新写一套——两处的键名协议由测试按字面文本对账（记忆 [派生副本要有与来源逐字相同的断言]）。
- 现实约束照抄怪物侧：`export-visuals` 导出 0 条 + 11 条"没有 prefab 路径"**是如实状态**（NPC 美术同样为空，别把它当链路坏了修）。

---

## 9. AI 智能程度（档位）

### 9.1 观测 → 可档位化

WoW 的 `MovementType`（`0=Idle 站桩 / 1=Random 随机踱步 / 2=Waypoint 走路线`）与 `AIName`
（`NullCreatureAI / IdleAI / AggressiveAI / SmartAI / TCC`）合起来，在玩家眼里就是**一条"它会做什么"的阶梯**：

| 档 | 玩家观察到 | 需要的内核能力 |
|---|---|---|
| 0 | 永远站着、不转头 | 无 |
| 1 | 你走近它会**转身面对你** | facing 追踪 |
| 2 | 鼠标划过/点击会**说话**（gossip 台词） | 台词表 + 触发 |
| 3 | 会**随机踱步**回原位 | `MovementType=1` |
| 4 | 沿**固定路线**走、到点停顿/做动作 | `MovementType=2` + 路径（§10） |
| 5 | 被打会**自卫/追击**，脱战后跑回原位 | 威胁 + leash（复用 `MonsterThreatCore`） |
| 6 | 主动攻击敌对目标、**呼叫增援** | 主动索敌 + 增援选取 |
| 7 | 会**施法**、会残血逃跑、会找玩家"要东西" | 复用 `MonsterCastingCore` + 条件分派 |

> 这条阶梯必须**编码成能力位组合**，而不是新的枚举——怪物侧 `MonsterBehaviorCore` 已经是"能力位档位 + 覆盖"的形状，
> NPC 只加"服务态"那几个位（转身/台词/自卫/呼叫），两边共用一套分派。

### 9.2 调度（WoW 的"作息"）

观测：某些 NPC 白天在铁砧旁、夜里在旅店门口；到点整条路线换一条。
→ 落点：`schedule_json`（时段 → 路线/姿态/台词集合），**求值用服务端 tick 时钟**，可被编辑器时间轴预览。
参考实现：NarrativePro 的 `ActivitySchedulePaths`（`NP\AI\NPCDefinition.cs:63`）已经有这个字段形状，
但它的装载数据是 JSON（`NP\Data\NarrativeNPCDatabase.cs:104`，`Content/NarrativePro/NPCs`）**且 `:309` 把外观直接 new 成空**——
即"形状可抄、数据与外观皆未通"。

---

## 10. 自动寻路（目标原话：参照 Flax 原生 API 与 NarrativePro 实现）

### 10.1 WoW 观测

- 平民沿**离散路径点**走（`creature_addon.path_id` → `path_nodes`），到点停一下再走下一点；
- 飞行点坐骑沿**样条**飞，玩家在座舱里能看见整条曲线（`unit_walkway` 一类），落地才交还控制；
- 巡逻守卫绕圈；被打会追出 leash 距离然后**跑回原位并回血**（怪物侧已实现 Evade）。

### 10.2 本工程可用的引擎 API（**已在盘上逐条核实**，`C:\Works\FlaxEngine\Source\Engine\Navigation`）

| API | 位置 | 用途 |
|---|---|---|
| `FindPath(start,end,out Array<Vector3>)` | `Navigation.h:32` | 取整条路径给路线编辑器与"预估长度" |
| `TestPath(start,end)` | `:40` | 两点间是否可走（校验 waypoint） |
| `FindClosestPoint(p,out r)` | `:48` | 把玩家点吸附到可行走面 |
| `FindDistanceToWall(...)` | `:23` | 半径内障碍探测 |
| `FindRandomPointAroundCircle(center,r,out)` | `:76` | 随机踱步取点 |
| `RayCast(...)` | `:85` | 视线/索敌 |
| `IsBuildingNavMesh()` | `:91` | **烘焙中→按"可通过"处理**（降级判据，见 §10.4） |
| `BuildNavMesh(Scene,BoundingBox,timeoutMs)` | `:104/:113/:121` | 编辑器内烘焙 |
| `NavMeshBoundsVolume` | `NavMeshBoundsVolume.h:11`（`AgentsMask` `:21`） | 只在编辑器里放，不带烘焙调用 |
| `NavMesh` / `NavMeshRuntime` | `NavMesh.h:17/:49/:65`、`NavMeshRuntime.h:44/:49/:124/:146/:191` | 按 mesh 查询 |
| `NavCrowd` | `NavCrowd.h:16/:32/:57/:99/:123` | 群体避障（**引擎有、NarrativePro 没用**，它的 crowd 是自写 C#） |

NarrativePro 的封装层：`NP\Navigation\NavigatorStatics.cs`（`TestPath:144`、`FindPath:175`、`FindClosestPoint:249`、
`FindRandomPointAroundCircle:263`、`FindDistanceToWall:277`、`IsBuildingNavMesh:132`），
真正的长跑逻辑在 `NP\AI\AITask_LongMove.cs:168/457/476`，由 `NPCController.cs:746` 驱动；
编辑器烘焙工具 `Plugins\NarrativePro\Source\NarrativeProEditor\Navigation\NavMeshBaker.cs:153`（`BuildNavMesh()`，自检 `:228`）；
游戏侧已有 `RP\Source\Game\PlayerController.cs:1190` 调导航。

### 10.3 关键现实：**这条链现在是断的**

场景里**没有任何 `NavMeshBoundsVolume`/`NavMesh` 资产**（全仓 `.scene` grep 零命中），
所以 `AITask_LongMove.cs:250-268/303` 走的是 `TickLinearMover` —— **所谓寻路今天等于直线**。
`docs/NARRATIVE_PRO_MAP_NAVIGATION_DESIGN.md` 自己拍板了这条降级（R3 `:376`：烘焙中/无图 → 视作可通过、退到 POI A\* 或直线）。

### 10.4 落点（与怪物侧合并，不另起炉灶）

- **服务端权威走图**沿用怪物侧已交付的 `MonsterPathCore`（高度图网格 A\*、全序堆、部分路径、确定性）
  与其装配开关（DsHost `--monster-nav`，现为 32×32 的 plumbing fixture）——NPC 与怪共用同一张图，
  因为"能不能走"是世界的属性，不是生物的属性。
- **真图来源**（未做，与设计文档 §决策 D6 同题）：由编辑器从 `Terrain(Main)` 采样导出高度图，或用 `BuildNavMesh` 烘焙后导出多边形图。
  注意两条既有教训：**地形高度只写不读**（`SetupPatchHeightMap` 有洞掩码、失败也返回 true，写完必须自己喂下游采样端）；
  **改图资产必须重启编辑器**。
- **路径点编辑**复用怪物侧先例：`Horizon.MonsterCli` 的 `route import <points.json>` + `navcheck`，插件里场景拾取点。
- **飞行/传送样条**（WoW 的观感）本期只做"数据 + 编辑器画曲线 + 客户端沿曲线演出"，不改服务端位移权威
  （`NARRATIVE_PRO_MAP_NAVIGATION_DESIGN.md` §11.3 定的口径：**插件只请求，游戏执行位移**，因为 ECS 的
  `PredictedTransformComponent` 每帧重写客户端位置）。

---

## 11. 掉落物品与掉落几率

### 11.1 结构（外部惯例，字段名以我们自己的表为准）

`creature_loot_template(Entry, Item, ChanceOrQuestChance, Groupid, MinCount, MaxCount, LootCondition, ExclusiveGroup)`
+ `reference_loot_template`（复用子表）。要点：**逐件独立掷**、`Groupid` 实现"这一组里只出 1 件"、
任务掉落用特殊标记（必掉）、金是 `mingold/maxgold` 的区间掷、剥皮/采集走 `skinloot` 独立表。

### 11.2 本工程现状（好消息，这层怪物侧已经做完了）

- 表已在：`MonsterLootBinding(binding_id, monster_id, loot_table_id, roll_chance_bps, min_count, max_count, condition_kind, quest_id, money_min, money_max)`
  与 `MonsterDropEntry(entry_id, binding_id, item_id, chance_bps, count_min, count_max, seq)`（`Horizon.Model/GameModel/MonsterWorldEntity.cs:193-279`）
  ——**逐件独立 + 数量区间 + 条件 + 任务掉落 + 金币区间都装得下**，比 WoW 单列 `drop_rate` 表达力强。
- CLI 的 `preview-drop --trials 10000` 蒙特卡洛已在盘上。
- **NPC 侧只做一件事**：把绑定的 owner 从"怪物 id"扩成"生物 id（怪 or 敌对 NPC）"。
  这是设计文档的决策 D4，**不要新建一张同构的 NPC 掉落表**（两份掉落判据必然漂）。

### 11.3 观测补充

- 友方/服务 NPC **通常不掉东西**，但被杀死会掉"信物/徽记"类任务物品；稀有精英NPC 有**额外附加表**。
- 掉落面板要区分"谁 rolls"——与 §12 的贡献账本是同一批参与者。

---

## 12. 死亡贡献经验（目标原话：PVP 时或敌对时有效，随玩家等级反比例衰减）

### 12.1 WoW 观测

- 击杀**敌对 NPC**：经验按怪物侧那条让分曲线（同级 ≈ `45+5×CL`，高差加成封顶 1.2 倍，低差线性衰减到零，灰名断供）。
- 击杀**敌对阵营玩家**（Honorable Kill）：给荣誉 + 经验按**对方等级**走同一条衰减曲线——
  大号杀小号**不给经验**，这正是"随玩家等级反比例衰减"在 PvP 侧的形状。
- **参与分配**：组队按等级加权、有范围限制、有 assist credit（近处分享）；怪物侧已实现 5 人参与上限 + 优先级截断。
- `KillCredit1/2`：击杀归属可转移到别的 Entry。

### 12.2 本工程现状（这层同样已经做完）

- `MonsterDifficultyCore`（`:148 Of`、`:168 LowGapDecay`、`:184 HighGapBonus`、`:196 ConcessionFactor`、`:123 GrayLevel`、`:133 ZeroDifferenceValue`，
  分段表 `:85 StageStartLevels={1,1,31,76,131,201,300}`、`:92 GrayGapByStage`、`:100 ZeroDifferenceByStage`，
  高差 `:107 CapLevels=4` / `:108 Rate=0.05`）与 `MonsterXpCore`（`:102 Compute`、`:106 Decompose`、`:141 ComputeForParticipants`、
  `:163 SortParticipantsByPriority`、`:95 MaxParticipants=5`）**在盘上，且有独立暴力参照全等级组合对拍**。
- 贡献账本链已在盘上：DsWorldSession 贡献者账本 → `KillXp` 事件 → 网关 `OrleansExperienceWriter` → `AddExpAsync`；
  客户端名牌的让分色与"经验断供"**同一判据**（`NameplateUI` 改走 `MonsterDifficultyCore`）。
- **NPC 增量只有两条**：
  1. **来源类型**：`KillSource = Mob | HostileNpc | PvpPlayer`，PvP 分支要把"对方等级"喂进同一条衰减函数，
     并产出荣誉/贡献记录（不是另算一套经验公式）；
  2. **不可击杀**：友好 NPC 走 `can_be_attacked=false`，**拒绝理由必须是状态位而不是猜文案**（记忆 [同族拒绝分支要回同一套对账字段]）。
- **纪律重申**：改让分/经验/颜色只改 `MonsterDifficultyCore`（怪物侧记忆原文："客户端与编辑器都是它的消费者，别在任何一侧另写判据"）。
  NPC 一律复用；新增的只是"喂进去的等级从哪来"。

---

## 13. 玩家视觉表现总清单（编辑器面板 ↔ 内核判据 ↔ 客户端绘制点）

| # | 玩家看到 | 数据来源（新/旧） | 内核判据 | 客户端绘制点 | 现状 |
|---|---|---|---|---|---|
| 1 | 名字 + `<头衔>` | 新 `npc_template.name/title` | — | `NameplateUI.cs:195` | 有 Title 通路（怪物侧已用） |
| 2 | 名字颜色（绿/黄/红） | 新 `reaction` + 阵营 | `NpcReactionCore`（纯函数） | 名牌色 | **无** |
| 3 | 等级数字 | 新 `level`（模板 min/max + spawn 覆盖） | 复用怪物等级口径 | 名牌 | NPC **无等级字段** |
| 4 | 头顶 `!` / `?` / `2!` | `quest_template.giver_*` 反查 + 玩家态 | `NpcQuestIndicatorCore` | 名牌新增槽 | **无** |
| 5 | 右键菜单项 | 新 `service_flags` | `NpcServiceCore.Resolve` | 交互菜单 | 只有任务一条 |
| 6 | 对话/台词 | 新 `gossip` 表 + `text_id` | 触发选行 | 对话面板 | 无（NarrativePro 有形状未通） |
| 7 | 商人窗口槽位与价格 | 新 `npc_vendor` + `item_template.sell/buy_price` | `NpcCommerceCore` | `MenuShopPage`（现全 mock） | **无服务端** |
| 8 | 训练师列表与灰行 | 新 `npc_trainer` + `SkillGrain` 门槛 | `NpcTrainerCore`（理由透传） | 技能面板 | 门槛有、NPC 无 |
| 9 | 制作面板可制清单/缺料 | 新 `recipe` 表 + `Material*` | `NpcCraftCore`（修 `CheckMaterialsAsync` 恒真） | `CraftingPage.cs:327-338` | **孤儿 grain** |
| 10 | 转身面对你 / 踱步 / 走路线 | 新 `ai_bits` + `waypoint` | 复用 `MonsterPatrolCore`/`PathCore` | Actor 位移（ECS 权威） | 图是 fixture |
| 11 | 死亡 → 刷新倒计时 | 新 spawn 表 `respawn_seconds` | 复用刷新秒曲线 | 消失/重现 | 怪物侧有 |
| 12 | 掉落面板与逐件概率 | 复用 `MonsterLootBinding/DropEntry` | 复用掷 | 战利品窗口 | 怪物侧有 |
| 13 | 击杀经验飘字（含 PvP） | 复用贡献账本 | 复用 `MonsterXpCore` | 战斗日志/飘字 | 怪物侧有 |
| 14 | 被打引来守卫 + 一句喊话 | 新 `calls_reinforcement` + `gossip` | `NpcAlertCore` | 喊话气泡 | **无** |
| 15 | 模型/体型/手持 | 新 `model_key/prefab/scale` | — | 复用怪物外观链（`MonsterVisualCatalog`） | NPC 用玩家占位 |

---

## 14. 证据分级（这份文档哪些能直接写码，哪些不能）

- **已核实（读过盘上代码，带 file:line）**：§1.2、§4.3、§5.3、§6.2、§7.2、§8.2、§10.2、§10.3、§11.2、§12.2、§13 的现状列。
  这些是本规格**唯一可以直接依赖**的部分。
- **结构级（来自 WoW 开源服务端的公开 DB/教程，是对暴雪的再实现，不是暴雪二进制）**：§2.1、§5.1、§7.1、§9.1、§10.1、§11.1。
  用来定"该有哪些字段"，**不用来定值**；bit 的十进制值一律不照抄。
- **观测级（玩家可看到的行为）**：§3.1、§4.1、§5.2、§6.1、§7.1、§12.1。用来定判据形状。
- **待读（不许凭本文实现）**：
  1. 玩家金币的权威存储与扣减口径（`CurrencyEntity` 零消费者，`AddGoldAsync` 只在注释里）→ 决定商人怎么写账；
  2. `ItemTemplateEntity` 的 `sell_price/buy_price` 单位与 0 值语义（0 是"免费"还是"不可买"）；
  3. 荣誉/PvP 击杀在本工程有没有既存表（本次未发现）；
  4. Flax NavMesh 烘焙产物能否离线导出为服务端可读的图（决定 §10.4 走"自采样高度图"还是"导多边形"）；
  5. 交付 NPC 的校验补上后，**已发布的存量任务**会不会被新校验判成不可交付（要跑一次全量对账再决定门槛）。

---

## 15. 与怪物编辑器的复用矩阵（避免"两套 NPC"）

| 能力 | 怪物侧已有 | NPC 侧动作 |
|---|---|---|
| 编号/模板/spawn/等级覆盖/刷新 | `MonsterTemplate`+`MonsterSpawnPoint` 8 表已建（SQL 未执行） | **新表**，同构字段同名单（发布状态/内容版本乐观锁都要） |
| 让分色与断供 | `MonsterDifficultyCore` | **只读复用**，NPC 加"反应色"这一个正交轴 |
| 击杀经验与参与分配 | `MonsterXpCore` + 贡献账本 | **只读复用**，加 `KillSource` 分派 |
| 威胁/追击/脱战 | `MonsterThreatCore` + Evade 刷血 | **只读复用** |
| 施法 | `MonsterCastingCore` | **只读复用**（敌对 NPC 也施法） |
| 巡逻与走图 | `MonsterPatrolCore` + `MonsterPathCore` | **共用同一张导航图**（世界属性） |
| 掉落 | `MonsterLootBinding`/`MonsterDropEntry` + `preview-drop` | **扩 owner 类型为生物 id**，不新建表 |
| 属性公式 | `MonsterStatsCore` | NPC 若可击杀，走同一公式；服务型 NPC 用"不可攻击"而非"属性为 0" |
| 内容装配 | `MonsterContent.Install` + DsHost `--monster-content` | NPC 走**同一个装配点**（同一份快照、同一个 rev 号），避免"配了却没装" |
| 编辑器插件 | `Plugins/MonsterEditor`（7 面板 + 3 自绘视图 + CLI 桥 + `_verify.sh`） | **照模板克隆**，视图复用 `PatrolMapView` 形状 |
| CLI | `Horizon.MonsterCli`（catalog/validate/save/publish/preview-*/export-content/route/navcheck） | 同输出契约 `ok/verb/message/data/warnings` + exit 0/1 |

---

Sources:
- [Warcraft Wiki – Quests（指示器语义与任务链）](https://warcraft.wiki.gg/wiki/Bonus_objective)
- [GAME Watch「World of Warcraft」クエスト編（灰色 ? = 已接未完成）](https://game.watch.impress.co.jp/docs/20050701/wow1.htm)
- [AzerothCore wiki – npc_vendor（slot/maxcount/incrtime/extended_cost）](https://www.azerothcore.org/wiki/npc_vendor)
- [AzerothCore wiki – creature_template（npcflag 位图/rank/MovementType/AIName/difficulty_entry）](https://www.azerothcore.org/wiki/creature_template)
- [魔兽世界自定义添加 NPC 教程（creature_template/gossip_menu/npc_vendor 关系）](https://zhidao.baidu.com/question/2280183804978475708.html)
- [AzerothCore 综合菜单商人制作（gossip_menu_option ActionMenuID ↔ npc_vendor）](https://uiwow.com/thread-17046-1-1.html)
- [3.3.5 TrinityCore 自定义商人（三表联动与字段清单）](https://blog.csdn.net/weixin_41982881/article/details/128636709)
