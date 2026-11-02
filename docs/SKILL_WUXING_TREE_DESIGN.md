# 五行技能树与技能书设计方案（天地玄黄白 × 三境 + 相生演化）

状态：**已确认（D1=75 式独立、D2=演化封存源技、D3=接入倍率并把基座降到 0.25、D4=保留五行强制兼修），进入落地**
作者：Qoder · 生成时间：2026-10-03
适用范围：`HundunWorld/Source/Game`（客户端）+ `Horizon.*`（服务端权威）

---

## 0. 一句话结论

以 **五行（金木水火土）× 五阶（白→黄→玄→地→天）× 三境（初→中→极）= 75 式基础技能**，
再加 **相生演化「道阶」5 系 × 3 境 = 15 式**（超越天阶），以及终式 **混沌一炁 1 式**，
合计 **91 式技能 + 91 本技能书**；战斗侧用 **循环节（相生连携，最多 5 层）** 与 **破势/夺元（相克）** 两条规则，
把"五行循环往复、不断对决"变成可判定的服务端状态，而不是文案口号。

**为什么必须是 75 式而不是 25 式**：见 §9 决策点 D1，这是本方案唯一需要你先拍板的结构性选择。

---

## 1. 现状盘点（证据，勿凭记忆改）

| 事实 | 位置 |
|---|---|
| 客户端技能配置 26 条硬编码，`skills.json` 目前**不存在** | `HundunWorld/Source/Game/Combat/Skills/SkillDatabase.cs:192,215,439-813` |
| `SkillConfig` 无"阶/境"字段，只有 `RequiredLevel/MaxLevel/DamagePerLevel` | 同上 `:61-65,106` |
| 数据驱动施放已可用：无 `SkillBase` 实例也能发 `SkillCastMessage` | `UI/Ink/ActionBar/ActionBarService.cs:241-260`、`Combat/CombatReporter.cs:76-90` |
| 服务端学习=Orleans 状态字典，不读库；`LearnSkillAsync` 写死 SkillInfo | `Horizon.Orleans.Grains/SkillGrain.cs:246-301`（硬编码 `:277-286`） |
| 服务端技能模板表已具备 `learn_level_require / learn_realm_require / pre_skill_id / pre_skill_level / comprehension_require / learn_cost / can_advance / advance_skill_id / combo_skill_ids / wuxing_element / skill_attack / skill_penetration` | `Horizon.Model/GameModel/SkillTemplateEntity.cs:78-204,251-265` |
| 已学技能表已具备 `skill_level / proficiency / max_proficiency / skill_realm / comprehension_rate / learn_source(1=秘籍学习) / is_equipped / slot_index`，**但没有任何 Grain 读写它** | `Horizon.Model/GameModel/CharacterSkillEntity.cs:42-91` |
| 技能书表 `Game_HunduShijie_SkillBook` 已建（`skill_id/quality/book_type/level_require/comprehension_require/learn_duration/learn_success_rate/max_skill_level/use_limit`），**零代码读取、无种子** | `Horizon.Model/GameModel/SkillBookEntity.cs:12-113`、`Horizon.Entities/GameEntityContext.cs:84,180` |
| 物品类型已有 `6=秘籍`，但没有 `TeachesSkillId` 列 | `Horizon.Model/GameModel/ItemTemplateEntity.cs:43-44` |
| 客户端 `UseItemRequest` 的服务端处理是**空壳**（恒返回成功，从不落库） | `Horizon.Game.Core/Handlers/CharacterHandler.cs:768-787` |
| 五行生克乘算已服务端权威：克 ×1.25、被克 ×0.8、相生 synergy、共鸣档位 | `Horizon.Orleans.Grains/CombatCalculator.cs:31-57,210-240,390-437` |
| 生/克关系定义 | `HundunWorld/Source/Game/Character/Attributes/WuxingElement.cs:9-28,38,54,74,95` |
| 服务端结算**没有吃 `DamageMultiplier`**（base = AttackPower × 0.6） | `Horizon.Orleans.Grains/CombatGrain.cs:263` |
| 角色阶段：武侠 Lv1-50 / 仙侠 Lv51-150 / 玄幻 Lv151-300 | `Character/Attributes/CharacterAttributesComponent.cs:9-19` |
| 元素枚举序：`None=0, Metal=1, Wood=2, Water=3, Fire=4, Earth=5`；**装备表 `element` 用的是另一套序**（0金1木2水3火4土5无） | `WuxingElement.cs:9-28`、`ItemTemplateEntity.cs:71` + `SkillTemplateEntity.cs:243-246` |

结论：**架子基本齐全（表列都在），缺的是数据、读写链路与"阶/境/演化/循环"的规则定义**。落地成本主要在 SQL 种子 + SkillGrain/UseItem 接线 + 循环状态，而不是重构。

---

## 2. 阶梯与境界的定义

### 2.1 阶（Grade）

| 阶 | 序 | 定位 | 等级门槛（起） | 角色阶段 | 对应 `learn_realm_require` |
|---|---|---|---|---|---|
| 白阶 | 1 | 入门票法，三式打基础 | Lv1 | 武侠 | 1 |
| 黄阶 | 2 | 门派主力过渡 | Lv31 | 武侠→仙侠 | 2 |
| 玄阶 | 3 | 一流高手 | Lv76 | 仙侠 | 3 |
| 地阶 | 4 | 一派宗师 | Lv131 | 仙侠→玄幻 | 4 |
| 天阶 | 5 | 当世绝顶 | Lv201 | 玄幻 | 5 |
| **道阶** | 6 | **超越天阶**，只能由相生演化得到 | Lv300 + 前置 | 玄幻圆满 | 6 |

`learn_realm_require` 直接复用为阶序 1..6（项目当前没有独立"境界枚举"，`CharacterStage` 只有武侠/仙侠/玄幻三段，粒度不够）。这样**不加新列也能表达"境界门槛"**，加列只用于反向查询效率（见 §8 A1）。

### 2.2 境（每阶内 3 式）

同一个「技系」内的三个境界，各自是一式**独立可学、独立可上技能栏、独立可施放**的招式：

| 境 | 序 | 通用定位 | 冷却档 | 射程 | 作用形式 |
|---|---|---|---|---|---|
| 初境（一式） | 1 | 主输出 / 短冷却 | 见 §3.3 | 8m | Single |
| 中境（二式） | 2 | 控制 / 群体 / 位移 | 一式 ×2.5 | 15m | Circle / Sector |
| 极境（三式） | 3 | 绝学 / 增益 / 领域 | 一式 ×7.5 | 25m（自身增益=0） | Circle / Self |

进阶链：`一式 → 二式 → 三式` 由 `pre_skill_id` + `pre_skill_level` 表达；三式圆满（熟练度满 + `skill_realm=3`）是本阶"圆满"，也是上一阶跨入下一阶的门槛。

> **⚠️ 这一行是 D1 之前的旧稿，已作废**（留着会让后来人去补一条已经废掉的轴）：D1 定的是"三境各为一式、独立可学可上栏可施放"，所以"熟练度满 + `skill_realm=3`"不再是圆满的定义——实现里的圆满 = **`Level == MaxLevel`**（§6.2，判在 `SkillGrain.CheckLearnGates(requireEvolveSourcesMaxed: true)`，客户端同口径见 §14.7 G19/G26）。**熟练度全仓既无存储也无写入方**：`GrainStateModels` 里那个 `Proficiency` 是**炼丹**熟练度（`WuxingAlchemyGrain` 在用），不是技能熟练度；`SkillProficiencyQuery/Update` 三个协议消息倒还在，两个网关处理器（`SkillHandler` 与不可达的 `CharacterHandler` 副本）恒回空字典。要把熟练度重新立起来，得先回答"它和等级轴谁作准"——见任务 #34。

---

## 3. 数值阶梯（全系统一，五行只改"特性向量"）

数值**与五行无关**，只与 (阶, 境) 有关；五行差异体现在 §4 的特性系数与独有效果。这样 91 行的表可核对、可回归测试，不会散成 91 个拍脑袋数字。

### 3.1 公式

```
k = 3 × (阶序 - 1) + (境序 - 1)          # 0..14 基础，15..17 道阶，18 混沌
倍率 DamageMultiplier = round(1.10 × 1.09^k, 2)
耗气 EnergyCost       = 30 + 18k（道阶 300/330/360，混沌 500）
```

### 3.2 倍率与耗气总表

| k | 阶·境 | 倍率 | 耗气 | 等级门槛 | 熟练上限 | 领悟门槛(五行亲和) |

> 上表的**"熟练上限"列是旧稿遗留**：技能表里没有这一列（`Game_HunduShijie_SkillTemplate` 无熟练度字段），91 式的种子也没灌它，运行时零读者。其余五列（倍率/耗气/等级门槛/领悟门槛 + 境界拆式）都是真的：倍率见 D3、耗气由 `CombatGrain.ResolveEnergyCost` 按公式服务端复算（不信客户端上报）、等级与亲和门槛见 §14 与 `WuxingDynamicGates`。
|---|---|---|---|---|---|---|
| 0 | 白·初 | 1.10 | 30 | 1 | 100 | 4 |
| 1 | 白·中 | 1.20 | 48 | 11 | 100 | 6 |
| 2 | 白·极 | 1.31 | 66 | 21 | 100 | 8 |
| 3 | 黄·初 | 1.43 | 84 | 31 | 200 | 12 |
| 4 | 黄·中 | 1.55 | 102 | 46 | 200 | 15 |
| 5 | 黄·极 | 1.69 | 120 | 61 | 200 | 18 |
| 6 | 玄·初 | 1.85 | 138 | 76 | 300 | 24 |
| 7 | 玄·中 | 2.01 | 156 | 96 | 300 | 28 |
| 8 | 玄·极 | 2.19 | 174 | 116 | 300 | 32 |
| 9 | 地·初 | 2.39 | 192 | 131 | 400 | 36 |
| 10 | 地·中 | 2.61 | 210 | 156 | 400 | 42 |
| 11 | 地·极 | 2.84 | 228 | 181 | 400 | 48 |
| 12 | 天·初 | 3.10 | 246 | 201 | 500 | 52 |
| 13 | 天·中 | 3.37 | 264 | 236 | 500 | 60 |
| 14 | 天·极 | 3.68 | 282 | 271 | 500 | 68 |
| 15 | 道·初 | 4.01 | 300 | 300 | 800 | 75 |
| 16 | 道·中 | 4.37 | 330 | 300 | 800 | 85 |
| 17 | 道·极 | 4.76 | 360 | 300 | 800 | 95 |
| 18 | 混沌 | 5.19 | 500 | 300 | 1000 | 100 |

领悟门槛 = 学习时要求的**该行亲和度**（数据源 `CharacterAttributesComponent.{Metal,Wood,Water,Fire,Earth}Affinity`，客户端雷达图已在用），上限 100 → 道阶最后一境 95 留有余量。

### 3.3 冷却 / 射程 / 作用形式

| 阶 | 一式 CD | 二式 CD | 三式 CD | 一二三射程 | 三式 AOE |
|---|---|---|---|---|---|
| 白 | 4s | 10s | 28s | 8 / 15 / 25 | 圆 6m 或 扇 90°×8m |
| 黄 | 6s | 14s | 40s | 8 / 15 / 25 | 圆 8m |
| 玄 | 8s | 18s | 55s | 10 / 18 / 28 | 圆 10m |
| 地 | 11s | 24s | 75s | 10 / 18 / 28 | 圆 12m |
| 天 | 14s | 30s | 100s | 12 / 20 / 30 | 圆 14m |
| 道 | 18s | 38s | 130s | 14 / 24 / 35 | 圆 16m |
| 混沌 | — | — | 300s | 40 | 圆 20m |

### 3.4 服务端接入点（数值何时生效）

**必须先修的一处不一致**：`CombatGrain.cs:263` 目前 `baseDamage = AttackPower × 0.6`，完全不吃 `DamageMultiplier`。本方案的倍率要生效，结算顺序定为：

```
finalDamage = (AttackPower × 0.6 + SkillAttack)     // 现有基座与行保留
            × DamageMultiplier                       // 新增：§3.2 倍率
            × WuxingMultiplier(攻方元素, 守方护体)     // 现有 1.25 / 0.8 / 1.0
            × LoopMultiplier(循环节数)                // 新增：§5
            × (1 + 共鸣加成)                          // 现有 CombatCalculator:390-437
```

倍率接入会让最高伤害从 1.0× 变成 4.76×（道阶），**这是数值层面的大改**，见 §9 决策点 D3。

---

## 4. 五行特性（同数值，不同性格）

| 行 | 关键词 | 附加机制（一式→三式递进） | 服务端挂载点 |
|---|---|---|---|
| 金 Metal | 锋锐、破甲 | 一式无视 10% 防御 → 二式 20% + 直线穿透 3 目标 → 三式 30% 并给自身"兵刃金意"（后续 3 式破甲不叠加） | `SkillPenetration`（`SkillTemplateEntity:265`，`CombatGrain:276-277` 取 max） |
| 木 Wood | 生发、缠绕 | 一式 DOT（每 2s 0.25×）→ 二式束缚 1.5s → 三式领域：友方持续回复 + 敌方缠绕 | `skill_effects` JSON（`:183`）+ `SkillEffectConfig` |
| 水 Water | 寒凝、净化 | 一式减速 20% 3s → 二式冰冻 1.2s（Circle）→ 三式驱散敌方增益 + 我方治疗 | 同上 |
| 火 Fire | 爆发、灼烧 | 一式灼烧 4s → 二式溅射（主目标 100%，余 40%）→ 三式业火：DOT 可被下一式"引爆"（伤害 ×1.5 并消耗 DOT） | 同上 |
| 土 Earth | 镇域、护盾 | 一式击退 → 二式地陷（矩形定身 1s）→ 三式护盾（吸收 0.8× 伤害）+ 减伤 15% 6s | `SkillDefense` 行 + `extra` 效果 |

**特性系数的落地载体**：`SkillTemplateEntity.skill_effects` / `level_effects`（已有列，140-146,183）以 JSON 描述；不新增列。

---

## 5. "循环往复不断决"的可判定规则

这是方案的核心机制，全部落在服务端，客户端只显示。

### 5.1 循环节（相生连携，Loop Stack）

- 施放任一本系技能命中后，服务端记录 `lastElement` 与 `loopAt`（时间戳），窗口 **3.0s**。
- **起链第一式自己占一层**。节数数的是"本轮周天里已经走过几行"，起手那一行就是其中一行；
  只在顺生时加层的话，按相生序打完五行只到 4 层，本节末尾的设计意图与 D4 都永远凑不成。
- 下一式若属于 `WuxingRules.GetGenerateElement(lastElement)`（金生水、水生木、木生火、火生土、土生金），判为**顺生**：
  - 该次伤害 ×1.15（复用现有相生 bonus，`WuxingElement.cs:95`）
  - 耗气 −20%
  - **循环节 +1**（上限 5）
- 循环节每层：全系伤害 +3%（与顺生 ×1.15 乘算，不替换）。
- 第 5 层触发 **周天圆满**：本次伤害额外 ×1.25、全部已学技能冷却立即减 20%、节数归零、`completionCount++`（写入 Grain 状态，是 §6 演化的计数门槛）。
- 判定为"逆生"（子行技能打在母行技能之后）：不加成、不清零，只刷新时间戳。
- 判定为"同属"（连续同五行）：不清零、不加成 → 鼓励兼修而非单系无脑。
- 只有 **3s 内断链** 或 **被夺** 才会丢节数。

> 设计意图：要凑满 5 层周天，必须按相生序把五行各打一式，于是"循环往复"变成玩家行为的必然结果，而不是奖励文案。也直接绑定了本方案的技能书经济（单系玩家吃不到周天）。

### 5.2 破势与夺元（相克，"决"）

- 攻方元素克守方当前护体元素（现有 1.25，`CombatCalculator:31-57`）→ 追加 **破势**：打断守方吟唱、守方循环节 −1、攻方循环节 +1（**夺**）。
- 守方被克（×0.8）时其下一次施放耗气 +10%，持续 3s。
- 护体元素 = 玩家 §5.3 设定的"镇岳行"，默认取当前亲和最高的一行。

### 5.3 镇岳（主动选眼）

玩家可从已学技能中选一个"镇岳行"（UI 上占一格），决定：
- 谁克我（被克关系判定用），以及 §5.2 的受击系数；
- 循环节清零后从哪一行重新起链。

每 10s 可切换一次，切换不耗气。**这是"不断决"的对决轴**：PVP 里换镇岳=换攻防假设。

### 5.4 状态存储

`SkillGrain` 的 `IPersistentState<SkillState>` 增字段：`LoopStack(0..5)`、`LastElement`、`LoopExpireAt`、`CompletionCount`、`GuardianElement`、`GuardianSwapAt`。
新增字段必须带 `[MemoryPackable] + [MemoryPackOrder]`（项目既有约束），且旧存档默认值 0/None 安全。

---

## 6. 演化（超越天阶）

### 6.1 道阶 = 相生对演化，5 系 × 3 境 = 15 式

| 演化系 | 相生对（母→子） | 系名 | 独有机制 |
|---|---|---|---|
| P1 | 金→水 | 流金化液 | 穿透与减速**可叠加**：每层循环节使穿透 +1 目标、减速 +6% |
| P2 | 水→木 | 润木成林 | 治疗溢出转 DOT：过量治疗变成敌方可缠绕的"湿气" |
| P3 | 木→火 | 燃薪御火 | DOT 引爆范围化：一次引爆对周围 8m 传染全部 DOT |
| P4 | 火→土 | 炼土成陶 | 护盾带灼烧反伤：受击者按护盾余量被灼烧 |
| P5 | 土→金 | 藏锋于山 | 格挡反击：格挡成功后自动射出一记金意（不计冷却） |

### 6.2 演化即"换代"：被演化的源技封存（已确认口径）

**演化不是白拿一格，而是两式融合**（用户确认：技能演化后，被演化的源技不再使用/不显示/不可操作）：

| 目标式 | 融合消耗的源技 | 额外前置 | 亲和 | 周天圆满次数 | 书 |
|---|---|---|---|---|---|
| 道·初（70x01） | 母·天初 + 子·天初 | 两式各三境圆满 | ≥75 | ≥12 | 道阶玉简，研读成功率 35% |
| 道·中（70x02） | 母·天中 + 子·天中 | 道·初 圆满 | ≥85 | ≥20 | 同上 |
| 道·极（70x03） | 母·天极 + 子·天极 | 道·中 圆满 | ≥95 | ≥28 | 同上 |

走完整对相生 = **投入 6 式（母行天阶三式 + 子行天阶三式），产出 3 式**。

**封存语义**（`is_retired=1` + `retired_into_skill_id`）：
- 服务端拒绝施放已封存式（`SkillGrain.CastSkillAsync` 前置校验）；
- 客户端技能树不再显示这些式，技能栏选择器不再列出，详情标注"已融于〈道阶式名〉"；
- **技能栏引用槽自动清空**：`ActionBarLayout` 收到 retire 通知后清掉对应槽位并提示重装配；
- 冷却、统计、PVP 结算一律排除。

**由此产生一条硬约束**：同一行既是某对的母、又是另一对的子（金=P1 之母、P5 之子），所以**相生环上同一时刻只能走一段弧**。要集齐多对必须"散功重练"——用《天阶传承玉简》重新研读解封（熟练度按 50% 折还，受 `use_limit` 限制）。这不是妥协方案，而是"循环往复"的玩法本体：登天阶 → 化道阶 → 散功 → 再登天阶。

### 6.3 混沌一炁（90001，终极）

- 条件：**任意 3 对道阶系三境圆满（9 式）** + 周天圆满 `CompletionCount ≥ 40` + 持《混沌一炁》（世界 BOSS 唯一掉落，`use_limit=1`）。
  原稿写"15 式全满"，在 §6.2 的互斥规则下不可达，已按 3 对修订。
- 效果：单次结算按当前循环节数在五行乘算中取**最高**组合（不求和，避免爆炸）；施放后固化 1 层循环节；CD 300s。
- 意义：闭合"混沌→分五行→相生换代→归一→再启混沌"的叙事环，也是"最高超越天阶"的落点。

### 6.4 散功（逆向）

- 道阶式可散功：**删除该道阶式并解封它融合的 2 个源技**（`is_retired=0`，熟练度按封存时 50% 折还，等级保留）。
- 散功不回收玉简（一次性消耗）；`CompletionCount` 不清零。
- 混沌式散功只删混沌式本身，已成的道阶式不受影响（不做连锁特例）。

**封存是派生态，不落库**（落地时改的口径）：某式是否封存 = "有没有已学的演化式把它列进 consume"。
好处是散功（删掉演化式）自动解封，不会出现"忘改标记导致格子永久灰着"的僵尸态，
技能栏/技能树/施放三处判定也天然一致。迁移里的 `is_retired / retired_into_skill_id`
两列因此只剩"给 UI 直接标注去向"的可选用途，不参与判定。
演化关系仍用现有列：`can_advance=true`、`advance_skill_id=<道阶式 id>`、`pre_skill_id` 保留母技。

### 6.5 相克的位置（D2 定稿：不做独立逆演支系）

1. 战斗侧：×1.25 / ×0.8 与 §5.2 破势夺元；
2. 演化侧：**替代准入**——若母行天阶某式已因走另一段弧而被封存，玩家可用"克方那一段"的同阶同境圆满 + 周天额外 +4 层作为替代路径。
   不新增技能，总数保持 91 式。

### 6.6 演化判定汇总（服务端一条请求的通过条件）

① 待融合的 2 个源技存在、`skill_realm=3`、`proficiency=max_proficiency`、未封存；
② 道阶前式已圆满（道·初免）；
③ 该行亲和达标；④ `CompletionCount` 达门槛；
⑤ 持有对应玉简且研读成功；⑥ 目标道阶式尚未习得。
任一不满足返回结构化原因，客户端 toast 明示缺哪一条。

**书籍消耗规则（落地口径）**：掷签失败**吃掉一本**（否则同一本可反复重读刷概率）；
门槛未达（等级/亲和/前置）**不吃书**（玩家不该因为还没长级被罚款）。

---

## 7. 技能书体系（91 本）

### 7.1 与阶位映射（复用 `SkillBookEntity` 现有列，不加列）

| 阶 | 书品质 `quality` | `book_type` | `learn_success_rate` | `learn_duration`(min) | `max_skill_level` | `use_limit` |
|---|---|---|---|---|---|---|
| 白 | 0 普通 | 0 完整秘籍 | 100% | 0 | 3 | 1 |
| 黄 | 1 优秀 | 0/1 完整或残卷×3 | 95% | 1 | 5 | 1 |
| 玄 | 2 精良 | 1 残卷×3 → 合成完整 | 85% | 3 | 7 | 2 |
| 地 | 3 史诗 | 1 残卷×5 | 70% | 8 | 9 | 2 |
| 天 | 4 传说 | 3 传承玉简 | 55% | 20 | 10 | 3 |
| 道 | 5 神话 | 3 传承玉简（演化产物，不可拾取） | 35% | 60 | 10 | 1 |
| 混沌 | 5 神话 | 3（世界 BOSS 唯一） | 25% | 120 | 10 | 1 |

`book_type=2 心得` 专门用于**境突破**（不习得新技，只把已学技 `skill_realm` +1 与熟练度上限解锁），这是把"三境"和"三书"分开的关键：境靠心得+实战，技靠秘籍。

> **心得书这条也没实现，而且按 D1 已经没有可破的境**：`SkillBookEntity.BookType` 全仓**零读者**（只有实体定义那一行），秘籍侧只用到"这本书对应哪一式 + 研读成功率"。三境既然各是一式（D1），"用心得把已学式的境往上顶"就没有对象了——要恢复这条玩法，等价于要求先撤销 D1，属产品决策，见任务 #34。

### 7.2 编号方案

```
技能 id：
  基础 75 式   id = 行(1金..5土) × 10000 + 阶(1白..5天) × 100 + 境(1..3)   → 10101 … 50503
  道阶 15 式   id = 70000 + 相生对P(1..5) × 100 + 境(1..3)                 → 70101 … 70503
  混沌 1 式    id = 90001
  身法（保留现状） 6001/6002 → 60100 段不动
技能书 item id： 800000 + 技能 id  → 810101 … 870503, 890001
```
`800000+` 与现有物品 id 段（10001+ 装备、50001+ 丹药、60001+ 宝石）无冲突；`7xxxxx` 技能段与现有 1xxx-6xxx 无冲突。

注意 **元素序号双轨**（技能用 `WuxingElement` 1..5，装备 `element` 用 0金1木2水3火4土5无）：技能书作为物品，其 `element` 列必须按装备那套写，落地时在种子脚本里做一次显式转换并加注释，否则金行书会被解析成木行。

### 7.3 获取途径（保证"循环"要求玩家兼修多系）

| 阶 | 主源 |
|---|---|
| 白·三式 | 门派入门传授（`learn_source=0`，免书） |
| 黄 | 野外精英怪掉残卷 + 坊市购完整 |
| 玄 | 秘境副本 3 号位必掉 1 残卷 |
| 地 | 世界事件 + 帮会贡献兑换 |
| 天 | 高难副本尾王 + 传承玉简（需前置圆满才能研读） |
| 道 | **只能演化**（`learn_source=4 顿悟`），不可拾取 |
| 混沌 | 世界 BOSS 唯一掉落（全服限次） |

