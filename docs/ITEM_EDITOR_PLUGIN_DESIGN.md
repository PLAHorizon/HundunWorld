# 物品编辑器插件设计方案（ItemEditor）

> 目标原话：**开发 MMORPG 物品编辑器插件，对标魔兽世界，同时参考本 MMORPG 的其它编辑器插件。**
>
> 本文是该目标的**唯一口径来源**。动手前先读 §1（交付物对照）、§10（决策清单）与 §11（进度与证据）；
> 记忆里的数字只能当线索，判据以本文列的实测回执为准。

---

## 0. 已有事实（2026-10-27 实测，不靠记忆）

| 事实 | 证据位置 |
| --- | --- |
| 物品**定义**表已存在：`Game_HunduShijie_ItemTemplate`，38 列 | `Horizon.Model/GameModel/ItemTemplateEntity.cs:12` |
| 物品**实例**表已存在：`Game_HunduShijie_Item`（在线背包实际在 Orleans grain 状态里，不读这张表） | `Horizon.Model/GameModel/ItemEntity.cs:12`、`Horizon.Orleans.Grains/InventoryGrain.cs:50` |
| 服务端**没有**物品目录，只按 id 查：价格、`base_attributes`、两处名字回填 | `Horizon.Orleans.Grains/InventoryGrain.cs:341`、`CombatEquipmentSource.cs:89`、`NpcCommerceCore.cs:38` |
| 客户端展示走**编译期硬编码目录**：装备 23 条、材料 41 条、消耗品 2 条、宝石 5 条、秘籍 `800000+技能id` | `HundunWorld/Source/Game/Inventory/ItemCatalog.cs:100`、`Equipment/EquipmentDatabase.cs:1071`、`Equipment/Material/MaterialData.cs:92` |
| 品质取值四处不一致：客户端 0..4、DB `base_quality` 0..5、DB `rarity` 0..5、`LootSystem` 0..6 | `ItemCatalog.cs:37`、`ItemTemplateEntity.cs:58/64`、`Game/Combat/LootSystem.cs:13` |
| **号段易混点**：外观是 `91000..91999`（既存 91001..91004），五行灵材/信物是 `910000..919999`（既存 910001..910015）——只差一位，不能并段 | `EquipmentDatabase.cs:72`、`SkillEvolutionLiveTests.cs:107`、`InventoryGrain.cs:439` |
| 掉落 / 任务 / 商店都只存 item id，**不校验模板是否存在** | `MonsterLootCore.cs:54`、`QuestRewardSpec.cs:9`、`NpcCommerceCore.cs:38` |
| 其它编辑器范式：DB 权威 + 无存储内核 + 短命 CLI 子进程 + Flax 插件 | MonsterEditor / NpcEditor / PetEditor / QuestEditor |

**结论**：物品编辑器不是"从零造物品系统"，而是把**一份分裂的权威**收拢成一条链：
`DB（权威）→ ItemCli（校验/写库/导出）→ Flax 插件（编辑）→ 客户端内容表（展示）`。
不收拢的话，编辑器只能改价格和战斗属性，改不了玩家看到的名字与图标——这正是当前物品系统最大的坑
（`docs/QUEST_SYSTEM_DESIGN.md:324` 已记录过一次）。

---

## 1. 目标原话 → 交付物对照（完成审计只看这张表）

| # | 要求（原话拆解） | 交付物 | 判据 |
| --- | --- | --- | --- |
| A1 | MMORPG 物品编辑器 | Flax 插件 `HundunWorld/Plugins/ItemEditor`，Tools 菜单可开 | `_verify.sh` 两模块 0 错；插件源码存在 7 个面板 |
| A2 | 物品**定义**全属性可编辑 | 面板覆盖：基础/品质/类型、需求与限制、属性与战斗、经济、图标模型、使用效果、插槽、随机词缀、套装、出处 | 每个字段都有控件，且 `CommitAll` 全部落草稿 |
| A3 | 对标魔兽世界 | 字段与语义按 WoW `item_template` 映射（§3.1），并写明本作取舍 | §3.1 映射表逐行有本作列名或"不做"理由 |
| A4 | 参考本作其它编辑器 | 分层与文件骨架逐文件镜像 PetEditor；异步 CLI 桥、未知键自证、草稿→发布两段式 | 与 `PetCliBridge.cs`/`PetDraft.cs` 同构；契约测试按源文本对账 |
| A5 | 数据可落库 | `Horizon.ItemCli` + `scripts/sql/023_item_editor_tables.sql`（**已对本地 `Game` 执行，回滚脚本就位**） | CLI `validate`/`save` 真跑；演练库 + 真库都验过产物 |
| A6 | 数值口径唯一 | `Horizon.Game.Sim/Items/ItemCore.cs` 确定性纯函数；插件/CLI 都调它，不各算一份 | 内核测试 + `preview-*` 与插件本地预览交叉核对 |
| A7 | 改动对玩家可见 | 客户端 `ItemCatalogOverlay` 读 `Data/Items/client_items.json`，命中覆盖、未命中回退硬编码 | 覆盖层 0 条时与今日行为逐字相同；有表时名字/图标/属性能变 |
| A8 | 不破坏既有数据 | 新列可空/有默认；不复用 `rarity` 当品质；外观号段按 `91000..91999` 新建 | 回滚脚本；§10 D5/D6 |
| A9 | 可验证 | 契约测试 + 内核测试 + 离线 csc + `dotnet build/test` 真跑 | §9 闸门表逐条有回执 |

