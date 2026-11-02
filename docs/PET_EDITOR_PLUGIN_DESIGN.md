# 宠物编辑器插件设计（PetEditor）

> 目标：新增 MMORPG 宠物编辑器插件，覆盖宠物编号（被抓后长期存在 → 必须处理重复副本）、技能、行为、外观、AI 智能程度、
> 技能学习、装备穿戴、孵化、蜕变、合成、合体、临时变异、辅助（含对角色的属性加成）、自动寻路等全部宠物系统行为。
> 行为规格的逆向分析来源见 `docs/PET_SYSTEM_REVERSE_ANALYSIS.md`（下文以 `REVERSE §x` 简称）。
> 架构**逐层镜像已交付并有执行证据的怪物编辑器**（`docs/MONSTER_EDITOR_PLUGIN_DESIGN.md`），不另起一套。

---

## 1. 目标与不做什么

**做**：
- 一套纯 C# 确定性宠物内核（`Horizon.Game.Sim/Pets/`），把四款游戏的宠物机制落成可调表 + 可单测的函数。
- 宠物**物种模板（PetSpecies）**与**个体实例（PetInstance）**分离：模板进编辑器与库，个体随玩家长期保存。
- 数据层：SQL Server 表 + 迁移脚本（**只生成不执行**，等用户点头；021/022 已于 2026-10-29 点头执行）。
- CLI 权威通路：`Horizon.PetCli`，Flax 插件以短命子进程调用它（同 MonsterCli/QuestCli 决定）。
- Flax 编辑器插件 `Plugins/PetEditor`：多面板窗口 + 自绘视图（成长曲线/技能格/资质雷达/谱系图）。
- 客户端消费链：宠物外观与跟随/寻路表现走 NarrativePro 导航 API。
- 门禁：`dotnet test --filter PetEditor` + 插件离线 csc `_verify.sh`。

**不做**（本期）：
- 不执行任何 SQL（生成 + 自查，等批准）。
- 不做宠物美术资产（沿用怪物编辑器的已知现实：`Content` 里可能只有材质没有网格；导出 0 条是如实状态）。
- 不改服务端 Orleans Grain 的宠物持久化实现（内核与表先就位，接入点单列 §9）。
- 不做 PIE 沙盘预览（PIE 脚本类型污染问题已知，见记忆 `flax-pie-unregisters-metahuman-scripts`）。

---

## 2. 架构总览

### 2.1 分层与权威源

```
docs/PET_SYSTEM_REVERSE_ANALYSIS.md     ← 行为规格（四款游戏逆向）
        ↓
Horizon.Game.Sim/Pets/                  ← 唯一事实源：纯函数内核（无 IO / 无 Flax / 定点数）
        ↓                                    ↑ 对拍
HundunWorld/Plugins/PetEditor/Source/    PetPreviewMath（插件侧第二份实现，测试按文本对账）
        ↓
Horizon.PetCli                          ← 校验 + 落库 + 导出（JSON 协议）
        ↓
SQL Server（权威）+ Redis（草稿）
        ↓
DsHost / 客户端（外观表 + 行为参数快照）
```

为什么用子进程 CLI 而不是 HTTP：与 MonsterCli/QuestCli 同一结论 —— 不把 SqlClient/EF 拖进 Flax 脚本域，
且避免"编辑器一份校验、服务端一份校验"分叉。校验器只有一份，在 CLI 里。

### 2.2 数据权威与草稿
- **权威**：SQL Server。发布态（`publish_state`）决定运行时是否可见。
- **草稿**：Redis（键 `pet:draft:{speciesId}`，ZSET 索引 `pet:drafts`，TTL 72h），照抄 `MonsterDraftStore` 的约定。
- 编辑器只改草稿；`publish` 才写权威表。

### 2.3 号段与白名单（宠物编号的核心决策）

**两层编号，不可混用：**

| 编号 | 含义 | 号段 | 生命周期 |
|---|---|---|---|
| `species_id`（宠物编号/物种号） | 编辑器里制定的"这一种宠物" | 500000–599999 普通宠；600000–649999 变异/稀有形态；650000–699999 神兽；700000+ 活动限定 | 长期，改一次升 `content_version` |
| `pet_instance_id`（个体编号） | 某一只具体宠物（服务端主键） | 由 `PetIdentityCore` 生成：`(species_id * 100000) + sequence`，sequence 单调递增 | 被抓后**永久存在**，不回收 |

**重复副本必须显式建模**（REVERSE §A2/B2/C2/D2：四款游戏皆无玩家可见全局唯一 ID，但都靠"昵称+等级+资质面板"区分同名个体）：
- 同一 `species_id` 可有任意多个 `PetInstance`（重复副本），它们**必须能共存**（玩家抓了 3 只同名吸血鬼）。
- 身份判定 = `(instance_id)` 唯一；显示判定 = `(species_id, nickname, level, 资质指纹)`。
- **显示文本绝不能当身份用**（记忆 `display-text-must-not-be-identity`）：昵称可改、可翻译，故 `nickname` 只做展示，
  任何跨端引用一律用 `instance_id`。
- 放生/交易/登录时"重复副本对账"要有明确规则：`(owner_id, species_id)` 上的计数与实例清单**必须一致**，
  不一致要大声报（而不是静默去重）。

### 2.4 为什么个体实例不进编辑器数据库
编辑器制定的是**模板**（资质区间、成长档位、技能池、AI 档位、外观）。个体实例是**运行时产物**，
由捕捉/孵化/合成/繁殖产生。两者在库里分表：`PetSpecies*`（编辑器读写）与 `PetInstance*`（运行时读写，本期只建表不接入）。

---

## 3. 内核模块（`Horizon.Game.Sim/Pets/`）

每个 core 都是 `static` 纯函数 + `readonly struct` 数据载体，带 `public const int Version` 作为金轨迹锚点，
坏配置**钳位不抛**，跨平台逐位一致靠**定点数（basis points）**不用 float。以下逐模块给规格出处。

### 3.1 `PetIdentityCore` — 编号与重复副本
- `NewInstanceSpeciesId(int speciesId, int sequence)` / `TryParseInstanceId(long, out speciesId, out sequence)`
- `DuplicateFingerprint`：`(speciesId, generation, 资质五维, 成长档)` 组成的**可比较指纹**，用于"这只是不是重复副本"。
- 断言：sequence=0 与 species=0 都不能被当成合法值（记忆 `struct-default-zero-can-look-like-a-valid-index`：
  "从未发生"必须多条件合取判据）。

### 3.2 `PetStatsCore` — 资质/成长/属性（REVERSE §B3 / §C3 / §D3）
- 五维资质：`Attack / Defense / Stamina / Mana / Speed`（梦幻口径）；`Power/Spirit/Vitality/Steadiness/Agility` 作天龙口径别名表。
- 有效资质 = 裸资 ×(1+悟性加成)×(1+灵性加成)× 变异系数 —— 天龙公式（§D3），悟性表 1–10 =
  `1,1.5,2.1,3,8,11,14.5,23.5,30,39.3` ‱×100（用 bps 存整数），灵性普通 `1..31`%、超灵 `1..34`%。