---

## 8. 全套技能清单（91 式）

约定：每系三式记作 `初/中/极`；`类型` 取客户端 `SkillConfig.Type` 现有枚举（ActiveAttack/Control/Dash/Support/Ultimate）；`作用` 取 `RangeType`（Single/Circle/Sector/Rectangle/Line）。
数值一律查 §3.2/§3.3，不再逐行重复。

### 8.1 金（Metal，10101–10503）

| id | 阶·境 | 技系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|---|
| 10101 | 白·初 | 青锋三式 | 启锋 | ActiveAttack | Single | 破甲 10% |
| 10102 | 白·中 | 青锋三式 | 断缕 | Control | Line | 断对方移动加速 2s |
| 10103 | 白·极 | 青锋三式 | 裂缺 | ActiveAttack | Sector | 命中后自身下式破甲 +5% |
| 10201 | 黄·初 | 裂金三式 | 崩棱 | ActiveAttack | Single | 破甲 15%，对护盾目标 ×1.2 |
| 10202 | 黄·中 | 裂金三式 | 削玉 | ActiveAttack | Line | 穿透 2 目标 |
| 10203 | 黄·极 | 裂金三式 | 贯岳 | Ultimate | Single | 无视 20% 防御并击碎 1 层增益 |
| 10301 | 玄·初 | 太白经天 | 流霜 | ActiveAttack | Single | 破甲 20% |
| 10302 | 玄·中 | 太白经天 | 割暮 | ActiveAttack | Line | 穿透 3 目标 |
| 10303 | 玄·极 | 太白经天 | 倾河 | Ultimate | Circle | 剑阵，持续 4s 每 0.5s 结算 |
| 10401 | 地·初 | 庚金绝脉 | 断岳 | ActiveAttack | Single | 破甲 25%，附带 1 层"锋意" |
| 10402 | 地·中 | 庚金绝脉 | 铁围 | Control | Circle | 兵牢定身 1.2s |
| 10403 | 地·极 | 庚金绝脉 | 万剑朝宗 | Ultimate | Circle | 3 层锋意引爆，每层 +0.4× |
| 10501 | 天·初 | 金德弥天 | 敛锋 | ActiveAttack | Single | 破甲 30% |
| 10502 | 天·中 | 金德弥天 | 铸兵 | Support | Self | 兵刃金意：后续 3 式破甲不叠加、耗气 −15% |
| 10503 | 天·极 | 金德弥天 | 金方辟易 | Ultimate | Circle | 领域：域内敌方护甲 −30%、我方顺生判定 +0.5s 窗口 |

### 8.2 木（Wood，20101–20503）

| id | 阶·境 | 技系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|---|
| 20101 | 白·初 | 青藤三式 | 缠枝 | ActiveAttack | Single | DOT 0.25×/2s，4s |
| 20102 | 白·中 | 青藤三式 | 抽芽 | Control | Single | 减速 15% 2s |
| 20103 | 白·极 | 青藤三式 | 落花 | Support | Self | 回复 6% 气血 |
| 20201 | 黄·初 | 绿绮长林 | 蔓引 | ActiveAttack | Sector | DOT 0.30×，蔓延至 1 名附近敌人 |
| 20202 | 黄·中 | 绿绮长林 | 荆棘 | Control | Circle | 束缚 1.5s + 反伤 |
| 20203 | 黄·极 | 绿绮长林 | 枯荣 | Support | Circle | 友方回复、敌方 DOT 双生效 |
| 20301 | 玄·初 | 春木生息 | 回春 | Support | Single | 治疗 + HOT |
| 20302 | 玄·中 | 春木生息 | 繁荫 | Control | Circle | 群体缠绕 2s |
| 20303 | 玄·极 | 春木生息 | 万叶 | Ultimate | Circle | 叶域：域内 DOT 结算频率 ×1.5 |
| 20401 | 地·初 | 苍梧生生 | 盘根 | ActiveAttack | Single | 定身 1s，DOT 0.5× |
| 20402 | 地·中 | 苍梧生生 | 腾莽 | Control | Rectangle | 突进缠绕 |
| 20403 | 地·极 | 苍梧生生 | 森罗 | Ultimate | Circle | 领域 8s，友方受疗 +20% |
| 20501 | 天·初 | 青帝临凡 | 布泽 | Support | Circle | 群体回复 + 驱散流血 |
| 20502 | 天·中 | 青帝临凡 | 荣枯 | ActiveAttack | Circle | 敌 DOT / 我 HOT 同半径 |
| 20503 | 天·极 | 青帝临凡 | 建木通天 | Ultimate | Self | 9s 内每次相生判定额外 +1 层 DOT |

### 8.3 水（Water，30101–30503）

| id | 阶·境 | 技系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|---|
| 30101 | 白·初 | 玄冰三式 | 凝霜 | ActiveAttack | Single | 减速 20% 3s |
| 30102 | 白·中 | 玄冰三式 | 激浪 | ActiveAttack | Line | 击退 2m |
| 30103 | 白·极 | 玄冰三式 | 镜面 | Support | Self | 反射下一次控制 |
| 30201 | 黄·初 | 寒潭印月 | 刺骨 | ActiveAttack | Single | 减速 30%，耗气 −10% |
| 30202 | 黄·中 | 寒潭印月 | 凝涛 | Control | Circle | 冰冻 1.2s |
| 30203 | 黄·极 | 寒潭印月 | 沉璧 | Support | Circle | 净化 2 个减益 |
| 30301 | 玄·初 | 沧浪之水 | 濯缨 | Support | Single | 解除控制并回复 |
| 30302 | 玄·中 | 沧浪之水 | 回澜 | Control | Circle | 拉拽聚怪 1.5s |
| 30303 | 玄·极 | 沧浪之水 | 雾锁 | Control | Circle | 域内敌方命中 −20% |
| 30401 | 地·初 | 玄武潜渊 | 冻浦 | ActiveAttack | Circle | 冰冻 1.5s（群体） |
| 30402 | 地·中 | 玄武潜渊 | 倒河 | ActiveAttack | Line | 穿透并减速叠层 |
| 30403 | 地·极 | 玄武潜渊 | 深渊 | Ultimate | Self | 潜渊：3s 免疫控制，出水时结算 |
| 30501 | 天·初 | 天河倒泻 | 冰壶 | Control | Circle | 冰封 2s + 破霜（受击 +15%） |
| 30502 | 天·中 | 天河倒泻 | 悬瀑 | Ultimate | Rectangle | 大范围冲击 + 击退 |
| 30503 | 天·极 | 天河倒泻 | 溟海归墟 | Ultimate | Circle | 域内每 1s 抽 1 层敌方增益回我方 |

### 8.4 火（Fire，40101–40503）

| id | 阶·境 | 技系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|---|
| 40101 | 白·初 | 星火三式 | 燃薪 | ActiveAttack | Single | 灼烧 0.3×/s，4s |
| 40102 | 白·中 | 星火三式 | 飞烬 | ActiveAttack | Circle | 溅射 40% |
| 40103 | 白·极 | 星火三式 | 燎原 | ActiveAttack | Sector | 点燃 2 名邻敌 DOT |
| 40201 | 黄·初 | 赤霄焚野 | 贯日 | ActiveAttack | Line | 贯穿灼烧 |
| 40202 | 黄·中 | 赤霄焚野 | 流火 | ActiveAttack | Circle | 落地区域 3s 持续灼烧 |
| 40203 | 黄·极 | 赤霄焚野 | 焚营 | Ultimate | Circle | 对 DOT 目标 ×1.5 |
| 40301 | 玄·初 | 离火真形 | 熔兵 | ActiveAttack | Single | 破甲 + 缴械（武器增益失效）3s |
| 40302 | 玄·中 | 离火真形 | 业锁 | Control | Circle | 定身 + DOT，被克目标额外 1s |
| 40303 | 玄·极 | 离火真形 | 炎龙 | Ultimate | Line | 龙形穿透 5 目标 |
| 40401 | 地·初 | 丹雀衔烛 | 爆烈 | ActiveAttack | Circle | 引爆自身 DOT 立即结算余量 |
| 40402 | 地·中 | 丹雀衔烛 | 焚身 | Support | Self | 耗 10% 气血换 3s 内火伤 +25% |
| 40403 | 地·极 | 丹雀衔烛 | 赤城 | Ultimate | Circle | 火域 6s，DOT 不可被驱散 |
| 40501 | 天·初 | 焚天灭地 | 劫火 | Ultimate | Circle | 全体灼烧 + 减疗 30% |
| 40502 | 天·中 | 焚天灭地 | 日陨 | Ultimate | Circle | 单点巨伤 + 落地区域 |
| 40503 | 天·极 | 焚天灭地 | 凤凰涅槃 | Support | Self | 致死免死并回复 30%，重灼周围 |

### 8.5 土（Earth，50101–50503）

| id | 阶·境 | 技系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|---|
| 50101 | 白·初 | 磐石三式 | 垒土 | ActiveAttack | Single | 击退 1.5m |
| 50102 | 白·中 | 磐石三式 | 飞砂 | Control | Circle | 命中 −15% 3s |
| 50103 | 白·极 | 磐石三式 | 地陷 | Control | Circle | 减速 25% 2s |
| 50201 | 黄·初 | 厚土重帷 | 岩甲 | Support | Self | 护盾 0.5× 吸收 |
| 50202 | 黄·中 | 厚土重帷 | 石刺 | ActiveAttack | Line | 突进 + 击飞 |
| 50203 | 黄·极 | 厚土重帷 | 尘障 | Support | Circle | 友方减伤 10% 5s |
| 50301 | 玄·初 | 坤岳镇形 | 踏脉 | ActiveAttack | Single | 眩晕 1s |
| 50302 | 玄·中 | 坤岳镇形 | 地刺 | Control | Rectangle | 定身 1s + DOT |
| 50303 | 玄·极 | 坤岳镇形 | 山门 | Support | Self | 护盾 0.8× + 嘲讽 3s |
| 50401 | 地·初 | 五岳真形 | 压顶 | ActiveAttack | Circle | 落石群控 1.2s |
| 50402 | 地·中 | 五岳真形 | 地动 | Control | Circle | 区域失衡，耗气 +30% 5s |
| 50403 | 地·极 | 五岳真形 | 山呼 | Ultimate | Circle | 队友获得 1 层护盾 |
| 50501 | 天·初 | 后土载物 | 安疆 | Support | Self | 镇域护盾，破势免疫 1 次 |
| 50502 | 天·中 | 后土载物 | 镇岳 | Control | Circle | 大范围定身 1.8s |
| 50503 | 天·极 | 后土载物 | 坤维载德 | Ultimate | Circle | 领域：域内我方循环节不掉线（判定窗口 +3s） |

> 金/木/水/火/土 各 15 式 = **75 式**，编号连续、无洞。50303 系名「山门」属坤岳，注意与 504xx 五岳真形区分。

### 8.6 道阶·相生演化（70101–70503，超越天阶）

| id | 系 | 招式 | 类型 | 作用 | 独有效果 |
|---|---|---|---|---|---|
| 70101 | P1 流金化液 | 金液 | ActiveAttack | Line | 破甲 30% + 减速叠 2 层 |
| 70102 | P1 流金化液 | 霜刃 | Ultimate | Circle | 每 1 层循环节 → 穿透 +1 目标 |
| 70103 | P1 流金化液 | 玄冥淬锋 | Ultimate | Self | 5s 内顺生判定必成（窗口不清零） |
| 70201 | P2 润木成林 | 沐川 | Support | Circle | 群体治疗 + 湿气 DOT |
| 70202 | P2 润木成林 | 青溟 | Control | Circle | 过量治疗转范围缠绕 2s |
| 70203 | P2 润木成林 | 万木朝海 | Ultimate | Circle | 领域内每秒 1 层 DOT，治疗溢出即补层 |
| 70301 | P3 燃薪御火 | 炜木 | ActiveAttack | Sector | 引爆 DOT 传染 1 名 |
| 70302 | P3 燃薪御火 | 焰华 | Ultimate | Circle | DOT 引爆范围化（8m 全传染） |
| 70303 | P3 燃薪御火 | 燧林焚天 | Ultimate | Circle | 火海 8s，循环节每层 +1 结算 |
| 70401 | P4 炼土成陶 | 赤壤 | ActiveAttack | Circle | 命中生成 0.4× 护盾 |
| 70402 | P4 炼土成陶 | 熔岩 | Control | Rectangle | 定身 + 护盾灼烧反伤 |
| 70403 | P4 炼土成陶 | 丹火封疆 | Ultimate | Circle | 封疆域：敌方破势失效、我方护盾余量灼烧来袭者 |
| 70501 | P5 藏锋于山 | 矿脉 | ActiveAttack | Single | 格挡 +15% 并记 1 层"锋藏" |
| 70502 | P5 藏锋于山 | 玉山 | Support | Self | 护盾破碎时自动反击 |
| 70503 | P5 藏锋于山 | 坤金镇岳 | Ultimate | Circle | 群体压制 + 每次格挡成功追加不计冷却金意 |

### 8.7 终式

| id | 名称 | 类型 | 作用 | 效果 |
|---|---|---|---|---|
| 90001 | 混沌一炁 | Ultimate | Circle 20m | 一炁化五行：单次结算按当前循环节数在五行乘算中取最高组合；施放后固化 1 层循环节；CD 300s |

**总计 75 + 15 + 1 = 91 式，配套 91 本技能书（含残卷合成规则）。**

---

## 9. 决策定稿（2026-10-03 用户确认）

| # | 决策 | 定稿 |
|---|---|---|
| **D1** | 每阶 3 境的形态 | **75 式独立技能**（3 境各为一式，独立可学/可上栏/可施放），总数 91 式 |
| **D2** | 相克是否单独成支系 | **不加 15 式逆演支系**；相克只做战斗乘算 + 演化的替代准入（§6.5）。同时确定**演化=换代**：被融合的源技封存，不再使用/显示/可操作（§6.2） |
| **D3** | 倍率接入 | **接入 DamageMultiplier，基座 `AttackPower × 0.6 → × 0.25`**，并按 §3.4 乘算顺序补黄金值回归测试 |
| **D4** | 循环节要求五行兼修 | **保留强制兼修**（周天圆满必须五行各打一式），演化门槛直接挂在它上面 |
| D5 | 旧 26 技处置 | 按 §10 兼容映射并入，旧 id 不删 |
| D6 | 91 技 vs 技能栏 60 槽 | 玩家手动装配；被封存的式自动从栏上清掉（§6.2） |

---

## 10. 与现有 26 技的兼容映射（旧 id → 新体系）

| 旧 id | 旧名 | 归入 |
|---|---|---|
| 1001 裂金斩 | → 10101 青锋·启锋（保留 id 作 alias，`pre_skill_id` 链到 10102） |
| 1002 断岳击 | → 10401 庚金·断岳 |
| 1003 金钟罩 | → 50201 厚土·岩甲（护盾类统一归土系基座） |
| 1004 万剑归宗 | → 10403 庚金·万剑朝宗 |
| 2001 青木藤缠 | → 20101 青藤·缠枝 |
| 2002 春回大地 | → 20301 春木·回春 |
| 2003 万木森罗 | → 20403 苍梧·森罗 |
| 2004 生生不息 | → 道阶 P2 润木成林（作为演化技原型，id 迁移到 70201） |
| 3001 寒冰掌 | → 30101 玄冰·凝霜 |
| 3002 水愈术 | → 30301 沧浪·濯缨 |
| 3003 冰封千里 | → 30401 玄武·冻浦 |
| 3004 滔天巨浪 | → 30502 天河·悬瀑 |
| 3005 水月镜花 | → 30103 玄冰·镜面 |
| 4001 烈焰掌 | → 40101 星火·燃薪 |
| 4002 火球术 | → 40102 星火·飞烬 |
| 4003 炎龙出海 | → 40303 离火·炎龙 |
| 4004 焚天灭地 | → 405 天阶系名（拆为 40501/40502） |
| 4005 凤凰涅槃 | → 40503 焚天·凤凰涅槃 |
| 5001 岩甲术 | → 50201 厚土·岩甲 |
| 5002 地刺术 | → 50302 坤岳·地刺 |
| 5003 山岳护盾 | → 50303 坤岳·山门 |
| 5004 地动山摇 | → 50402 五岳·地动 |
| 5005 泰山压顶 | → 50401 五岳·压顶 |
| 5006 厚土载物 | → 505 天阶系名（拆为 50501-50503） |
| 6001 凌波微步 / 6002 风驰电掣 | 保留 60xxx 身法段，不参与阶/境阶梯（位移系单独成"步"支） |

`scripts/sql/007_seed_skill_templates.sql` 的 25 行按上表改写，新增 `011_seed_wuxing_skill_tree.sql` 落 91 行 + 91 书 + 91 物品模板。

---

## 11. 落地计划（确认 D1–D6 后按序执行）

**A. 数据与契约（服务端优先）**
1. `SkillTemplateEntity` 加 3 列：`skill_grade`(1..6)、`grade_stage`(1..3)、`series_id`（string50，技系名，用于 UI 分组）；`SkillBookEntity` 加 `grade`、`grade_stage`。EF 迁移 + `dotnet ef migrations script --idempotent`（项目既有约束：设计期不展开 `${VAR}`），**先应用迁移再重启 Silo**，否则新列会让该表查询整行失败并静默降级。
2. `011_seed_wuxing_skill_tree.sql`：91 技能模板 + 91 书 + 91 物品模板（`item_type=6`）+ 相生对演化关系（`can_advance/advance_skill_id`）。
3. `SkillGrain`：删掉 `LearnSkillAsync` 的硬编码 SkillInfo（`:277-286`），改为读模板；新增 `LearnFromBookAsync(characterId, bookItemId)` —— 校验等级/阶门槛(`learn_realm_require`)/亲和/前置/成功率，写 `CharacterSkillEntity`（`learn_source=1`）；`AdvanceAsync` 实现 §6.1 四条演化条件。
4. `CharacterHandler.HandleUseItemAsync` 空壳补真：`item_type==6` 转发 SkillGrain，其余转 `InventoryGrain.UseItemAsync`。
5. `CombatGrain`：接入 `DamageMultiplier` 与新乘算顺序（§3.4）；`SkillState` 加循环节/镇岳/圆满计数（`[MemoryPackable]`+`[MemoryPackOrder]`）。

**B. 客户端**
6. `SkillDatabase`：导出 `Content/GameData/Skills/skills.json`（91 行，当前文件缺失、全走硬编码兜底），并补 `Grade/GradeStage/SeriesId` 字段 + `ApplyServerTemplates` 透传。
7. `ItemCatalog`：新增 `ItemKind.Book` 与秘籍解析（展示它会教哪一式、品阶、前置）。
8. 技能树 UI：把 `SkillPanelPage` 从写死字形改为数据驱动（五行盘 × 5 阶 × 3 境 + 演化连线），并让它可以**拖进技能栏**（接现有 `ActionBarDrag`，技能栏 60 槽沿用）。
9. 循环节显示：HUD 加一个 5 档环形指示（金水木火土序），圆满时触发一次墨韵涟漪；客户端只显示，判定在服务端。

**C. 验证**
10. 黄金值回归测试（必须可执行，不只是编译通过）：白·初 1.10 / 天·极 3.68 / 道·极 4.76 三档在固定 AttackPower 下的期望伤害；相生顺生 ×1.15 与 5 层周天 ×1.25 的乘算次序；破势夺元使对方节数 −1；演化四条门槛逐条拒绝用例。
11. 环境双客户端：PVP 对练验证"换镇岳"手感。

---

## 12. 我这轮不会碰的东西

- 不改任何现有技能的实际手感数值（倍率接入等你确认 D3）。
- 不动 Orleans Grain 契约以外的服务端行为。
- 不新增美术资产；图标沿用现有 `icon` 列与五行色（`InkWashTheme.Element*`）。

---

## 13. 落地进度（滚动更新）

| 步骤 | 状态 | 产物 / 证据 |
|---|---|---|
| 设计方案 + D1–D6 定稿 | ✅ | 本文档 |
| 唯一数据源 | ✅ | `scripts/skilltree/wuxing_skill_tree.json`（91 式语义字段，数值不落地抄） |
| 生成器与自检 | ✅ | `scripts/skilltree/generate.mjs`：校验 91/75/15/1、id 唯一、前置链与演化消耗 id 可解析；按 006/007 的真实列序拼 INSERT，缺列即报错 |
| A1 表结构 | ✅ 已应用 | 迁移 `20261003113725_AddWuxingSkillTreeAndRetireColumns`：SkillTemplate 加 `skill_grade/grade_stage/series_id/damage_multiplier`，CharacterSkill 加 `is_retired/retired_into_skill_id`。迁移已用 `migrations script --idempotent` + sqlcmd 落库，6 列与 history 逐项查回 |
| A2 种子数据 | ✅ 已应用（91/91/91 已在库） | `scripts/sql/011_seed_wuxing_skill_tree.sql`（2128 行：91 技能 + 91 秘籍 + 91 秘籍物品，幂等） |
| B6 客户端目录 | ✅ | `HundunWorld/Content/GameData/Skills/skills.json`（**对象根 `{ "Skills": [...] }`**，与 `TryLoadFromJson` 的 `SkillCatalogFile` 一致；2026-10-06 由裸数组改来，见 §12 缺陷 1 的纠正）；`SkillConfig` 加 `Grade/GradeStage/SeriesId/Pair/PreSkillId/ComprehensionRequire/EvolveFrom`；`Initialize` 改为"内置表打底 + JSON 叠加"，否则 91 行一到就把 SkillBase 运行时依赖的旧 id（1001-6002）整张换掉 |
| B7 秘籍物品 | ✅ | `ItemCatalog`：`ItemKind.Book=6`（与服务端 item_type 同序）、`BookIdBase=800000`、`ItemDefinition.TeachesSkillId/IsBook`，秘籍名与阶境反查技能目录不再另立表；`IsUsable` 仍只认真消耗品，所以秘籍不会被当成药品装进技能栏 |
| B8 技能树页 | ✅ | 新增 `UI/Ink/Pages/Character/SkillTreePage.cs`（5 行五行 × 7 列阶位 × 3 枚境章，四态渲染：已学/未学/**已封存(斜杠)**/暂不可学并列出缺项）；封存态由已学演化式的 `evolveFrom` 推导，页面不抄相生对照表；已学式可按住拖进底部技能栏。dom-id `nav-skill-tree` 已注册，HUD 导航"技"入口改指本页（`nav-skill-panel` 那份 mock 保留可导航） |
| B8.5 树语义单一来源 | ✅ | 新增 `Combat/Skills/WuxingSkillTree.cs`：阶/境排布、`ComputeRetired`、`LearnedIds`、`DescribeBlockers`（等级·亲和·前置·演化源缺项）。技能树页与技能栏都问它，免得两处对"封存"的理解分叉成"栏上留着一格点了没反应" |
| B9 循环节反馈 | ✅（客户端预测） | 新增 `Combat/Skills/WuxingCycleTracker.cs`（顺生判定复用 `WuxingRules.GetGenerateElement`、3s 窗口、满 5 层周天清零并计 `CompletionCount`）+ `UI/Ink/ActionBar/InkWuxingCycleIndicator.cs`（栏上一排相生序节点：已叠层亮起、下一环金边高亮、满层闪一次）。服务端权威节数下行后只换数据源，界面不动 |
| 技能栏尊重封存口径 | ✅ | `ActionBarService`：选择器不再列出已封存式；按下封存式直接提示"该式已融于演化式，需先散功"；tooltip 补"五行阶梯 X阶·Y境" |
| 客户端编译 | ✅ | `dotnet build Game.csproj` 无 CS 错误，DLL 刷新且含 `SkillTreePage / WuxingSkillTree / WuxingCycleTracker / InkWuxingCycleIndicator` |
| A3 学习/演化/封存（服务端） | ✅ 编译通过 | `SkillGrain`：模板与秘籍按 Grain 缓存一份；`LearnSkillAsync(characterId, skillId)` 改为**按模板建行**并过等级/亲和(`AttributeAllocationData` 五行点)/前置门槛，不再写死 3000ms·10 气的假 SkillInfo；新增 `LearnFromBookAsync`（含秘籍成功率掷签）、`EvolveAsync`（两段弧源式须练满）、`UnlearnAsync`（散功）；`CastSkillAsync` 拒绝已封存式。`ISkillGrain` 同步扩约，`SkillHandler` 按 `LearnMethod==2` 走研读并把失败原因原样下行 |
| A3 依赖注入核对 | ✅ | `SiloStartupExtension.cs:37-46` 按 `[EntityStorage]` 反射逐实体注册 `IDataContext`，`SkillBookEntity` 已带该特性 → 新注入的秘籍上下文可在运行期解析（不是假设，是照注册路径读出来的） |
| A4 使用秘籍 | ✅ 编译通过 | 秘籍分支落在 Grain 咽喉：`IInventoryGrain.UseItemWithResultAsync(characterId, itemId, count)` + `InventoryGrain`（模板 id ≥ `BookItemTemplateBase=800000` → 转 `SkillGrain.LearnFromBookAsync`，按 `BookConsumed` 决定扣不扣一本）。`LearnSkillResponse` 加 `BookConsumed`（order 7）。**顺带修掉一个既有缺陷**：`MessageType.UseItem` 被 `ItemHandler:31` 与 `CharacterHandler:100` 同时声明，后者是恒回 `Success=true` 的空桩——一旦被选中，玩家用任何物品（含秘籍）都会收到"成功"而数量纹丝不动；现在两条路径共用同一咽喉，注册顺序不再影响行为 |