---

## 2. 分层与权威源

```
┌─────────────────────────────┐
│ Flax 编辑器插件 ItemEditor   │ 只做界面；不引 SqlClient/EF
│  DraftModel（JSON 协议副本） │
└──────────────┬──────────────┘
               │ 短命子进程 + stdin 草稿（UTF-8 无 BOM）
┌──────────────▼──────────────┐
│ Horizon.ItemCli             │ 校验 / 写库 / 导出内容表 / 试算
│  Validator + Store          │ 规则只此一份
└──────────────┬──────────────┘
               │ EF（ItemTemplate 主表）+ 从表
┌──────────────▼──────────────┐
│ SQL Server                  │ 权威源
│ ItemTemplate + 9 张从表     │
└──────────────┬──────────────┘
               │ export-client
┌──────────────▼──────────────┐
│ Data/Items/client_items.json│ 随包发布
│ 客户端 ItemCatalogOverlay   │ 命中覆盖，未命中回退硬编码
└─────────────────────────────┘
               ▲
               │ 双方共用
┌──────────────┴──────────────┐
│ Horizon.Game.Sim/Items      │ 零存储零框架依赖的确定性内核
│  ItemCore（预算/价格/插槽） │
└─────────────────────────────┘
```

**三条硬规矩**（与 PetEditor 同源）：

1. **校验只在 CLI/内核**。插件里再写一份范围检查，迟早与写库那份分叉，而分叉表现是
   "编辑器说合法、保存被拒"或更糟——"两边都说合法、线上解析不了"。
2. **preview 一律调内核**。编辑器显示的"DPS / 属性预算 / 售价"必须与将来运行时同一函数算出。
3. **草稿与发布分离**。`save` 只写草稿态（`publish_state=0` 的新建），跨到线上只能走 `publish`。

---

## 3. 数据模型

### 3.1 与 WoW `item_template` 的映射（对标依据）

WoW（AzerothCore）`item_template` 的主干字段与本作落点：

| WoW 字段 | 语义 | 本作落点 | 备注 |
| --- | --- | --- | --- |
| `entry` | 物品编号 | `item_id` | 编辑器按号段校验（§3.2） |
| `class` / `subclass` | 大类/子类 | `item_type` / `sub_type` | 本作 0武器 1防具 2饰品 3消耗品 4材料 5任务 6秘籍 7配方 8宝箱 |
| `InventoryType` | 装备部位 | **新增** `inventory_type` | 映射到客户端 `EquipmentSlot`（§3.3） |
| `Quality` | 品质 0..6 | `base_quality`（**0=普通，沿用本作语义**） | `rarity` 保留为"掉落稀有度"，两者不混用（D2） |
| `ItemLevel` | 物品等级 | **新增** `item_level` | 属性预算与掉落档位的输入 |
| `RequiredLevel` | 等级需求 | `level_require` | 已有 |
| `AllowableClass` / `AllowableRace` | 职业/种族位掩码 | **新增** `allowable_class` / `allowable_race` | 本作职业少，掩码按位即可 |
| `RequiredSkill` / `RequiredSkillRank` | 专业需求 | **新增** `required_skill` / `required_skill_rank` | 与 `profession_limit` 并存：一个管"能否用"，一个管"能否学" |
| `stat_typeN` / `stat_valueN` | 最多 10 条属性 | **从表** `ItemStatTemplate` | 结构化，避免"第 7 条属性没人读"（D3） |
| `dmg_min/max/type`（两段） | 武器伤害 | **从表** `ItemDamageTemplate` | 双手/副手两段同表，按 `seq` |
| `armor` / `block` | 护甲/格挡 | **新增** `armor` / `block` | 本作防具主数值 |
| `delay` | 攻击间隔(ms) | **新增** `attack_delay_ms` | DPS 由内核算，不存派生值 |
| `holy_res..shadow_res` | 六系抗性 | **新增** `resistance_element` + `resistance_value` | 本作五行，只保留一系（与 `element` 区分：元素是亲和，抗性是减伤） |
| `spellid_N/trigger/charges/ppm/cooldown` | 使用/命中/装备触发法术 | **从表** `ItemSpellTemplate` | 一行一个触发，最多 5 行（D4） |
| `bonding` | 绑定 | `bind_type` | 已有 0..3 |
| `BuyPrice` / `SellPrice` | 买/卖价 | `buy_price` / `sell_price` | 已有；内核给"售价 = 买价/4"的默认推导 |
| `Stackable` / `MaxCount` | 叠加上限/持有上限 | `max_stack` / **新增** `max_count` | `is_unique` 是布尔，`max_count` 才表达"最多 3 个" |
| `ContainerSlots` | 背包格数 | **新增** `container_slots` | 背包类 |
| `socketColor_1..3` + `socketContent` | 插槽 | **从表** `ItemSocketTemplate` | 与现有 `gem_slot_rates` 并存：概率用于随机生成，固定槽用于模板 |
| `socketBonus` | 插槽奖励 | **新增** `socket_bonus` | 枚举 id |
| `RandomProperty` / `RandomSuffix` | 随机词缀 | **从表** `ItemRandomAffixTemplate` + 词缀属性行 | 编辑器内可新建词缀（D7） |
| `itemset` + 套装加成 | 套装 | **从表** `ItemSetTemplate` / `ItemSetBonusTemplate` / `ItemSetBonusStat` | `set_id` 已有 |
| `MaxDurability` | 耐久 | **新增** `durability` | |
| `startquest` / `PageText` / `LanguageID` / `PageMaterial` | 任务书/书页 | **新增** `start_quest` / `page_text` | 书页材质本作不做 |
| `BagFamily` / `TotemCategory` | 背包族/图腾 | **新增** `bag_family` | `totem_category` 不做（本作无该概念） |
| `DisenchantID` | 分解产出 | **新增** `disenchant_id` | 分解系统未落地，先留字段 |
| `duration` | 有效期(秒) | `valid_days`（天）+ **新增** `duration_seconds` | 两套并存时以 `duration_seconds` 优先，0 用天 |
| `flags` / `flagsCustom` | 行为位 | **新增** `flags` / `flags_custom` | 位定义见 `ItemFlags` |
| `ScriptName` | 脚本钩子 | **新增** `script_name` | 本作按名字派发，不做 per-item 脚本 |
| `Material` / `sheath` / `FoodType` / `HolidayId` / `MinMoneyLoot` | 材质/挂点/食物/节日/钱袋 | 只落 `food_type`、`min/money_loot`；其余**不做** | 见 §3.4 |

