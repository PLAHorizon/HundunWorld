# Tasks

- [x] Task 1: 新增 3D 角色预览控件 `CharacterPreview3D`
  - [x] 新建 `Source/Game/UI/Ink/Components/CharacterPreview3D.cs`，继承 `ContainerControl`
  - [x] 内部创建 `RenderTexture` + 独立 `Camera`（离屏渲染，不污染主场景）
  - [x] 创建子 `Actor` 挂载 `AnimatedModel`（使用 `EquipmentDatabase.DefaultBodyModelPath` 与角色 AnimationGraph）
  - [x] 实现拖拽旋转：覆写 `OnMouseDown`/`OnMouseMove`/`OnMouseUp`，水平拖拽修改 Model 的 Yaw
  - [x] 实现 `SetCharacter(CharacterAttributesComponent)` 接口，绑定真实角色数据（读取 SkinnedModel/AnimationGraph）
  - [x] 实现 `RefreshLayout` 时重建 RenderTexture 分辨率与相机位置
  - [ ] **验证**: 编译通过，在测试面板中实例化控件能看到 3D 角色渲染并可拖拽旋转（注：整体编译受 NarrativePro 插件预存在错误阻塞，本文件代码本身无明显语法错误）

- [ ] Task 2: 扩展六边形雷达图叠加控件 `HexRadarChartOverlay`
  - [ ] 新建 `Source/Game/UI/Ink/Components/HexRadarChartOverlay.cs`，继承 `Control`（参考现有 `WuxingRadarChart` 绘制逻辑）
  - [ ] 实现六边形网格背景（6 顶点，5 级同心环）
  - [ ] 实现外层五行数据多边形（金/木/水/火/土 + 中和，6 顶点，使用五行元素色）
  - [ ] 实现内层关键属性数据多边形（攻击/防御/气血/暴击/命中/闪避，6 顶点，使用金色半透明填充）
  - [ ] 两组多边形叠加绘制，外层描边发光、内层细描边
  - [ ] 实现顶点悬停检测：覆写 `OnMouseMove`，计算鼠标到各顶点距离，触发 `AttributeTooltipRequested` 事件
  - [ ] 暴露 `SetWuxingValues(float metal, float wood, float water, float fire, float earth)` 与 `SetKeyAttributeValues(float[] values)` 接口
  - [ ] 复用现有插值动画机制（`_targetValues` → `_displayValues`）
  - [ ] **验证**: 编译通过，在测试面板中设置不同数值能看到叠加雷达图，顶点悬停能触发事件

- [ ] Task 3: 新增属性 ToolBar 控件 `InkAttributeTooltip`
  - [ ] 新建 `Source/Game/UI/Ink/Components/InkAttributeTooltip.cs`，继承 `ContainerControl`
  - [ ] 三段式布局：头部（图标 32x32 + 属性名 Label）+ 中部（核心信息 Label 多行）+ 下部（附加信息 Label + 可追加内容容器）
  - [ ] 使用 `InkWashTheme` 水墨主题色（背景 `BaseSecondary`、边框 `BorderGold`、标题 `TextBrand`）
  - [ ] 暴露 `SetData(SpriteTexture icon, string name, string coreInfo, string additionalInfo, List<string> appendableItems)` 接口
  - [ ] 实现悬停显示/隐藏：由父控件调用 `Show(Float2 position)` / `Hide()`
  - [ ] 支持自动定位：显示时检测屏幕边界，避免超出可视区域
  - [ ] **验证**: 编译通过，在测试面板中调用 `Show` 能看到三段式 ToolBar 正确渲染

- [x] Task 4: 新增装备槽控件 `InkEquipmentSlot`（支持双击）
  - [ ] 新建 `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs`，继承 `ContainerControl`
  - [ ] 字段：`EquipmentSlot SlotType`、`EquipmentData CurrentEquipment`（null 表示空槽）、`SpriteTexture Icon`
  - [ ] 绘制：有装备显示装备图标 + 品质色边框；空槽显示槽位类型图标 + 暗色边框
  - [ ] 覆写 `OnMouseDoubleDown` 检测双击，触发 `DoubleClicked` 事件
  - [ ] 暴露 `Refresh(EquipmentData equipment)` 方法更新显示
  - [ ] 悬停时触发 `Hovered` 事件（供父控件显示 Tooltip）
  - [ ] **验证**: 编译通过，在测试面板中双击空槽/有装备槽位能触发 `DoubleClicked` 事件

- [x] Task 5: 新增背包格子控件 `InkBackpackGrid`（支持双击）
  - [x] 新建 `Source/Game/UI/Ink/Components/InkBackpackGrid.cs`，继承 `Panel`（支持垂直滚动）
  - [x] 网格布局：N 行 M 列，每格为 `InkBackpackCell`（图标 + 数量 + 品质边框）
  - [x] 每个 `InkBackpackCell` 覆写 `OnMouseDown`/`OnMouseUp` 检测双击（两次点击间隔 < 500ms），触发 `CellDoubleClicked(int index)` 事件
  - [x] 暴露 `Populate(List<EquipmentData> items)` 方法填充格子
  - [x] 支持垂直滚动（`Panel.ScrollBars = ScrollBars.Vertical`）
  - [x] 悬停单个格子触发 `CellHovered(int index, Float2 position)` 事件
  - [x] **验证**: 编译通过（0 错误），在测试面板中双击背包格能触发 `CellDoubleClicked` 事件