| A5 倍率+循环节权威化 | ✅ 编译通过 | `CombatCalculator.GenerateOf`（**有向**相生表，无序的 `WuxingSynergyPairs` 判不出续链方向）；`SkillState` 加 `LoopStack/LoopLastElement/LoopExpireAt/LoopCompletionCount`；`SkillGrain.CastSkillAsync` 结算顺生/超时重链/满 5 层周天（+3%/层、圆满那一击再 ×1.25、计数 +1），把权威值写进同一条 `SkillCastMessage` 带下去；`CombatGrain` 改为 `AttackPower × 0.25 × 模板倍率 × 循环系数`，**但只有 `skill_grade>0` 的行才接管**，旧技能/未种子逐值退回 ×0.6（接线不许暗砍没参与改动的老技能）。刻意不做"CombatGrain 反调 SkillGrain"：SkillGrain 此刻正 await 下游，非重入 Grain 的回身调用是死锁常客 |
| A5 破势夺元 | ✅ 编译通过 | `CombatCalculator.CounteredElement/Counters`（有向相克表）；`ISkillGrain.BreakCycleAsync/GrantCycleAsync`；`CombatGrain` 命中后按"技能所克 = 守方最高防御行"夺一层（护体行暂取防御向量最高行，主动镇岳行留 §5.3 落地）。夺元失败只记 warning，不回滚已算好的伤害；两个 Grain 调用都不回身指向 CombatGrain，不构成环 |
| A5 权威节数下行 | ✅ | 服务端把 `LoopStack/LoopMultiplier/CycleCompleted/LoopCompletionCount` 写进回执；客户端新增 `Network/MessageHandlers/AttributeUpdateResponseHandler.cs`（此前全项目没有任何 handler 认领 `MessageType.AttributeUpdate`）+ `WuxingCycleTracker.SyncFromAuthority`：只信层数与周天次数，链方向仍由本地记录；取不到值就保持预测不动，重复的圆满不补第二次闪烁 |
| 客户端编译验证 | ✅ 已解除 | 根因是 Editor 配置引用 Flax Tools 里那份过期 `Horizon.Game.Message.dll`（不是代码问题）。提权分发后：**0 个 error CS**，那 24 处 `ScriptOp/GmScriptCommandMessage` 一并消失；唯一残留 `MSB3073 Flax.Build 退出码 1` 是本机缺 VS/Windows SDK 的既有噪声 |
| A3/A4/A5 服务端逻辑（旧阻塞已解除） | ⛔→✅ | 阻塞原因（`IGmCommandGrain` 无实现）已由并行的 GM 功能补齐；期间另一处无关缺失 using（`WorldScriptCommandHandler` 引用 `ScriptedSpawnOptions`）由我补了一行 `using Horizon.Game.Core.Security;` 以解锁验证，回退只需删该行 | `Horizon.Orleans.Grains` 当前**因与本方案无关的进行中改动**编译失败：`IGmCommandGrain` 声明了 `GetScriptPermissionsAsync/GrantScriptAsync/…`，`GmCommandGrain` 无实现（CS0535）。不修好就无法验证学习/演化/结算改动 |
| 数据库应用 | ✅ | 已按 应用迁移 → 跑种子 → 重启 Silo/Gateway/DsHost 走完，细节见上方"数据库落地"两行 |
| 镇岳（§5.3，服务端权威） | ✅ | `SkillState` 加 `GuardElement/GuardChangedAt`（order 9/10）；`SkillGrain.SetGuardElementAsync`——10s 冷却、只能选已练过的行（0=放开，回到自动口径），回执带**生效行**与剩余冷却，被拒时保持原值让客户端回滚；`CombatCalculator.ResolveGuardRow`（指定行压过防御最高行，口径只有一处）；`BreakCycleAsync(attackElement, dominantDefenseRow)` 把"克没克到"的判定搬进有状态的一侧，CombatGrain 因此**只发一次 RPC**（原来先在攻方判 `Counters` 再调守方，多一次往返）且不会再出现"双方各加一层"的互赠；`AdvanceCycle` 在周天清零后把链的当前行落到镇岳行上（§5.3 第二条：清零后从这一行重新起链） |
| 镇岳（§5.3，客户端选择器） | ✅ | `InkWuxingCycleIndicator` 那五枚节点改为可点：点=钉住该行，再点同一枚=放开；命中用 `OnMouseDown(Float2)` 的本地坐标比对 Draw 时顺手记下的 `_chipBoxes`（与技能栏同一口径，别用屏幕坐标再算一遍缩放）。金粗边 + 底部金线全画在框内——控件只有 24 高，把"岳"字顶到框外会被裁掉。新增 `Network/MessageHandlers/GuardElementHandler.cs`（`MessageType.SetGuardElement`，ServiceType 与网关侧 SkillHandler 一致取 Combat），回执只写 `WuxingCycleTracker.ApplyGuardResult`，**本地不预判**：被拒时服务端回的还是原行，界面因此自动弹回。倒计时由指示器驱动（tracker 不是控件，没有自己的 Update）。`MarkGuardPending(0.6)` 只挡连点 |
| ↳ 分发与产物自证 | ✅ | 提权把 8 个 `Horizon.*` 复制进 Flax Tools，读 Tools 里那份 DLL 的字节确认 `SetGuardElementMessage/GuardElementResult/LoopCompletionCount` 三项都在，再 `Flax.Build -genproject` 把新 handler 写进 `Game.csproj`，构建 **0 个 error CS**、`Game.CSharp.dll` 含 `GuardElementHandler/ApplyGuardResult/LoopNeed/CountCompleteEvolutionSeries`。副带效果：并行 WIP 那 24 处 `GmScriptCommandMessage/ScriptOp` 找不到的报错随 DLL 刷新一起清了 |
| ↳ 分发脚本的坑（下次别再踩） | ⚠ | v1 用 `Join-Path $root (Join-Path $p 'bin\Debug')` + `Get-ChildItem -Recurse -Filter "$p.dll"` 在提权与非提权下都报 `SKIP(no dll)`，而同样的语句写死路径就能命中——`$p` 参与的路径拼接在这台机的 PS 5.1 上不 work。**判据不是"命令没报错"**：v1 全程 exit 0，只有日志里的 SKIP 暴露了它其实什么都没拷。v2 逐个 `Test-Path -LiteralPath` 打印实际路径才看清。相关：[[ef-design-time-no-env-expansion]] |
| 数据库落地 | ✅ 已应用 | 顺序照约束走完：`migrations script 20260929063321 → 20261003113725 --idempotent` + sqlcmd（凭据取 `Horizon.Orleans.Silo/bin/Debug/net10.0/.env`）→ 6 列与 `__EFMigrationsHistory` 逐项查回 → 再跑 011 种子 → 停旧 Silo/Gateway/DsHost → 重建 → 按 Silo→Gateway→DsHost 起。跑完库里 **91 技能 / 91 秘籍 / 91 秘籍物品**，`金·青锋三式·启锋`=1.1、`混沌一炁`=5.19 逐值对上门禁公式。Silo 11111/30000 在听，网关日志出现 `初始订阅状态: True（1/1）`。**线上库与客户端目录逐行对账**（不止比文件）：把 `skill_id|skill_grade|grade_stage|ROUND(damage_multiplier,2)|wuxing_element` 拉下来与 `skills.json` 比 → id 集合一致、四元组 **0 处不一致**、五行各 15 式 + 道阶 15 + 混沌 1、倍率区间 1.1…5.19。 |
| ↳ 种子第一次"跑成功但零行"的两条根因 | ✅ 已修 | ① 生成器把两张表的 `SET IDENTITY_INSERT ... ON` 并列写在文件头，而 **SQL Server 一个会话只允许一张表为 ON** → 第二条 SET 直接 Msg 8107 整批中止，一条都写不进去；已把物品那张的 ON 移到技能段 OFF 之后，并在生成器加了一道自检（扫描全文的 ON/OFF 配对，冲突或未收尾就 throw）。② sqlcmd 默认按机器码页读文件，这份种子是无 BOM 的 UTF-8 → 中文全成乱码、注释吞掉换行，**连报错都读不出来**（我第一次就是这么"exit 0 无输出"的）；执行必须带 `-f 65001` |
| ↳ 重启时踩到的"混合版本"陷阱 | ✅ | 我从 08:37 那轮构建起网关，报 `Unable to resolve service 'IDsScriptDispatcher' while activating 'WorldScriptCommandHandler'`——看着像并行 WIP 缺注册，其实注册在 `Program.cs:700` 好好的：真正原因是 **`Horizon.Game.Gateway.dll` 还是 10-01 的旧物**（那轮 build 的 6 个"错误"是 CopyToFlax 写 Program Files 被拒，我误当成全项目已刷新），旧网关 exe + 新 Horizon.Game.Core.dll 才拼出这个 DI 缺口。判据：重启服务前逐个看 `bin/Debug` 里产物时间，别只看 `error CS=0` |

| ↳ 破势这一跳的代价（有意保留） | ✅ 已知取舍 | 判定搬进守方 Grain 后，CombatGrain 无法再用"克没克到"做本地预筛（镇岳可以指在防御向量根本不是最高的那一行，预筛就会漏判），于是**每一次技能命中都会多一次到守方 SkillGrain 的 RPC**。可接受：这条边只在玩家主动施法路径上（各阶 CD 4s 起，混沌 300s），且原本这条路已经有 SkillGrain→CombatGrain→CharacterGrain 三跳。要收回这一跳的正路是把守方镇岳行并进 CombatGrain 已有的战斗画像缓存（`InvalidateCombatProfile` 已经会在技能变动时失效），**不要**改回"攻方判定+守方只执行"——那等于把镇岳的判定权交给对手。 |
| 目录下行补齐阶梯语义 | ✅ | `SkillTemplateEntryInfo` 加 6 列（order 13-18）：`SkillGrade/GradeStage/SeriesId/DamageMultiplier/PreSkillId/LearnCostJson`。此前客户端技能树/封存/倍率全靠本地 `skills.json` 推导——**库里加了新一式而 JSON 忘了重导**时，客户端建出来的骨架行 `Grade=0`，会被树页当作"不在五行阶梯上"直接跳过，等于数据在库里有、界面上没有。门槛走 `learn_cost` 原文透传而不是再拆列：以后加一条门禁（比如要几本残卷）只改生成器，契约不用动第二次。映射抽成纯函数 `SkillCatalogMapper.ToEntry`（可逐值回归），`SkillGrain` 的公式折数值也共用这一处口径。客户端 `ApplyServerTemplates` **只补不覆盖**：未种子旧行的 `skill_grade` 是 0，无条件写回会把画好的格子抹掉。新增 2 条用例（道阶行带全 5 列 + 脏 learn_cost/NULL series 不抛），`WuxingSkillTreeTests` 46/46 |
| 习得入口（§7 经济的第一道闸） | ✅ | 此前**全项目没有任何客户端代码发送 `LearnSkillRequest`**，技能树页还挂着"读书链路待服务端接入"的旧文案 —— 也就是 91 式在局内一式也学不到。补：服务端把传授通道（`LearnMethod=0`）只放给白阶（`WuxingEvolveRules.AllowsTransmission`，纯函数可回归），黄阶以上必须回"此式属 N 阶，需研读对应秘籍"；`ISkillGrain.LearnSkillAsync` 由 `Task<bool>` 改成回 `LearnSkillResponse`，失败原因原样下行（`SkillHandler` 因此不再自己编一句"门槛未达或模板缺行"）；客户端新增 `SkillLearnHandler`（发送 + 回执，成功后**重拉权威目录**而不是就地改本地"已学"集合，免得出现"栏上亮着、服务端没这条"）+ 树页右下角「习得」按钮。为什么这条拦必须在服务端：`LearnMethod` 是客户端上报的，放开传授就等于按一个按钮绕过整条掉落/购书链 |
| ↳ CharacterHandler 的重复声明收敛 | ✅ | `CharacterHandler` 一处就认领了 52 种消息，其中 **8 种与 `SkillHandler` 重叠**（LearnSkill / UpgradeSkill / SkillCooldown / SkillProficiency / ComboAttack / Defense / NeiGong / QingGong），而它自己在 `:835` 那份是**恒回 `Success=true` 的空桩**——被选中时"习得成功"就成了一句谎话，比 UseItem 当年那个空桩更致命（UseItem 至少还有 Grain 咽喉兜住，这里根本没人写状态）。已从 `MessageTypes` 摘掉这 8 支并在块首写明真实现在哪；`switch` 里那几个 case 留作不可达兜底，没顺手删是为了把改动面压在技能域内。**同一文件里还剩 11 支与 `ItemHandler` 重叠**（EquipItem/Crafting/…），本方案不碰，另开条目处理 |
| ↳ 第二次分发与重启 | ✅ | 契约第二次变更后重走一遍：停 Silo/Gateway/DsHost → 重建（三个工程各自 `error CS=0` 且主 dll 时间戳刷新）→ 提权分发（读 Tools 那份 DLL 字节确认 `LearnCostJson/SkillGrade/GradeStage/DamageMultiplier` 四项都在）→ 客户端 `Game.csproj` **0 个 error CS** 且 `Game.CSharp.dll` 含 `ApplyLadder/ReadPositiveInt/LearnCostJson` → 按 Silo→Gateway→DsHost 起，网关再次出现 `初始订阅状态: True（1/1）` |
| C10 回归测试 | ✅ 46/46（含本轮新增的下行映射 2 条） |`Horizon.Game.Gateway.Tests/WuxingSkillTreeTests.cs`：① 相生/相克有向表与"五步回金"；② `StepWuxingCycle` 黄金值（1.03/1.06/1.09/1.12、五行各一式即满 5 层 ×1.25 并清零、超时重链、同属与逆生不清零、无属性不碰链）；③ `SkillLadderFactor`（旧行逐值 0.6、白初 0.275、天极 0.92、道极 1.19、倍率列被写成 0 时按 1.0 不哑火）；④ 交付物一致性——直接解析 011 种子 SQL 与 skills.json 两份投影（91/91/91、每行 15 式、秘籍 item id = 800000+skill_id、`item_type=6`、每本秘籍的 `element` 按技能行 wuxing 逐项映射、阶梯公式复算）；⑤ §6.2 两条计数门槛。跑法：`dotnet test Horizon.Game.Gateway.Tests -p:BaseOutputPath=bin/Verify/ --filter FullyQualifiedName~WuxingSkillTreeTests`（**绕开正在运行的 Silo/Gateway 对 `bin/Debug` 的文件锁**，不是绕开失败）。全量 2892 例：2884 通过 / 0 失败 / 8 既有跳过 |
| §5.1 周天起链层数修正 | ✅（测试逼出来的真缺陷） | 原实现只在"顺生"时加层，起链那一式不占层 → 按相生序打完五行只到 **4 层**，周天圆满**永远触发不了**，与本文 §5.1 末尾的设计意图和 D4（周天圆满必须五行各打一式）直接矛盾。现规定**起链第一式自己占一层**（节数数的是"本轮走过几行"），服务端 `CombatCalculator.StepWuxingCycle` 与客户端 `WuxingCycleTracker.NoteCast` 同步改，本文 §5.1 补了这条口径 |
| §6.2 演化门槛补全 | ✅ | 种子把 `loopNeed`（道阶 12/20/28、混沌 40）与 `needPairsCompleted:3` 写进了 `learn_cost`，但**服务端此前一条都没判**——玩家读道阶书只需练满两式即可过关。新增 `WuxingEvolveRules`（纯函数：解析 + 道阶系对数 + 两条计数门槛，含"脏 learn_cost 退化成无门槛"）；`SkillGrain.CheckLearnGates` 改为按字段判而非按身份判（旧写法在 `consume` 为空时提前 return，正好把不带 consume 只带计数的混沌终式整个放过）；客户端 `skills.json` 补 `loopNeed/needPairsCompleted`，技能树页研读前就显示"周天圆满 3/12" |
| 演化入口收敛 | ✅ | `ISkillGrain.EvolveAsync` **零调用方**且与 `LearnFromBookAsync` 逐行重复（同一套 `CheckLearnGates(requireEvolveSourcesMaxed: true)` + `CommitLearnAsync`），已从契约与实现删除。道阶演化式与普通式只差 §6.2 那几条门槛，用同一条研读路径即可——多一个入口只会多一处漏判 |

### 14.7 落地进度（接 §13 的表）

