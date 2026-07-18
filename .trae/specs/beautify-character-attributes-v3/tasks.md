# Tasks

- [ ] Task 1: 扩展 `EquipmentSlot` 枚举至 15 个槽位
  - [ ] 在 `EquipmentData.cs` 中将枚举扩展为：Head, Neck, Shoulder, Back, Body, Waist, Legs, Feet, RightHand, LeftHand, RightRing, LeftRing, RightWrist, LeftWrist, Face
  - [ ] 编译验证 `EquipmentSlot` 改动不破坏现有引用

- [ ] Task 2: 扩展 `EquipmentDatabase` mock 装备以覆盖 15 槽
  - [ ] 为新增槽位各创建 1 件默认/示例装备（共至少 15 件，可复用低品质装备）
  - [ ] 保证每件装备有 `Slot`、`BaseStats`、`WuxingBonus`、`Quality`、`RequiredLevel` 等字段
  - [ ] 更新 `GetEquipment`、`GetAllEquipments`、`GetEquipmentsBySlot`

- [ ] Task 3: 新增春色/墨青色调 Token（可选但推荐）
  - [ ] 在 `InkWashTheme.cs` 中新增 `SpringGreenPrimary`、`SpringGreenBright`、`InkCyanBase`、`PanelSpring` 等颜色
  - [ ] 不破坏现有 Token 使用

- [ ] Task 4: 优化 `InkEquipmentSlot` 视觉（品质发光边框）
  - [ ] 调整默认尺寸为 56×56，支持可选 64×64
  - [ ] 空槽显示水墨风槽位图标（使用文字或简化图形）
  - [ ] 有装备时根据 `Quality` 绘制对应品质色发光边框（普通/优秀/精良/史诗/传说）
  - [ ] 保留双击与悬停事件

- [ ] Task 5: 重构 `MenuCharAttributesV2Page` 为三栏布局
  - [ ] 将布局常量改为三栏：LeftPanelWidthRatio=0.30、CenterPanelWidthRatio=0.40、RightPanelWidthRatio=0.30
  - [ ] 左侧：基础属性（6 项）+ 进阶属性（4 项）+ 雷达图 + 武学摘要
  - [ ] 中间：顶部战力大字 + `CharacterPreview3D` + 底部角色名/等级/门派/称号/阶段
  - [ ] 右侧：15 装备槽人体拓扑布局（5 行 3 列）+ 背包 + 武学摘要/细节切换
  - [ ] 调整所有子控件的 Build 与 Layout 方法

- [ ] Task 6: 升级 `MenuCharAttributesV2Page` 色调与视觉层次
  - [ ] 三个主面板使用墨青半透明背景 + 细金色边框
  - [ ] 分区标题左侧增加金色装饰竖线
  - [ ] 战力数字使用更大字号、居中、鎏金辉光效果
  - [ ] 属性条变细（高度 6-8px）、颜色按五行/语义区分
  - [ ] 文字层级：标题金色、正文纸色、次要信息暗纸色

- [ ] Task 7: 更新装备切换与属性计算逻辑以适配 15 槽
  - [ ] 更新 `DisplayedSlots` 数组为 15 槽顺序
  - [ ] 更新 `InitializeMockEquipment` 初始装备状态
  - [ ] 确保 `EquipFromBackpack`、`OnEquipmentSlotDoubleClicked`、`OnBackpackCellDoubleClicked` 正确处理新槽位
  - [ ] 确保 `RecalculateAttributes` 遍历所有 15 个已装备槽位

- [ ] Task 8: 整体编译验证
  - [ ] 运行 Flax.Build，0 错误
  - [ ] 代码审查确认三栏布局常量、15 槽枚举、mock 数据、绑定逻辑一致

# Task Dependencies
- [Task 2] depends on [Task 1]
- [Task 4] depends on [Task 3]
- [Task 5] depends on [Task 1, Task 2, Task 4]
- [Task 6] depends on [Task 3, Task 5]
- [Task 7] depends on [Task 1, Task 2, Task 5]
- [Task 8] depends on [Task 5, Task 6, Task 7]