### 3.2 号段（编辑器强制）

| 段 | 范围 | 语义 | 现状 |
| --- | --- | --- | --- |
| 任务/杂项 | `1..9999` | 历史遗留 | 只读兼容 |
| 装备 | `10001..49999` | 武器/防具/饰品/背包 | 装备种子 10001..10013、背包 2001..2004（历史，允许） |
| 消耗品 | `50001..59999` | 丹药/食物/卷轴 | 50001/50002 |
| 宝石/镶嵌 | `60001..69999` | 五行宝石 | 60001..60005 |
| 材料 | `700001..799999` | 采集/合成材料 | 700001..700041 |
| 秘籍/配方 | `800000..899999` | **`800000+技能id` 硬绑定**，编辑器不得自行占用 | 011 种子 91 本 |
| 外观 | `91000..91999` | 外观装备 | 018 种子 91001..91004，与此一致 |
| 五行灵材/信物 | `910000..919999` | 灵材与信物（**不是外观**） | 910001..910015 既存 |
| 词缀/套装 | `900001..909999`（词缀）、`920000..929999`（套装） | 编辑器分配 | 空 |

> 判据：`ItemCore.IsTemplateIdInSegment(id, segment)` 是唯一实现；CLI 写库前、插件输入时都调它。

### 3.3 `inventory_type` → 客户端槽位（唯一映射）

`0=不装备 1=头 2=颈 3=肩 4=背 5=衣 6=腰 7=腿 8=脚 9=右手 10=左手 11=右戒 12=左戒 13=右腕 14=左腕 15=面 16=外观 17=背包`。

映射是 `EquipmentSlot = inventory_type - 1`（1..16）。**注意 Body 是 5 而非 4**：早期草案漏了"背部"那一格，
会让身体之后的每件装备都穿到前一格上，而库里完全看不出来（已由内核测试与覆盖层映射同步修正）。
17=背包只用于背包类，不进战斗槽位表。
判据：内核 `ItemCore.EquipSlotIndex` 与客户端枚举同序，契约测试锁死这份映射。

### 3.4 明确不做（对标 WoW 时的减法）

- **不做** `Material`/`sheath`/`TotemCategory`/`PageMaterial`/`HolidayId`：本作没有对应渲染或系统消费者，加了就是"声明了没人读"。
- **不做 per-item 脚本**（`ScriptName` 只落名字，运行时不加载程序集）。
- **不做** 多语言完整 RPG 化：`ItemLocalization` 表与面板保留，首批只有 `zhCN`/`enUS` 两列可用。
- **不做** 物品实例编辑：实例是玩家资产，编辑器只编辑**模板**（与 PetEditor 同一条线）。

---

## 4. 内核 `Horizon.Game.Sim/Items/`

零存储、零框架依赖、确定性。所有函数都写成纯函数，输入草稿/数值，输出结果或拒绝理由。

