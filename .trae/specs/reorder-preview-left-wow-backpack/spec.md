# 角色属性 UI 二次重组与魔兽世界式背包系统 Spec

## Why

当前角色属性页 V2 已按上一轮 spec 将装备区合并到中间面板、角色信息区合并到左侧面板，但用户根据最新截图反馈：左侧面板顶部战力区（红框）与角色信息区（蓝框）需要互换位置；中间面板的红圈黑屏 3D 预览占位需用正确的角色 3D 实时预览替换，并与下方的装备槽区域合并为一个整体，装备槽位置需合理布局（不能遮挡 3D 预览区域）；同时背包系统需要仿照魔兽世界，改为 6×6 默认格子 + 背包槽 + 背包装备后扩展格子的结构。

## What Changes

### UI 区域再次重组
- **红框与蓝框互换位置**：左侧面板顶部从"战力区 → 角色信息区"改为"角色信息区 → 战力区"。左侧面板不再放置 3D 预览。
- **中间面板 3D 预览正确渲染**：修复 `CharacterPreview3D` 黑屏问题（已在上一轮 `merge-and-swap-char-ui-regions` 中处理），确保中间面板红圈区域显示真正的角色 3D 实时预览。
- **中间面板 3D 预览与装备槽合并布局**：将 3D 预览控件与下方装备槽区域合并为一个整体区域，装备槽围绕 3D 预览合理布局（借鉴魔兽世界角色面板：3D 预览居中，装备槽分布在 3D 预览下方或两侧的合理位置），**不得遮挡 3D 预览渲染区域**。
- **装备槽布局重构**：从当前纸娃娃式人体拓扑布局改为更紧凑的网格/环形布局，确保与 3D 预览共存且不遮挡。

### 魔兽世界式背包系统
- **默认 6×6 背包格**：右侧背包区域默认显示 36 个格子（6 列 × 6 行）。
- **背包槽（Bag Slots）**：在背包区域顶部或底部新增 4 个背包槽位，用于装备"背包"类型物品。
- **扩展格子**：每个已装备的背包物品按其 `ExtraSlots` 字段扩展可用格子数；卸下背包后对应扩展格子被回收（若扩展格中有物品则提示"请先清空背包"）。
- **背包类型物品**：新增 `EquipmentType.Bag` 与 `EquipmentSlot.Bag`。

### 数据层扩展
- **InventoryComponent 扩展**：新增 `BagSlots` 列表记录已装备背包（槽位索引 + 模板ID + 扩展格子数）。
- **InventorySystem 扩展**：新增 `EquipBag`、`UnequipBag` 方法，维护 `TotalCapacity = BaseCapacity + Σ(BagSlots.ExtraSlots)`。
- **EquipmentData 扩展**：新增 `ExtraSlots`（背包扩展格子数，非背包为 0）。
- **EquipmentDatabase 扩展**：新增若干 mock 背包物品。

### 布局比例调整
- **BREAKING**：三栏宽度比例可能需调整以适应新布局。中间面板需容纳 3D 预览 + 装备槽（合并后可能更紧凑），左侧面板不再有 3D 预览（可适当收窄），右侧面板背包格数增加（6×6）可能需要加宽。具体比例由实现时根据内容尺寸决定。

## Impact

- **Affected specs**：
  - `merge-and-swap-char-ui-regions`（刚完成，本轮在其基础上继续调整，不回滚）
  - `deep-beautify-char-attributes-v5`（V5 美化效果保留）
  - `enhance-character-attribute-ui`（3D 预览功能复用，位置不变仍在中间）
- **Affected code**：
  - [MenuCharAttributesV2Page.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Pages\Character\MenuCharAttributesV2Page.cs) — 红蓝框互换、中间面板 3D 预览与装备槽合并布局、装备槽布局重构、背包区重构
  - [CharacterPreview3D.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Components\CharacterPreview3D.cs) — 确保中间面板尺寸下正常渲染（黑屏修复已应用）
  - [InventoryComponent.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\ECS\Components\InventoryComponent.cs) — 增加 BagSlots 与容量计算
  - [InventorySystem.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\ECS\Systems\InventorySystem.cs) — 增加 EquipBag/UnequipBag
  - [EquipmentData.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\Equipment\EquipmentData.cs) — 增加 ExtraSlots 与 EquipmentType.Bag
  - [EquipmentDatabase.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\Equipment\EquipmentDatabase.cs) — 增加 mock 背包数据
  - [InkBackpackGrid.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Components\InkBackpackGrid.cs) — 支持 6×6 默认格、背包槽、扩展格显示