- 战斗属性用梦幻后验公式作**初值表**（§B3，标注"拟合初值"）：
  - HP = level×体力资质/1000 + con×growth×6
  - MP = level×法力资质/500 + spirit×growth×3
  - ATK = level×攻资×(14+10×growth)/7550 + str×growth
  - DEF = level×防资/433 + end×growth×4/3
  - SPD = 速资×总速度点/1000
- 成长率定点数存储（`growth_bps`，1.000 = 10000），避免 float 累加漂移。
- 每级潜能 5 点（§B3/§D3 一致）。

### 3.3 `PetGrowthCore` — 孵化与蜕变（REVERSE §B1/§C1/§D8）
- **孵化**：蛋 → 0 级宝宝。阶段 `Egg → Hatchling → Adult → Awakened`；孵化时长/进度 tick 表；
  梦幻"捕捉即得 0 级宝宝"与 WoW"无孵化"作为两种模板配置（`hatch_seconds=0` 即 WoW 档）。
- **等级上限受主人约束**：`levelCap = min(物种上限, ownerLevel + capBonus)`，天龙 +10（§D3）、WoW +0（§A3）→ 可配 `ownerLevelSlack`。
- **蜕变**：`MetamorphosisStage`，前置 = 等级/默契/材料；结果 = 形态阶段 +1、资质重算、外观 key 切换（§B8 进阶、§C8 点化/化形、§D8 幻化）。
- 经验曲线与"同级不再得经验"（§A3）。

### 3.4 `PetSkillCore` — 技能学习与打书（REVERSE §A4/§B4/§C4/§D4）
- 两级分离：**技能池 learned**（WoW BP "学 6 装 3"）⊇ **装配槽 slots**（梦幻/大话/天龙"格子里有什么"）。
- 槽位上限 `MaxSlots = f(species 上限, 等级段, 悟性档)`：
  - 梦幻：按物种上限（吸血鬼 5、僵尸 4），顶书开格仅在 参战≤125 且 已有≤3（§B4）
  - 大话：普通 4 / 高级 6 / 特殊 8 / 神兽 11 / 珍惜 13（§C4）
  - 天龙：基础 4，悟性 4 级 +1、7 级 +1 → 6（§D4）
- **打书**（`LearnSkill`）：空格优先；满槽时按概率覆盖非锁定、非天生槽；冲突表命中则新技生效旧技失效（§B4 冲突、§C4 大概率顶替）。
- **锁定**：`lockedSlots`，大话 8 格可锁 2 技、天生技不可锁（§C4）。
- **领悟**（`EpiphanyRoll`）：参战结算后按概率自动学随机技能（含终极技），满栏不领悟（§C4）；内丹升层同理（§B4 内丹 1–5 层）。
- 全部随机走**确定性哈希**（同 seed 同结果），不用 `Random`。

### 3.5 `PetBondCore` — 忠诚/快乐/寿命/亲密（REVERSE §A6/§B6/§C6/§D6）
四条轴独立，事件表驱动增减（bps 或整数）：

| 事件 | 忠诚 | 快乐 | 寿命 | 亲密 |
|---|---|---|---|---|
| 随主人出战 1 次 | −1 / 2 次（梦幻） | 缓慢衰减 | −1（梦幻/大话） | 概率 +1（大话） |
| 被击飞/战斗死亡 | −20% 当前（梦幻）/ −20（大话） | 大幅下降 | −50（梦幻）/ −10（大话） | — |
| 宠物升级 | +10（大话） | — | — | 升级也提（大话） |
| 主人死亡 | −10（大话） | — | — | — |
| 主人逃离战斗 | −5（大话） | — | — | — |
| 死亡后复活 | +20（大话） | — | — | — |
| 更换主人/交易 | −20（梦幻/大话） | — | — | — |
| 喂食 | 涨忠诚（WoW，食物等级差决定回量 +350/+170/+80） | +350/+170/+80（WoW 0–1000） | — | — |

- **出战门槛**：忠诚 ≤70 → 概率拒绝出战（梦幻）；快乐 <60 → 禁止出战（天龙）；寿命 <51 且非永生 → 禁止（梦幻）；寿命 <0 → 禁止（天龙）。
- **倍率效果**：快乐三档伤害倍率 **75% / 100% / 125%**（WoW 红/黄/绿脸，§A6）。
- **衰减**：快乐随时间衰减，速率由忠诚决定（§A6）。
- 门槛要产出**可显示的拒绝理由**，不是静默 0（REVERSE §E2.4；记忆"拒绝理由给状态位别猜文案"）。

### 3.6 `PetAiCore` — AI 智能程度与行为（REVERSE §A5/§B5/§C5/§D5）
- **指令**（玩家给的）：`Attack / Follow / Wait / Defend / Retreat`（WoW 控制条 §A5）。
- **姿态**（常驻的）：`Cowardly / Passive / Defensive / Aggressive`（WoW 四档；经典版三档 → 表里共存）。
- **智能程度档位** `PetIntellectTier`：`Mindless / Instinct / Trained / Clever / Master`
  决定 5 个能力位（是否自主选目标、是否按概率放技能、是否避让、是否护主、是否用控制技），
  三态展开同 `MonsterAiFlags.Resolve(profile, int? override)`（允许 null=跟随档位默认）。
- **性格**（天龙 §D5）：`Brave / Clever / Timid / Cautious / Loyal` → 影响触发概率与受伤概率的乘数表。
- **技能释放策略**：按技能等级概率自动释放（天龙 §D5）；目标选择（敌方主目标/最低血/仇恨，复用 `MonsterThreatCore` 的升序 Span 表思路）。
- **参战位约束**：每玩家仅 1 只出战（§D5），附体位 3（§D11）。

### 3.7 `PetEquipmentCore` — 装备穿戴（REVERSE §B7/§C7/§D7）
- 部位集合**按物种模板可配**：`{Wrist,Collar,Armor}`（梦幻）/ `{Ring,Armor,Bell}`（大话）/ `{Head,Claw,Body,Collar,Talisman}`（天龙）/ `{}`（WoW 档=无）。
- 每件：`equipLevel(75/85/95)`、`star(1–5)`、`appraisalTier(普通→超完美)`（超完美 +80% 基础属性）、`gemSlots`。
- 灵石/宝石数值表（梦幻单颗：防+8 速+6 伤+10 躲+20 灵+4 血+30；每件只镶一种 §B7）。
- 套装：按性格成套，集齐同套同星级给额外属性（§D7）；觉醒格条件：品质 2500/5000/10000 + 强化 8/13/16（§C7）。
- 装备**绑定角色可跨宠复用**（§D7）→ 装备实例与宠物实例是两条独立记录，穿戴是引用。