| 步骤 | 状态 | 产物 / 证据 |
|---|---|---|
| E1 门槛数据契约 | ✅ 已落库 | `learn_cost` 新增 `favorNeed/materials/attrNeed` 三族键，判定输入零契约变更（该列本来就原文下行）。生成器按 §14.2 矩阵产出：**好感 46 式**（玄15+天15+道15+混沌1）、**材料 46 式**（地15+天15+道15+混沌1）、**基础属性 16 式**（道15+混沌1）、白/黄 0 式；灵材 10 行（910001-910010，`item_type=3`、可堆 99、带 `source_detail.domain`）一并进种子。库里核对：`learn_cost LIKE '%favorNeed%'` = 46、`%materials%` = 46 ✓ |
| ↳ 生成器自检 | ✅ | 三道新自检：门槛引用的灵材必须真有物品行（否则现场表现是"玩家永远凑不齐"且无任何报错）；好感域必须落在 1..5 或用 allDomains；白/黄不许带材料。另加一条通用不变式：扫描全文的 `SET IDENTITY_INSERT` ON/OFF 配对 |
| E2 纯函数判定 | ✅ 54/54 | 新增 `WuxingDynamicGates`（好感/材料/属性三判 + 缺项文案）与 `WuxingEvolveRules.Parse` 扩读；`LearnCost.NeedsDynamicContext`。**缺上下文一律 fail closed**：装配漏一次若当成"条件满足"，等于高阶技能无材料、无好感直接学，比一次拒绝严重得多 |
| E3 分域好感度 | ✅ 代码就位 | 项目原先只有角色级单值 `CharacterEntity.Reputation`（外加同样是全局的侠义值），问不出"这个人在金域攒了多少心意"，而玄阶以上要的正是后者。新增 `FavorabilityGrain`（Guid 键同 SkillGrain，`GrainKeys.ForEntity`）+ `FavorabilityState.ByDomain/ByTarget`；**单一来源默认累计上限 20**，否则"逮着一个 NPC 反复对话"就能刷满一域，门槛形同虚设 |
| E6 材料扣除 | ✅ | `InventoryGrain.ConsumeByTemplateAsync`：先全量核对再动手，不够就一票否决、一格都不扣（避免"扣了一半材料、习得又失败"）。研读路径的扣减挂在习得成功之后，清单由 `LearnSkillResponse.MaterialItemIds/Counts` 带回（判定在 SkillGrain、背包在 InventoryGrain，中间没有第三人能动包） |
| ↳ 判定输入的装配口 | ✅ | `LearnGateContext`（材料计数/名称/分域好感/属性）放 Message 程序集并带 MemoryPackOrder；**只由 `InventoryGrain.BuildLearnGateContextAsync` 装配**。SkillGrain 不去问背包/好感：`InventoryGrain → SkillGrain` 这条边已存在，反向就成 Grain 环。读不到好感按零好感判、读不到属性直接拒，一律 fail closed |
| E7 取消门派限制 + 通道归服务端 | ✅ | 事实核对：`SkillTemplateEntity.SectId/ProfessionLimit` **全仓零读取方**，`JoinSect/SectSkill` 只是 echo——门派门槛从来没生效，所以"取消"要做的是不再往这套语义里挂任何东西，并把 `profession_limit` 在种子/实体里明确留空不用。真正收紧的是通道：`LearnMethod` 不再由客户端声明，`SkillHandler` 一律走直接学习，而**登记过秘籍的行被 `DirectLearnBlocker` 拒回"此式需研读《X》"**（否则客户端报一个 `LearnMethod=2` 就能白读一卷并不存在的书）；真研读只在"背包使用物品"那条路上 |
| ↳ 顺带清掉的任务域重复声明 | ✅ | `CharacterHandler` 又摘掉 4 支（`AcceptQuest/CompleteQuest/QuestUpdate/SectQuest`），全是恒回 `Success=true` 的空桩；`QuestHandler.HandleCompleteQuestAsync` 原本同样**根本不调 QuestGrain**（任务"完成"了、进度没动、东西没给），而 §14.2 的初级书产出全押在这条链上 |
| E4 任务奖励发放 | ✅ 代码就位 + 回归绿灯 | 约定写成数据而不是加列：`QuestData.Rewards` 是 `Dictionary<string,int>`，新增两族键 `item:<模板id>=数量`（初级书、灵材都走这条）与 `favor:<域>=点数`（来源记成 `quest:<questId>`，同任务反复交不能无限刷好感）。未认识的键只记日志不吞掉；发放失败不改判"任务已完成"这件事。（此前担心的编译被并行 GM 脚本 WIP 挡住一事已自行转绿：Core 0 error，`WuxingSkillTreeTests` 63/63、全量 2931/0） |
| F1 任务模板表（产出侧权威归服务端） | ✅ 实机跑通 | 用户拍板"任务产出以服务端为权威"后落地：`QuestTemplateEntity` → `Game_HunduShijie_QuestTemplate`（EF 迁移 `AddWuxingQuestTemplate` 已应用到 Game 库）+ `scripts/sql/012_seed_wuxing_entry_quests.sql`（每域一条入门任务：该域白阶三本 + 2 枚信物 + 该域好感 20，正好够"玄·初"的 favorNeed 20）。`IQuestGrain.AcceptFromTemplateAsync(questId)` 只收 id，名称/描述/奖励一律读模板，读不到模板就接不了任务（fail closed）；`QuestHandler.HandleAcceptQuestAsync` 从恒回 `Success=true` 的空桩改成真调用。**关键取舍**：没有复用现成的 `AcceptQuestAsync(..., rewards)`——那个签名让调用方自带奖励字典，接到网络上等于允许玩家自己声明"这个任务给我三十本秘籍、五千点好感"，§14 全套门槛当场作废；而这条口子一旦开着，后面所有人都会沿用它。真集群证据：`Live_QuestGrain_AcceptsFromTemplateAndRewardsComeFromServerSideTable` 与 `Live_QuestChain_AcceptCompleteThenGrantBooksAndFavorability` 均通过（后者把"接→交→发"三段按 `QuestHandler` 的同一组调用顺序打到真实 Grain：物品入账、好感精确落在 `byDomain[3]=20` 且不串域；环境门控同 `OrleansGrainLiveIntegrationTests`，Redis `127.0.0.1:9379`，角色键用 `9_900_000_00x` 合成值不碰真人存档）。**仍欠两件，都不是代码问题**：① 客户端 `NarrativeProNetworkAdapter` 发的 questId 来自 NarrativePro 自己的任务定义，所以要 PIE 里真的触发，需要有一条 NarrativePro 任务的 id 落在 90001..90005 —— 这是内容编排，不是接线；② Ink 任务页 `MenuQuestsPage` 仍是本地硬编码列表、未列服务端任务；`objectives` 列已建但完成判定未接线（空目标=接了就能交）。 |
| G1 经验/升等/境界权威（阶梯的地基） | ✅ 实机跑通 | 排查 §14 时发现一个比门槛本身更靠前的缺陷：`CharacterEntity.experience` 列一直存在，但**创建置 0 后全仓再无写入方**，也没有升等逻辑（`CharacterLevelUp` 事件只写日志、零发送方；`GmCommandGrain` 里的 `AddExpAsync` 只剩一行注释）。库里所有存档 level=1、realm_level=0 ——于是黄/玄/地/天/道阶的 `learn_level_require`（实测 31…300）永远过不去，整条阶梯只有白阶可达，§14 的动态条件形同虚设。补法：`ProgressionCurve`（纯函数：`ExpToNext=100+25×(lv-1)`、`MaxLevel=300` 对齐 §2.1 阶梯顶、`RealmForLevel` 按 白1/黄31/玄76/地131/天201/道300 派生境界）+ `ICharacterGrain.AddExpAsync`（状态与库两份都写；落库失败不判"这次经验没给"，但必须报出来，因为它是"重启后等级回落"的唯一线索）+ 任务奖励 `exp` 键接进发奖。`learn_realm_require`(1..6) 此前也没有任何判定读它，现由 `RealmBlocker` 在等级门之后判——境界按等级派生，不读那条同样无人维护的 `realm_level` 列。真集群证据：`Live_WuxingLadder_FavorGateIsTheRealBlockerForMidTierBooks` 通过——在开发库的一次性测试角色（`character_id=8`，名 "123"）上先升到 ≥76 级、分配五行亲和，白·三式+黄·三式全部研读成功；随后玄·初在"该域零好感"时被拒且拒的是好感，补 20 点该域好感后学得。这条把 §14 的 等级/境界/亲和/前置/好感 五道门在真实 Grain + 真实库上依次验穿。曲线数值是**可调默认值**，不是最终平衡：要定节奏需拿目标升级速度反推。 |
| G3 相克侧实机 + 周天状态权威下行 | ✅ 服务端跑通 / ⚠ 客户端待编译 | `Live_BreakCycle_UsesTheChosenGuardRowAndUnlearnRestoresThem`（真集群）用**可判别**的元素组合证明两件事：① 护体行取镇岳指定行而不是"防御最高行"——把护体行设为水、把自动行当金传进去，`火` 破不回（火克金不克水），`土` 才破得动；② 破势真扣层——先把层夺空（基线自己定，因为角色状态持久、上一轮演化测试推过 66 次周天），`GrantCycle` 到恰好 1 层，第一次土破成功、第二次必须失败，且判负的那次不许白扣层。**为此补的权威下行**：`SkillCatalogSyncMessage` 新增 `LoopStack/LoopLastElement/LoopCompletionCount/GuardElement`——此前客户端指示器只能靠累积每一次施放回执来猜自己有几层，重登/换端就是空的，被破势后也看不到基线；服务端一直知道全部事实，却没给过这一帧。测试直接读这几项定基线，所以字段本身也在真集群上被验了一遍。客户端 `SkillCatalogService.Apply` 已接上（`SyncFromAuthority(..., cycleCompleted:false)` 免得重登时白闪一下），DLL 已提权分发进 Flax Tools（内容哈希 src=dst 一致、Tools 里的 DLL 已含新字段）。**没编过的原因要说清**：`dotnet build Game.csproj` 现在红在 `NarrativePro/CommonUI/NarrativeRuntimeBootstrap.cs:282`（CS1646/CS1658），那个文件由并行会话在我改动之后（15:48）写过、当前处于半编辑状态，不是我引入的，也没有替他们补 API；等他们落地后重编客户端即可。全量回归 **3041 通过 / 0 失败 / 8 跳过**。 |
| G5 材料门与属性门的"缺一样就拦人" | ✅ 真集群跑通 | 之前的实机测试都是把料/属性给足再看放行，那只证明了"不拦"。`Live_MaterialGate_...`：把土行推到地·初之前，用 `BuildLearnGateContextAsync` 读计数 + `ConsumeByTemplateAsync` 把十味灵材全扣到 0 → 研读被拒且原因确实是材料，**并且被拒之后计数仍为 0**（判定与扣除分离：顺序写反就会白吃材料）。`Live_AttributeGate_...`：`AllocateAttributeAsync` 收的是"目标分配态"，于是可以让五行点保持 200、四维清零，把属性做成唯一短板 → 演化道阶被拒且报的确实是四维之一，补回 70/70/100/130 即放行。至此 §14 的四族动态条件——好感(G1)、材料(G5)、基础属性(G5)、技能树完整性(G2/G4)——每一族都有"缺时被拒 + 补齐放行"两侧实机证据。最新全量 **3044 通过 / 0 失败 / 8 跳过**（含 7 条 live）。 |
| G6 倍率黄金值对账（数据 ↔ 设计） | ✅ 已钉 | `Seed_LadderMultipliers_MatchTheDesignFormulaForAllNinetyOneRows`：直接解析 011 种子里 91 行技能，逐行断言 `damage_multiplier == round(1.10 × 1.09^k, 2)`（k 按 §3.1 展平；混沌终式钉在 k=18），并要求**同一技系同一阶内倍率不回退**。倍率是 D3 之后真正决定伤害的量，同时住在生成器公式、种子 SQL、服务端模板三处，任何一次"顺手改个数"都没有报错信号——表现只是某个境界的伤害悄悄变了。单调性只能在"同一技系同一阶"内要求：id 顺序是先走完一行的一整阶再换行，换行时 k 归零、倍率本就该回落。最新全量 **3045 通过 / 0 失败 / 8 跳过**。 |
| G8 怪 → 五行域（E5 的最后一段：把装配好的 75 条接上） | ✅ 链已接通（含一处新发现的耐久性缺陷） | **落点纠正**：此前写"怪物模板这一层在本仓库里还不存在"是错的——库里确实没有怪物表，但代码侧有 `Horizon.Game.Sim/MonsterTemplateCatalog`，它就是怪物模板层（名称/等级/模型键，注释里写明"生产应由配置表下发表数据替换"）。所以用户选的"怪物模板加一列"有确切落点：给 `MonsterTemplateInfo` 加 `WuxingDomain`（1金2木3水4火5土，0=未定域）。既有三行的 id/名称/等级一个都没动（脚本生成白名单与头顶信息卡的显示断言按它们写过），只补域号，并新增两兽把五个域凑齐；DsHost 的开发刷怪循环随之从 `1+(i%3)` 改 `1+(i%5)`，否则开发世界永远只刷得出三个域。**两处消费读同一列**：`WuxingDomainLootMapping`（掉落表 ID = 域号，替掉恒等映射）与击杀好感（`OrleansGameplayEventSink` 的 Kill 分支 → 新 `IFavorabilityWriter`/`OrleansFavorabilityWriter` → `IFavorabilityGrain.AddAsync(domain, 1, "monster:{templateId}")`，玩家键复用 `GrainKeyMapper.ToInventoryKey`，与研读门槛读的那份必须同一个键）。好感解析走 `IMonsterDomainResolver` 接口而不是静态调用目录，就是为了不在重演 G7 那个坑（静态/工厂依赖在脚手架里被 catch 吞成"什么都没发生"）。未定域一律不记账、不掉任何东西：**猜域的表现是"金域信物从火域怪身上掉"，任何报错里都看不出来**。<br>证据：单测 8 条（`WuxingMonsterDomainTests`：五域各一兽、未登记模板域为 0、映射读的是列而不是模板 ID、按域掷骰只出该域标记物品、击杀给对域加 1 点且 targetKey 带模板号、无域怪不记账、好感写失败不拖任务进度、没有好感写入端时击杀照常）；真集群一条 `WuxingLootDomainLiveTests`——把 5 个已登记模板过真实 `LootTableGrain` 预热后各掷 400 次，断言**掉出的每本书都落在被杀怪那一域的 800000+域\*10000 号段**、信物必须是 `910010+域`，且 §14.2 第一条产出通路（白·初那一本）在每一域都真能刷出来。<br>**新发现的缺陷（要一次 Silo 重启才能修，故未动代码）**：`LootTableGrain._config` 是普通字段、没有 `IPersistentState`，装配只活在一次激活里。取证时 5 个域**全部读回为空**——本 Silo 早前确实打印过"装配完成：5 张 / 75 条"，所以现在读不到，只能是激活回收把配置带走了（也不排除运行中的 Silo 早于装配代码）。后果很安静：掉落从"该掉"变成"什么都不掉"，两端都有日志、中间没有。测试改成"缺则按种子补填、已有绝不覆盖"才能继续取证，并把补了几张打在输出里。修法两种（都要重启）：给 `_config` 上 `IPersistentState`（需按仓惯例补 `[MemoryPackable]`+`[Id]`，属状态形状变更，旧 Silo 混跑会踩 TypeLoad/反序列化），或把 `WuxingLootTableSeedService` 从"启动跑一次"改成按需/周期重装配（不改状态形状，代价最小）。<br>最新全量 **3060 通过 / 0 失败 / 8 跳过**（含 8 条 live；跑全量时 `NetworkPerformanceBaselineReportTests` 报过一次文件写入失败——它每次运行都要重写 `docs/NETWORK_PERFORMANCE_BASELINE.md`，与并行会话撞锁，单跑即通过，与本节改动无关）。 |
| G7 破势夺元的跨 Grain 编排（相克的"施放侧"） | ✅ 已钉（脚手架级） | `BreakAndStealCycleAsync` 是相克唯一有游戏内收益的一支：克中则破掉守方周天层，并把这一层记到攻方身上。G3 已在真集群验过**守方那一侧的语义**（`BreakCycleAsync` 用可判别元素真扣层、判负不许白扣），但"施放时到底有没有去破、破成才不给攻方记层"这半段一直零断言——原因很具体：它走 `GrainFactory.GetGrain<ISkillGrain>()`，而 Moq 脚手架里没有工厂，异常被外层 `catch` 吞成 `false`，测试只看见"没破"，永远看不见"根本没调用"。按仓库既有的 `_characterGrainProvider` 惯例补一个可注入的 `Func<ulong, ISkillGrain>? _skillGrainProvider`（构造里默认仍是 `GrainFactory`，生产路径零变化；provider 缺失时**如实放弃破势但不回滚已算完的伤害**——破势是附加语义，不该把这次施放整体判废）。`SkillCastCycleStealTests` 三条：① 破势用的是**模板的五行**（金）而不是施法者自身行（火），并把服务端算出的护体行一起传过去，只有破成才 `GrantCycleAsync` 恰好一次；② 守方扛住时一次都不赠层；③ 守方 Grain 抛异常时施放仍然成功、不赠层（相克失效不该变成"打不出伤害"）。**证据等级要说清**：这三条证的是调用形状与前后件（用哪一行去破、破成才记层），不是跨进程真实状态转移；后者由 G3 的真集群测试覆盖。两边合起来才是"相克"这一支的完整证据。最新全量 **3048 通过 / 0 失败 / 8 跳过**。 |
| G4 混沌一炁与 §6.2 计数门槛（目标的封顶那一句） | ✅ 真集群跑通 | `Live_ChaosRequiresThreeCompletedDaoSeriesAndPassesWhenTheyAreThere`：在一次性角色上把金/木/水/火四行从白阶推到天阶并练满（60 式）→ 推 220 次周天（≥40 轮圆满）→ 学满**前两对**道阶三境后，研读混沌被拒且原因如实是"道阶系圆满不足：需 3 对（每对三境俱全），当前 2 对"→ 补齐第三对，`90001 混沌一炁` 习得并出现在已学列表。**门槛口径被实测纠正了一次**：第一次跑被拒时报"当前 0 对"——因为"圆满对数"按 `Level==MaxLevel` 计，只学到 1 级不算，测试补了"每境练满"这一步才过关；这正是 §6.2 那两条计数器当年"数据里有、代码不判"的反面：它们现在既生效、也确实拦人。 |
| G9 五行域掉落表的自愈（G8 里那条缺陷的修法） | ✅ 代码与证据到位，等一次 Silo 重启才算生效 | `LootTableGrain._config` 是普通字段、没有 `IPersistentState`，所以启动装配的那份配置只活在一次激活里；激活被回收后集群里就是空表，DS 若在"空表期间"启动预热，整场会话都不掉东西。**没有把状态形状改掉**（上 `IPersistentState` 要补 `[MemoryPackable]`+`[Id]`，属 Grain 状态变更，旧 Silo 混跑会踩反序列化/TypeLoad），改的是装配的**形状**：把"写不写"从服务里剥出来成 `WuxingLootTableEnsure.EnsureAsync(tables, grainProvider, onlyIfAbsent)`（provider 是缝隙，测试能用假 Grain 断言，不必依赖 `IClusterClient`——同 G7 的教训），服务侧启动跑一次无条件装配，之后每 5 分钟跑一次"缺则补填"。三条纪律都有断言兜：**已有数据的域一张都不许写**（覆盖会把库里真值换成本周期读到的那份，两边一分叉自愈反而在悄无声息地抹掉生产改动）、单域失败不带走其余（少一域产出比整批半途抛出更好看清）、取消要如实抛出而不是被兜底吞成一条失败记录。间隔不许短于 1 分钟，因为每轮都要重读整张物品模板表；也不必很短，因为 DS 只在启动预热时读一次配置，真正受影响的是"在空表期间启动的那些 DS"。<br>证据：`WuxingLootTableEnsureTests` 5 条（含 null 与零条目两种"空"都算缺、无条件首装、失败域带异常上报、`OperationCanceledException` 传播、间隔下限）；真集群那条 `WuxingLootDomainLiveTests` 改成**用生产自愈函数本身**补表，并在表已存在时再跑一次全五域，断言 `Seeded=0 / Failed=0 / Skipped=[1,2,3,4,5]`——"有不覆盖"这半句从此在真实 Grain 上有证据，而不只是在假 Grain 上。<br>**仍然欠一次部署**：跑着的 Silo 是旧构建，自愈循环要重启才生效（任务 #24）。重启由用户掌握，我不擅自停别人的进程。最新全量 **3067 通过 / 0 失败 / 8 跳过**（全量首跑曾见 1 例 `NetworkPerformanceBaselineReportTests` 写 `docs/NETWORK_PERFORMANCE_BASELINE.md` 撞文件锁，与本节无关，重跑即绿）。 |
| G10 任务目标（objectives）接线：把"接了就能交"变成真要求 | ✅ 服务端跑通，种子待点头 | `QuestTemplateEntity.objectives` 列一直是死的：`AcceptFromTemplateAsync` 不解析它，于是入门任务实际上**接完就能交**，§14.2"通过任务获得初级技能书"是个空转按钮。补 `QuestGrain.ParseObjectives`（`[{"type":"kill_domain:3","desc":"…","count":5}]`）并在接任务后逐条 `AddQuestObjectiveAsync`。**关键取舍是不对称的**：奖励解析坏了只丢那一条键（任务照接），目标解析坏了要**整条丢掉并计入日志**——因为一条永远推不动的目标会把任务永久卡在"进行中"，玩家什么提示都拿不到，而少一条目标只是少一路进度。DS 侧 `KillObjectiveTypeMatcher` 原来登记着一条已知局限："只认得这是个击杀目标，不认得杀的是哪种怪，任意击杀都推进"；现在支持 `kill_domain:N`，域号读的就是 G8 那唯一的 `WuxingDomain` 列，所以**"任务要杀的水域生物"与"掉水域书的怪"不可能两套口径**。类型串写坏（`kill_domain:`、`kill_domain:9`）一律判"不推进"，而不是降级成"任意击杀"——降级会让配错的任务悄悄能交。两端各持一个字面量 `kill_domain`（Grain 校验 / DS 推进），中间没有编译期约束，所以 `QuestObjectiveContractTests.EveryObjectiveTypeTheGrainAccepts_IsRecognizedByTheDsMatcher` 拿五个域 + 遗留 `kill` 全过一遍匹配器，那是唯一的分叉报警器。012 种子改为每条入门任务带一个本域击杀（5 只）并加了第三条自检（目标缺失或域号与 `wuxing_domain` 不一致就 ROLLBACK）。<br>证据：契约与匹配器 20 条（含"配错的域号不许被当成任意击杀"、"未定域生物不推进任何域目标"、"坏行只丢坏行"）+ 真集群 1 条 `Live_DomainKillObjective_...`：接任务→加水域击杀目标→**一次击杀都没有时交付被拒**→拿别域生物刷 10 次进度仍是 0 且仍不能交付→换目标域刷 5 次转"可提交"→交付成功且奖励是服务端那份。`questId` 每次运行现取时间戳派生，因为一次性任务交付第二次就接不上，而"已完成"的任务又撤不掉（状态机不允许 Abandon）——这是共享持久存档上第四条必带护栏。<br>**没做的那半**：种子改了但没往库里写（改库需用户点头，任务 #25）；`MenuQuestPage` 仍未消费服务端任务列表。<br>本轮全量：一次 3087 通过 / 1 失败（`GatewaySyncWiringTests.EndToEnd_BatchFanout_*`，墙钟型并发抖动，单跑与此改动无关；同一轮里上一版的失败点已消失，改的是别的测试），**新增/涉及的 live 与契约测试单跑与在套件内均通过**。 |
| G11 天阶与混沌的产出通路（"超越天阶"的上游原来是断的） | ✅ 接通并实机验证 | 自查整条阶梯时发现的洞，性质和 §6.2 那两条计数器当年一样——**数据里有、代码不判**：生成器把天阶 15 本与混沌 1 本写成 `via:["worldBoss"]`（`generate.mjs` grade 5/7），而 `WuxingLootTables.Build` 只认 `via` 含 `loot` 的行，全仓也没有第二处消费 `worldBoss` 这个标记。于是这 16 本不进任何掉落表：小怪不掉、任务不给（任务是白阶三本）、演化和材料都用不上它。而**道阶演化的前置是"该域天阶三境练满"**，所以"最高可超越天阶"这条核心卖点在实现里根本走不通，且断得极安静——BOSS 身上什么都不掉，看起来只像运气差。修法：① `Build` 按通道分档，`worldBoss` 行进**另一档表**（表 ID = `100 + 域`，无域的行进总表 100），一行可以同时属于两档（既刷得到又在 BOSS 身上掉）；② 怪物模板再补一列 `IsWorldBoss`，`LootTableIdOf` 给出"普通怪=域号 / BOSS=100+域 / 无域 BOSS=100 / 未登记=0"；③ 目录登记五行各一只 BOSS（Lv210..230，贴着 §2.1 天阶 Lv201）加一只无域的 `混沌遗蜕`（Lv300）。**日常档一条都没动**：天阶一旦混进小怪表，"高阶一张书就是学问"这条设计当场被小怪冲垮，而这也正是本次要防的那种静默降级。<br>两端各写一次 `WorldBossTableBase = 100`（Silo 不便为这一个常量新增到 Sim 的工程引用），由 `WorldBossTableBase_IsTheSameNumberOnBothSides` 兜住分叉——分叉的表现依然是"BOSS 掉不出天阶、道阶练不满、两边日志都正常"。<br>证据：`WuxingBossLootTests` 5 条单测（档位路由、目录里五行各一 BOSS 且与日常档彻底分开、`Build` 只把天阶放进 100+域、混沌只许在总表 100、BOSS 掷 BOSS 表 / 普通怪掷日常表互不串门）+ 真集群 1 条 `Live_WorldBossDropsItsOwnDomainTierFiveBooks_...`：用生产自愈函数把 11 张表按种子补进真实 `LootTableGrain`（缺则补、有不覆盖），六只 BOSS 各掷 4000 次，断言**只掉该域天阶号段或混沌**、且 15 本天阶 + 1 本混沌一本都不许缺席（概率条目 1%/0.5%，600 次会有百分之几的样本掉不到某一本，那属于抖动而不是规则，故取 4000）。顺带把两处口径的自检改了向：`LootTables_BuiltFromTheShippedSeed` 现在要求 11 张表，并且明确断"890001 只许出现在表 100"。<br>**仍欠内容**：BOSS 不在 DsHost 的开发轮换循环里（`1+(i%5)` 只出普通怪）——世界 BOSS 要靠脚本化生成出现，PIE 里现在就能验：游戏内输入 `/spawn monster 锐金夜叉 1 20,0,0`（等价写法 `/spawn monster 6 …`），击杀后应掉出 `810501/810502/810503` 之一；无域那一只 `/spawn monster 混沌遗蜕 1 20,0,0` 掉 `890001`。白名单用的就是 `MonsterTemplateCatalog.TryGetRegistered`（`ScriptAuthorityCore.cs:394`），模板 6..11 已登记并有断言兜住（任务 #26 只剩"要不要把它做成常态世界事件"这一条内容决策）。本轮全量 **3099 通过 / 0 失败 / 8 跳过**。 |
| G12 相生循环本体的逐值回归（目标第一句话的那条规则，此前零直接断言） | ✅ 已钉 | 做完成度自查时发现的最大一块：**"五行相生相克循环往复不断决"这条核心规则住在 `CombatCalculator.StepWuxingCycle`（纯函数）里，由施放路径 `SkillGrain.AdvanceCycle` 每次施放调用一次，而它一行测试都没有**。已有的实机测试推周天用的是 `GrantCycleAsync`（那是破势夺元/演化的入账口），**根本不经过相生判定**——也就是说"按相生序施放会续上链"一直是靠读代码相信的，把规则改坏不会有任何测试变红。补 `WuxingCycleRuleTests` 9 条，覆盖 §5.1 的每一句：起链那一式自己占第一层（少这层，打完五行只到 4 层，D4 的"五行各一式"永远凑不成）→ 顺生逐层累加、倍率 `1+0.03×层` → 满 5 层判圆满（清零、计数 +1、那一击 ×1.25）→ 同属与逆生都不掉层、只挪链并刷新窗口 → 窗口超时重新起链且不清已有圆满计数 → 无属性式（0）完全不碰链 → 连打两轮能圆满两次（"不断决"的字面要求）→ 相克表与相生表是两张不同的有向表（混同会让破势打在错误的行上）。**相生链不抄字面量**：链由 `GenerateOf` 现取，并断它五行各占一次且闭合（`GenerateOf(第五行)==起链行`），抄成"金生水…"那种字面量一旦与服务端表分叉，测试会跟着错。写用例时自己踩到两次倍率误算（`1+0.03×n` 写成 `1.03×n`），是测试被实现纠正、不是实现被测试纠正。<br>顺带一条数值观察（不是缺陷，留给平衡阶段）：圆满那一击是 1.25，而第四式已有 1.12，"攒满周天"的收益边际很薄（+0.13）；若 PIE 手感上觉得周天圆满不够"值得"，调的是 `CycleCompletionMultiplier` 与 `CycleBonusPerStack` 这一对，不是倍率阶梯本身。<br>本轮全量 **3112 通过 / 0 失败 / 8 跳过**。 |
| G13 角色 Guid 的两套推导：任务书与击杀好感曾经记在"没人看的那把键"上 | ✅ 已修 + 实机钉住 | 想把 §5.1 的相生循环改打真实施放路径时，撞出一串东西。**同一角色 ID 在本仓有两套 Guid 推导**：`GrainKeyMapper.ToInventoryKey`（大端）是**背包**的权威键（行囊 UI、使用物品、DS 掉落入包都用它，这条在早期记忆里有明文"别改 grain 的 Guid 键"），而 `GrainKeys.ForEntity`（小端）是**好感**读者的键（`InventoryGrain` 合成研读门槛、信物入账都用它）。两套都在用的后果不是报错，而是"写进另一把键下的 grain"：<br>① **任务发的书进不了玩家行囊**——`QuestHandler.GrantQuestRewardsAsync` 用 `ForEntity` 拿 `IInventoryGrain`，而 UI/使用路径读 `ToInventoryKey`。这条是我昨天写 F1 时埋的，而当时那条 live 测试**两端都用了同一个键**，所以它是自洽地绿的：测试证明了"能写能读"，却没证明"读的是玩家看的那份"。<br>② **击杀攒的好感开不了中级门**——G8 的新 `OrleansFavorabilityWriter` 用背包那把键记好感，而研读门槛读的是 ForEntity 那把。表现正是需求里"与区域生物好感度触发中级技能学习条件"整条不生效，而且两端日志都正常。<br>改法都是**让写侧对齐读侧**（不动两套推导本身，也不动背包权威键）：任务发奖改 `ToInventoryKey`，击杀好感改 `ForEntity`，两处都在代码里写明"为什么是这把键"。证据：`Live_KillEventThroughTheRealSinkCreditsTheKeyTheLearnGateReads`（真集群）——事件汇发一次击杀后，门槛读的那把键 +1、背包那把键一分不动，把"不串键"钉成断言而不是注释。<br>**顺带撞出来的第二个洞（未修，任务 #28）**：`CharacterHandler.HandleResurrectAsync` 是空桩——把请求体抄成响应直接回包，从不碰任何 grain。所以 PIE 里死后点复活会"成功"但永远是死亡状态，之后每次施放被 `SkillGrain` 的 `CurrentHealth <= 0` 挡回"施法者已死亡"，战斗循环断在那里。这也是我这两条施放测试只能**显式跳过**的直接原因（跳过文案里写明了原因与任务号）；规则本身仍有 G12 的逐值断言与破势夺元的脚手架断言兜着。<br>本轮其余：`QuestGrain.ParseObjectives` 改成直接问并行会话新加的 `QuestObjectiveCodec`（语法只留一份权威，不认识的目标串一律接不到），并补了"编辑侧编得出 → 接任务侧必须收得下"的 8 条对账；`CharacterGrain.GetCharacterInfoAsync` 的日志在无存档时解引用 null dto（施放路径传的是 null），已修。**还有一处测试侧的同款自洽**：`Live_QuestChain_...` 原来把生产 handler 的调用顺序手抄一遍、两端都用 `ForEntity`，所以它对键分岔天生免疫——已改成与修正后的 handler 一致（背包 `ToInventoryKey` + 好感 `ForEntity`），并在注释里写明"两端同键"正是这类缺陷的照不出来。另两条施放路径的取证仍是跳过，但跳过改成**自证**的（断言挡住的确实还是那句"施法者已死亡"，#24/#28 一变就红并指回本行）。本轮全量 **3146 通过 / 0 失败 / 8 跳过**（含 11 条 live）。 |
| G14 动态条件的**数据侧**全量审计（91 行逐行，不只代码支持） | ✅ 已钉 | §14 那套动态条件此前只有"判定代码 + 挑几行实机"的证据；本轮补一条把**整张种子**钉死的测试 `Seed_DynamicGates_HoldThePerGradeMappingForAllNinetyOneRows`：白/黄 30 行必须**不带**任何好感/材料/属性门槛（初级只靠任务与刷怪，这正是 §14.2 的语义）；玄阶 15 行门槛必须是**本域**好感且 ≥20；地阶 15 行必须是材料且不带好感；天阶 15 行材料与好感（≥55）双限；道阶 15 行材料+好感（≥75）+基础属性+`consume` 回指两式+`loopNeed>0`（"技能树完整性"在这里）；混沌 1 行要求 `favorNeed.allDomains`、`needPairsCompleted>=3` 与跨全域材料。并按阶汇总断死分布：好感 46 / 材料 46 / 属性 16 / 演化回指 15。顺带对每一行断一次"不许再出现 `professionNeed`"（门派限制的最后一道栅栏，与 `NoSectGateTests` 那条代码侧护栏配对）。<br>**为什么值得单独钉**：生成器自己有同类自检，但它跑在"有人手改种子"之外——种子被改一位数字，表现就是某一阶的书忽然不需要好感就能读，玩法梯度塌掉而没有任何一行日志会提。测试首跑即绿，说明当前 011 与 §14 完全对齐（不是空转：门槛缺失会让对应的 `case` 分支先红，分布总数也会从 46/46/16 掉下去）。<br>**同轮另一处修复的验证**：施放死亡判定的三条脚手架断言（真死仍挡 / 上限为 0 不挡 / 无角色信息不挡）通过——第一条若把 `MaxHealth > 0` 的条件回退掉就会红。本轮全量 **3148 通过 / 2 失败 / 8 跳过**，两条失败**都是同一件事**：并行会话给 `IQuestGrain.AcceptFromTemplateAsync` 加了 `characterLevel` 参数，而跑着的 Silo 是旧构建，稳定报 `TypeLoadException`（任务 #24 已从"让改动生效"升级成"解这两条红的必要条件"）。 |
| G15 生命上限基值收口（"施法者已死亡"的上游） | ✅ 已钉（数据落库仍欠） | 顺着 G13 那条诊断往上游走，事实是这样的：`Game_HunduShijie_Character` 的 `health / max_health` 对现有角色全是 0，**没有任何路径把聚合后的血上限写回那一列**；建号时写的是 `100f`，而客户端预览镜像 `_maxHealthBase = 1000f`，`CombatGrain` 缺省又另写一份 `100f`。同一个"生命上限"存在三个数，后果不是显示问题：服务端的死亡判定、复活、克制结算全在读一个不存在的血池，客户端却显示满血。收口三处：`AttributeDerivation.HealthBaseValue = 1000f` 成为唯一定义点（与蓝的 `EnergyBaseValue` 同结构同量级），建号默认改用它，`CombatGrain` 的画像初始化改成"**表里 (0,0) 是没写过，不是上限为 0**"——按推导出的满池起步，同时保留"打过之后 HP 由裁决写回"的旧语义。防回潮两条：`Client_And_Server_Share_One_HealthBase` 直接读客户端源码正则取 `_maxHealthBase` 与常量对账（改名会先红并说明为什么）；`ServerSide_HealthPoolIsNotHardcodedAnywhereElse` 扫 grains 目录，再出现 `MaxHealth = 100f` 这类硬编码即红。本轮全量 **3150 通过 / 2 失败 / 8 跳过**，两条失败仍是 #24 那对 `TypeLoadException`，与健康池改动无关。<br>**没做完的那半要说清**：这轮只是让服务端不再拿"缺失"当"死亡"，**并没有把属性权威写进 `max_health`**——那需要真正的落库路径（在 enter-game 或属性刷新时写入），涉及角色表数据写入与 `CharacterGrain` 生命周期，建议与 #24 的重启一起排期，别在同一轮里既改判定又落库（两件事都会改变现有存档的可观测行为，同时动就无法归因）。<br>**同日续（把算法收成一处并补了状态侧兜底）**：新增纯函数 `AttributeDerivation.DeriveMaxHealth(加点, 表里已写值, 装备加成)`——基值（或"那一列被权威写过"的正数）+ 加点加成 + 装备，`CharacterGrain.EnterGameAsync` 在状态上限 ≤ 0 时按它补一次池子（并按满池起血、置 `IsAlive`），只写状态**不落库**，所以下一步做落库时仍可独立归因。配套 4 条断言：基值两端对账、grains 里不许再出现 `MaxHealth = 100f`、推导逐值（含"表里已写就不再叠基值"这条双重计算陷阱）、以及**"这个纯函数真的有消费者"**（扫 `CharacterGrain`/`CombatGrain` 是否引用它——本仓已经因为"数据里有、代码不判"断过两次链，算法搬进纯函数却没人调是同一类事故）。本轮全量 **3152 通过 / 2 失败 / 8 跳过**，两条失败仍是 #24 的 `TypeLoadException`。 |
| G16 客户端产物与服务端种子的跨产物对账（门槛显示不是第二套真相） | ✅ 已钉 | §14 的可玩性全押在"缺什么提前说"上，而客户端读的是 `skills.json`、服务端判的是 011 落的技能表——两份都是生成器输出，但**没有任何东西保证它们不分叉**。分叉的表现不是报错：树页说好感够了、读卷却被拒；或树页根本不显示某项要求，玩家不知道要做什么。新增 `ClientSkillsJson_AgreesWithTheSeedOnIdsLevelsGradesAndEveryGate`：91 个 id 集合两两相等；逐行断 `requiredLevel == learn_level_require`、`grade == skill_grade`、`bookItemId == 800000+技能id` **且这件秘籍真的存在于物品种子**（点书认得出学哪一式）；再按阶断门槛族是否真的带到了客户端（玄=好感、地=材料、天=双限、道=三类+`consume` 两式+`loopNeed`、混沌=`needPairsCompleted`+三类），并断带门槛的行数恰好 61（玄地天各 15 + 道 15 + 混沌 1）——这条让"逐行扫过"不能靠空转变绿。首跑即绿说明两份产物当前完全对齐；非空转由 61 与逐阶断言共同保证。<br>本轮全量 **3152 通过 / 3 失败 / 8 跳过**：两条失败仍是 #24 那对 `TypeLoadException`，另一条是 `DsWorldScaleTests` 的 tick 预算墙钟抖动（隔离单跑绿的既有已知项）。 |
| G17 显示链的消费者闸门（门槛不是只躺在 JSON 里） | ✅ 已钉 | E8 说过"客户端会提前显示缺什么"，此前只有代码在、没有闸门。`WuxingClientGateConsumerTests` 钉三段：`SkillDatabase.cs` 必须解析六族原始键（`favorNeed/materials/attrNeed/loopNeed/needPairsCompleted/evolveFrom`）、`WuxingSkillTree.cs` 的门槛文案必须**消费**解析出来的模型属性（只解析不用 = 玩家看不见）、`SkillTreePage.cs` 必须真的调用 `DescribeBlockers`（否则前两段是死代码）。三条通过，且是从磁盘实读——任何一族在显示侧掉队都会红并点名是哪一族。<br>与 G14/G16 的分工：G14 钉"服务端判定按阶带门槛"，G16 钉"两份产物一致"，G17 钉"玩家那一侧真的看得到"。§14 那句"纯动态条件增加可玩性"要成立，缺任何一段都会退化成读卷被拒才知道缺什么。 |
| G18 "源式圆满"必须有玩家走得通的入口 | ✅ 入口已落地（成本口径待定） | §6.2 的演化前置写着源式要练满（`Level == MaxLevel`），服务端 `SkillGrain.UpgradeSkillAsync` 也早就支持逐级往上——**但客户端零发送方**：整棵树只能"习得"不能"精研"，于是"超越天阶"那一段（道阶/混沌）的前置要么永远卡住，只能靠 GM 指令。新增 `SkillUpgradeHandler`（发 `UpgradeSkillRequest`，回包只当"重拉权威目录"的信号，不自演算等级；`InFlight` 闸门防连点，发送失败自己清标记否则按钮永久锁死）；树页加「精研」钮 + 权威等级显示（`第 x/y 重`，`MaxLevel<=0` 与服务端同口径视为不可再进并显示"已圆满"）。<br>网关回包对"未习得/已满级"都只回一句"技能升级失败"，不区分——所以满级在点按钮前用权威快照拦下并另发文案。<br>**编译已用规定路径背书**（`scripted-spawn-preflight.ps1 -ClientCompile`：`SkillTreePage.cs` 已从错误名单消失）。注意该路径吃的是编辑器生成的 `Game.CSharp.response`，**新文件不会自动进清单**：本轮把 `SkillUpgradeHandler.cs` 手工追加进 `HundunWorld/Cache/.../Game.CSharp.response`（编辑器下次生成工程会自行覆盖，无需回滚）；剩余 14 条 `QuestClientService` CS0234 是并行会话的新文件同因缺清单，不属于本轮改动。<br>**仍待用户定口径**：精研成本（`UpgradeSkillResponse` 的 `ConsumedGold/ConsumedItems/ConsumedExperience` 三字段已在协议里、恒为 0）。定了只需在 `SkillGrain` 加校验，入口与回包都不用再动。 |
| G19 "源式圆满"在树上不能只对玩家隐身 | ✅ 已钉 | G18 打通了推进（精研），但**显示侧还漏着同一条规则**：客户端 `WuxingSkillTree.DescribeBlockers` 对演化源只判"学没学"，服务端研读路径却要求 `Level == MaxLevel`（`SkillGrain.cs:207`）。于是"两式都学了、各练到一半"的道阶式在树上显示成**条件已具，待研读秘籍**，玩家拿书去读才被一句"演化源未圆满：X（L/M）"退回。不亏书（门槛未达不扣卷，`LearnFromBookAsync` 的注释就写了这条取舍），但 §6.2 这条前置等于对玩家隐身——正是 G16/G17 那一类"数据/判定都有、显示少一维"的退化。<br>修法：新增单一口径函数 `EvolveSourceMaxingBlocker`（`MaxLevel<=0` 与 `Level>=MaxLevel` 都算圆满，与服务端逐字对齐），道阶演化文案分报"未习得"与"未圆满（l/m），需先精研"；`MaxedDaoIds`（道阶系计数，混沌终式的"任意 3 对"门槛就靠它）改为复用同一函数，避免两处各写一遍 `<` 号后走岔。<br>闸门：`EvolveSourceBlockerDistinguishesLearnedFromMaxed` 既断文案里有"未圆满"，又断 `EvolveSourceMaxingBlocker` 的引用数 ≥3（定义 + 两处调用），把"口径只有一份"也钉住。该文件 5/5 绿；`-ClientCompile` 错误数不变（仍 14 条 `QuestClientService`，属并行会话新文件缺 rsp 清单）。 |
| G23 §14 好感门槛的 **NPC 半边**入账口 | ✅ 已落，待网关重启生效 | 原文写的是"与某地区域**生物/NPC** 好感度触发中级技能学习条件"。此前只有生物那半有账（击杀 → `OrleansGameplayEventSink` → `AddAsync`），NPC 那半**根本没有口**：`IFavorabilityGrain.AddAsync` 的注释里连 `npc:10086` 这个 targetKey 示例都写好了，`QuestInteractionAuthority.Evaluate` 也早已能从实体注册表解出服务端权威的 `TemplateId` 与 `Kind`（客户端自报的一律不用），但那份权威只拿去推进任务目标就丢了。我先前"DS 玩法事件只有 Kill/Loot/PlayerDeath，所以 NPC 好感需要先加协议层"的说法**是错的**——不需要新事件类型，现有交互裁决就是那条口。<br>落地三处：① `NpcTemplateInfo` 加 `WuxingDomain`，**故意不给默认值**（给了 0，新 NPC 忘记写域照样编过，症状是"和这位说话永远不涨好感"，不报错不显示），铸剑师→金 / 守门人→土 / 游方商队→木，水火两域**有意留白**（击杀/任务/信物三条口已够，别为对称美感硬塞两个空壳 NPC）；② `QuestHandler.CreditNpcTalkFavorAsync`：挂在 `verdict.Ok` **之后**（放前面等于把记账交给客户端自报），并**必须按 `Kind == Npc` 闸**——NPC 与怪物共用同一个整数 id 空间（都落在 `MonsterTemplateId` 字段、只靠 `ScriptedKind` 分流），少了这道闸"砍一只墨麒麟"会被记成"和铸剑师聊过天"；③ 来源上限显式压到 **5**（默认 20 是给击杀的），否则站在 NPC 面前连点二十次就白拿一整个玄阶 `favorNeed`。未登记 NPC 的 `DomainOf` 返回 0 → 不记账（显示仍然给兜底名：显示与记账是两件事）。<br>闸门 `NpcTalkFavorTests`：每个已登记 NPC 域号必须落在 1..5、`DomainOf` 对未登记/越界返回 0、网关侧的类别闸与上限参数存在、且入账点在裁决之后。18/18 绿。<br>**待办**：改动跨 `Horizon.Game.Sim`（`QuestManagerGrain` 也引用它），要让这条口在生产路径生效需再重启一次网关（Silo 侧无 grain 接口变更，但 `NpcTemplateInfo` 记录形状变了，按本仓"引用 Message/Sim 的进程一起重启"的纪律一併重启最稳）。<br>**2026-10-06 早：已重启上线（Silo 40888 / Gateway 36292 / DsHost 12400，端口齐、err 日志 0 字节），并把闸门从源码扫描升级成行为断言。**`GatewayNpcTalkFavorTests` 用真 `DsEntityRegistry` + 假集群跑整条 `RouteHandlerAsync`，钉实际传给 `AddAsync` 的四个值：铸剑师→(1,1,"npc:1",5)、守门人→域5、游方商队→域2；未登记 NPC 与未知实体均不记账。<br>写这条时踩到一个**新形态的"绿不代表测到东西"**：原本用"怪物 + NPC 模板 id"验类别闸，结果红在最后一句——怪物在 `Evaluate` 里就被 `not_interactable` 挡掉，**根本走不到记账点**，所以"没记好感"其实是被提前 return 实现的，类别闸等于完全没测。换成 **Prop（`ScriptedKind=3`，可交互）+ NPC 模板 id**，断"裁决通过但好感没记"，才真钉住 `Kind == Npc`。顺带钉明可交互集合是 Npc / Item / Prop，Monster 与 Zone 不可对话。<br>同日一条 live 抖动：合池 34 条里 `WuxingLootDomainLiveTests.Live_DomainedBeast_RollsItsOwnDomainTable...` 红 1 条，单跑与重跑同组（15 条）均绿；查 Silo 日志确认掉落装配没有失败（只有启动期 client routing 到旧 silo epoch 的正常拒绝 + 一条与五行无关的 FlowerOrder LINQ 翻译错误），判为并发时序抖动，不改代码。 |
| G24 `max_health` 有了写入方（G15 的下半） | ✅ 代码与取证完成，生效等下次 Silo 重启 | G15 只做了"补状态"：上限从 `AttributeDerivation` 算出来灌进 `CharacterState`（Redis 热缓存），而角色表的 `max_health` 列**仍然只有读方**（同 Grain 的 `LoadCharacterRowForProfileAsync` 在读它）。读写分家的表现不是报错，而是"清过 Redis 或换环境的角色上限回到 0"——死亡判定随即把活人当死人，客户端血条却仍是满的。<br>补上写入方 `PersistServerHealthToDatabaseAsync()`：只碰 `max_health` 与 `health` 两列；`MaxHealth <= 0` 时**不写**（写 0 就是我们要消灭的那个值，等于把别的系统算出来的数盖掉）；行不存在只记日志、不代创建角色行（那是建号流程的职责）；挂在两个**低频转移**上（EnterGame 的兜底之后、`ResurrectAsync` 写状态之后），**没有**挂到每次掉血——那是热路径，DB 写不起。<br>取证 `CharacterHealthPersistTests` 4/4：完全复活后行里是 (1234,1234) 且 `Level` 未被顺手改写、半血复活是 (1000,500)、上限未建立时 `Times.Never`、缺行时既不 Add 也不 Update。两个当场踩到的脚手架坑也记下来：`CharacterId` 是自动属性不是字段（`InjectField` 会断言失败，要走属性 setter 并断属性存在，将来改名得炸在测试里而不是静默写成 0 号角色）；`dotnet test` 不认 `--no-incremental`，而 obj 里躺着并行会话**已删除**的 `QuestManagerRevocationLiveTests.cs` 会让 CSC 报 CS2001 —— 正解是 `dotnet build -t:Rebuild` 后 `dotnet test --no-build`，且 `--no-build` 必须复用同一 `BaseOutputPath`（换了目录就找不到程序集，输出还一句不提）。<br>顺带一条与五行无关但会咬人的实测：`CharacterEntity` 没有 `UpdateTime` 列（只有 CreateTime/LastLoginTime/DeleteTime），照抄别的 upsert 会编不过——别拿 CreateTime 冒充更新时间。全量 **3182 通过 / 0 失败 / 8 跳过**。 |
| G25 效果数据与引擎能力之间的**账本**（一条硬事实 + 一份可执行的挂账） | ✅ 已钉（实现仍待做） | 普查方法：从库里 `OPENJSON(skill_effects)` 把 91 行的键去重，得 **69 个键**；再按单词边界在四个服务端工程（Grains/Core/Sim/DsHost）与客户端工程里逐键找读者。结论按数量：<b>引擎真结算的只有 1 个（`pen` 破甲）</b>，门槛类 4 个（`grade/gradeStage/loopNeed/needPairsCompleted`，注意 favorNeed/materials/attrNeed 属于 `learn_cost` 那一列的词表，不在这里），表现类 6 个（`rangeType/aoeRadius/maxTargets/spread/dash/stack`），<b>其余 59 个没有任何读者</b>——包括 `stunSec/rootSec/slowSec`（控制）、`dotRatio/dotSec/tickEvery`（持续伤害）、`healPct/shieldRatio/reflect`（治疗护盾反射）、`dispel*/pierce/knockback/tauntSec/immuneSec/vulnerability…`。<br>这条硬事实直接压在"制作全套技能·增加可玩性"上：**技能详情里写的定身、DOT、护盾目前不会在游戏里发生**（伤害/冷却/射程/耗气/倍率是真的）。症状不是报错，而是"照着说明玩，发现效果没出现"。<br>这一轮做的是把落差变成**可枚举的账**而不是当场实现：`Horizon.Game.Sim/Skills/WuxingSkillEffects.cs` 给 69 个键逐一登记角色（Applied / Gate / Presentation / Unresolved），并提供 `KeysIn / UnknownKeysIn / NotAppliedKeysIn`。契约测试 `WuxingSkillEffectContractTests` 5 条：先从 011 反解出效果行并**断言行数 ≥91**（锚点错了这条会空跑——本仓反复踩过，所以先钉形状），再断"数据里的键全部登记过"与"登记的键在数据里都真出现（无孤儿）"，再加未兑现清单与 Applied 集合各一条。<br>实现一条效果时的动作：把那一行从 `Unresolved` 挪到 `Applied`，并让它真被结算；反过来，往种子里加新键而不登记，这条测试会直接红。写表时踩到的一处语义修正也留在注释里：`NotAppliedKeysIn` 不能把表现类键算成缺口（形状与位移本来就由客户端兑现），只算 `Unresolved` 与未登记的键。 |
| G26 账本兑现第一条：`healPct` 真的回血了 | ✅ 已实现并取证（待下次 Silo 重启生效） | 没有停在"记账"上——把第一条从 `Unresolved` 挪进 `Applied` 并真接上权威结算：`SkillGrain.ApplySkillHealAsync` 在施放成功前按 `healPct`（占生命上限的比例）调 `ICharacterGrain.RequestHpChangeAsync(amount, casterId, World.DamageType.Healing)`，**只补施法者自己**（`toAllies` 的目标语义还没实现，绝不能"顺手也奶一下目标"——那是把没做的功能伪造成做过了）。生效量取服务端返回的 `ActualDelta`：满血时被挡掉的那部分不能算成治疗量，否则血条没动、回执却写"回复 120 点"，这种假反馈最难查。实现侧不猜数值（`healPct` 缺失 / ≤0 / 非数值 / 上限未建立 → 什么都不做）。回执把真实数字带进 `Message`（"技能施放成功，回复 120 点气血"），于是 PIE 目视有一条可核对的数字，且**不需要改协议**。<br>库里声明 `healPct` 的共 8 式（`OPENJSON` 实测）：木 5（20103 落花 / 20203 枯荣 / 20301 回春 / 20501 布泽 / 20502 荣枯）、水 1（30301 濯缨 0.10）、火 1（40503 凤凰涅槃 0.30）、道阶 1（70201 润木成林·沐川 0.18）——生发之木占大头，语义对得上。<br>取证 `SkillHealCastTests` 7 条（脚手架打真 `CastSkillAsync`，断的是**跨 Grain 调用参数**而不是文案）：2000 上限 ×6% → 请求 120 且类型为 `Healing`；没配 `healPct` 的式一次都不碰 HP；满血时请求照发但文案回到普通"施放成功"；上限未建立时完全不发；比例 0 / 负数 / 非数值三种坏数据都当"没配"。<br>**两条被测试当场抓出来的自错**（都值得记住）：① `Dictionary` 初始化器里 `["healPct"]` 写了两次（Applied 在前、Unresolved 在后）——**后者静默覆盖前者且不报错**，账本于是退回"没实现"；抓它的是契约测试里 `Assert.Equal(["healPct","pen"], AppliedKeys)`（精确钉集合，断"非空"是抓不到的）。② 第二次 `character.Setup(...)` 会顶掉第一个的记录闭包，忘了在新 lambda 里继续 `hpCalls.Add`，断言就红成"根本没调用 HP"——两件不同的事被混成一条假失败。另外 `DamageType` 仓里有两份（`Horizon.Game.Message.Enums` 与 `Horizon.Orleans.Interface.World`），这里要的是后者（`Healing = 4`）。全量 **3195 通过 / 0 失败 / 8 跳过**。 |
| G27 账本第二条：`hpCost`（以血为引）真的扣血，且血不够就拒绝施放 | ✅ 已实现并取证（待下次 Silo 重启生效） | 接着 G26 再兑现一条，而且挑的是**不需要新协议、不需要 buff 容器、也不碰并行会话正在改的 DS 结算文件**的那条：`SkillGrain` 拆成**纯函数计划 + 权威执行**两步 —— `TryPlanSkillHealthFlow(maxHealth, currentHealth, effectsJson, out flow, out refusal)` 只算"该付/该回各多少"，`ApplySkillHealthFlowAsync` 把它们作为**两次独立的** `RequestHpChangeAsync` 打出去（代价 `World.DamageType.Internal`、回复 `Healing`）。<br>四条口径都是被要求写下来的那种：① **代价会把人打死就拒绝施放**（"以血为引"不是自杀按钮；扣完正好 0 也算不够，因为 0 血在权威那边就是死）；② 血上限还没建立时**既不收费也不拦人**（拦了就是让 G15 那个"不存在的池子"反过来惩罚玩家）；③ 实现侧不给默认值，比例缺失/≤0/非数值一律当 0；④ 同一式同时有代价与回复时**不净成一个数**：净算会让回执说不出"付了多少、回了多少"，还会把抵抗与上限裁剪混成一团。数字仍然只认服务端返回的 `ActualDelta`（被抵抗掉的自伤不能报成"自损 200"）。<br>数据面（`OPENJSON` 实测）：带 `hpCost` 的只有 1 式 —— 40402 火·丹雀衔烛·焚身（0.1）。<br>取证：`SkillHealCastTests` 扩到 14 条，断的全是跨 Grain 参数与拒绝行为：2000 上限 → 请求 -200 且类型 `Internal`；1500 血可付 → 成功；血正好等于代价 → 拒绝且**一次 HP 调用都不发**；上限未建立 → 成功但不收费；被权威抵抗成 0 → 文案回到普通"施放成功"；同时带两键 → 恰好两次调用、顺序先付后回、文案把两个数字都带出来。<br>**这条顺手把 G26 那个自错变成了结构性防线**：契约测试原本只能靠 `AppliedKeys` 精确相等抓字典初始化器的重复键，现在再加一条"键总数 == 数据里去重键数"——重复覆盖会让条目少 1，这条直接钉死。另外那条"Applied 得有代码撑着"的断言被我改成锚在**当前实现名**（`ApplySkillHealthFlowAsync` + 两个字面量键名）：第一次改名重构它立刻就红了，说明这类锚点确实会跟着代码走，而不是写死的装饰。全量 **3200 通过 / 1 失败（`DsWorldScaleTests` 的 tick 预算，隔离单跑 3/3 绿，既有墙钟抖动）/ 8 跳过**。 |
| G28 熟练度/破境是 D1 之前的遗留轴（把"看起来没做完"和"确实已作废"分开） | ⚠️ 需拍板：删除 or 撤销 D1 重立（任务 #34） | 普查这条线的"还有哪些没做"时撞到：设计文档里三处把**熟练度**当核心轴 —— §4"三式圆满（熟练度满 + `skill_realm=3`）是本阶圆满"、§5 表的"熟练上限"列、§13"`book_type=2` 心得书用于境突破"。但代码侧一律不存在：技能熟练度**没有存储**（`GrainStateModels.Proficiency` 那个是**炼丹**的，写方在 `WuxingAlchemyGrain.cs:128`）、**没有上限列**（技能表没这列，011 也没灌）、**没有读者**（客户端 `HundunWorld/Source` 零引用）、`SkillBookEntity.BookType` **全仓零读者**；而 `SkillProficiencyQuery/Update` 三个协议消息还在，并有**两份恒回空字典的处理器**（`SkillHandler.cs:265` 是真被路由的那份，`CharacterHandler.cs:1509` 是不可达副本 —— 它的 `MessageTypes` 明确不认领技能域）。<br>结论不是"这里坏了"，而是**D1（三境各为一式）已经把这条轴替换掉**：圆满的现实现是 `Level == MaxLevel`。所以这一轮做的两件事都是防错，不改行为：① 在那三处旧稿下面各加更正注记（否则下一个读方案的人会去补一条已经废掉的轴，做出第二条"圆满"真相源）；② 在真被路由的那份空表处理器上写清"恒回空表是刻意的 + 要改该改哪儿"，别让人顺手"补个数"。<br>**留给用户的一次决定**（#34 里写清了两条路的具体动作）：A 删掉 `SkillProficiency*` 与不可达的技能域 switch 分支（我判断更贴合已确认设计）；B 撤销 D1、把熟练度与破境重新立起来（那是整套新机制：存储列、上限数据、实战累加方、心得书消费方，还要先回答"熟练度与等级谁作准"）。拍板前不动代码：现在这个空表是"确实没有这份数据"的真话。 |
| G29 部署后复跑：击杀好感的**来源上限**第一次被真集群钉住（并修掉一条运行次数相关的假失败） | ✅ 已取证 | 第三次重启（用户授权；要停的 Silo 是并行会话 14:05 起的那份，不是我起的）把 G24/G26/G27 一起带上线：Silo 16496 / Gateway 12440 / DsHost 36372，11111/7789/19871 在 Listen，带 `HORIZON_ORLEANS_REDIS` 复跑 10 类 live = **24/24 绿**。<br>过程中一条 live 红了：`Live_KillEventThroughTheRealSinkCreditsTheKeyTheLearnGateReads` 报 `Expected: 481 / Actual: 480`，而 `FavorabilityGrants == 1`、`FavorabilityFailed == 0` 都通过。根因不是代码回归，而是这条测试**复用共享存档上的角色 8**：好感按来源累计到顶（同一只怪 `monster:{模板id}` 上限 20），我前面为取证反复跑过多次，该来源已经吃满，于是"再杀一只应 +1"必然随运行次数变成假失败 —— 正是仓里那条"live 打真实 grain 必须自带幂等护栏"的形状。<br>修法不是把断言放宽：① 换成每次一个新的合成角色键（`9_800_000_000 + ticks%100000`，与任务链那几条同一手法），于是"落在研读门槛读的那把键上、不串到背包键上"仍然是**精确 +1** 断言；② 顺手把设计里那条可玩性主张变成实测：连发 25 次同来源击杀后域好感**恰好停在 `FavorabilityGrain.DefaultPerTargetCap`**，再换一只**同域的另一来源**（水域日常怪只有墨麒麟一只，另一只同域模板是水行 BOSS —— 第一版我按"非 BOSS"筛，于是 `Sequence contains no matching element` 直接炸在这里，这也是一条值得记的目录形状）应当继续 +1。两条连续跑都绿，证明它不再依赖运行次数。<br>一处诚实边界：`G24`（血上限落库）在库里目前只看到 **33 个角色里有 1 个 `max_health>0`（character_id=27，值 100）**，不能拿它当"我的写入方跑过"的证据 —— live 套件走的是直接 `ResurrectAsync`，共享角色 8 的上限本来就是 0，而我的守卫是"上限 ≤0 不写"（不写 0 正是设计），所以那条路径**不会**产生库内变化；27 那个 100 更可能来自建号或别处。要真验它需要走一次 `EnterGameAsync` 或客户端 PIE 登录后回读同一行。另外 `healPct`/`hpCost` 也只到"部署完成 + 脚手架 14 条"这一档：施放入口在真集群被调用时才会触发，（另记一条同族现象：合池跑里 `GameNetworkServerSplitTests.LoginRequest_FullPipeline...` 也红过一次，隔离单跑 1/1 绿 —— 它自己 `ReserveTcpPort()` 取空闲端口，不与在跑的网关抢 7789，所以是并发下的时序抖动，与前面两条墙钟族同源；这类红要按"隔离复跑"定性，别去改代码。）|
| G30 好感阶梯的**可达性算账**：顶端那两档只靠信物那条没有来源上限的口 | ✅ 已钉（算的是数，不是印象） | §14 把"好感度门槛"当成中高级技能的动力来源，但**没人算过每域到底能攒到多少**。这一轮把四个口的数字摆出来（全部实测自目录/种子，不是估）：① 击杀——`FavorabilityGrain.AddAsync` 只在**传了 `targetKey` 时**记逐源上限（`DefaultPerTargetCap = 20`），键是 `monster:{模板id}`，而 `MonsterTemplateCatalog` 每域恰好**一只日常怪 + 一只世界 BOSS = 两个源** → 每域 40；② NPC 对话——`npc:{模板id}`，G23 把上限压到 **5**，登记了三只（金/木/土，水火有意留白）；③ 入门任务——`repeat_limit = 0` 即**一次性**（`QuestGrain.cs:211`：`RepeatLimit <= 0` 直接不再提供），给该域 **20**；④ 剧情任务（014）里另有 `favor:1=10`、`favor:3=20` 各写了两遍（UPDATE+INSERT），**按宽算全计**。<br>按宽算出来的天花板：金 85、木 65、水 100、**火 60**、土 65。对照数据里的门槛（`favorNeed` 实测：单域 20/25/30/55/60/65/75/80/85，混沌 90001 是 `allDomains.min = 110`）——**火域连天阶第一档（75）都摸不到，任何域都到不了混沌（110）**。把顶端撑起来的只有第 ⑤ 个口：**信物 910011–910015**。它是唯一可重复的口，因为 `InventoryGrain.cs:317` 那笔账**不传 targetKey**（`// 不记来源上限：信物本身就是一次性消耗品，成本即闸门`），而它的产出走掉落（`item_type=2`、`via` 含 `loot`、`drop_rate=0.1`，所以 `WuxingLootTables.Build` 会把它折进域表）。<br>**为什么这个"没有上限"是承重墙而不是漏洞**：如果哪天有人"为了口径统一"给信物也补一个 `item:{id}` 键，该域立刻被 `DefaultPerTargetCap` 卡在 20——表现是树上最后两档永远学不了、好感涨一点就再也不动，**而这条改动会编译通过、测试全绿、零报错**。所以这轮把它从默契变成断言：`WuxingFavorSupplyCeilingTests` 5 条（全绿，不需要集群）——门槛形状先钉（`favorNeed` 单域 ≥40 条、allDomains 恰好 2 条、最大 85/110，锚点错了就红而不是空跑）；天花板按上面那套算法算出来并**先证明它不是空字典**（五域各有怪、入门任务确实给好感、NPC 恰好 3 只），再断"火 < 75、全域 < 110"；信物那 5 行必须 `item_type=2` + `via` 含 loot + `drop_rate>0` + 带正 delta 的 favor 效果；`FavorabilityGrain` 打**真方法**做对照（同 30 次 +10：带 `targetKey` 停在 20、不带则涨到 300 ≥ 110）；最后一条锚在调用点形状——`InventoryGrain.cs` 里 `favor.AddAsync(...)` 必须**恰好一处且只带两个参数**。<br>一处诚实边界：这条测试**不证明**信物掉落率在实机里"刷多久能凑够 110"，那是数值手感（与 #30 精研成本同一族待用户定的口径）；它证明的是**这扇门没被焊死**，而且今后把它焊死的人会当场看到一条红。另记一条算账时用到的事实，免得下次再查：`FavorabilityGrain.DomainCap = 1000` 是域内总量上限，与逐源上限是两件事，110 那档不受它约束。 |
| G33 招式说明不再把"数据写了"说成"游戏里有"（账本的第一个真消费方） | ✅ 已落，待 PIE 目视 | G25 写下账本时就留了一句"UI 若要避免把『定身 2 秒』写成事实，就用 `NotAppliedKeysIn` 决定哪些键只能当风味描述"——但那一句**长期没有消费方**（全仓 grep 只有定义与我的测试），而实测的落差很具体：客户端读的 `skills.json` 里 49/91 行带 `effects`，类型分布为 `Penetration` 9、`Stun` 7、`DamageOverTime` 13、`Slow` 6、`Heal` 8、`Root` 8、`Shield` 5，其中服务端今天真结算的只有破甲与回复两族（`pen` / `healPct`）。也就是说树页上三十多式的说明在承诺定身、DOT、减速、禁足、护盾，而实机一个都不会发生，症状不是报错而是"照着说明玩发现没效果"。<br>做法刻意做成**最小且可核对**的一处显示：`WuxingSkillTree.DescribeEffects(cfg)` 在原文后面补一行"（实装中，暂未生效：定身、持续伤害）"，页面 `DetailLine` 的 `default` 分支从 `cfg.Description` 改调它（详情区那行是 `TextWrapping.WrapWords`，多一行不会截断），未生效名称由 `PendingEffectLabels` 表驱动而不是写死句子。<br>因为"哪些已结算"这个事实于是存在于两处（服务端账本 + 客户端镜像表），照本仓既有先例（世界 BOSS 表基址在 Sim/Silo 各写一次由测试兜住）加了三条常驻闸门 `WuxingClientEffectHonestyTests`：① 客户端 `SettledEffectTypes` 与 `WuxingSkillEffects.AppliedKeys` **一一对应**（映射表里还留了一条强制说明：服务端新记一条 Applied 而这里没决定它算什么效果类型，测试直接点名要求表态）；② 页面真的消费了 `DescribeEffects`（否则①是死代码，同 G17 的第三段）；③ `skills.json` 里出现的每个效果类型都得有归宿——已结算或在待生效登记表里有中文名，新增类型忘了登记会当场红（这条防的是"静默地完全不提"）。<br>取证：`WuxingClientEffectHonestyTests` 3 条 + 计划层 10 条 + 键契约 6 条 + 好感可达性 5 条 = **24/24 绿**；客户端编译按唯一规定路径背书（`scripted-spawn-preflight.ps1 -ClientCompile`：**client sources compile clean**，578 条响应清单，未新增文件所以不必动 rsp）。<br>**留给 PIE 的一条目视**：选中 20101 青藤三式·缠枝（DOT）或 10301 太白经天·流霜，说明块末尾应当出现"（实装中，暂未生效：持续伤害）"一类字样；选中只做破甲的 10101 应当**没有**这行——反向对照也成立才算这条真的在跑。另注：这一条只是把落差说清楚，**不等于**效果已实现；账本里那 57 个键仍按 §15.5/§15.6 的选路待接线。 |
| G32 客户端产物里**早就有一个没人结算的等级收益承诺**（`damagePerLevel`）：键名已对齐，幅度必须一次拍板 | ✅ 已查，待拍板 | 给 #30 写 017 之前先普查了两份产物，结果比预想的麻烦：`HundunWorld/Content/GameData/Skills/skills.json` 的 91 行**每一行都带 `damagePerLevel`**，值是 0.11/0.12/0.13/0.14/0.15/0.16/0.17（按 `skill_grade` 一档加 0.01，共 15/15/15/15/15/15/1 行）。也就是说"每重 +11%~+17% 伤害"这个承诺**早就写在客户端读的那份文件里**了。<br>但它今天不成立：`SkillConfig.DamagePerLevel`（`SkillDatabase.cs:106-107`，默认 0.1f）在客户端**零读者**（除 DTO 反序列化外全仓无引用），服务端也不读 `level_effects`（91 行全空），伤害里 `Level` 依然没有任何消费方。所以那不是一个"已经生效但没同步"的功能，而是**一份谁都没结算过的占位文案**——而且那套数是生成器按阶位递增编的：满重（第 10 重）等于 +99%~+153% 最终伤害，任何一个玩家把一式练满都会让这一式的倍率翻一倍以上。<br>**本轮做的两件事**：① 把服务端读取器的键名**改成与客户端同名**——顶层 `damagePerLevel`（原先我准备用自造的 `damagePct`，那会让同一件事在两份产物里长出一个同义新词，正是本仓反复要避免的双真相源；`WuxingStatusEffects.ReadLevelDamagePct` 与 `WuxingStatusEffectPlanTests` 已同步改名，16/16 绿）；② `017_seed_skill_level_effects.sql` 的头部把这份既有字段**写进去并标为待决**（仍是只生成不入库）。<br>**剩下唯一要拍板的是幅度**，二选一：按客户端已经承诺的 0.11~0.17（练满翻倍，等号右边是"精研是主要成长来源"），或按 017 提的 0.02/0.015/0.01/0.008（练满 +7%~+18%，等号右边是"倍率来自阶位、等级只给一点甜头"）。**定了之后两份产物必须写同一个数**——这条对齐本仓已有先例（G16 的"两份产物一致"闸门），实现时在 `SkillCatalogMapper`/生成器与 `level_effects` 之间加同一条对账断言即可。<br>顺带一条同源发现（写在这里免得下次再查）：`skills.json` 每行还带一个 `effects` 数组（`{type,duration,value,chance}`，例如 `{type:"Penetration",value:0.1,duration:0,chance:1}`），形状与 §15.5 的加法/比率之争是同一类问题——`duration:0` 在现容器的 `IsExpired` 里同样是"永久"。 |
| G31 接 §15 之前先量准容器：**加法 vs 比率**、**寻址 vs 归属**（接线前必躲的两个坑）+ 017 已生成 | ✅ 已查并写进 §15.5/§15.6 | 真去接 §15.4 的第 ① 步时撞出两件文档里看不到的事，都已在正文落成小节，摘要在此：**(a) 加法与比率不是一回事**——`EffectInfo` 的三个载荷向量走的是绝对点数（`AttributeDerivation.cs:43-51` 每点 5 点行防御 / 每点 30 点破甲）与"攻击求和、防御破甲逐行取高"的聚合（`CombatGrain.cs:1001`、`:1013`），而数据说的是比率（`enemyArmorDown:0.3`、`vulnerability:0.15`、`damageTakenDown:0.1`）；更要命的是**减伤真正吃的入参是"只由 Buff 构成的防御"** `skillTargetBuffDefense`（`:253` → `SyncHpToCharacterAsync` 第 6 个实参），基础防御在 `CharacterGrain.RequestElementalHpChangeAsync:1370` 才与 Buff 合并——**在 Buff 向量上乘 (1 − 30%) 对绝大多数玩家等于什么都没做**。三条选路写在 §15.5，推荐 R1：复用 `EffectInfo` 里那个**写着但没人读**的 `Intensity`（`GrainStateModels.cs:203`；:213 的注释早就断言"只有一个 Intensity 浮点不够参与结算"，如今三个向量到位它就是现成的幅度位）当比率载体 = **零状态 schema 变更**，缩放落在"基础与 Buff 合并之后"。**(b) 容器寻址 ≠ 效果归属**——`CombatGrain` 的键是 `GrainKeys.ForEngagement(target, fallback)`，而它的实现就是 `ForEntity(受击方)`（`GrainKeys.cs:28`；我一开始怀疑它是"对战对"键，查了才确认不是）。这使**给对手的减益**与**施法者自己被控**都读得到（都落在归属者的键上），但**给自己的增益**若就地写在 `ProcessSkillCastAsync` 里会落进**受击方**的 Grain——表现是"这层盾对第一个对手有效、对第二个凭空消失"，且 `RefreshElementVectors` 按 `CharacterId` 过滤，两处都不报错。规则写在 §15.6：**效果写到归属者的键上；施法者与受击方分属两键时，两笔账分开下**（自增益由网关处理器另写 `ForEntity(施法者)`，绝不放 `CombatGrain` 回头调自己同类型的另一个键——两人互打时就是真死锁；§14.4 那条禁令换个方向会再踩一次）。<br>另：`scripts/sql/017_seed_skill_level_effects.sql` 已按 #30 的拍板**生成但未入库**（分档 45/30/15/1 行、每重 +2%/+1.5%/+1%/+0.8%，满重分别 +18%/+13.5%/+9%/+7.2%）；谓词是**按编号契约反解**而不是 `BETWEEN 10101 AND 50503` 那种大范围扫（会误伤 10601 这类不存在的行），三档划分与零重叠/零漏行都拿 011 的 91 行离线实测过；结构上避开了 012 的三重假安全（无裸 `IF <条件>;`；不用 `BEGIN TRY` 免得 RAISERROR 跳进 CATCH 报第二遍；自检用**字面量全等**而非 `JSON_VALUE`——后者在别的行上引发过 Msg 13609），形状或内容不对就整批回滚。 |
| G20 基础属性门槛：显示侧读的量必须与服务端比的是同一个 | ✅ 已钉 | 先按字段名 `AttrNeed` 全仓 grep 服务端**零命中**，差点落成"§14 的属性门槛服务端根本没判、只是装饰"这个否定型结论；反查实现名才发现它住在 `WuxingDynamicGates.CheckAttributes`，字段叫 `StrengthNeed/AgilityNeed/HealthNeed/IntelligenceNeed`。<br>**真正的缺陷在显示侧**：`WuxingSkillTree` 把 `attrNeed` **无条件列进缺项**，于是加点早就达标的玩家会看到"暂不可学：基础属性 力量 60"，而服务端此时放行研读（与 G19 材料那一族同一类假缺项）。修法两条：① 够则不列、不够报"已有/需要"（`AppendAttrNeed`），加点数未下行时退回"只说要什么"而不是猜 0；② **取数源换成服务端比的那个量**——`AttributeAllocationService.Instance.Allocation`（加点数），不是面板上的最终属性（基值+装备+加点）。读错源的两种错法都会出现：面板 60/40 看着够而服务端按加点判不足；或加点达标、身上没装备导致面板显示不足。<br>顺带把"等级 30"改成"等级 12/30"（亲和一直是 `当前/需要`，等级此前是孤例）。闸门：`AttributeGateReadsTheAllocationSourceTheServerCompares`。该文件 7 条 + `WuxingSkillTreeTests` 71 条共 **78/78 绿**，`-ClientCompile` 错误数不变（仍只余并行会话那 14 条）。<br>结论性事实（供后续对照）：§14 四族门槛在**服务端**全部有判定（`WuxingDynamicGates.Check` 依次判 好感→材料→属性，另有 `CheckLearnGates` 判等级/亲和/前置/演化源圆满），通道也不再由客户端上报的 `LearnMethod` 决定（`DirectLearnBlocker`：登记过书的行必须持书研读，白阶同样不给绕过）。 |
| G21 好感度门槛的"现在有多少"（§14 显示侧最后一族） | ✅ 代码已落，生效等 #24 那趟重启 | 四族里只剩 `favorNeed` 不能报进度：好感只活在 `FavorabilityGrain`，没有任何下行通道，于是树页永远只能说"火域好感需 60"。做法刻意选小：**不新增查询接口**，把快照挂在技能目录下行上（打开树页本来就拉一次，另开一次查询只会多一个不同步的帧）——`SkillCatalogSyncMessage.FavorByDomain`（order 11，可空字典），网关在 `HandleSkillCatalogSyncAsync` 里顺手 `GetGrain<IFavorabilityGrain>(GrainKeys.ForEntity(id)).GetByDomainAsync()` 回填。**三处刻意的取舍**：① 键用 `ForEntity` 而不是背包那把大端 Guid —— 这个错配本仓踩过两次（任务发奖、击杀好感），且它不报错、只让玩家永远看到 0，所以直接钉成测试；② `null`（没取到）与空表（确认零好感）**必须可分**，否则读失败会被渲染成"你一点好感都没有"、新角色反而被渲染成"读不到"，为此把字段做成可空且失败时保持 null；③ 好感读失败**不**让整次目录同步失败（那会让树整屏空掉，而判定本来在服务端）。客户端 `SkillCatalogService` 每帧原样接收（不做"上一帧凑数"的合并），`DescribeBlockers` 抄服务端 `CheckFavorability` 的口径：五域那档点名**最差的一域**（服务端也正是按第一个不达标的域回绝），单域报"火域好感 24/60"，达标则整条不列。<br>闸门：`SkillCatalogFavorSnapshotTests` 三条（过线后 null/零 仍可分、网关读的是哪把键、客户端真的消费了）。
  **同日晚自纠一处**：客户端 `allDomains` 那档我先写成"点名数值最低的一域"，而服务端是"按金→土扫到的第一处不足"就返回——两者在"最差的恰好最先扫到"的数据上看着一样，语义却不同（玩家会拿着"火不够"去刷火、其实卡在金）。现改成与服务端**同序取第一处不足**；同时把那条原本名叫 `ReportsTheWorstDomain`、喂的却是"first==worst"数据（因此钉不住语义）的服务端测试，换成 `first≠worst` 的夹具（`[1]=50,[4]=60` 先报金、补金后报火），把"顺序"这条真正钉死。首跑那条键断言就红了——它锚在文件里第一个 `FillFavorSnapshotAsync`（那是调用点），改成锚方法定义后才对上；这正是"闸门会咬人"的证据。<br>**部署**：本轮改了 `SkillCatalogSyncMessage`，按本仓协议变更规矩 Silo+网关+DsHost 等所有引用 Message 的进程要一起重启（正好与 #24 同一趟），客户端编译期靠 preflight 的引用重定向已验，运行期还要 Flax Tools 的 DLL 提权分发一次。全量 **3165 通过 / 0 失败 / 8 跳过**，`-ClientCompile` 回到 14 条基线（全是并行会话那批 `QuestClientService`）。 |