## ADDED Requirements

### Requirement: 红框与蓝框互换位置

系统 SHALL 将左侧面板顶部的战力区（红框）与角色信息区（蓝框）互换位置，使角色信息区位于战力区上方。左侧面板不放置 3D 预览。

#### Scenario: 左侧面板顺序
- **WHEN** 角色属性页显示
- **THEN** 左侧面板从上到下依次为：角色信息区 → 战力区 → 基础属性 → 进阶属性 → 雷达图
- **AND** 角色信息区的角色名 text-shadow、等级辉光、门派 InkTag、称号渐变装饰线等视觉效果保留
- **AND** 战力区的 GlowLabel 辉光、阶段标签、战力加成等视觉效果保留

### Requirement: 中间面板 3D 预览与装备槽合并布局

系统 SHALL 在中间面板将角色 3D 实时预览控件与装备槽区域合并为一个整体，3D 预览居中显示，装备槽围绕 3D 预览合理布局，不得遮挡 3D 预览渲染区域。

#### Scenario: 3D 预览正确渲染
- **WHEN** 角色属性页显示
- **THEN** 中间面板红圈区域显示角色 3D 实时预览（非黑屏）
- **AND** 模型居中显示，相机距离与 FOV 合理
- **AND** 鼠标左键水平拖拽可旋转角色，松开后保持角度

#### Scenario: 装备槽与 3D 预览合并布局
- **WHEN** 角色属性页显示
- **THEN** 中间面板同时显示 3D 预览控件与装备槽
- **AND** 3D 预览居中显示，装备槽分布在 3D 预览下方或两侧的合理位置
- **AND** 装备槽不遮挡 3D 预览渲染区域（装备槽位于 3D 预览控件边界之外，或 3D 预览控件尺寸预留出装备槽空间）
- **AND** 15 个装备槽全部可见且可交互（hover、双击换装、Tooltip）

#### Scenario: 装备槽布局借鉴魔兽世界
- **WHEN** 实现装备槽布局
- **THEN** 装备槽采用紧凑网格或环形布局（参考魔兽世界角色面板：3D 预览居中，装备槽环绕在下方/两侧）
- **AND** 不再使用纸娃娃式人体拓扑背景
- **AND** 装备槽按类型分组排列（如头部/颈部/肩部一组，手部/腰部/腿部一组，主手/副手一组，戒指/手腕一组）

#### Scenario: 角色数据绑定
- **WHEN** 本地玩家数据就绪并调用 `BindCharacter`
- **THEN** 3D 预览加载玩家 Actor 的 SkinnedModel 与 AnimationGraph
- **AND** 若玩家数据未就绪，回退到默认模型

### Requirement: 魔兽世界式背包系统 — 默认 6×6 格子

系统 SHALL 将右侧背包区域默认显示为 6 列 × 6 行 = 36 个背包格子。

#### Scenario: 默认背包格子
- **WHEN** 角色属性页显示
- **THEN** 右侧背包区域显示 36 个默认格子（6 列 × 6 行）
- **AND** 空格子显示暗色边框，有物品的格子显示图标与品质色边框

### Requirement: 魔兽世界式背包系统 — 背包槽

系统 SHALL 在背包区域顶部或底部新增 4 个背包槽位，用于装备"背包"类型物品。

#### Scenario: 背包槽显示
- **WHEN** 角色属性页显示
- **THEN** 背包区域显示 4 个背包槽位
- **AND** 空背包槽显示"背包槽"提示
- **AND** 双击背包槽可卸下已装备背包
- **AND** 背包槽支持 Tooltip 显示

### Requirement: 魔兽世界式背包系统 — 背包装备后扩展格子