| 类型 | 职责 | 关键判据 |
| --- | --- | --- |
| `ItemQuality` / `ItemClass` / `InventoryType` / `ItemStatType` / `SocketColor` / `ItemBindType` / `ItemFlags` / `ItemSegment` | 枚举与位定义 | 取值与 DB 注释、客户端枚举逐项同序 |
| `ItemCore` | 号段、槽位映射、品质颜色/名称、预算、价格、DPS、插槽颜色合法性、词缀抽取、套装聚合、tooltip 行 | 见下 |
| `ItemBudget` | `ComputeStatBudget(itemLevel, quality)`：WoW 口径的"属性点预算" | 同品质下 `ilvl↑ ⇒ 预算↑`；同 ilvl 下 `quality↑ ⇒ 预算↑`（严格不等式，测试锁） |
| `ItemPricing` | `DefaultSellPrice(buyPrice, stackable)`、`DefaultBuyPrice(sellPrice)` | 售价 = `max(1, buy/4)`；不可交易时为 0 |
| `ItemCombat` | `ArmorFromItemLevel`、`WeaponDps`、`AttackPowerFromStats` | DPS = 平均伤害 × 1000 / 攻击间隔 |
| `ItemSockets` | 颜色合法性（`meta` 只能插 `meta`，普通槽可插匹配色或无色） | 错色拒绝，理由带枚举名 |
| `ItemAffixes` | 确定性抽取（`seed` → 词缀 + 数值带） | 同种子同结果 |
| `ItemSets` | 按已穿戴件数聚合加成 | 3 件套不生效 2 件套以上；上限按最大档 |
| `ItemTooltip` | 生成 WoW 风格 tooltip 行（名称/品质/物品等级/绑定/槽位/伤害/护甲/属性/插槽/套装/售价/描述） | 行顺序固定，**客户端与编辑器共用同一份行生成器** |
| `ItemValidation` | 结构/范围/交叉引用校验 | 全是纯函数，返回 `errors[]` + `errorPaths[]` |

**为什么 tooltip 放内核**：现在客户端 `Tooltip` 是 UI 自己拼字符串（`ItemCatalog.Stats`），
编辑器再拼一份，两边迟早不一致。把"行"变成内核产物，UI 只负责画。

---

## 5. CLI `Horizon.ItemCli`

输出恒为一行 JSON：`{ok, verb, message, data, warnings}`；退出码 0/1。
异常必须带 `errorType`/`errorStack`（教训：只回 `ex.Message` 会让同一个神秘异常被猜三次）。

| 动词 | 作用 | 连库 |
| --- | --- | --- |
| `catalog` | 号段、枚举、内核默认值与限值；编辑器启动先调 | 否 |
| `list` | 模板列表（可 `--type` / `--quality` / `--segment` 过滤） | 是 |
| `get <id>` | 读成草稿（主表 + 9 张从表） | 是 |
| `validate [-|file]` | 结构 + 跨域对账（技能/套装/词缀引用） | 可选 |
| `save [-|file]` | 先校验，再事务写 10 张表 | 是 |
| `publish <id>` / `retire <id>` / `delete <id>` | 状态与删除 | 是 |
| `draft save|list|get|delete` | 草稿暂存（Redis 可用时），不可用时降级为本地文件并如实报 | 否/是 |
| `export-client --out <path>` | 导出客户端内容表（只导已发布） | 是 |
| `preview-tooltip [-|file] --level N` | 内核生成 tooltip 行 | 否 |
| `preview-budget --ilvl N --quality Q [--sweep]` | 属性预算曲线 | 否 |
| `preview-random --affix A --seed N` | 词缀抽取 | 否 |

**未知键自证**：草稿 JSON 里出现未知键直接拒绝写库。STJ 默认静默丢弃，字段名漂一个字母
会变成"值全是初值"，而两侧同为初值时自校验反而全绿（PetEditor 已踩过）。

---

## 6. 编辑器插件 `HundunWorld/Plugins/ItemEditor`

### 6.1 骨架（逐文件镜像 PetEditor）

```
ItemEditor.flaxproj
Source/ItemEditor/ItemDraftModel.cs          # 草稿模型（JSON 协议副本）
Source/ItemEditor/ItemPreviewMath.cs         # 本地即时预览（与内核同表）
Source/ItemEditor/ItemEditor.Build.cs
Source/ItemEditor/ItemEditor.Gen.cs
Source/ItemEditorEditor/ItemCliBridge.cs     # 子进程桥（同步 + 异步）
Source/ItemEditorEditor/ItemEditorPlugin.cs  # Tools → 物品编辑器
Source/ItemEditorEditor/ItemEditorWindow.cs  # 主窗口与面板
Source/ItemEditorEditor/ItemViews.cs         # tooltip 预览 / 预算曲线 / 套装图
Source/ItemEditorEditor/ItemEditorEditor.Build.cs
Source/ItemEditorEditor/ItemEditorEditor.Gen.cs
Source/ItemEditorTarget.Build.cs
Source/ItemEditorEditorTarget.Build.cs
_verify.sh                                   # 离线 csc 双模块闸门
```

**插件要被编辑器发现，还差两步（离线 csc 只证明源码能编译，不证明菜单出现）**：
1. `HundunWorld/HundunWorld.flaxproj` 的 `References` 是**显式清单**，必须加 `$(ProjectPath)/Plugins/ItemEditor/ItemEditor.flaxproj`；
2. 用 Flax.Build 产出 `Plugins/ItemEditor/Binaries/ItemEditorEditorTarget/Windows/x64/{Debug,Development}/*.CSharp.dll`：
   `Flax.Build -log -build -mutex -buildTargets=ItemEditorEditorTarget -platform=Windows -arch=x64 -configuration=Development -workspace=<插件目录>`。
   外部 Flax.Build **不会热重载**运行中的编辑器，改完要重启编辑器才会加载新装配。

### 6.2 面板（左侧列表 + 顶部工具栏 + 12 个页签 + 右侧视图 + 控制台）