### 3.8 `PetAssistCore` — 辅助/对角色的属性加成（REVERSE §A11/§B11/§C11/§D11）
三条通路，档位可全 0（WoW 档）：
1. **辅主特技**（天龙 §D11）：`借力→力量、凝神→灵气、强身→体力、瞬影→身法、移魂→定力`，出战即加在主人身上，按技能等级。
2. **附体位**（天龙）：75 级开 3 位，附体宠按**等级/资质**给主人属性，**技能等级不影响**该加成（§D11 明确）。
3. **护主/反哺**：`共生`（按比例把宠物生命转化为主人生命上限，体力越高越多）、`血迹`（按体力资质/等级回复主人内力）、
   `忠心`（概率替主人承受会心）、大话`伤害 15% 转化主人生命/法力`（§C11）。
- 输出 `PetOwnerBonus`（六维 + 百分比修正），供 DS/角色属性面板消费；**必须能逐条归因到"哪个技能/哪个附体位给的"**。
- **多宠合成 `AggregateOwnerBonuses`**（1 只出战 + 至多 3 只附体 → 一份主人面板）：
  **属性列相加、概率列取最大**。概率不能相加——3 只"忠心 30%"相加会得出 90%，4 只直接超 100%，
  而四款游戏的实际表现都是"有会挡的宠时按最强的那只算"。超出出战/附体位上限的宠物被**排除并计数**，
  没有个体编号（=0）的宠物同样排除：默认 0 会伪装成"合法的第 0 号"。
- **落到主人面板 `ApplyToOwnerStats`**：同时返回"合算值"与"其中宠物贡献量"两份。
  一旦把加成混进基础值，面板就再也回答不了"这 1060 攻击是谁给的"
  （四款游戏在"宠物给的算不算战力"上口径不一致：WoW 完全不算、天龙全算）。
  换算系数（力量/灵气→攻击 2、体力→生命 8、定力→防 2、身法→命中 3）是**本工程口径**，四款游戏都不公开这一层。
  **乘法先走 long 再饱和钳位**：先 int 相乘会先回绕成负数，于是"宠物给了几百万攻击"
  反而把主人属性打回基础值——这条由测试 `属性相加不许溢出成负数` 守住（它当场抓出了这个缺陷）。

### 3.9 `PetMergeCore` — 合成与合体（REVERSE §B9/§C9/§D9）
两个模式，共用"多亲本 → 新个体"骨架：
- **合成/炼妖**（梦幻，消耗双方）：材料 ≥2 只且 ≥30 级；资质 ≈ mean(A,B) × [70%,110%]；成长 ≈ mean × [96%,102%]（标 §B9 **[低]拟合初值**）；
  技能取并集抽样，常出 8 技能上限；形态重置为幼体；顶书过的胚子成功率 ×3（社区口径）。
- **合体/繁殖**（天龙，保留父母）：9 条前置（同类型 / 皆宝宝 / 异性 / 等级差≤5 或双方≥婚配级 / 无配偶或互为配偶 /
  非变异非二代非成年 / 快乐=100 且寿命≥1000 / 非出战 / 队长≥2 两黄金）；
  婚配等级 30/50/70/90（每繁殖一次 +20）；产出通常 2 只二代；**48 小时不领取则丢失**；变异不可繁殖。
- 结果必须**确定性**（同 seed 同结果），并给出"结果分布"可检视字段（继承自哪一亲本的哪一项）。

### 3.10 `PetMutationCore` — 临时变异（REVERSE §B1/§B8/§C10/§D8/§A10）
- **永久变异**（稀有度层）：资质上限 ×1.01–1.08（§B1）、独有色、不可用于炼妖（§B1）。
- **临时变异**（带有效期的状态层）：`TimedMutation { key, expireTick, statCapsBps, colorOverride, slotBonus }`，
  到期整体回退且**不污染持久身份**；来源：染色/幻色丹（§B8）、鎏金/灵契变色（§C10）、事件变体（§A10 季节宠物）。
- 临时层的加成与永久层**分开算**，回退只削临时层。

### 3.11 `PetFollowCore` + `PetPathCore` — 跟随与自动寻路（REVERSE §A12/§D12/§E2）
- 服务端：复用 `MonsterPathCore` 的确定性 8 邻域 A*（同图同起终点路径每次相同，已有测试原型），
  新增"跟随目标=主人"的滚动重算策略：`resampleDistance`、`repathIntervalTicks`、`leashRadius`、`teleportBeyond`。
- 表现规格（WoW 是唯一即时制样本，§A12）：短距跟随、待命不动、超距**传送回主人**、死亡**尸体滞留原地**、
  复活是独立动作（4 秒施法 / 1.5 秒 GCD / 恢复部分 HP）、解散→兽栏瞬召无 CD。
- 客户端寻路（**参照 Flax 原生 API 与 NarrativePro 实现**，已核实签名）：
  - `NarrativePro.Navigation.NavigatorStatics.IsNavigationReady()`（`Plugins/NarrativePro/Source/NarrativePro/Navigation/NavigatorStatics.cs:130`）
  - `NavigatorStatics.TryFindPath(Vector3 start, Vector3 end, out Vector3[] path)`（同文件 `:156`，另有带 `NarrativeNavQueryFilter` 重载 `:170`）
  - `NavigatorStatics.ResamplePath(Vector3[] corners, float distanceBetweenPoints, out List<Vector3> result)`（`:295`）
  - 用法照抄 `Source/Game/PlayerController.cs:1190-1212`（`waypoints[0]` 是当前位从第二个点起走；导航不可用时降级直线）。
  - **坐标系**：ECS↔Flax 要 Y/Z 交换（记忆 `3d-y-z`、`ds-transform-3d`），宠物跟随同样适用。

### 3.12 `PetContent` — 内容装配
照抄 `MonsterContent` 的原子整包替换：`PetContentBundle` / `PetRuntimeConfig` / `IPetSpeciesSource` / `PetContent.Install/ResetToHardcoded/TryGetConfig`。
**声明的字段必须有人读**（记忆 `schema-key-declared-but-never-applied`）：每个表列都归入某个 core，由契约测试遍历。

---

## 4. 数据模型（SQL）

命名沿用：`scripts/sql/021_pet_editor_tables.sql`、`scripts/sql/022_seed_pet_species.sql`（019/020 已被怪物占用，已核实）。
生成时**只生成不执行**；2026-10-29 用户点头后已在演练库彩排并在 `Game` 库执行（§10.3）。
表前缀 `Game_HunduShijie_`，每表带平台基列 `IsValid/GameId/AreaId/ServerId/GameUserId`。