系统 SHALL 在装备背包物品后，按背包的 `ExtraSlots` 字段扩展可用格子数；卸下背包后回收对应扩展格子。

#### Scenario: 装备背包扩展格子
- **WHEN** 玩家将背包物品双击装备到背包槽
- **THEN** 背包区域新增该背包对应的扩展格子（如 +6、+12、+18 格）
- **AND** 扩展格子与默认格子视觉上可区分（如边框颜色、背景色）
- **AND** 总可用容量 = 36 + Σ(已装备背包 ExtraSlots)

#### Scenario: 卸下背包回收格子
- **WHEN** 玩家双击已装备背包槽卸下背包
- **AND** 该背包对应的扩展格子中没有任何物品
- **THEN** 对应扩展格子被移除，总容量减少

#### Scenario: 卸下背包但格子非空
- **WHEN** 玩家尝试卸下已装备背包
- **AND** 该背包对应的扩展格子中有物品
- **THEN** 显示提示"请先清空扩展背包中的物品"
- **AND** 不允许卸下

### Requirement: 背包类型物品数据支持

系统 SHALL 支持"背包"类型装备数据，允许配置扩展格子数。

#### Scenario: 背包装备数据
- **WHEN** 定义一个背包物品
- **THEN** 可配置 `EquipmentType.Bag` 或等效标识
- **AND** 可配置 `ExtraSlots`（≥ 0）
- **AND** 背包物品可像普通装备一样有图标、名称、品质、描述

## MODIFIED Requirements

### Requirement: InventoryComponent 支持背包槽

`InventoryComponent` SHALL 在保留现有 `Items`、`Capacity`、`NextSlotIndex` 的基础上，新增以下成员：
- `public const int BaseCapacity = 36;` — 默认 6×6 格子数
- `public List<EquippedBag> BagSlots;` — 已装备背包列表（槽位索引 + 模板ID + ExtraSlots）
- `public int TotalCapacity => BaseCapacity + (BagSlots?.Sum(b => b.ExtraSlots) ?? 0);` — 总容量
- `public bool IsFull => CurrentCount >= TotalCapacity;`

`TryAddItem` SHALL 使用 `TotalCapacity` 判断容量上限，并在添加时优先填充默认格子，再填充扩展格子。

### Requirement: InventorySystem 支持装备/卸下背包

`InventorySystem` SHALL 新增以下方法：
- `bool EquipBag(World world, ulong playerId, int bagTemplateId, int bagSlotIndex)` — 将背包物品装备到指定背包槽
- `bool UnequipBag(World world, ulong playerId, int bagSlotIndex)` — 从指定背包槽卸下背包
- `int GetTotalCapacity(World world, ulong playerId)` — 获取总容量

装备背包时 SHALL 校验：
1. 背包槽索引在有效范围内（0-3）
2. 目标背包槽为空
3. 背包物品存在且为背包类型
4. 装备后总容量不超过最大值（如 108）

卸下背包时 SHALL 校验：
1. 目标背包槽非空
2. 该背包对应的扩展格子中无物品（否则提示）

### Requirement: InkBackpackGrid 支持动态容量与背包槽

`InkBackpackGrid` SHALL 支持：
- 默认列数 6（可通过 `Columns` 设置）
- 总容量 `Capacity` 动态变化（默认 36 + 扩展格）
- 新增 `BagSlots` 集合，显示 4 个背包槽位
- `Populate` 方法接受 `List<EquipmentData>` 和 `List<EquippedBag>`，正确渲染默认格子和扩展格子
- 背包槽双击事件 `BagSlotDoubleClicked`（参数：槽位索引）
- 扩展格子与默认格子视觉区分

### Requirement: 中间面板装备槽布局重构

`MenuCharAttributesV2Page` 的 `LayoutEquipmentSlots` SHALL 从纸娃娃式人体拓扑布局改为紧凑网格/环形布局，使装备槽与 3D 预览合并显示且不遮挡 3D 预览区域。装备槽按类型分组（头部/颈部/肩部/背部/身体/腰部/腿部/足部/主手/副手/戒指×2/手腕×2/面部），紧凑排列在 3D 预览下方或两侧。

## REMOVED Requirements

无