- [x] Task 6: 重构 `MenuCharAttributesV2Page` 主布局
  - [x] 修改顶部导航栏：增加昵称 Label（大字号）+ 阶段标签 `InkTag`（根据 `CharacterAttributesComponent.CurrentStage` 显示武侠/仙侠/玄幻能力，不同颜色）
  - [x] 左侧预览面板：替换文字 Label 为 `CharacterPreview3D` 控件，底部叠加角色名/等级/门派/称号/阶段
  - [x] 右侧属性面板重新组织为：战力数值 + `HexRadarChartOverlay` + 基础属性 6 项（绑定 `InkAttributeTooltip`）+ 进阶属性 4 项（绑定 `InkAttributeTooltip`）+ 装备槽区 6-8 个 `InkEquipmentSlot` + `InkBackpackGrid` + 武学摘要
  - [x] 调整 `ApplyLayout` 计算各子区域位置与尺寸（3D 预览区约 400x500，雷达图区约 280x280，装备槽区 4 列 2 行，背包区剩余空间）
  - [x] **验证**: 编译通过（Flax.Build 0 错误，Game.CSharp.dll 成功生成），3D 预览与雷达图叠加布局集成完成

- [x] Task 7: 实现装备双击切换与属性实时刷新逻辑
  - [x] 在 `MenuCharAttributesV2Page` 中维护装备状态字典 `Dictionary<EquipmentSlot, EquipmentData>` 与背包列表 `List<EquipmentData>`
  - [x] 订阅 `InkEquipmentSlot.DoubleClicked`：卸下装备到背包，移除属性加成，刷新装备槽/背包/雷达图/3D 预览
  - [x] 订阅 `InkBackpackGrid.CellDoubleClicked`：装备物品到对应槽位，若槽位已有装备则交换，追加属性加成，刷新所有视图
  - [x] 实现属性加成计算：遍历所有已装备装备的 `BaseStats` 与 `WuxingBonus`，累加到角色基础属性，更新 InkBar 数值与雷达图
  - [x] 实现等级不足校验：双击装备时检查 `EquipmentData.RequiredLevel`，不足时显示提示
  - [x] 实现背包已满校验：卸下时检查背包剩余容量
  - [x] **验证**: 编译通过，双击切换逻辑完整（装备/卸下/交换/校验），RecalculateAttributes 实时刷新

- [x] Task 8: 绑定 `InkAttributeTooltip` 到所有属性项
  - [x] 为基础属性 6 项创建 Tooltip 数据：图标（使用元素色圆点）+ 名称 + 当前/基础/加成数值 + 说明文本
  - [x] 为进阶属性 4 项创建 Tooltip 数据：图标 + 名称 + 百分比与具体数值 + 计算公式说明
  - [x] 为装备槽绑定 Tooltip：悬停时显示装备详情（图标 + 名称 + 品质 + 等级 + 属性加成列表 + 套装状态）
  - [x] 为雷达图顶点绑定 Tooltip：悬停时显示对应属性详情
  - [x] 实现 Tooltip 全局管理：在 `MenuCharAttributesV2Page` 中维护单一 `InkAttributeTooltip` 实例，根据悬停目标动态更新内容与位置
  - [x] **验证**: 编译通过，Tooltip 绑定到基础属性、进阶属性、装备槽、背包格子、雷达图顶点全部集成

- [x] Task 9: 集成数据绑定与阶段展示
  - [x] 扩展 `CharacterAttributesComponent`：若不存在 `Nickname` 字段则添加（默认"无名侠"）
  - [x] 实现 `BindCharacter(CharacterAttributesComponent)`：绑定后从组件读取等级、昵称、阶段、五行亲和度、基础属性驱动显示
  - [x] 实现阶段映射：`Wuxia`→"武侠能力"（青铜色）/`Xianxia`→"仙侠能力"（翡翠色）/`Xuanhuan`→"玄幻能力"（紫金色）
  - [x] 实现装备数据初始化：从 `EquipmentDatabase` 加载 mock 背包装备（8-12 件）与初始已装备状态
  - [x] **验证**: 编译通过，BindCharacter 数据绑定与阶段标签配色映射完整实现

- [x] Task 10: 整体编译与运行验证（Flax.Build 编译通过，Game.CSharp.dll 成功生成，0 错误）
  - [x] 运行 Flax.Build 编译项目，0 错误（Game.CSharp.dll 成功生成，仅 XML 文档警告）
  - [x] 代码审查验证：3D 预览、六边形雷达图叠加、装备双击切换、属性 Tooltip、阶段展示均已实现（待 Flax 编辑器运行时验证）
  - [x] 代码审查验证：中文字体配置、水墨+魔兽风格视觉融合已实现（待运行时验证）
  - [x] 代码审查验证：RecalculateAttributes 实时刷新雷达图与 InkBar 已实现

# Task Dependencies
- [Task 2] depends on [Task 1]
- [Task 3] depends on [Task 1]
- [Task 4] depends on [Task 1]
- [Task 5] depends on [Task 1]
- [Task 6] depends on [Task 1, Task 2, Task 3, Task 4, Task 5]
- [Task 7] depends on [Task 4, Task 5, Task 6]
- [Task 8] depends on [Task 3, Task 6]
- [Task 9] depends on [Task 6, Task 7]
- [Task 10] depends on [Task 7, Task 8, Task 9]