模板侧（编辑器读写）：
1. `PetSpecies`（`species_id` PK）— 名称/稀有度/形态阶段/外观 model_key+prefab/尺寸/可携带等级段/孵化配置/潜能点/owner_level_slack/publish_state/content_version
2. `PetSpeciesStat`（1:1）— 五维资质区间 min/max、成长 5 档、悟性/灵性上限、变异倍率上限
3. `PetSkillSlotRule` — 槽位上限族（按物种/等级段/悟性档三张规则）
4. `PetSkillPool` — 天生必带 / 可学池 / 互斥表 / 锁定允许数
5. `PetBehavior` — AI 档位 + 5 能力位（可空三态）+ 性格 + 指令默认 + 跟随参数（leash/传送/复活秒）
6. `PetEquipRule` — 部位集合、套装族、觉醒格条件、灵石数值表引用
7. `PetAssistRule` — 辅主特技映射、附体位、护主折算系数
8. `PetMergeRule` — 合成/繁殖前置、继承带宽、产物数量、领取时限
9. `PetMutation` — 永久变异档 + 临时变异模板（时长/加成/色）

个体侧（运行时；本期建表不接入 Grain）：
10. `PetInstance`（`pet_instance_id` PK）— species_id / owner / 昵称 / 阶段 / 等级经验 / 六维属性点 / 有效资质 / 成长 / 四轴默契 / 技能槽 JSON / 装备引用 / 变异标记 / **重复副本指纹** / 捕获来源 / 状态（野外/背包/兽栏/出战/附体/蛋/待领取/放生）
11. `PetInstanceHistory` — 关键操作流水（捕捉/孵化/打书/蜕变/合成/繁殖/放生），可审计

自查段照 020 的做法在脚本末尾放 `SELECT COUNT` 断言，并在头注释写明值来源（哪个 core 的哪张表）。

---

## 5. 编辑器插件 `Plugins/PetEditor`

### 5.1 骨架（逐文件镜像 MonsterEditor）
- `PetEditor.flaxproj`（Name/Version/Company、GameTarget/EditorTarget、`$(EnginePath)/Flax.flaxproj`、`MinEngineVersion 0.0.6194`）
- `Source/PetEditorTarget.Build.cs` / `PetEditorEditorTarget.Build.cs`
- `Source/PetEditor/PetEditor.Build.cs`：`GameModule`，`BuildNativeCode=false`，SystemReferences `System.Text.Json`、`System.Linq`
- `Source/PetEditorEditor/PetEditorEditor.Build.cs`：`GameEditorModule`，`PublicDependencies.Add("PetEditor")`，SystemReferences 加 `System.Diagnostics.Process`、`System.Threading`
- **不引用 Horizon.Game.Sim**（Flax 脚本域引不到仓库工程）；改为 `PetDraftModel.cs`（镜像 CLI 草稿）+ `PetPreviewMath.cs`（内核第二份实现，测试按文本逐字对账）
- 注册：`HundunWorld/HundunWorld.flaxproj` References 追加 `$(ProjectPath)/Plugins/PetEditor/PetEditor.flaxproj`；`HundunWorld/HundunWorld.slnx` 的 `/Plugins/PetEditor/` 文件夹加两 csproj 与 BuildDependency；`Horizon.sln` 加 `Horizon.PetCli`。

### 5.2 面板（`Tools → 宠物编辑器`）
左侧物种列表 + 页签：
1. **基础·外观**（名称/稀有度/形态/编号号段/modelKey/prefab/scale/染色基色）
2. **资质·成长**（五维区间、成长 5 档、变异上限、潜能点）
3. **技能·打书**（槽位规则、天生技、可学池、互斥、锁定数、领悟率）
4. **行为·AI**（智能档位 + 能力位三态、性格、指令默认、忠诚度/快乐表）
5. **默契·孵化**（四轴初值/事件增减/出战门槛/孵化时长/蜕变条件）
6. **装备·辅助**（部位集合、套装、觉醒格、辅主特技映射、附体位、护主系数）
7. **合成·变异**（炼妖/繁殖前置、继承带宽、产物数、领取时限、临时变异模板）
8. **跟随·寻路**（leash 半径、重算间隔、超距传送、复活秒数、路径预览图）

自绘视图（只用 `Render2D.FillRectangle/DrawText/DrawRectangle`，避开未证实的 `DrawLine` 重载 —— 同 `MonsterViews.cs:26-29` 结论）：
- `GrowthCurveView`：等级→战斗属性曲线，点击放读数游标
- `SkillSlotView`：技能格矩阵（天生/锁定/空/占用四色）
- `StatRadarView`：五维资质雷达（用方块近似折线）
- `PedigreeView`：合成/繁殖谱系图（父母→产物→代际）
- `FollowRadiusView`：俯视跟随半径与路径预览

布局纪律：**全部 TopLeft 锚 + 显式 `LayoutNow()`**（记忆 `flax-anchor-presets-near-far`、`flax-gui-location-topleft`），
`RowHeight=26` 的 `Number/Drop/Caption` 行助手推进 `ref float y`。

### 5.3 后端 CLI `Horizon.PetCli`
- `Program.cs` 分发：`help | catalog | list | get <id> | validate - | save - | publish <id> | retire <id> | delete <id> | draft save|list|get|delete | preview-stats --species .. --level .. [--sweep] | preview-learn --species .. --skill .. [--seed ..] | preview-merge --a .. --b .. --mode .. | preview-bonus --species .. | export-content --out .. | export-visuals --out .. | instance list [--owner ..]`
- 输出契约：单行 JSON `{ok,verb,message,data,warnings}`，camelCase + `UnsafeRelaxedJsonEscaping`，退出码 0/1（同 MonsterCli）。
- 读草稿路径先 `FindUnknownKeys` 拒绝未知顶层键（防 STJ 静默丢字段）。
- `PetCliBridge.cs` 直接照搬 `MonsterCliBridge` 的进程调用纪律：`UseShellExecute=false`、`CreateNoWindow=true`、
  只在有 stdin 时 `RedirectStandardInput`、**绝不设 `StandardInputEncoding`**（未重定向会抛、`Encoding.UTF8` 会写 BOM）、
  stdin 用 `Encoding.UTF8.GetBytes` 裸写 BaseStream 后 Close 作 EOF、`WaitForExit(120000)` 超时 Kill、
  结果解析剥 U+FEFF 并从末行往前找 `{`、回主线程用 `Scripting.InvokeOnUpdate`、单飞 `GuardBusy`。

### 5.4 巡逻/跟随点采集
照搬 `MonsterEditorWindow.cs:1369` 的做法：读 `Editor.Instance.SceneEditing.Selection` 的 `Actor.Transform.Translation`，
**Y/Z 交换**后写入（Flax Y-up → 服务端 Z-up）。

---

## 6. 验证闸门