1. **基础**：编号、名称、描述、大类/子类、物品等级、叠加上限、绑定、唯一、可交易/可销毁、有效期。
2. **品质与稀有度**：`base_quality`（0..6，唯一品质）、`rarity`（掉落稀有度）、品质色预览、`element`。
3. **需求与限制**：等级/境界/职业掩码/种族掩码/专业/专业等级。
4. **属性**（从表）：最多 10 行 `stat_type + value`，带"按预算填充"按钮（调内核）。
5. **战斗**（从表 + 主表）：`inventory_type`、护甲、格挡、抗性、两段伤害（min/max/type/delay）、DPS 预览。
6. **经济**：买价/卖价（自动推导按钮）、`max_count`、`container_slots`、钱袋 loot。
7. **图标与模型**：`icon_path`、`model_path`、字根（Glyph）回落。
8. **使用效果**（从表）：`spelltrigger` 行（法术 id、触发方式、充能、PPM、冷却、类别）。
9. **插槽**：固定槽颜色序列、`socket_bonus`、与 `gem_slot_rates` 的关系提示。
10. **随机词缀**：词缀 id、类型（前缀/后缀）、属性行、数值带。
11. **套装**：`set_id`、套装名、逐档加成（件数 + 描述 + 属性行）。
12. **出处**：`source_type` + 结构化来源行（怪物/掉落表/任务/商店 NPC/合成），与 `source_detail` JSON 同步。

右侧视图：**Tooltip 预览**（实时；CLI 可用时调 `preview-tooltip`，读得到套装就连档位一起画，读不到只画 `#setId`）、**属性预算曲线**、**套装档位图**。
控制台：所有 CLI 回执、`writtenRows` 逐表行数、`errors` 带 `errorPaths`。

### 6.3 与其它编辑器的一致性

- 工具栏按钮位置与语义完全对齐 PetEditor（探测 / 刷新目录 / 新建 / 读草稿 / 校验 / 存草稿 / 保存到库 / 发布 / 停用 / 删除）。
- `GuardBusy` 单飞：一次只允许一个子进程。
- 回执解析只取最后一行 JSON；`data.errors` / `data.errorPaths` 成对打印。

---

## 7. 运行期接线（防"改了没人读"）

| 消费方 | 现状 | 本次改动 |
| --- | --- | --- |
| 客户端 `ItemCatalog.Resolve` | 硬编码目录 | 先查 `ItemCatalogOverlay`（`Data/Items/client_items.json`），命中返回导出定义，未命中走现有解析 |
| 客户端 Tooltip / 物品详情 | 各 UI 自己拼 | 详情面板改为**以目录定义为最新**（名称/品质/类型/字形/属性/说明/套装名），不再用背包行里缓存的旧名字 |
| **覆盖层放行范围** | — | 装备/外观（必须有部位）、秘籍、消耗品、宝石、材料、任务物、配方；**挡住**历史号段（含 2001..2004 背包）与背包大类。专属字段：`hpRestore/energyRestore`、`gemElement/gemAffinity` |
| **品质 7 档** | 客户端只有 5 档 | `InkWashTheme.InkQuality` 扩到 Artifact(神器)/Mythic(神话)；详情/背包/动作条/弹窗的品质名与颜色按 0..6 渲染，不再把 5/6 钳成传说 |
| **内容表热重载** | 只读一次 | 文件 mtime 变化（节流 1 秒）自动重载并清 `ItemCatalog` 缓存；每次 `StartAsync` 也显式 Reload 一次（静态层跨 PIE 不重置） |
| 服务端 `InventoryGrain` 价格/名称 | 读 `ItemTemplate` | 不改（已读）；外观 91000..91999 与灵材 910000..919999 是两段，互不冲突 |
| **服务端血池 `CharacterInfo.MaxHealth`** | 只按加点算（装备生命被忽略） | `RefreshServerHealthPoolAsync` 把装备 `MaxHealth` 计入；穿戴/卸下、宝石、加点都会经 `RefreshCombatProfileAsync` 重算并落库；客户端装备桥**不再**重复加生命/内力上限 |
| 服务端 `base_attributes` | `ItemAttributeContract` 解析 JSON | **不改契约**；新 `ItemStatTemplate` 与旧 JSON 并存，导出时合并 |

**未闭环的明确项**：服务端战斗属性目前只认 `base_attributes` JSON。本次让编辑器**同时写**该 JSON
（由 `ItemStatTemplate` 派生），因此不需要改服务端读取路径。反向（只改从表不改 JSON）由 `save` 拒绝。

---

## 8. 与既有系统的接缝

- 掉落：`MonsterLootCore` 只吃 `item_id`；编辑器提供"被哪些掉落表引用"的只读反查（`list --referenced-by`）。
- 任务：`QuestRewardSpec.TemplateId`；同上反查。
- 商店：`NpcCommerceCore` 只吃 id；同上。
- 秘籍：`800000+技能id` 是硬约定，编辑器在秘籍类下把编号设为只读并显示推导航号。
- 外观：`91000..91999`（既存 91001..91004）；`910000..919999` 是五行灵材/信物，服务端按该段读名称——**两段不能并**。

---

## 9. 验证闸门（每条都要真跑，不接受"编译过了"当证据）

