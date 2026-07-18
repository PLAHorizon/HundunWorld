# 角色属性面板 Spec

## Why
当前 `GameMainUI` 的 "Character" 面板仅展示基础属性列表，缺少完整的角色信息呈现：背包物品、装备插槽、装备穿戴预览以及体现东方玄幻风格的五行（金木水火土）六边形雷达图。为提升玩家对自身角色状态的感知，需要新增一个集成上述功能的角色属性面板。

## What Changes
- 扩展 `GameMainUI.PopulateCharacterPanel`，将角色面板划分为：属性概览、装备插槽、穿戴预览、背包、五行雷达图五个区域
- 新增 `WuxingRadarChart` 自定义绘制控件：六边形雷达图，展示金/木/水/火/土五项数值
- 新增 `EquipmentSlotView` 控件：显示单个装备插槽，支持当前装备图标、空槽提示、点击交互
- 扩展 `InventoryUI`：提供可在角色面板内嵌使用的背包格子列表（只读/交互模式）
- 扩展 `EquipmentComparisonUI`：支持在角色面板中预览选中装备与当前穿戴装备的属性对比
- 扩展 `CharacterPersistenceService`/`EquipmentDatabase`：为面板提供当前角色属性、装备、背包数据来源
- **本阶段不做**：拖拽换装备、装备模型 3D 旋转预览、网络同步其他玩家面板数据

## Impact
- Affected code:
  - `Source/Game/UI/GameMain/GameMainUI.cs` — 角色面板内容填充
  - `Source/Game/UI/GameMain/InventoryUI.cs` — 背包列表内嵌支持
  - `Source/Game/UI/GameMain/EquipmentComparisonUI.cs` — 装备对比预览
  - `Source/Game/UI/Components/WuxingRadarChart.cs` — 新增五行雷达图绘制控件
  - `Source/Game/UI/Components/EquipmentSlotView.cs` — 新增装备插槽控件
  - `Source/Game/Equipment/EquipmentDatabase.cs` — 提供装备图标与属性数据
  - `Source/Game/Services/CharacterPersistenceService.cs` — 提供角色属性与背包数据
  - `Source/Game/UI/UIHelper.cs` — 可能新增雷达图/插槽辅助样式方法

## ADDED Requirements

### Requirement: 角色属性面板布局
系统 SHALL 在玩家打开角色面板（GameMainUI 的 Character 页签）时，展示一个结构化的角色属性界面。

#### Scenario: 打开角色面板
- **WHEN** 玩家点击主界面角色按钮或按对应快捷键
- **THEN** GameMainUI 显示 Character 面板，包含左侧属性/装备区、中间角色预览区、右侧背包与五行雷达图区

### Requirement: 装备插槽展示
系统 SHALL 在角色面板中显示当前角色所有装备插槽，并反映每个插槽的穿戴状态。

#### Scenario: 已穿戴装备
- **WHEN** 某个装备槽（如头盔、衣服、武器）已装备物品
- **THEN** 该槽位显示装备图标与名称；鼠标悬停显示装备tips

#### Scenario: 空装备槽
- **WHEN** 某个装备槽未装备物品
- **THEN** 该槽位显示空槽图标与槽位名称（如 "头盔"、"武器"）

### Requirement: 装备预览与对比
系统 SHALL 支持点击装备插槽或背包中的装备时，弹出/内嵌装备属性对比视图。

#### Scenario: 点击已穿戴装备
- **WHEN** 玩家点击已穿戴装备
- **THEN** 显示该装备的详细属性与“卸下”按钮

#### Scenario: 点击背包中可装备物品
- **WHEN** 玩家点击背包中可装备的物品
- **THEN** 显示该装备与当前同槽位装备的属性对比，并提供“穿戴”按钮

### Requirement: 背包展示
系统 SHALL 在角色面板中内嵌显示当前角色的背包物品。

#### Scenario: 查看背包
- **WHEN** 角色面板可见
- **THEN** 右侧显示可滚动的背包格子列表，每个格子显示物品图标与数量；点击格子可触发预览/对比

### Requirement: 五行雷达图
系统 SHALL 在角色面板中以六边形雷达图形式展示角色五行属性（金、木、水、火、土）。

#### Scenario: 显示五行数值
- **WHEN** 角色面板可见
- **THEN** 雷达图五个顶点分别对应金、木、水、火、土，顶点距中心距离正比于该属性数值；图表下方显示各属性具体数值

#### Scenario: 属性变化刷新
- **WHEN** 角色五行属性因装备/等级变化而改变
- **THEN** 雷达图在下一次打开或刷新时反映最新数值

### Requirement: 数据绑定
系统 SHALL 从持久化服务与装备数据库读取数据，驱动面板显示。

#### Scenario: 进入 World 场景后打开面板
- **WHEN** 玩家进入游戏并打开角色面板
- **THEN** 面板显示当前角色的真实属性、装备、背包与五行数据

## MODIFIED Requirements

### Requirement: GameMainUI.Character 面板
**原有**: 角色面板仅展示基础属性文本列表。

**修改为**: 角色面板采用分栏布局，集成属性概览、装备插槽、装备预览、背包、五行雷达图。

### Requirement: InventoryUI
**原有**: InventoryUI 可能是独立浮窗或未完整实现。

**修改为**: InventoryUI 支持以内嵌模式（embedded mode）填充到指定 Panel 中，提供背包格子列表与点击回调。

### Requirement: EquipmentComparisonUI
**原有**: 装备对比界面为独立面板。

**修改为**: 装备对比界面支持以内嵌预览模式显示在角色面板中，接收“当前装备”与“待选装备”两个数据源。

## REMOVED Requirements
无