1. `dotnet build Horizon.Game.Sim --no-incremental`（记忆：增量假绿）
2. `dotnet test --filter PetEditor`（新内核 + 契约测试）
3. 全量 `dotnet test` 不回退（当前基线 3289 绿）
4. `HundunWorld/Plugins/PetEditor/_verify.sh`：用引擎 Roslyn `csc` 离线编译插件两模块（`dotnet build` Flax csproj 是假绿、Flax.Build 无逐插件可查日志）
5. `scripts/verify-client-scripts.sh` 客户端脚本 0 错
6. 列对账脚本（仿 `scripts/verify-monster-seed-columns.py`）：022 的种子列必须与 core 的表逐字同名
7. 契约测试用**源文本解析**比对插件草稿与 CLI 草稿的 `Known*Keys` 集合（Flax 程序集不可被 xUnit 引用）

---

## 7. 开工前已核实的事实（不靠记忆）

- `scripts/sql/` 现最大编号 **020** → 宠物用 021/022。
- `HundunWorld.flaxproj:14-29` 已注册 6 个插件；追加处一致。
- 导航 API 三个方法签名已实读（§3.11），客户端用法在 `PlayerController.cs:1190-1212`。
- `Horizon.MonsterCli/bin/Debug/net10.0/` 是 CLI DLL 的解析位置；插件只启动 exe。
- 全仓无既有宠物系统（`grep -riE "\bpet\b|宠物"` 只命中 `repeat/dep` 之类子串与 SyncGuard 注释）→ 绿地产出。
- 四款游戏皆**无玩家可见全局唯一 ID**（REVERSE §E3）→ "宠物编号+重复副本"是超出原作的工程需求，方案自定，见 §2.3。

---

## 8. 决策清单（需要用户点头，默认取"最完整那档"）

| # | 决策 | 选项 | 我的默认 |
|---|---|---|---|
| D1 | 个体编号方案 | ①`species_id*100000+seq` ②GUID ③雪花 | **①**（可读、可反解物种、与项目"id 即契约"风格一致）；seq 溢出用 64 位兜底 |
| D2 | 物种号段 | 500000+ 起步 | **500000–599999 / 600000–649999 / 650000–699999 / 700000+**（避开怪物 1–99999/…300000+ 与技能 800000+） |
| D3 | 默契轴数量 | ①合并 1 条"默契" ②保留 4 条(忠诚/快乐/寿命/亲密) | **② 4 条**（各家语义与门槛不同，合并会丢可配置性；编辑器里可按模板把不用的轴置为常量） |
| D4 | 技能模型 | ①只有槽 ②池(learned) ⊇ 槽(equipped) 两级 | **②**（同时要 WoW BP 的"学6装3"和梦幻的"格子里有什么"） |
| D5 | 反哺主人 | ①做(WoW 档=0) ②完整三条通路 | **②** 辅主特技/附体位/护主，档位可降到 0 |
| D6 | 装备部位 | 固定 5 部位 / 按物种模板可配 | **可配** |
| D7 | 临时变异 | 只做外观 / 外观+资质上限+槽位加成且带有效期 | **后者**，到期整体回退不污染身份 |
| D8 | 个体实例表 | 本期只建表不接 Orleans Grain / 一并接入 | **只建表 + 内核可达** → 2026-10-29 用户点头反转为**接入**：`PetRosterGrain` 读侧与反哺到角色面板已闭环（§10.2b），写入口与战斗结算仍留接缝（§10.4） |
| D9 | 021/022 SQL | 生成 + 自查，**不执行** | **不执行** → 2026-10-29 用户点头改为**已执行**（先演练库彩排，§10.3 抓到 3 条语法缺陷后才进 `Game` 库） |
| D10 | 美术 | 沿用现有 prefab / 新做 | **沿用 + 如实报告 0 条导出**（怪物侧已证实"没美术不是链路断"） |

### 8.1 实施顺序
1. REVERSE 文档（已交付）→ 2. 设计文档（本文）→ 3. 内核 cores（纯函数，最快可测）→
4. 内核测试（含独立暴力参照）→ 5. SQL 021/022 + EF 实体 → 6. `Horizon.PetCli` →
7. Flax 插件（draft model → preview math → window → views → bridge → `_verify.sh`）→ 8. 契约测试 → 9. 客户端跟随/寻路消费链 → 10. §10 实施记录回填。

---

## 9. 与既有系统的接缝

| 接缝 | 现状 | 宠物侧动作 |
|---|---|---|
| 角色属性面板 | 已有属性结算 | **已接（2026-10-29）**：`PetRosterGrain` → `IPetBonusReader` → `CharacterAttributeRefresh` 的 `FromPets*` 归因列 → 面板"宠物加成"那一行。逐条来源清单（`PetBonusSource`）目前仍只在 CLI/插件侧消费，面板给的是四项合算归因 |
| 战斗技能系统 | 五行技能树（id 契约 `800000+技能id`） | 宠物技能引用同一 skill id 域，不另立编号 |
| DS 通道同步 | Identity v14 已带怪物外观 5 字段 | 宠物实体走 DS spawn/despawn；**Despawn 必须全量广播**（记忆 `despawn.md`）；快照 Transform **必须 Y/Z 交换** |
| 导航 | NarrativePro `NavigatorStatics` + 服务端 `MonsterPathCore` | 复用 `MonsterNavGrid`；宠物跟随走同一网格 |
| 捕捉 | 怪物模板存在、可捕捉标记尚无 | `PetSpecies.catchable` + 捕捉概率内核（梦幻：等级/猎术/残血/场景；WoW：引导 20 秒 + 栏位检查） |
| DLL 清单 | `Game.Build.cs` + `copy-shared-dlls-to-flax-tools.ps1` 双清单，`FlaxToolsRefreshListTests` 守 | 若新增共享 DLL 必须同步两处 |
| 注释规范 | 全仓简体中文注释 | 新代码即遵守（记忆 `code-comments-must-be-simplified-chinese`） |

---

## 10. 实施记录

### 10.1 决策结果
§8 的十项决策已由用户逐条确认（2026-10-25）：范围取**全链**（内核+表+CLI+插件+客户端）、
编号用 **species_id×100000+seq**、默契**保留 4 条独立轴**。其余项按 §8 默认列执行。

### 10.2 已在盘上并有执行证据