| G22 演化封存在施放入口必须有**常驻**断言 | ✅ 已钉 | D2 定的是"演化换代即封存源技：不可施放、不显示、技能栏清空；散功自动解封"。施放侧代码确实做了（`SkillGrain.CastSkillAsync` 在习得校验之后立刻问 `ComputeRetired()`，回"该式已融于演化式，需先散功"），但**这条规则此前只有 `SkillEvolutionLiveTests`（环境门控，缺 Redis 直接 return）覆盖** —— 也就是说日常回归里它等于不存在。少问一次 `ComputeRetired()` 的后果很安静：树上格子画着斜杠、技能栏也清空了，母技却照样能放，"换代即让位"的地基就这么漏掉，而且没有任何一条会跑的红会报。<br>补 `SkillCastRetirementTests` 三条（与 `SkillCastDeathCheckTests` 同脚手架，不需集群）：① 源技被挡且文案对得上；② **演化式自己不挡**（防止集合把演化式自己或整行吞掉——换代后一个都放不出来是另一半错法）；③ **无关式不受牵连**（派生集合按 `consume` 精确匹配）。断言口径刻意收窄成"只看封存与否"，冷却/射程/目标等原因被拒都算通过，避免越界断言造成假红。 |
| ↳ 四条实机测试的互斥口径 | ✅ 全量单跑 3042/0 | 四条 live 测试共用同一个一次性角色，封存/习得状态互相叠（`"Wuxing live cluster"` 串行只解决并发，不解决顺序）。因此演化测试的封存断言改成**按规则算**：从目录下行里读每个已学演化式的 `learn_cost.consume` 求并集，再逐式断"被封的必须拒、未被封的不许牵连"——把"某一式此刻没被封"写成快照，会在混沌测试跑过之后假失败；把"整行都该封"写死，则是过度封锁。混沌那条的负向断言加了"仅当混沌尚未习得时可观测"的守卫（研读会先回"已习得此式"），正向路径每轮都验。 |
| G2 演化即换代的实机端到端（D2 的核心卖点） | ✅ 真集群跑通 | `Live_EvolveToDaoSealsBothSourceRowsAndUnlearnRestoresThem`：在同一一次性角色上把金、水两行从白阶一路读到天阶（30 式，等级门槛 300、亲和、材料、好感全由服务端判定放行），六式天阶练满 → 推足 12 次周天圆满（`GrantCycleAsync`，5 层一轮）→ **道阶·初 70101 习得成功**（"超越天阶"这条路第一次在真实 Grain 上走通）。换代语义按数据真值断言：道·初的 `consume=[10501,30501]`，所以这两式施放被 `"该式已融于演化式，需先散功"` 挡下，而**未被融合的 10502 不受牵连**（把整行六式都算成封存是过度封锁，是我一开始写错的口径）；`UnlearnAsync` 散功后两式自动可施放，重新演化又能封 —— 证明封存确实是派生态、没有僵尸标记。取证过程中钉住三条实机注意事项（见记忆 live-grain-tests-need-idempotency-guards）：施放冷却判定早于封存判定，上一轮残留会伪装成"机制没生效"；重复研读回"已习得此式"要归一化成成功；同角色的 live 测试必须并进同一 collection 串行。全量回归 **3040 通过 / 0 失败 / 8 跳过**。 |
| F2 任务清单下行（1383→1384） | ⚠ 服务端已答，客户端页面未消费 | `QuestListRequestMessage/QuestListResponseMessage` 与客户端 `QuestMessageHandler`（等 `QuestListResponse`）一直都在，缺的只是网关侧处理方——客户端问了没下文。现补 `QuestHandler.HandleQuestListAsync`：只读 `IQuestGrain` 的进行中/已完成，摘要进度按"已完成目标数 / 目标总数"给（多目标结构不在这里展开，免得摘要与详情两套口径分叉）。**没做的那半**：`MenuQuestsPage` 目前是设计稿的静态复刻（6 条写死的任务、无交互、不发消息），把它改成真服务端任务页（拉列表 + 接取/交付按钮 + 布局）是一个完整特性，不该半桩式塞进来。**好感门槛的实机端到端已在 G1 补做**（同一条阶梯测试），当时卡住它的是"没有等级写入方"与"加点与判定读两份数据"。 |
| ↳ 五行信物（好感第二条来源） | ✅ | 好感不能只挂在任务上：种子加了 5 件 `item_type=2` 信物（910011-910015），`use_effect={"kind":"favor","domain":n,"delta":m}`，由 `InventoryGrain` 的使用咽喉解析后交给 `FavorabilityGrain.AddAsync`。它同时是该域怪/任务的产出物，所以"与该域的往来"是有去处的：交出去换好感，而不是攒一堆没用的物品 |
| E5 刷怪掉落 | ✅ 装配已实机跑通，只剩内容映射 | 启动装配 `WuxingLootTableSeedService` 在 `ApplicationStarted` 后用 `IServiceScopeFactory`（单例 HostedService 不能吃 scoped `IDataContext`）读物品表，纯函数 `WuxingLootTables.Build` 折成"表 ID=五行域"的 `LootTableConfig`，再按接口 `GetGrain<ILootTableGrain>(domain)` 注入。实机证据：`五行域掉落表装配完成：5 张 / 75 条产出`，且 75 与库里按同一规则独立算出的期望值逐域吻合（每域 12 本可刷的书（白/黄/玄/地 4 阶 × 3 境）+ 2 灵材 + 1 信物 = 15 条）；天阶走 `worldBoss`、道阶靠 `evolve`、混沌靠世界 BOSS，都不在这 15 条里，是规则而不是漏。**通道闸门读 `source_detail.via` 而不是 `source_type`**：需求要的是"初级书既能在任务里拿到、也能刷怪掉"，而 `source_type` 是单值列（1=任务 2=战斗），拿它当闸门会把白/黄 30 本初级书全挡在掉落表外——"刷怪获得初级技能书"退化成一句空话，而"什么都不掉"在日志里没有任何痕迹。`source_type` 因此退回粗粒度标签，目前零读取方。接线落点已存在且只有一个：`IMonsterLootMapping.ResolveLootTableId(monsterTemplateId)`（`DsGrainContracts.cs:67`），默认实现 `IdentityMonsterLootMapping` 是"表 ID=模板 ID"。**这条链今天静默空转**：本方案的表 ID 是 1..5，怪物模板 ID 远大于 5，所以一律查不到表、掉落恒为空——不是 bug（宁可什么都不掉也不掉错域），但"装配 75 条"离"怪掉得出书"就差这份 `monsterId→domain` 映射。三种可选形态：怪物模板加一列 `wuxing_domain`／单独一张映射表／约定模板 ID 某段按域分（最省事也最容易被后人忘掉）。**但"怪物模板"这一层在本仓库里其实还不存在**：Game 库 `INFORMATION_SCHEMA` 里 `%Monster%/%Creature%/%Spawn%` 零张表，DS 侧的怪物只是刷怪配置里的一个 `MonsterTemplateId` 整数（`DsHost/Program.cs` 甚至写的是 `1 + (i % 3)` 合成值）。所以用户选的"加一列"没有可加的对象——先要决定怪物模板落在哪张表（新建怪物模板表 = 一张表的完整生命周期），否则映射只能挂在"单独一张 `monsterId→domain` 表"这一种形态上。不做按 id 取模猜域——猜错的表现是"金域信物从火域怪身上掉"，任何报错里都看不出来。**（此段结论已被 G8 纠正：代码侧的 `MonsterTemplateCatalog` 就是怪物模板层，"加一列"有确切落点，映射已接通。）** |
| 客户端门槛显示（E8） | ✅ | `SkillDatabase` 解析 `skills.json` 的 `learnCost`（`loopNeed/needPairsCompleted/favorNeed/materials/attrNeed` 全套 DTO），`WuxingSkillTree.DescribeBlockers` 据此出文案："金域好感 12/40""缺锐金砂×6""体魄 20/36"。树页的「习得」按钮把服务端回执里的 `MaterialItemIds/Counts` 一并显示，所以缺什么以服务端判定为准，本地只做可读性 |
| 编译验证边界（已被后续覆盖） | ✅ | 本轮曾记录 `Horizon.Game.Core` 22 条 `CS0117/CS1061`（`ScriptOp.Grant/Revoke/Audit`、`GmScriptMessage.TargetOperatorId` 等）——那是并行 GM 脚本 WIP 未落地的符号，本方案没有替他们定义 API。他们的改动落地后 Core 自行转绿，无需本方案改动配合 |