| # | 闸门 | 命令 | 通过判据 |
| --- | --- | --- | --- |
| G1 | 内核单测 | `dotnet test Horizon.Game.Gateway.Tests --filter ItemEditorKernel` | 全绿且用例数 > 15 |
| G2 | 跨宿主契约 | `dotnet test ... --filter ItemEditorContract` | 键集合/默认值/枚举序两侧逐字相同 |
| G3 | CLI 无库动词 | `dotnet run --project Horizon.ItemCli -- catalog` / `preview-budget --sweep` | `ok=true`；预算严格单调且打印差值 |
| G4 | CLI 连库动词 | `save` + `get` + `export-client`（连不上库时如实报，不算通过） | `writtenRows` 逐表 > 0；`get` 回读逐字段等于写入 |
| G5 | 插件离线编译 | `HundunWorld/Plugins/ItemEditor/_verify.sh` | `rt`/`ed` 两模块 `errors=0` |
| G6 | 客户端脚本闸门 | `scripts/verify-client-scripts.sh` | 0 错（含新覆盖层） |
| G7 | SQL 脚本 | 语法干跑（演练库） | 建表数 = 预期；回滚脚本可执行 |
| G8 | 覆盖层回退 | 无 `client_items.json` 时跑客户端闸门 | 与改动前逐字相同（只有新增文件） |

**G4 明确不执行生产库**：迁移/写库只生成脚本，等用户点头（本仓库既有规矩）。

---

## 10. 决策清单（默认取"最完整那档"；已按推荐档实施，改一条要连带改测试）

| # | 决策 | 取档 | 理由 / 影响 |
| --- | --- | --- | --- |
| D1 | 权威源 | **DB 为唯一权威**，客户端硬编码降级为回退 | 不这么做编辑器改不了玩家看到的任何东西 |
| D2 | 品质字段 | `base_quality` 为唯一品质（0..6）；`rarity` 只表示掉落稀有度 | 现有四处品质语义不一致是已记录坑。**编号必须沿用本作语义 0=普通**（客户端 `InkWashTheme.InkQuality` 与库列注释、种子数据三方一致）；照抄 WoW 的 0=粗糙会让库里每件物品显示品质整体下移一档 |
| D3 | 属性存法 | 从表 `ItemStatTemplate`，最多 10 行；同时派生写 `base_attributes` JSON | 兼容服务端旧读取路径，又给编辑器结构化编辑 |
| D4 | 法术触发 | 从表，最多 5 行，`seq` 保序 | 与 WoW 上限一致 |
| D5 | 新列 | 全部可空或有默认；脚本幂等（`COL_LENGTH` 先问再建） | 怪物迁移在空库上整批中止过一次，别重犯 |
| D6 | 外观号段 | 新建一律 `91000..91999`；`910000..919999` 留给五行灵材/信物 | 两段只差一位；并段会让 150+ 件灵材被编辑器判成外观 |
| D7 | 词缀/套装是否可在编辑器里新建 | **可以**（与 WoW 一致） | 只读的话策划要手工写 SQL |
| D8 | 物品实例 | 不编辑 | 玩家资产，模板与实例分离 |
| D9 | 多语言 | 表 + 面板保留，首批 `zhCN`/`enUS` | 完整 RPG 化不在本次范围 |
| D10 | EF 迁移 | **本次生成 SQL 脚本，不跑 `migrations add`**；理由：并行会话会把别人的实体卷进差分（NPC 迁移已踩过），且 Pet 表至今无迁移；后续补迁移时按 NPC 的做法用脚本剔除无关实体 | 先保证数据面能建，再补迁移 |
| D11 | 客户端消费 | 覆盖层（`client_items.json`）而非改硬编码表 | 包体与热更都能换内容 |
| D12 | 工具链 | 插件不引 SqlClient/EF；一切经 CLI | 校验只一份 |

---

## 11. 实施顺序（每步带闸门，跨步不并行改同一文件）

| 步 | 内容 | 闸门 |
| --- | --- | --- |
| S1 | 本文 | 人工过一遍 §1/§10 |
| S2 | 内核 `Items/` + 内核测试 | G1（先红后绿：先写"预算单调"让它在空实现上红一次，再实现） |
| S3 | SQL 023 + 回滚（只生成）+ 实体 + DbSet | G7 干跑；G3 `catalog` 不需要库 |
| S4 | `Horizon.ItemCli` + 解决方案接入 | G3、G4 |
| S5 | 插件骨架 + 12 面板 + 视图 | G5 |
| S6 | 客户端覆盖层 | G6、G8 |
| S7 | 契约测试 + 全套回归 | G1/G2 + 既有 3680 绿不回退 |

**跨步禁令**：S2 未绿不进 S4；S5 与 S6 不并行改 `ItemCatalog.cs`。

---

## 12. 进度与证据（实施时逐条追加）

### 12.1 已完成

- [x] S1 设计文档（本文件）
- [x] S2 内核 `Horizon.Game.Sim/Items/`（`ItemEnums.cs` + `ItemCore.cs`）
- [x] S3 数据层：`scripts/sql/023_item_editor_tables.sql` + 回滚、`ItemEditorEntity.cs`（11 张从表）、`ItemTemplateEntity` 扩展 31 列、两个上下文的 DbSet
- [x] S4 `Horizon.ItemCli`（已加入 `Horizon.sln`）：catalog/list/get/validate/save/publish/retire/delete/draft/export-client/preview-tooltip/preview-budget/preview-random/references
- [x] S5 插件 `HundunWorld/Plugins/ItemEditor`：12 页签 + tooltip/预算/套装三视图 + `_verify.sh`
- [x] S6 客户端覆盖层 `ItemCatalogOverlay` 接入 `ItemCatalog.Resolve`（逐条覆盖、未命中回退硬编码）
- [x] S7 闸门：见 §12.4 回执