| 层 | 产物 | 证据 |
|---|---|---|
| 行为规格 | `docs/PET_SYSTEM_REVERSE_ANALYSIS.md`（四款游戏 × 14 维度 + 跨游戏合并表 + 低置信清单） | 文档；内核每个 core 的注释都按 `§x` 回指它 |
| 设计 | 本文 | — |
| 内核 | `Horizon.Game.Sim/Pets/` 14 个文件（13 个 core + 装载器）：Identity / Stats / Growth / Skill / Bond / Ai / Equipment / Assist / Merge / Mutation / Follow(+Path 复用) / Capture / Content / ContentLoader | `dotnet build Horizon.Game.Sim --no-incremental` **0 错误** |
| 内核测试 | `Horizon.Game.Gateway.Tests/PetEditorKernelTests.cs`（95 条） | `dotnet test --filter PetEditorKernelTests` **95 通过 / 0 失败** |
| 数据层 | `Horizon.Model/GameModel/PetTemplateEntity.cs`(4 类) + `PetWorldEntity.cs`(7 类)，`GameEntityContext.cs` 两上下文各登记 11 个 DbSet | `dotnet build Horizon.Entities --no-incremental` **0 错误** |
| SQL | `scripts/sql/021_pet_editor_tables.sql`（11 表 / 238 列，843 行）、`scripts/sql/022_seed_pet_species.sql`（9 张规则表种子 + 7 段自检，242 行） | 由 `Tools/gen-pet-sql.py` 生成，四道闸门当场通过：`seeded_tables=9/9`、`parsed_cols=183 == source_columns=183`、无尾逗号、IDENTITY_INSERT 夹层与 MERGE 分号自洽。**已执行**（§8 D9 2026-10-29）：`Game` 库 66→77 表、种子行数 5/5/5/15/5/5/10/5/4、运行时两表 0 行、复跑幂等 |
| CLI | `Horizon.PetCli/`：`Program.cs`(18 动词) + `PetDraft.cs` + `PetDraftStore.cs`(Redis) + `PetValidator.cs` + `PetStore.cs`(九表 upsert，写序=引用序) | `dotnet build --no-incremental` **0 错误**；无库依赖动词**全部真跑过**：`catalog`、`preview-stats`(有效资质 1200→2189)、`preview-learn`(Added/roll 9053)、`preview-merge` 两个模式(fuse 产 1 吃亲本 / breed 产 2 留亲本 + 48h)、`preview-bonus`(附体五维 + 忠心 2400bps；带 `--ownerAttack 100` 时回 **attack=1160 / fromPetsAttack=1060 / hp 2000→3920 / critTaken 3000bps**)、`preview-capture`(个体号 50000300003 = 该物种第 3 只)、`preview-bond`(不可出战 LowHappiness、红脸 7500、拒绝率 4285bps) |
| 编辑器插件 | `HundunWorld/Plugins/PetEditor/`：flaxproj + 两 target + 两 module + `PetDraftModel.cs`(协议副本) + `PetPreviewMath.cs`(内核第二份) + `PetEditorPlugin.cs` + `PetEditorWindow.cs`(8 页签 + 10 个工具条控件：第一行 路径框/探测/刷新目录/草稿▾+执行，第二行 库操作▾+执行、试算▾+运行、导出外观表；2026-10-29 由 17 个平铺按钮收成三组下拉——"删除/停用"不再是一下就点到的东西，条目带组名前缀（`草稿：校验`）让下拉看起来像下拉) + `PetViews.cs`(5 自绘视图) + `PetCliBridge.cs`；已登记进 `HundunWorld.flaxproj` 与 `HundunWorld.slnx` | **两级证据**：① `Plugins/PetEditor/_verify.sh` 离线 csc **rt 0 错 / ed 0 错**（这道门当场拦下 8 个猜错的引擎 API）；② **真编辑器已编译过本插件**——`HundunWorld/Logs` 里有 `Building referenced target PetEditor` 与 `Compiling …\PetEditor.CSharp.dll` / `…\PetEditorEditor.CSharp.dll`，只留两条 CS0414（未用字段，已删） |
| 客户端 | `Source/Game/Pets/PetVisualCatalog.cs`（读 `Data/Pets/client_visuals.json`）+ `PetFollowComponent.cs`（跟随/leash/超距传送/尸体/复活 + 走 NarrativePro 导航） | `scripts/verify-client-scripts.sh` **476 源 0 错** |
| 运行时装配 | `Horizon.Game.Sim/Pets/PetContentLoader.cs`（快照 → `PetContent.Install`）+ `Horizon.DsHost` 的 `--pet-content=<p>` + 夹具 `HundunWorld/Data/Pets/dev_content.json`（2 条合法 + 1 条越界号段） | **DS 宿主实跑回执**：`宠物内容已装配：宠物内容 rev=3 物种=2 挡下=1 问题=1`，随后 `DS 就绪…数据面端口=19870`。越界号段的 300007 被挡下并计数，没有静默收下 |
| 契约测试 | `PetEditorKernelTests`（95 条：含内容装载器 5 条、反哺合成与主人面板落地 8 条）+ `PetEditorContractTests`（20 条：草稿键集合逐字对账、插件表与内核逐值、算式顺序、DDL↔实体↔种子三方对账、自增禁令、导航入口同源性） | `dotnet test --filter PetEditor` **115 通过 / 0 失败**；`Horizon.Game.Gateway.Tests` 全量 **3470 通过 / 0 失败 / 8 跳过（总计 3478，2026-10-25 时点；在线链接通后的全量见 §10.2b）** |

### 10.2b 反哺在线链（2026-10-29 接通，§8 D8 由用户点头反转）