**途中修掉的五个真实缺陷**（都不是顺手美化）：
1. 生成器原本输出 `{ "skills": [...] }`，而 `SkillDatabase.TryLoadFromJson` 反序列化裸数组 —— 会解析失败并静默退回 26 条硬编码，整套技能树看起来"没生效"。**（2026-10-06 反转：现在文件必须是对象根 `{ "Skills": [...] }`，读取端改成 `SkillCatalogFile`。原因是 Flax 编辑器启动时会把 `Content` 下每个 `.json` 当 JSON 存储资产探测 `ID`/`TypeName` 成员，`JsonStorageProxy::GetAssetInfo` 走的 rapidjson `FindMember` 带 `IsObject()` 断言 —— 数组根不是返回"不是资产"，是弹 Assertion failed 崩溃框并写 Crash 目录。引擎侧同时补了 `if (!document.IsObject()) return false;` 守卫，需重编 FlaxEditor 才生效；在那之前，`Content` 下不要放数组根的 json。）**
2. `Initialize` 原本是"JSON 成功就跳过内置表"，叠加新目录后旧 id 会从目录里消失；已改为叠加语义。
3. **种子的 UPDATE 分支漏写产出列**：91 件秘籍在库里早就存在（007 种子），所以 011 走的是 `IF EXISTS ... UPDATE` 而不是 INSERT，而那条 UPDATE 只列了名称/等级/`use_effect`，没有 `drop_rate/source_type/source_detail`。后果是"种子 exit 0、客户端目录逐行对得上、技能树看着一切正常，而 `ItemTemplate` 里的 91 件秘籍产出渠道仍是 `source_type=0`"——掉落表里一本都不会有。已补齐四列，并在生成器加了一条自检：任何 `UPDATE Game_HunduShijie_ItemTemplate` 都必须写全这四列，否则直接抛。这条自检的通用形态值得记住：**给已有表加列时，INSERT 分支和 UPDATE 分支是两个独立的真相来源，只改一个不会有任何报错**。
4. **掉落闸门用错了列，而且测试恰好照不出来**：`WuxingLootTables.Build` 原先要求 `source_type=2`（战斗产出），而生成器给白/黄阶初级书写的是 `source_type=1`（因为它们的 `via` 同时含 `quest` 与 `loot`，单值列只能记一条）。于是"刷怪获得初级技能书"这条明确写进需求的路，在实现里是**永远为空**的。更糟的是两个测试都绿：单测里我手写的白阶书行用了默认的 `sourceType: 2`，真数据对账测试干脆没读 `source_type` 列、也走默认值——**测试给参数兜了默认值，就等于把被测的那一列从测试里删掉了**。现在闸门改读 `source_detail.via`（唯一能表达"多产出渠道"的字段），测试改成显式传真值并把每域期望写成精确条数（15=12 书+2 材+1 物），运行态也从 45 条升到 75 条。
5. **玩家加的五行亲和，门槛永远读不到**：`AllocateAttributeAsync` 写的是 `CharacterState.Allocation`，而技能门槛读 `CharacterInfo.Allocation`（`AffinityOf(info?.Allocation, …)`，判定输入也是 `info?.Allocation`）。真集群上实测：分配接口回"加点成功"，紧随其后的研读仍报"五行亲和不足：需 4，当前 0"。两个字段各活各的，表现是玩家反复加点、界面也显示加了，但门就是不开。修在唯一的读取口 `GetCharacterInfoAsync` 上把状态里的分配同步进 `CharacterInfo`，让"谁分配"与"谁判定"共用一份事实——这条是 G1 阶梯测试逼出来的，纯函数层不可能看见（它只喂自己的假 `CharacterInfo`）。