### 12.2 实施中抓到的真缺陷（记录，不掩盖）

| 日期 | 缺陷 | 证据 | 处理 |
| --- | --- | --- | --- |
| 2026-10-27 | **设计初版把号段判反了**：把 `910000..919999` 当外观；实测该段是五行灵材/信物（`LoadMaterialNamesAsync` 按此段读名称、测试用 910001..910015），外观是 `91000..91999`（91001..91004） | `InventoryGrain.cs:439`、`SkillEvolutionLiveTests.cs:107`、`EquipmentDatabase.cs:72` | 号段改为精确区间表；新增"既存外观与灵材各自落在正确号段"回归；插件"新建外观"与号段显示同步 |
| 2026-10-27 | 品质语义四处不一致（客户端 0..4、DB 注释 0..5、`rarity`、LootSystem 0..6），另有 `Message.Enums.ItemQuality` 与 `WuxiaGameMessages.ItemQuality` 两套枚举 | `InkWashTheme.cs:849`、`GameCoreEnums.cs:47`、`WuxiaGameMessages.cs:234` | D2 收口为 0=普通；内核不引用那两套旧枚举（编号不同，引用会整体错位） |
| 2026-10-27 | **新物品第一跳就写不进去**：`item_id` 是 IDENTITY，而编号是业务键 | 演练库 `Msg 544`：IDENTITY_INSERT OFF | `ItemStore.Save` 先查 `sys.columns.is_identity`，插入时临时 `SET IDENTITY_INSERT ON`，异常路径也复位（会话级开关会污染连接池） |
| 2026-10-27 | 历史表有 **13 个 NOT NULL 字符串列**（gem_slot_rates/random_attributes/use_effect…），新行不填就 `Msg 515` | 演练库实测 | 新建行显式给空串；更新路径不碰这些列（编辑器不编辑它们，覆盖=静默清空） |
| 2026-10-27 | `remark` 列可空但实体属性非空 ⇒ 读老行直接 `SqlNullValueException` | 演练库插入 `remark=NULL` 的 10002 行 | 实体属性改 `string?`（[[ef-nullable-column-needs-nullable-clr-prop]] 同款） |
| 2026-10-27 | 保存失败时 `writtenRows` 仍带 `template=1, stats=3` 等 | 首次演练回执 | 失败分支清空计数——打印"写了 N 行"而实际回滚，比不报更坏 |
| 2026-10-27 | 回滚脚本被具名 DEFAULT 约束挡住（`Msg 4922`），整批原子回滚 | 演练库回滚失败 | 先用 `sys.default_constraints` 查约束名并删除，再 DROP COLUMN |
| 2026-10-27 | 老行 `inventory_type` 默认 0 ⇒ 防具被校验拒绝"必须指定部位" | 10002 行首次保存被拒 | 这是**有意**的：部位是防具必填项。编辑器打开老物品时要求先选部位，错误路径会点名 `inventoryType` |
| 2026-10-27 | `inventory_type` 草案漏了"背部"，Body 之后的部位整体错一格 | 与 `EquipmentSlot` 逐项比对（`EquipmentData.cs:31`） | 枚举改为 1..16 与客户端槽位一一对应（Body=5），背包=17；内核/插件/覆盖层/实体注释/文档同步 |
| 2026-10-27 | **编辑器 Tools 里看不到插件**：源码齐、离线 csc 全绿，但 `HundunWorld.flaxproj` 没登记 ItemEditor，且没有 `Binaries` 产物 | 用户截图 + `HundunWorld.flaxproj` 的 References 清单 | 补 References + Flax.Build 两配置产物；新增闸门 `插件已在主工程引用清单里` |
| 2026-10-27 | **工具条按钮压字**：`Button(x,y,0,h)` 在 Flax 里不会按文字撑开，固定 x + 0 宽让长标签压到下一个按钮 | 用户截图 | 改为显式宽度 + 顺序排布；新增闸门 `工具条按钮宽度必须显式给` |
| 2026-10-27 | **导出被一条坏 JSON 打断**：真实库 `base_attributes` 里有 `0.05` 这类小数，`GetInt32()` 抛 `FormatException`，`export-client` 整体失败 | 真库首次导出回执（`Horizon.Game.Sim/Items/ItemCore.cs:461`） | 改为 `TryGetInt32` → `TryGetDouble` 四舍五入/超界跳过；并给单条物品加 try/catch，一件脏行不拖垮整批 |
| 2026-10-27 | **一次"刷新目录"之后所有按钮都报"上一次子进程还没结束"**：`RunList`/`RunRead` 走 `GuardBusy` 但回调没有复位 `_busy` | 用户截图（连续被忽略） | 两个回调每条出口都调 `EndBusy`；`GuardBusy` 加 180 秒看门狗；回调异常也如实打印 |
| 2026-10-27 | **装备 +5000 生命，血条仍 100/100**：服务端 `DeriveMaxHealth` 支持装备参数，但调用点只传了加点；装备变更也不重算血池 | 用户截图 + `CharacterGrain.EnsureServerHealthPool` | 新增 `RefreshServerHealthPoolAsync`（含装备）+ 穿戴/卸下时 `InvalidateCombatProfile`；客户端装备桥移除重复的 MaxHealth/MaxEnergy；`血池重算` 日志留对账 |
| 2026-10-27 | 同一件物品两处品质不一致**：详情弹窗直接用 `def.Quality`（已 0..6），装备槽 Tooltip/槽位边框走 `ToInkQuality` 仍钳 0..4 ⇒ 弹窗"神话"、Tooltip"传说" | 用户截图（同一件默认长剑两个面板） | `ToInkQuality` 改 0..6（Tooltip、槽位边框、背包格共用这一处）；品质名开关补神器/神话 |
| 2026-10-27 | **消耗品/宝石/材料被导出闸门挡下**：专属字段没进导出表 | 导出 110 件 / 65 问题 | 导出表新增 `hpRestore/energyRestore/gemElement/gemAffinity`；覆盖层按号段识别宝石与五行信物；025 补 50001/50002 两行消耗品；导出 **173 件 / 4 问题**（只剩历史背包 2001..2004） |