| 层 | 产物 | 证据 |
|---|---|---|
| JSON 列编解码 | `Horizon.Game.Sim/Pets/PetInstanceCodec.cs`（技能槽 / 穿戴清单 / 临时变异三列的唯一出口，确定性写出 + 逐条校验）；穿戴只存引用 `PetWornGear{ItemInstanceId,Slot}` 不复制本体 | `dotnet build Horizon.Game.Sim --no-incremental` **0 错误**；判据见 `PetRosterTests` ①（含"认外部手写的 JSON"这条独立参照） |
| 读侧 | `Horizon.Orleans.Grains/Pets/PetRosterMapper.cs`：实体行 → `PetRosterEntry`（身份自证、有效资质走内核 `EffectiveQualities`、辅主技只认该物种在 `PetAssistRule` 登记过的 id、按个体编号定序） | 纯函数，被 `PetRosterTests` ②直接喂假行验证（9 条） |
| Grain | `IPetRosterGrain`（**角色键**，与设计文档 §9 的归属口径一致）+ `PetRosterGrain`：`GetSnapshotAsync` 取数聚合、`SetDeployStateAsync` 是出战/附体/收回的唯一写入口（白名单目标态、只许改自己主人的、出战顶替旧宠、附体位上限、默契门复用 `PetBondCore.CheckDeployable`、状态变更写 `PetInstanceEvent` kind=17）。**不带 Grain 状态**：个体表就是权威，再存一份 Redis 就是两套账 | `PetRosterTests` ⑦ 用"真的执行谓词"的假上下文跑了 10 条（含"只取本主人的行""读库异常如实报空账""附体位满不挤掉已有那只""幂等不记事件"） |
| 取数门 | `IPetBonusReader` + `PetBonusReader`（跨 Grain 编排包一层，失败回空账并把原因写进 `Issues`）；在 `Horizon.Orleans.Silo/Program.cs` 注册 | `PetRosterTests` ④⑥：抛异常/silo 掉线要念出来；角色 id≤0 时**根本不打 Grain**（`Times.Never` 断言） |
| 折算 | `PetBonusProjection`（快照 → 主人面板归因量；主人基础值传 0 只取 `FromPets*`，换算系数一律问内核） | `PetRosterTests` ③：与手算参照逐位相等（力量×2 + 灵气×2 等） |
| 下行 | `CharacterAttributeRefreshResponse` **追加** 15–22 八个字段（`FromPetsAttack/Health/Defense/Hit`、`PetCritTankChanceBps`、`PetBonusContributors`、`PetRosterCount`、`PetBonusIssues`）。没新增消息类型：联合体标签 0..255 只剩 132 一个空位，且 `CharacterAttributeRefresh` 通道本来就在 | `PetRosterTests` ⑤：反射断言槽位 0..22 连续、`[Id]` 与 `[MemoryPackOrder]` 同值、新字段全在追加区 |
| 客户端 | `CharacterAttributesComponent.ApplyPetBonus(PetBonusInfo)`（按字段名赋值，不走八个位置参数）+ `CharacterAttributeRefreshHandler` 转发与日志 | `scripts/verify-client-scripts.sh` **493 源 0 错** |
| 面板 | `CharacterPanelPage` 基础属性卡新增"宠物加成"一行（`攻/血/防/命中` 斜杠摊开 + 行序图例；`-`/`无宠`/`未出战`/数字四种状态长得不一样） | 同上编译门；**PIE 目视未做**（本轮编辑器桥不可用） |
| 写入口 | `PetStore.AddInstance` + `Horizon.PetCli instance add`（个体号由内核算：序号取"该物种已用最大 + 1"，删除/放生不回收；写入后**从库里回读**才算成功）；`instance list` 的文案改成不再断言"写入口未接线" | 真跑回执：`个体 50000300001（物种 500003 第 1 只）已落库并回读到`、`个体 50000300002（第 2 只）已落库并回读到`；`instance list` 回 `500003 → instanceCount 2`。**同主同种两只并存**就是"重复副本"这一条的正证 |
| 测试 | `PetRosterTests.cs` **31 条**（编解码/读侧/投影/取数门/消息槽位/名册 Grain）+ `PetRosterLiveTests.cs` **1 条活库判据** | `--filter PetRoster` **32/32**；`--filter PetEditor` 仍 **115/115**；全量 **4027 通过 / 2 失败 / 8 跳过（总计 4037）**——那 2 条是 DS 规模与心跳的性能判据，单跑 **2/2 通过**，本机同时跑着 Silo/DS/网关/VS 时被负载挤掉，与宠物无关。**活库那条要 `HORIZON_PET_LIVE_DB=<连接串>` 才真跑**（仓库 `Live_` 系列的同一口径：没设环境就直接 return，等于没跑——所以它在 CI 上不构成证据）；开着门而库里不足 2 只个体时它会**大声失败**并给出重造数据的命令。真库跑通的实际读数：两只宠（出战带借力 4 级、附体带忠心 10 级）→ `Strength = 4×5 + 31×4×攻资/1000`、`FromPetsAttack = 力量×2 + 灵气×2`，与测试里另写的手算参照逐位相等 |
### 10.3 测试与闸门抓出的真缺陷

**内核（由 `PetEditorKernelTests` 抓到，全部是产品缺陷不是夹具错）**
1. **跟随/传送目标点算反了**：`OffsetToFollowDistance` 原按"从宠物沿朝主人方向走 followDistance 米"实现，
   得到的是"离主人 dist−3 米"；超距传送于是把宠物放到**离主人 97 米**处——即"传送了但仍在画面外"。
   改为从主人往宠物方向回退。由 `超距直接传送而leash内走位` 抓到。
2. **驯服概率泄漏了猎术加成**：引导未走完时 `chance=0` 却在 switch 之后又被 `+= skill×600` 捡回来，
   于是"引导到一半也能抓住"。改为 early-return。由 `驯服不看血量而战斗捕捉只看血量` 抓到。
3. **`StartTick == 0` 被当成哨兵**：开局施加的临时 buff 一律判成"未生效"。真正的哨兵是 `ExpireTick <= 0`。
   由 `临时层叠加永久层相乘且到期整体回退` 抓到。
4. **`ParentsConsumed` 在炼妖失败时报 false**：会把"材料没了"说成"没消耗"。补 `Success`（= 前置过 **且** 真有产出），
   与 `Ok`（只看前置）分开——否则"前置全过但概率未命中"会被界面报成成功。
5. **跟随推进把缓冲长度当路径长度**喂给 `AdvanceAlongPath`，会朝缓冲区里的垃圾格走。补 `ref int pathLength`。
   由 `有导航网格时宠物走真实路径且不穿墙` 抓到（该测试同时逐格验路径不穿墙、相邻格不跳格）。
6. **变异个体的拒绝原因被更泛化的判据抢先**：`Origin != Baby` 先命中，玩家看到"出身不符"而不是"变异不能配种"。
   调整判据顺序，具体原因优先。
7. **草稿声明了初始默契四轴而行为表没有列** —— 这是"键写了没人落库"的静默失效；
   补 `initial_loyalty/happiness/lifespan/intimacy` 四列并重生成 021/022。
8. **宠物加成叠主人属性时先 int 相乘再钳位**：极大加出会先回绕成负数，`SafeAdd` 看到"负加成"就原样返回基础值，
   于是**加成越大主人属性越差**。改成长乘先加、`ClampToInt` 饱和钳位。由 `属性相加不许溢出成负数` 抓到。

**SQL 脚本（由演练库彩排抓到，三条都是"列数对账看不见语法"的形态）**
9. **`VALUES` 行构造器留了尾逗号**：9 条 MERGE 全部在 `) AS s(...)` 处报 Msg 102，整份种子一条都没进去。
   生成器按 `",\n".join(...)` 重写，并补静态闸门（块数自洽 + 行数下限 + 缺陷形态零命中三条一起看）。
10. **`entry_id` 是 IDENTITY 却显式给号**：报 Msg 544（IDENTITY_INSERT 未开）。种子的 MERGE 用固定 `entry_id`
   做匹配键，交给自增则 `ON` 永远不命中、复跑一次多一套行且不报错。改为三张表各自 `SET IDENTITY_INSERT ON/OFF` 夹住，
    闸门数 ON/OFF 各 3 个。