---

## 13.9 交接快照（2026-10-04，按"已验证 / 等你 / 下一步"分）

**已实现且有可执行证据（2026-10-05 复测：全量 3168 通过 / 0 失败 / 8 跳过）**

- 阶梯与全套：91 式（75 基础 + 15 道阶 + 1 混沌）已入库并生成秘籍物品；倍率与 §3.1 公式逐行对账（G6）。
- 相生相克循环：施放路径每次施放都走 `StepWuxingCycle`，§5.1 每条规则有逐值断言（G12）；破势夺元的跨 Grain 编排有断言（G7）；镇岳护体行参与判定（G3）。
- 演化超越天阶：道阶换代封存 + 散功解封 + 再演化实机闭环（G2）；混沌终式的 3 对道阶圆满 + 周天 ≥40 门槛实机拦得住也放得开（G4）。
- 动态门槛四族：好感（任务/信物/击杀三路来源）、材料、基础属性、技能树完整性——每族都有"缺时被拒 + 补齐放行"两侧实机证据（G1/G5）。
- 产出通路：任务模板表为服务端权威 + 击杀目标按五行域限定（F1/G10）；刷怪掉落接通"怪 → 域"这一列并自愈持久性（G8/G9）；天阶与混沌的世界 BOSS 档补齐（G11）。
- 门派限制：取消，且有护栏测试 `NoSectGateTests`——判定/编排/契约三层里不许再出现 `profession_limit` 标识符（列保留、无人读取）。

**已并入回归（2026-10-05 更新，此前"parked"的前提已经不成立）**
`Horizon.Game.Gateway.Tests/WuxingCastCycleAndKillFavorLiveTests.cs`（相生序**施放**逐层累加 + 击杀事件经真实 sink/writer 只给该怪所属域记账）所在的全仓现在**编译通过、随全量一起跑**，不再是"写完编不过"的状态。它仍是 `HORIZON_ORLEANS_REDIS` 环境门控：不带变量时直接 return 且**在汇总里算通过**（同 G22 那一类"绿不代表跑过"），所以要拿真证据必须带变量实跑，见下面 runbook 第 4 步。
顺带一条协作提示（仍然有效）：实体身份/描述批上报那条链正是"NPC 交互好感"的前置，别在两条线上各建一套。

**等用户动作：一次跑完的 runbook（按顺序，每步都给了"怎么算成"）**

0. **口径待拍板（数值题，不拍就会一直"能做但没意思"）**
   - 精研的**成本**与**收益**（#30）：入口与文案已经在了（树页「精研」+ `第 x/y 重` + "已臻圆满"），
     `UpgradeSkillResponse` 的 `ConsumedGold/ConsumedItems/ConsumedExperience` 三字段恒为 0 等填。
     **本轮查实**：技能等级今天**没有任何战斗收益**——`skill.Level` 在 SkillGrain 只有三个消费方（演化"圆满"判定、
     散功退点、升级顺手改的 `NeiLiCost×1.1`/`Range+0.5`），伤害结算不读它 → 练满的净效果是"更费内力 + 射程远一点 + 能跨门槛"。
     建议与成本一起定（例如每重 `DamageMultiplier +x%`，服务端下发、DS 结算才认）。
   - 世界 BOSS 是常态世界事件还是 GM 脚本刷（#26）：天阶/混沌书的掉落档已经在了（G11），只差刷出口径。

1. **构建**（在仓库根；`global.json` 钉了 SDK，从子目录跑会报 SDK 错）：`dotnet build Horizon.sln`。
   本地验证不想被运行中的 Silo 锁住 pdb：加 `-p:BaseOutputPath=bin/VerifyN/`，别去杀进程。
2. **重启集群（#24）**：✅ **已于 2026-10-05 深夜做完并取证**（停 10-04 的 Silo 35584 / Gateway 39688 / DsHost 15728 → 分别 `dotnet build` 三个工程 → 按 Silo→Gateway→DsHost 用 `nohup ./X.exe` 起回，日志进 TEMP）。
   **取证**：11111/7789/19871 在 Listen、三个 `.err.log` 全 0 字节、网关 0 条 Error/Critical，
   且带 `HORIZON_ORLEANS_REDIS` 连跑 5 类 live **18/18 绿**，原本必现的
   `TypeLoadException: ... IQuestGrain ... 8F533FF0` 消失。
   两条别踩的坑（都是实测）：① **别拿 `dotnet build Horizon.sln` 当"能不能部署"的判据**——它红的项目前
   （`Game.csproj` 的插件源码折叠假错、`Horizon.PerformanceTests` 引用已删除的 `ICharacterGrain.MoveAsync`）
   与这三个可执行无关，逐个 build 三个工程才是正道；② 起进程前确认 `.env` 在被锁释放后仍在原位（Silo/Gateway 各自读自己 `bin/Debug/net10.0/.env`）。
   **一趟生效**：掉落表自愈（G9）、复活真落 grain、死亡判定不再把"属性未初始化"当死亡、相生序施放累层、击杀分域好感记账、好感快照下行（G21）。
3. **提权分发协议 DLL**：✅ **已做完（2026-10-06 凌晨，用户授权后 UAC 提权）**。`scripts/copy-shared-dlls-to-flax-tools.ps1` 跑完后
   `Flax_1.12/Binaries/Tools/Horizon.Game.Message.dll` 与仓库构建**尺寸与 mtime 完全一致**（2626048 / 10-06 01:48）。
   运行期那份（`HundunWorld/Binaries/GameEditorTarget/Windows/x64/Development/`）由编辑器构建自行刷新，**需要重启一次 Flax 编辑器**。
   **判定"客户端能否编译"只认打包好的 `scripts/scripted-spawn-preflight.ps1 -ClientCompile`**（复用 Flax 自己的 response 文件，
   并把协议引用重定向到仓库构建）：`dotnet build HundunWorld/Source/Game.csproj` 会把插件源码一起编进 Game 工程，
   于是 NarrativePro/PCG 的插件侧报错被算到客户端头上——这条误判持续过两天，2026-10-05 才用打包路径纠正。
   **持续两天的 14 条 `QuestClientService` CS0234 已于 2026-10-06 归零，结论要更正**：那不是代码坏了，也不是"要等编辑器重生成 rsp"，
   而是 `Game.CSharp.response` 那份**快照缺文件**——用磁盘 walk 与清单做差集，缺的只有 `Network/QuestClientService.cs`
   和 `Game/Game.Build.cs` 两个；补上前者后立刻 **`client sources compile clean`（578 项，0 错）**。
   `Game.Build.cs` **绝不能**补进去（它是 Flax 规则程序，进脚本清单会炸出 201 条假错——本仓踩过）。
   做差集的正确姿势：拿 `os.walk(Source/Game)` 排除 `bin/obj` 去对 rsp 里的 `"..."` 路径行，别按文件名猜。
4. **带环境变量的 live 取证（三条 filter，一次跑完）**：✅ **已做完并扩到 10 类** —— 带 `HORIZON_ORLEANS_REDIS=127.0.0.1:9379` 连跑
   OrleansGrainLive / QuestKillObjective / WuxingCastCycleAndKillFavor / WuxingLootDomain / WuxingLootTableEnsure /
   SkillEvolution / WuxingChaos / WuxingAttributeGate / WuxingMaterialGate / WuxingCounter / SkillLadder = **24/24 绿**。
   复跑命令（保留给以后）：
   ```bash
   export HORIZON_ORLEANS_REDIS="127.0.0.1:9379"
   dotnet test Horizon.Game.Gateway.Tests/Horizon.Game.Gateway.Tests.csproj -p:BaseOutputPath=bin/VerifyN/ -v q --nologo \
     --filter "FullyQualifiedName~OrleansGrainLiveIntegrationTests|FullyQualifiedName~QuestKillObjectiveLiveTests|FullyQualifiedName~WuxingCastCycleAndKillFavorLive"
   ```
   **怎么算成**：这三条现在应当全绿（今天不带变量实测还有 3 条红，全部是旧 Silo 的 `IQuestGrain` 方法 id 不匹配）。
   不带 `HORIZON_ORLEANS_REDIS` 时它们是 return-即-通过，**不能拿"全量绿"当这一步的证据**（G22 那条自查问句）。
5. **写库（#25）**：✅ **已应用（2026-10-05 深夜）**。回读结果：`entry_quest_rows=5 / with_valid_domain=5 / with_objectives=5`，90001..90005 每行都是 `kill_domain:N`（N 与 `wuxing_domain` 一致）。回退脚本 `scripts/sql/revert_012_entry_quest_objectives.sql`（把 5 行恢复成 `N'[]'`）。
   **顺手记下这条最值钱的一课**：这个脚本第一次真跑就暴露出三重假安全 —— `IF <条件>;` 是语法错误（整批零写入，万幸文件里没有 `GO`）、`IF @@ROWCOUNT > 0` 读的不是"有没有坏行"（永远不成立）、自检又写在 `COMMIT` 之后（真要回滚会报"没有对应 BEGIN"）。已改成自检在提交前用 `IF/ELSE` 决定 `COMMIT`/`ROLLBACK`，并在汇报 `SELECT` 里加了 `with_objectives` 列，让"到底写进去没有"跑完一眼可见。
   **给以后写种子脚本的规矩**：`-i` 首次执行的是**脚本本身**的编译，不是意图；自检必须能在"什么都不写"的干跑里被证伪。
   （015 灵材由并行会话先于本步入库。）
6. **PIE 逐页目视（视觉判定只能你来做，我不启动编辑器）**——本轮新增三项，其余沿用 §13.9 老清单：
   - 树页选一个已习得的式 → 右下出现「精研」，详情行显示"已习得 · 第 x/y 重"；点到上限后按钮文案变"已圆满"。
   - 选一个玄阶式 → 缺项里好感应是"火域好感 24/60"这种带实数的形式；**从没刷过好感的号应显示 0/60**（服务端确认零），
     而网关读失败时才退回"需 60"（不知道就说不知道）。
   - 选一个地阶/道阶式 → 材料与基础属性只在**真的不够**时列出来；备齐后状态行应变成"条件已具，待研读秘籍"。
   - 演化换代后：源技格子画斜杠、拖不上技能栏，且**施放会被服务端挡**（"该式已融于演化式，需先散功"）；散功后自动解封。

**尚未开始的特性（明确不是"做了一半"）**
- 客户端 `MenuQuestsPage` 的任务页展示（服务端任务模板与 objectives 已权威化，页面消费仍归并行会话那条线）。
- NPC 交互好感：DS 玩法事件只有 Kill/Loot/PlayerDeath，**没有交互事件类型**，所以"和 NPC 对话涨好感"这条入账口需要先从协议层起（不是我漏接，是链路上没有这一环）。
- ~~技能"练满"的玩家入口~~（#30 的入口半已在 2026-10-05 落地，见 G18；只剩上面第 0 步的成本与收益口径）。
- 服务端推导的血上限真正落库（#29：`max_health` 至今无写入方，Redis 清空/换环境后要靠重启装配补）。
- 升级曲线与产出节奏的平衡数值（`ProgressionCurve` 是可调默认值）。

## 14. 动态习得条件（2026-10-04 追加需求）

追加口径：**取消学习技能的门派限制**，改由"纯动态条件"决定一式能不能学、书从哪里来 ——
初级靠任务与刷怪，中级靠区域生物/NPC 好感度，高级靠特殊材料，融合靠角色基础属性 + 技能树完整性。

### 14.1 先说清楚现状（这三条决定了工作量与架构，不是推测）

| 事实 | 证据 | 后果 |
|---|---|---|
| 门派/职业门槛**从来没有生效过** | `SkillTemplateEntity.SectId:59` / `ProfessionLimit:66` 全仓零读取方；`JoinSect`/`SectSkill` 只是 `CharacterHandler:1066/1089` 的 echo，不落库 | "取消门派限制"不需要拆判定，需要的是**把已经写进语义里的"门派传授"这个通道名改掉**，并保证新体系不再往 `sect_id` 里塞门槛 |
| 好感度体系**不存在** | `MessageType.Reputation/MasterApprentice/ChivalryPoint` 全是 echo；只有角色级单值 `CharacterEntity.Reputation:192`、`ChivalryPoints:178`，**没有按区域/按 NPC 的维度**，也没有"取某角色对某目标好感"的方法 | 必须新建一套按角色的**分域好感度**存储，不能假装能复用声望 |
| 任务奖励只回发不发放；掉落表与 `ItemTemplate.drop_rate` 脱节 | `QuestGrain.CompleteQuestAsync:221-274` 只 `return Rewards`；`World/QuestGrain.cs:103` 明写 `// TODO: 发放奖励`；`LootTableGrain.RollLootAsync` 用的是内存注入的 `LootTableConfig`，生产链路没人从 DB 读 `drop_rate`（该列零读取方）；`CraftingGrain.CheckMaterialsAsync:101` 是 `return true` 占位 | 三条获取/消耗渠道都要**补真实现**，不是配置问题 |

### 14.2 分层矩阵（阶位 → 书从哪来 / 学之前还要满足什么）

| 阶 | 书的来源（动态） | 额外动态条件（研读时判定，不过就不吃书） |
|---|---|---|
| 白 | 主线/门派任务奖励 + 该域普通怪掉落 | 等级 + 该行亲和（沿用 §6） |
| 黄 | 支线任务 + 该域精英怪掉落 | 同上 |
| 玄（中） | 区域事件 + 好感商店 | **该五行域好感 ≥ N**（与域内生物/NPC 相处出来的） |
| 地（高） | 世界 BOSS 掉落 + 声望任务 | **特殊材料齐备**（研读成功时一并消耗） |
| 天（高） | 稀有掉落 + 高级任务链 | 材料 + 好感双条件 |
| 道（融合） | 不可拾取，由两段弧演化而来 | 材料 + 好感 + **基础属性**（力量/敏捷/体魄/神识）+ **技能树完整性**（源式练满、道阶系对数、周天次数） |
| 混沌（终式） | 世界 BOSS 唯一 | 同上，属性与完整性门槛更高 |

### 14.3 数据契约：全部塞进 `learn_cost` JSON，不加列

门槛是内容数据而不是表结构，加一条门槛就改一次列的做法不成立。`learn_cost` 已经在服务端被
`WuxingEvolveRules.Parse` 读，且已作为原文下行给客户端，所以新增键零契约变更：

```jsonc
{
  "grade": 4, "gradeStage": 2,
  "favorNeed":   { "domain": 3, "min": 40 },              // 玄阶起：第 3 域（水）好感 ≥ 40
  "materials":   [ { "itemId": 910001, "count": 6 },
                    { "itemId": 910012, "count": 2 } ],    // 地阶起：研读成功时一并扣除
  "attrNeed":    { "strength": 30, "agility": 0, "health": 45, "intelligence": 60 },  // 融合式：基础属性
  "consume":     [10502, 30502], "loopNeed": 20, "needPairsCompleted": 3              // §6.2 既有
}
```

`domain` 直接复用五行编号 1..5（金木水火土五域），不新造一套区域 id；怪物/NPC 属于哪一域由内容侧
一张映射给出（`monsterDomain` 配置，缺省按该怪自身的五行归属推）。

**为什么好感度按域而不是按具体 NPC id**：技能树是五行结构，"金行四式要金域好感 40"这类条件天然可读、
可自动生成、也不会因为内容组加一个新 NPC 就得改技能表。具体 NPC 的好感仍然单独存（送礼、对话加成），
判定看**域内累计**，两条不冲突。

### 14.4 架构硬约束：SkillGrain 绝不能回头调 InventoryGrain

现状已经有一条边：`InventoryGrain.UseItemWithResultAsync → SkillGrain.LearnFromBookAsync`
（秘籍咽喉，A4 落的）。而动态条件要读背包（材料数量）与好感度。若让 SkillGrain 反过来调
`IInventoryGrain`，两边互相 await，Orleans 默认非重入 Grain 当场死锁 —— 这条是本节设计的第一约束。

**做法：门槛判定需要的"事实"由调用方在服务端组装好再传进来。**

```
LearnGateContext {                       // 服务端组装，客户端无法伪造
    int CharacterLevel;                  // ← CharacterGrain
    AttributeAllocationData Allocation;  // ← CharacterGrain（亲和 + 力量/敏捷/体魄/神识）
    Dictionary<int,int> ItemCounts;      // ← InventoryGrain（按模板 id 汇总的数量）
    Dictionary<int,int> Favorability;    // ← FavorabilityGrain（按域）
}
```

- 判定本身是**纯函数**（`WuxingLearnGates.Evaluate(template, context, state)`）：可逐值回归，不需要起集群。
- `SkillHandler`（直接学习/传授通道）负责组装 context 再调 SkillGrain；
  `InventoryGrain`（研读通道）自己就有背包，组装后调 SkillGrain。两条边都只朝 SkillGrain 走，无环。
- 材料扣除发生在**习得提交之后、由持有背包的一方执行**（InventoryGrain / Handler），
  判定阶段只看数量。这样"材料不够"不会留下半扣状态；扣失败只记 warning，不回滚已成的习得（与夺元同口径）。

### 14.5 取消门派限制之后，`LearnMethod` 不再由客户端声明

