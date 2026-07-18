# Tasks

- [x] Task 1: 定义角色属性面板数据结构与常量
  - [x] 在 `Source/Game/Character/Attributes/`（或合适目录）新增 `CharacterAttributes.cs`，定义角色基础属性（力量、敏捷、智力、体质、攻击力、防御力等）与五行属性（金、木、水、火、土）的数据结构
  - [x] 扩展 `CharacterPersistenceService` 中角色数据，支持读写五行属性与背包列表
  - [x] 在 `EquipmentDatabase` 中补充装备图标 `IconPath` 与基础属性加成字段，确保默认装备有可用数据
  - [x] **验证**: 编译通过，能从持久化服务读取模拟的五行与背包数据

- [x] Task 2: 实现五行雷达图自定义控件
  - [x] 新建 `Source/Game/UI/Components/WuxingRadarChart.cs`，继承 `Control`
  - [x] 实现五边形/六边形背景网格绘制（5 个顶点对应金木水火土，可选中心点构成六边形）
  - [x] 实现根据五行数值绘制填充多边形与顶点标记
  - [x] 实现每个顶点外侧的属性名称与数值标签绘制
  - [x] 暴露 `SetValues(float metal, float wood, float water, float fire, float earth)` 接口
  - [x] **验证**: 在测试 UI 中实例化控件，设置不同数值后能看到正确雷达图

- [x] Task 3: 实现装备插槽视图控件
  - [x] 新建 `Source/Game/UI/Components/EquipmentSlotView.cs`，继承 `Control` 或 `Panel`
  - [x] 字段：`SlotType`（头盔、衣服、手套、裤子、鞋子、项链、戒指、主手、副手等）、当前装备数据、空槽提示文本
  - [x] 实现图标显示（有装备显示装备图标，无装备显示空槽占位图标）
  - [x] 实现鼠标悬停提示（可选：显示装备名称或槽位名称）
  - [x] 暴露 `Clicked` 事件与 `Refresh(EquipmentData current)` 方法
  - [x] **验证**: 编译通过，在测试面板中点击空槽/有装备槽位能触发事件

- [x] Task 4: 扩展 InventoryUI 支持内嵌背包
  - [x] 读取 `Source/Game/UI/GameMain/InventoryUI.cs`，分析现有结构
  - [x] 新增 `PopulateEmbeddedPanel(Panel container, List<InventoryItemData> items, Action<int> onItemClick)` 方法，将背包格子列表填充到指定容器
  - [x] 每个格子显示物品图标与数量；点击时回调物品索引/ID
  - [x] 支持格子容器溢出时滚动（使用 Panel.ScrollBars 垂直滚动）
  - [x] **验证**: 编译通过，在角色面板中能看到背包格子并可点击

- [x] Task 5: 扩展 EquipmentComparisonUI 支持内嵌预览
  - [x] 读取 `Source/Game/UI/GameMain/EquipmentComparisonUI.cs`，分析现有结构
  - [x] 新增 `PopulateEmbeddedPreview(Panel container, EquipmentData current, EquipmentData selected, Action onEquip, Action onUnequip)` 方法
  - [x] 显示选中装备与当前装备的关键属性对比（如攻击力、防御力、五行加成）
  - [x] 根据情况显示“穿戴”或“卸下”按钮，并绑定回调
  - [x] **验证**: 编译通过，在角色面板中点击装备能显示对比视图

- [x] Task 6: 重构 GameMainUI 角色面板布局
  - [x] 读取 `Source/Game/UI/GameMain/GameMainUI.cs` 的 `PopulateCharacterPanel` 方法
  - [x] 将面板划分为三栏：左侧属性+装备区、中间角色预览+装备对比、右侧背包+五行雷达图
  - [x] 集成 `EquipmentSlotView` 显示当前穿戴装备
  - [x] 集成 `WuxingRadarChart` 显示角色五行
  - [x] 集成 `InventoryUI.PopulateEmbeddedPanel` 显示背包
  - [x] 绑定插槽与背包点击事件到 `EquipmentComparisonUI` 预览
  - [x] 从 `CharacterPersistenceService` 与 `EquipmentDatabase` 读取真实数据驱动显示
  - [x] **验证**: 编译通过，在 Flax 编辑器中打开 Character 面板能看到完整布局

- [x] Task 7: 数据绑定与刷新机制
  - [x] 在 `CharacterAttributePanelController`（或直接在 `GameMainUI` 中）实现 `Refresh()` 方法，从服务读取最新数据
  - [x] 打开角色面板时自动刷新
  - [x] 穿戴/卸下装备后刷新装备插槽、背包、雷达图
  - [x] **验证**: 在游戏中切换装备后重新打开面板，数据正确更新

- [x] Task 8: 整体编译与运行验证
  - [x] 运行 Flax.Build 编译项目，0 错误
  - [x] 在 Flax 编辑器中进入 World 场景，打开角色面板
  - [x] 确认装备插槽、背包、五行雷达图、装备对比均正常显示，中文无乱码

# Task Dependencies
- [Task 2] depends on [Task 1]
- [Task 3] depends on [Task 1]
- [Task 4] depends on [Task 1]
- [Task 5] depends on [Task 1, Task 3]
- [Task 6] depends on [Task 2, Task 3, Task 4, Task 5]
- [Task 7] depends on [Task 6]
- [Task 8] depends on [Task 7]