11. **无 `name` 列的表 MERGE 没有分号收尾**（Msg 10713）：`;` 原本只写在 `WHEN MATCHED ... UPDATE` 行尾，
    技能池/辅助/变异三张表只有 `NOT MATCHED` 一支，于是整条语句语法不完整。改为块尾独立一行 `;`，闸门数
    "收尾分号个数 == MERGE 块数"。
    这三条的共同教训：**列数/表数/种子覆盖三道闸门全绿，SQL 仍然一条都跑不了**——生成器必须至少被真库念一遍，
    所以 021/022 的首次执行一律先过演练库。

**在线反哺链（2026-10-29，由真库对账与新测试抓到）**
12. **个体表的主人键建成了 `owner_user_id UNIQUEIDENTIFIER`**：本仓所有玩家私有表都挂 `character_id bigint`
    （背包/技能/货币/物品），而 `UserEntity.Id`、`CharacterEntity.Id` 也都是 long——这个 Guid 列从角色面板
    那条链**根本连不上**，且没有任何代码读过它（只有实体自己声明）。改列 + 重生成 021 + 对已执行的两座库
    做 `DROP/ADD COLUMN` 补丁（表还空着，不需要迁数据），并把顺手加的默认约束删回与脚本一致。
    教训与记忆 `field-type-must-be-constructible-by-its-host` 同族：**列的类型必须问"谁来 JOIN 它"**。
13. **`Contributors` 把被聚合层挡下的那只也算成"参与反哺"**：名册有 4 只附体而附体位只有 3 个时，
    快照报 `Contributors=4 / Excluded=1`，与面板"参与数"和加成来源清单对不上。改成 `ActivePets − excluded`。
    由 `附体位超出上限时多出的那只被挡下且原因可读` 抓到（我原先写的那条断言用 `|| true` 自我掩护，一并改掉）。
14. **顶替旧出战宠时边枚举查询结果边写集合**：`foreach (var other in battlingNow)` 里直接 `UpdateAsync`，
    在按活集合实现的取数器上抛"Collection was modified"。改为先 `ToList()` 物化再写。
    这条真实 EF 路径不会炸（查询已物化），但**测试夹具的第一次失败暴露了"取数→改值"两步之间没有明确边界**。

**活库判据抓出的接线陷阱（15–16）**
15. **`GetPrimaryKeyLong()` 把 GrainId 的键串按十六进制解析**：给 `"424242"` 得到 `0x424242 = 4342338`，
    于是名册按一个不存在的角色 id 查库，回 `RosterCount=0` 且 **Issues 是空串**——"查无此主"与"库里真没宠"
    在回执上完全同形。测试基建必须给 `owner.ToString("x")`，而这条永久钉在
    `Assert.Equal(TestOwnerCharacterId, grain.GetPrimaryKeyLong())` 上。
    **只有连真库才抓得到**：假上下文的 owner 恰好是 7（`0x7 == 7`），错误被巧合掩盖了。
16. **个体表的主人键口径**（§10.3 第 12 条）修完之后，真库那条才第一次跑起来——两条是同一件事的两面：
    列的类型要问"谁来 JOIN 它"，Grain 的键要问"它怎么被解析"。执行后回执：`Game` 库 66→77 张表（正好 +11）、行数
    5/5/5/15/5/5/10/5/4、运行时两张表为 0、越界号 0、倒挂区间 0、重复天生技 0；复跑 022 行数不变（幂等）。

**编辑器插件（由离线 csc 闸门 `_verify.sh` 抓到，8 个 API 猜错）**
`ScriptingAPI`(误写 ScriptAPI)、`System.Text.Encodings.Web` 在脚本域里不存在（故插件侧 Contract 刻意不设 Encoder）、
`FlaxEditor.Windows.EditorWindow` 缺 using、控件绘制重写的是 `Draw()` 而不是 `OnDraw(Rectangle)`、
`SceneEditing.Selection` 本身就是 `List<SceneGraphNode>`（没有 `.Nodes`）、
`Actor` 只在 `SceneGraphNode as ActorNode` 之后才有、`out` 不能接属性（须先落局部变量）。

**客户端（同一条离线门）**
`Script` 的帧回调是 `OnUpdate()` 而不是 `Update()`。

**我自己的尺子错的地方（红了但代码是对的，记下来防再犯）**
`species_id×100000` 的期望值按 ×10⁶ 写；婚配等级豁免与等级差判据的先后顺序期望写反；
`Assert.NotEqual` 误伤兜底文案；数组字面量正则只认带 `new` 的写法（本仓库多为简写，导致 7 条契约断言
"找不到锚点"而不是发现不一致）；`-?\d+` 把 `7_500` 抠成 `7` 和 `500` 两个值；
内丹分段探针用了"边界减一"去要求源码里出现该字面量；测试方法名里带空格与 `base` 作局部名（两条编译期红）。

### 10.4 明确未完成
- **021/022 已执行**（2026-10-29 用户点头；先演练库彩排后进 `Game` 库，回执见 §10.3 第 9–11 条）。
  仍**没有** EF 迁移类、也没有 `__EFMigrationsHistory` 记账——021 是手写等价 DDL。配对的 EF 迁移必须在
  NPC 编辑器的迁移之后生成：现在跑 `dotnet ef migrations add` 会把别人尚未迁移的 NPC 十二张表一起卷进宠物这条迁移。
- **宠物个体已进 Orleans 读侧，但没进 DS/战斗结算**（§8 D8 由用户在 2026-10-29 反转，接通到角色面板为止）：
  `PetRosterGrain`（角色键）+ `IPetBonusReader` + `CharacterAttributeRefreshResponse` 追加字段 + 面板"宠物加成"一行已闭环（§10.2b）。
  还缺三段，各自都是独立一条链：
  ① **写入口没人调**：`SetDeployStateAsync`（出战/附体/收回）服务端已实现并有判据，但没有客户端按钮与协议消息；
     捕捉落库（`PetCaptureCore` 已能算概率与发号）同样没有写通路——所以个体表现在仍是 0 行，
     面板那栏的如实状态是"无宠"。
  ② **加成没进权威画像**：宠物量走 `CharacterAttributeRefresh` 的**归因列**，`GetCombatProfileAsync` 的
     `BaseAttack/BaseDefense` 与受击裁决都不含宠物。混进去需要给 `CombatProfile` 加一个来源类别，
     表现差异是"面板写着 +1060 攻而打怪没变"——这条刻意没做，因为宁可少给buff也不要两套账。
  ③ **客户端宠物实体与跟随表现**：`PetFollowComponent` 仍无挂载点（要等宠物实体走 DS spawn/despawn，
     注意 Despawn 全量广播 + 快照 Transform 的 Y/Z 交换，见 §9）。
- 宠物美术资产与 prefab 路径：目录 5 行的 `PrefabPath` 全为 NULL，`export-visuals` 会如实报 problem。
- PIE 目视验证（PIE 脚本类型污染问题已知，见记忆 `flax-pie-unregisters-metahuman-scripts`）：
  本轮"宠物加成"那一行只过了编译门，**没有像素证据**。