现在 `LearnMethod` 是客户端上报的：0=门派传授、2=秘籍研读。它有两个问题——一是名字带着"门派"，
二是客户端自己声明"我这式是传授来的"。既然本节的立足点就是"习得路径由内容决定"，通道不能由客户端挑。

**新语义（服务端定）：**
- 目标式在 `SkillBook` 表里有对应书 → **只能研读**，必须真持有那本书（走 InventoryGrain），并按 `learn_success_rate` 掷签；
- 表里没有书 → 直接学习通道，但仍要过全部动态门槛（等级/亲和/好感/材料/属性/树完整性）。
- 白阶按 §7 仍登记了书（供补读），所以"直接学习"实际只对**没有登记秘籍的行**开放 —— 这条把
  "按一个按钮绕过整条技能书经济"的口子从源头堵掉，比上一轮"只放白阶"的临时闸更准确。

### 14.6 落地清单（E = 本轮追加）

- **E1** 数据契约：`learn_cost` 增 `favorNeed/materials/attrNeed`；`generate.mjs` 按 14.2 矩阵填 + 自检；重生成种子与 `skills.json`
- **E2** 纯函数门槛：`WuxingLearnGates`（解析 + 判定 + 人话文案）+ 黄金值用例
- **E3** `FavorabilityGrain`（按角色、分域累计、持久化）+ 击杀/NPC 交互两个入账口
- **E4** 任务发奖落地：`QuestGrain.CompleteQuestAsync` 真调 InventoryGrain 发初级书（单向边，无环）
- **E5** 刷怪掉落接 DB：`LootTableConfig` 从 `ItemTemplate.drop_rate/source_detail` 装配，初级书进该域怪表
- **E6** 材料：研读成功后由 InventoryGrain 按 `materials` 逐种扣除；判定阶段只核数量
- **E7** 取消门派：`LearnMethod` 改由服务端按"有没有书"决定；文案与文档去掉"门派传授"；不变式测试（判定路径不读 sect/profession）
- **E8** 客户端：树页把新门槛显示出来（好感 12/40、缺 玄铁×6、体魄 20/45），被拒原因照原文上屏
- **E9** 回归 + 部署：全量测试 → 重跑种子 → 重建 → 分发 → 重启三服务

---

## 15. 状态效果容器：#33 那 57 个键的共同前置（方案 + 决策清单，**待拍板后才动代码**）

G25 把"数据写了但引擎不做"记成账（57 个键），G26/G27 兑现了两条不依赖状态的（`healPct`/`hpCost`）。
剩下的键（控制、DOT、护盾/反伤、驱散、脆弱/减攻）不是"一个个实现"的关系——它们**缺同样三样前置**：
谁来把效果灌进容器、谁来让时长往前走、谁来拦住被控住的动作。这一节把这三样的现状查清楚，
并把要拍板的点列成清单。**下面是实测（file:line 都可核），不是推测。**

### 15.1 现状：管子两头都通，只是没人往里倒水

1. **容器已经存在**：`CombatState.ActiveEffects : Dictionary<ulong, EffectInfo>`
   （`Horizon.Game.Message/Network/GrainStateModels.cs:25`）；`EffectInfo`（同文件 :175–240）带
   `Duration / RemainingDuration / StackCount / AppliedAtUtc` 与
   `AttackBoost / DefenseBoost / PenetrationBoost` 三个 `ElementStatVector`；
   `EffectType` 里连 `Silence=16 / Stun=17` 都已枚举（`Enums/GameEnums.cs:154-172`）。
2. **写入方健在，但全仓零调用方**：`CombatGrain.ApplyEffectAsync`（`Horizon.Orleans.Grains/CombatGrain.cs:637`）
   用 `EffectKey(目标, 效果)` 做键（注释里写明早期用 `Guid.NewGuid()` 导致同一 Buff 多份并存、无从移除，已修），
   语义是**覆盖式刷新、不叠层**（理由是"叠层要 Buff 模板表给每层上限，那张表不存在，宁可少给也不要给错"）。
   在 Grains / Core / Sim / DsHost 四个工程里 grep `ApplyEffectAsync` **只有这一处定义**——没有入口。
3. **读取方是活的**：`RefreshElementVectors`（:970）把未过期效果的三个向量折成
   `CombatContribution(CombatSourceType.Buff)` 参与聚合，而它在普攻（:128/:129）、技能（:225/:253）、
   受击（:380）路径上**每次都调**。也就是说：只要有人倒水，攻击/防御/破甲这三类增益**今天就能真进伤害公式**
   （多防 Buff 走"已高者计"、多攻 Buff 走求和，都不必另立规则）。
4. **过期是惰性的**：只在 `RefreshElementVectors` 里顺手剔除；`IsExpired`（:1048）把
   `Duration <= 0` 与 `AppliedAtUtc == default` **都当永久**。没有 tick、没有到期事件、没有到期通知。
5. **显示通道也差一半**：`BuffDisplayMessage`（协议号 190，`InteractionMessages.cs:2038`）
   客户端 `MainUIManager.cs:3265` 已经在处理，**服务端零发送方**。
   反过来 `EffectMessage`（221）已注册进 `MessageUnion`，却没有任何网关处理器认领——
   客户端能发、服务端不接，这是个要显式拒掉的口子（见 K6）。
6. **权威是分家的**：玩家 HP 只有一个写口（`CharacterGrain.RequestElementalHpChangeAsync`，
   由 `CombatGrain.cs:550` 调用）；怪物 HP 住在 DS 侧（`DsWorldSession.SubmitCombatAction:895` →
   `CombatCore.Resolve:933` → `ApplyDamageToMonster:992`），**与 Silo 不同进程**，但 DS 有 60Hz tick
   （`DsWorldSession.Tick:741`）。仓里另有两份没并账的 HP 副本（`Combat/MonsterGrain.cs:146`、
   `CombatSystemGrain.cs:134`），任何"改伤害结算"的方案都要先回答这两份要不要留。
7. **动作阻断只有"死亡"这一道**：`SkillGrain.cs:619`、`DsWorldSession.cs:1206`。
   服务端现在**没有任何 stun/root/slow 的执行点**——控制类键缺的不是数值，是一个拦截位。

### 15.2 决策清单（每条给了推荐项与代价；我按选中的结果再写码）

| 编号 | 要拍板的 | 选项与代价 | 我的推荐 |
|---|---|---|---|
| **K1** | 状态容器落在哪 | A. 复用 `CombatState.ActiveEffects`（读侧已进伤害聚合，改动最小）；B. 新建 Sim 纯函数容器由 DS 持有（与怪物/移动同侧，但要把聚合与 HP 权威重做一遍） | **A** |
| **K2** | 第一批兑现哪些键 | 只有能写成三个向量的才吃现成管子：`vulnerability`（负 DefenseBoost）、攻/防削弱、破甲增益。护盾/反伤/驱散/DOT/控制都要新增机制 | **只做向量族那一批**（其余继续挂账） |
| **K3** | 时长怎么往前走 | A. 惰性：下次接触才结算（零新增定时器，但 DoT 会"不打就不掉血"、Buff 会"不放技能就一直有效"）；B. 每角色 Grain 计时器 1s（真到期真生效，代价是每秒一次状态写）；C. 借 DS 的 60Hz tick 再回灌 Silo（跨进程，最贵，且与并行会话正在改的 DS 文件冲突） | **A 起步**，且**这一批不做 DoT**（DoT 在 A 下语义不成立，别硬做） |
| **K4** | 控制类拦到哪一层 | 只拦施放（在 `SkillGrain.CastSkillAsync` 入口判 Stun/Silence，纯 Orleans、便宜、今天就能生效）vs 连移动一起拦（root/slow 必须进 DS 移动校验 + 客户端预测，是另一整件事） | **第一期只拦施放**，root/slow 显式挂账 |
| **K5** | 叠不叠层 | 现容器是覆盖式刷新。数据里有 stack 类语义（锋意层数等）。要叠层就得先有 Buff 模板表（每层上限、刷新规则），否则只能继续挂账 | **不引入叠层**，把 stack 类键留在 Unresolved |
| **K6** | 入站安全 | `EffectMessage`(221) 已在线协议里但无人认领。任何新处理器都不该把它当可信输入；同时新写入方应**拒收 `Duration <= 0`**（"永久"这个默认值对种子数据不安全） | **加"入站即拒" + 时长下限校验** |
| **K7** | 表现与双权威 | 服务端开始发 `BuffDisplayMessage`(190)；客户端 `Combat/Effects/SkillEffectSystem.cs:250-301` 那份**本地自算 DoT/时长**必须降级成纯表现（或直接关掉），否则会出现"客户端在掉血而服务端不知道" | **发送方补上，本地那套改成只画不判** |

### 15.3 这一轮明确不做

不动 DS 的移动校验与伤害结算文件（并行会话正在改，冲突代价高）；不为凑数给 57 个键编默认数值；
不在 K1–K4 没拍板前写任何实现——按本仓纪律，这类"看起来只差一个 if"的功能最容易做出
**编译通过、测试全绿、但游戏里不发生**的第二真相源。

### 15.4 拍板结果（2026-10-06 用户）与本轮已落的这一段

**决策落定**（覆盖上面"我的推荐"那一列，之后以实现为准）：
- **范围 = 全做**：向量族 + DoT + 定身/减速（含打通 DS 移动权威）。不是只做一批。
- **K3 时长驱动 = 三方分工**：Redis 存"到期时限 + 时间戳" → 通知 DS / Orleans / 客户端；
  **Orleans 计算并权威校验、DS 同步、客户端最终展现**（分工协作、各司其职、结果统一、效果相等）。
  这条比我推荐的"惰性结算"和"单纯每角色计时器"都更明确，而且有个好消息支撑：
  `OrleansConst.GameStore` 本来就是 `AddRedisGrainStorage`（`Horizon.Orleans.Silo/Program.cs:500`），
  而 `CombatState` 就存在它上面——所以"带 TTL 的容器"与"权威方在 Orleans"天然对上，
  不需要新建一套存储。到期通知的两块现成料：`RegisterGrainTimer`（`Flower*Grain` 里已有先例）
  与已经有人接收、只差发送方的 `BuffDisplayMessage`（协议号 190，客户端 `MainUIManager.cs:3265` 在处理）。
- **#30 精研收益 = 先给等级一点真收益**（再谈成本）。

**本轮已落（纯函数 + 常驻测试，不碰被并行会话改着的文件）**：
`Horizon.Game.Sim/Skills/WuxingStatusEffects.cs` 是**计划层**——把 `skill_effects` 读成
"给谁挂什么 / 挂多久 / 多久跳一次"，零存储零框架依赖；测试 `WuxingStatusEffectPlanTests` 10 条，
连同 G25 的契约与 G30 的可达性一并跑 **21/21 全绿**。三条刻意口径值得记：
1. **数据没配时长时走"写明出处的引擎常量"**（比例类 6 秒、DoT 每 1 秒一跳），
   而不是"留空当永久"。原因实测在数据里：91 行中 `enemyArmorDown` 与 `vulnerability` 各只有 1 行、
   且都没配 `durationSec`，而现容器的 `IsExpired`（`CombatGrain.cs:1048`）把 `Duration <= 0` 判成**永久**——
   直通下去就是永久削甲。`PlannedStatus.DurationFromData` 把"这个 6 秒是引擎给的"标出来。
   DoT 同理：13 行 DOT 都只配 `dotRatio` + `dotSec`（总时长），间隔属于"引擎怎么切这 4 秒"。
2. **`level_effects` 的收益键必须在顶层**（`{"damagePct":0.02}` = 每重 +2%，第 1 重为基准）。
   `WuxingSkillEffects.ReadNumber` 只看根对象，写成 `{"perLevel":{…}}` 会**安静地读成"没配"→ 每级 +0%**。
   这条坑由测试直接钉住（嵌套形态断言为 0）。没配就是 0，不给默认成长曲线。
3. **账本没动**：`stunSec / vulnerability / dotRatio …` 仍然是 `Unresolved`，
   并有 `PlanningADoesNotClaimTheKeyIsSettled` 守着——写了计划器不等于效果生效（G26 的同款纪律）。

**下一轮接线的顺序**（本轮刻意没做，因为 `CombatGrain.cs`、`DsWorldSession.cs`、
`HpSingleAuthorityContractTests.cs` 都在并行会话的改动清单里，现在跨进去只会做出两边都不认的中间态）：
① 施放结算里按 `Plan()` 写进 `CombatState.ActiveEffects`（带 §15.2 K6 的"时长必须为正"校验，
并给 `EffectMessage`(221) 补"入站即拒"）→ ② 计时器驱动到期与 DoT 结算，HP 只走
`CharacterGrain` 那一个权威口 → ③ `BuffDisplayMessage` 上行（上/下/到期三种事件）→
④ 客户端 `Combat/Effects/SkillEffectSystem.cs:250-301` 那份本地自算降级成"只画不判" →
⑤ root/slow 进 DS 移动校验（这条要等 HP 单权威那轮落地后再动，否则两边同时改同一段判定）。
⑥ 为 #30 生成 017 种子（填 `level_effects`）——**只生成不入库**，等点头。

### 15.5 接线时撞到的真实障碍：**加法载荷 vs 比率数据**（写这一步之前必须解决）

本轮真去接 ① 的时候撞出一件查文档看不到的事，值得单独记，因为**照着 §15.2 的 K1"复用容器"直接写就会写错**：

1. 容器的载荷是**加法**的。`EffectInfo` 带 `AttackBoost / DefenseBoost / PenetrationBoost` 三个
   `ElementStatVector`，聚合走 `CombatProfile.Aggregate`（`CombatGrain.cs:1001`）——攻击侧求和、
   防御与破甲侧**逐行取高**（:1013 `MaxSources`）。而行值本身是**点数换算出来的绝对值**
   （`AttributeDerivation.cs:43-51`：每点 5 点行防御、每点 30 点破甲），不是百分比。
2. 数据说的是**比率**。`enemyArmorDown: 0.3` / `vulnerability: 0.15` / `damageTakenDown: 0.1`
   全是"按对方/自己当前值打折"的意思。比率**没法**当加法项塞进同一套聚合：要塞就得先知道对方当前那一行是多少
   ——而"施加那一刻把比率折算成固定点数"会在对方换装/改加点后**过期失真**（防御涨了，削甲还按旧点数削）。
3. 更要紧的是**减伤真正吃的入参**：`ProcessSkillCastAsync` 传给权威减伤的
   是 `skillTargetBuffDefense`（`CombatGrain.cs:253` → `SyncHpToCharacterAsync` 的第 6 个实参），
   它等于**只由 Buff 构成的防御**（`MaxSources(buffSources…)` 的返回值），基础防御在
   `CharacterGrain.RequestElementalHpChangeAsync`（:1370，裁决段 :1383-1419）里才与 Buff 合并。
   **所以"在 Buff 向量上乘一个 (1 - 30%)"在大多数人身上等于什么都没做**——他们本来就没有 Buff 防御行。
   比率类效果要生效，缩放必须落在**基础与 Buff 合并之后的那一层**，而那正是"HP 单权威"这条线正在改的位置。

结论与选路（不是"先写着"能糊过去的三条）：
- **方案 R1（推荐）**：`EffectInfo` 增一个比率字段（复用已经写着但**没有任何读者**的 `Intensity`——
  `GrainStateModels.cs:203`，:213 的注释早就写明"只有一个 Intensity 浮点不够参与结算"，
  如今三个向量到位之后它就是现成的幅度位；复用=**零状态 schema 变更、零 MemoryPack [Id] 新增**），
  并在 `CharacterGrain` 合并基础防御与 Buff 防御**之后**、判定减免之前，按活跃比率把合并行缩放一次。
  代价：动 HP 权威那一段，必须与并行会话排期对齐。
- **方案 R2**：把比率在施加时折算成加法点数（不动 HP 段）。省一天工作量，换来一个**安静失真**
  的 bug：换装/加点后削甲幅度不跟着变。不建议。
- **方案 R3（先做也不亏）**：只接**不碰伤害**的两类——控制类在施放入口拦
  （`SkillGrain.CastSkillAsync` 已有死亡闸，`CombatGrain.cs:619` 同族；stun/silence 只需读 `ActiveEffects`，
  与加法/比率之争无关），以及 DoT 的到期与每跳（走 `CharacterGrain` 现成的 `RequestHpChangeAsync`，
  是新增写入方而不是改裁决段）。这样 ① 的两条最难合的分支先分开落地，等 HP 单权威落定再补 R1。

附带一条与接线顺序有关的实测：`CombatGrain` 已经持有 `Func<ulong, ISkillGrain>`（:94）并在
`BreakCycleAsync` 上回头调 `SkillGrain`（:728）。**所以"倒水"绝不能放在 `SkillGrain` 里再调 `CombatGrain`**——
那会凑成 Grain 环，两个非重入 Grain 当场互等死锁（§14.4 关于 InventoryGrain 的同款禁令，这里换个方向又踩一次）。
落点只能是 `CombatGrain.ProcessSkillCastAsync` 内部（同一 Grain 直接写自己的状态）或网关处理器。

### 15.6 容器**寻址**实测：状态要按"归属者"落位，不能按"这场对抗"落位

`CombatGrain` 的键是 `GrainKeys.ForEngagement(targetId, fallback)`（`SkillHandler.cs:349`、`:385`），
而 `ForEngagement` 的实现是 **`ForEntity(targetId)`**（`GrainKeys.cs:28`，目标为 0 才退回发起方）——
它不是"对战对"键，而是**受击方**键（用意是让并发打同一目标的多名攻方串行落进同一个 Grain，判死只发生一次）。
这条读对了以后，结论比"加法 vs 比率"更硬：

- **给对手的减益**：写在"我正在打的这个人"的 Grain 里（`TargetId = 受击方`），下次别人打他时读的还是同一个键 ✓
  施法者自己被控也一样（要打他的人进的是**他**这个键）。所以**控制类在施放入口拦是通的**。
- **给自己的增益**（`damageTakenDown` 这类 `ToTarget=false`）：如果在 `ProcessSkillCastAsync` 里就地写，
  它会落到**受击方**的 Grain 里（记录上写着 `TargetId = 施法者`）。等到第三人来打施法者时，
  进的是施法者自己的键，那份自增益**看不见**——表现是"盾对第一个对手有效、对第二个凭空消失"，
  而且 `RefreshElementVectors` 按 `info.CharacterId` 过滤，两处都不会报错。
  所以自增益必须由**知道两把键的一方**（网关处理器）另写进 `ForEntity(施法者)`，
  而不是让 `CombatGrain` 回头跨 Grain 调自己同类型的另一个键（两名玩家互打时
  两个 `CombatGrain` 互相等待就是真死锁，与 §14.4 禁 InventoryGrain 回头调同理）。

一句话规则，接线时照这条走：**效果的写入地址 = 效果的归属者；施法者与受击方分属两个键时，两笔账分开下**。
（顺带把 K1 的"复用现容器"改成有条件成立：复用**结构**，不复用"这场对抗写一处"的现成用法。）

### 15.7 更正 §15.4 的第 ④ 步：客户端那套本地效果引擎**根本没有入口**

§15.4 里我写过第 ④ 步是"把客户端 `SkillEffectSystem.cs:250-301` 那份本地自算 DoT 降级成只画不判"。
真去查的时候发现前提就错了，这条更正必须留在文档里，否则下一个人会去"关"一个从没开过的开关：

- `SkillEffectSystem` 有 8 个硬编码模板（:40-135，含 DoT/HoT/Stun/Silence/MoveSpeed 等）、
  有周期结算（`ProcessPeriodicEffect` :298-311 会 `DealPeriodicDamage` / `ApplyPeriodicHealing`）、
  有到期与属性回滚（:240-283）、也写控制状态（`SetControlState`）。它**被驱动**：
  唯一 tick 入口是 `CombatSystemManager.Update`（注释见 `HundunWorldGame.cs:1955`）。
- 但**没有任何地方往里加东西**：全客户端 grep 它的施加入口零命中，
  只有 `CombatHUDManager.cs:311/325` 与 `BuffBarUI` 在**读** `GetActiveEffects`。
  也就是说 `_activeEffects` 恒为空——整台本地效果引擎、它的假伤害、它的假眩晕，**今天都不执行**。
  HUD 那条 buff 栏因此永远画不出东西（`BuffBarUI.cs:273` 的注释"实际应该从 SkillEffectSystem 获取"
  是同一件事的另一半）。

于是两件事同时成立：
1. **不存在"客户端在掉血而服务端不知道"的双权威**——§15.2 的 K7 担心的是真问题，但在这份代码里还没发生。
   接线时**不需要关掉什么**，只要别把它当成现成的执行器复用。
2. 第 ④ 步的正确写法是反过来：**给客户端加一个由服务端驱动的入口**。
   现成的那一半是 `BuffDisplayMessage`（协议 190，客户端 `MainUIManager.cs:3265` 已在处理），
   缺的是服务端发送方（§15.4 第 ③ 步）。顺序收紧为：服务端在施加/到期时发 190 →
   客户端处理器把消息交给 `SkillEffectSystem` 做**纯表现**（图标、剩余时间、飘字的数字一律取自消息）。
   约束成一句话：**客户端可以显示"还剩 2.4 秒"，不可以自己决定"因此他掉 60 血"**。

防回归的常驻闸门：`WuxingClientEffectHonestyTests.ClientSideEffectEngineHasNoLocalAuthorityEntryYet`。
今天它断"没有本地施加入口"；将来有人加一个，就要他直面这条约束（入口必须由服务端消息驱动）。实测细节记一笔以免被误读：`ApplyEffect`/`RemoveEffect` 全客户端零调用方，`ClearAllEffects` 则有 1 处调用（`CombatSystemManager` 重置时清理）——它只减不增，造不出数值，所以不在拦的范围里；闸门第一版把它一起算进去，红在了一条无害的 housekeeping 上。

### 15.8 这一节现在欠的决定（集中一处，免得散在正文里）

| 决定 | 状态 |
|---|---|
| K2/K4 首批范围 | ✅ 用户定"全做"（向量族 + DoT + 定身/减速） |
| K3 时长驱动 | ✅ 用户定"Redis 存到期时限，通知 DS/Orleans/客户端；Orleans 判、DS 同步、客户端显示"；配套事实：`GameStore` 本就是 `AddRedisGrainStorage`（`Program.cs:500`） |
| 比率类效果怎么进聚合（§15.5 的 R1/R2/R3） | ⚠ 待定；推荐 R1（复用无读者的 `Intensity` 当比率位，零状态 schema 变更） |
| 精研等级收益幅度（G32：客户端已承诺 0.11~0.17 vs 017 提的 0.02/0.015/0.01/0.008） | ⚠ 待定；两份产物必须写同一个数 |
| 017 是否入库 | ⚠ 已生成、未执行 |
| #34 熟练度/破境遗留轴：删除 or 撤销 D1 重立 | ⚠ 待定 |

### 15.9 把"等级收益幅度"这个悬着的决定变成能一眼看完的数（实测自 011，不是估算）

91 式的 `damage_multiplier` 实测形状：**同阶内相邻境 ≈ +1.3%**（15 式从 1.10 爬到 1.31），
**跨一阶 ≈ +8%~+15%**（如 1.31→1.42、3.68→4.01），整条阶梯是 1.10 → 5.19（约 4.7 倍）。
把两个候选值放进这把尺子里量：

| 候选 | 每重 | 满重（第 10 重 = 9 级加成） | 换成阶梯上的位置 |
|---|---|---|---|
| **A：客户端已承诺的** 0.11~0.17 | +11%~+17% | **+99% ~ +153%** | 相当于**连升 10~13 阶**——比整条五行阶梯（4.7 倍）本身还大 |
| **B：017  proposed** 0.02 / 0.015 / 0.01 / 0.008 | +2%~+0.8% | **+7% ~ +18%** | 约等于**升一阶**（+8%~+15%），或 6~14 个境 |

结论（这次不用凭感觉选）：**A 会让"精研练满"这一件事压过整条阶位体系**——玩家留在原地把一式练满，
收益比往前走一整个大阶还高，而 §6.2 恰恰要求"源式练满"才能演化，等于用数值惩罚推进；
**B 的量级刚好是"一阶"，与阶梯同尺**，练满是一个可见但不至于颠倒秩序的甜头。
（`max_level` 全 91 式都是 10，所以这两档的换算不需要额外假设。）

若真的想让精研成为主要成长来源（A 的意图），那必须**同时把阶位倍率阶梯压平**
（1.10→5.19 这条线重做），否则两条成长轴会互相吞掉——那是改整套 D3 的数值口径，
不是一行 `damagePerLevel` 的事，所以这里按现状推荐 **B**。

**拍板即一句话**：选 B（+2%/+1.5%/+1%/+0.8%）→ 我把 017 与 `skills.json` 写成同一批数、
在 `CombatGrain` 的伤害里接上这个读者（`SkillCastMessage` 目前**不带**技能等级，
所以要么给协议加一字段、要么 `CombatGrain` 复用已有的 `ISkillGrain` 边去读等级——两者都要重建并重启三服务）；
选 A → 顺带把阶位倍率阶梯一并重做（工程量大一档）；不选 → 两份产物继续各说各话（客户端 0.11~0.17 无人结算，017 不入库）。