### 12.3 明确未完成（下一轮目标，不在本轮假装完成）

- 服务端运行时物品目录（Grain 侧校验"模板存在"）。
- 完整多语言（只留 zhCN/enUS 两列）。
- 分解/重铸/幻化等 WoW 扩展系统。
- EF 迁移文件（D10）。
- 插件窗口的目视验收（需要起 Flax 编辑器 + 重启加载插件；本机同时开两个编辑器会损坏 AssetsCache，留到用户侧）。

### 12.4 闸门回执（2026-10-27 实测）

| # | 命令 | 结果 |
| --- | --- | --- |
| G1/G2 | `dotnet test Horizon.Game.Gateway.Tests --filter "FullyQualifiedName~ItemEditor|FullyQualifiedName~CharacterHealthPool"` | **51 通过 / 0 失败**（含写回点、套装 tooltip、号段、引用、按钮宽度、导出闸门、小数 JSON、血池含装备生命、客户端不重复加血） |
| G5b | `Flax.Build -buildTargets=ItemEditorEditorTarget -workspace=<插件目录>` | Debug/Development 各产出 `ItemEditor.CSharp.dll` + `ItemEditorEditor.CSharp.dll` + Build.json；`HundunWorld.flaxproj` 已登记引用，重启编辑器后 Tools 可见 |
| 全量 | `dotnet test ... --no-build -p:BaseOutputPath=bin/Verify/`（三进程占着默认 `obj`/`bin`，见 [[dotnet-build-while-hosts-running]]） | **3817 通过 / 2 失败 / 8 跳过**；两条失败均属并行会话在途文件（`NpcEditorContractTests.NpcVisuals_BothSidesUseTheSameKeys`、`MonsterEditorContractTests.内容普查…`），与物品无关，未去改别人的文件 |
| G3 | `Horizon.ItemCli catalog` / `preview-budget --ilvl 20 --quality 3 --sweep` | `ok=true`；预算 220；20 级曲线严格递增（`strictlyIncreasing=true`） |
| G4 | 演练库 `Game_Item023Probe`：`save` → `get` → `save(更新)` → `publish` → `export-client` | 首次 `writtenRows` 覆盖 10 张表；回读逐字段相等；`contentVersion` 1→2；导出 1 件、0 问题 |
| G4b | 老行（只有 `base_attributes` JSON）`get` → 改名 → `save` | 属性 `Attack=7,CriticalRate=3` 保住并把 JSON 原样重写 |
| G5 | `HundunWorld/Plugins/ItemEditor/_verify.sh` | `rt` 0 错 / `ed` 0 错 |
| G6 | `scripts/verify-client-scripts.sh` | **487 源 / 0 错** |
| G7 | 演练库执行 023 → 断言 11 张从表 + 31 列；重复执行幂等；回滚脚本恢复成 0 表 / 43 列 | 全部成立，演练库已删除 |
| G7b | **真库 `Game` 执行 023（2026-10-27，用户授权）** | 31 新列 + 11 张从表；`ItemTemplate` 共 74 列 / 175 行；重复执行幂等；`list` 175 件；`get 10001`/`get 91001` 成功；`references` 可用 |
| G7c | **真库执行 024 回填（用户授权）** | `updated 19`（19 件装备/外观拿到部位）；`inventory_type>0` 的命中行 19/19 |
| G7d | **真库执行 025（用户授权）** | 新增 50001 金创药（`{"hp":300}`）/ 50002 回气丹（`{"mp":200}`）；编辑器可列可改 |
| G4c | 真库 `export-client`（导出到工程 `Data/Items`） | **173 件 / 4 问题**（只剩历史背包 2001..2004）；消耗品带 300/200，宝石带元素+亲和，五行信物按材料导出 |
| G4d | 同构演练库（真库 schema + 023）做"新建→读回→更新→删除" | `save` 返回 10 张表行数、`get` 逐字段相等；证明"保存到库"链路本身没坏 |
| 工具坑 | Git Bash 里 `sqlcmd -i` 会报「-E 与 -U/-P 互斥」 | 用 `cmd //c "sqlcmd ... -i ..."` 且带 `-f 65001`；`-Q` 直连不受影响 |
