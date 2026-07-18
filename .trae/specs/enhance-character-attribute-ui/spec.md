# 角色属性 UI 优化完善 Spec

## Why
当前 `MenuCharAttributesV2Page` 左侧预览区仅显示文字（角色名/等级/门派/称号），缺乏 3D 角色实时预览；五行雷达图为单一五边形，无法叠加展示关键属性；属性项无悬停提示，玩家难以了解属性详情；装备切换需跳转到独立的 `MenuEquipmentPage` 通过"单击选中+按钮更换"完成，操作割裂且不直观；顶部信息缺少阶段（武侠/仙侠/玄幻能力）展示。为达到燕云十六声与魔兽世界角色面板的体验水准，需要在现有 V2 页面基础上进行缝合式增强。

## What Changes
- **新增 3D 角色预览控件**：在左侧预览面板嵌入实时 3D 角色渲染（使用 `RenderTexture` + 独立 `Camera` + 角色 `AnimatedModel`），支持拖拽旋转
- **扩展六边形雷达图叠加**：将现有 `WuxingRadarChart` 升级为 `HexRadarChartOverlay`，支持双图层叠加（外层五行金木水火土 + 内层关键属性如攻击/防御/气血/暴击/命中/闪避）
- **新增属性 ToolBar 悬停控件**：`InkAttributeTooltip` 三段式结构（头部图标/图片 + 中部核心信息 + 下部附加信息及可追加内容），鼠标悬停属性项时显示
- **装备双击切换**：在角色属性页面集成装备槽与背包双栏，双击装备槽物品卸下到背包，双击背包物品装备到对应槽位，切换后实时刷新角色属性（追加或移除装备属性值）
- **扩展顶部信息**：增加昵称、阶段（武侠/仙侠/玄幻能力）展示，阶段根据 `CharacterAttributesComponent.CurrentStage` 与等级动态显示
- **融合燕云十六声 + 魔兽世界风格**：采用魔兽世界属性面板的纸娃娃+装备槽布局骨架，叠加燕云十六声的水墨/金属蚀刻视觉语言
- **本阶段不做**：3D 预览的换装联动（仅展示当前角色外观）、网络同步属性面板、属性历史曲线

## Impact
- Affected specs:
  - `character-attribute-panel`（已完成，本 spec 为其增强版，不修改其原有交付物）
  - `character-equipment-system`（已存在，本 spec 复用其 `EquipmentData`/`EquipmentSlot` 数据模型）
- Affected code:
  - `Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs` — 主重构对象，新增 3D 预览、雷达图叠加、装备双击切换、阶段展示
  - `Source/Game/UI/Components/WuxingRadarChart.cs` — 扩展或新建 `HexRadarChartOverlay` 支持双图层叠加
  - `Source/Game/UI/Ink/Components/InkAttributeTooltip.cs` — 新增三段式属性 ToolBar 控件
  - `Source/Game/UI/Ink/Components/CharacterPreview3D.cs` — 新增 3D 角色预览控件（RenderTexture + Camera + AnimatedModel）
  - `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs` — 新增/扩展装备槽控件，支持双击事件
  - `Source/Game/UI/Ink/Components/InkBackpackGrid.cs` — 新增背包格子控件，支持双击装备
  - `Source/Game/Character/Attributes/CharacterAttributesComponent.cs` — 可能扩展昵称字段（若不存在）
  - `Source/Game/Equipment/EquipmentDatabase.cs` — 复用现有数据，可能补充 mock 背包装备
  - `Source/Game/Services/CharacterPersistenceService.cs` — 可能扩展昵称持久化

## ADDED Requirements

### Requirement: 3D 角色实时预览
系统 SHALL 在角色属性面板左侧预览区嵌入实时 3D 角色渲染，玩家可拖拽旋转查看角色。

#### Scenario: 打开角色面板显示 3D 预览
- **WHEN** 玩家打开角色属性面板
- **THEN** 左侧预览区显示当前角色的 3D 模型（使用角色的 SkinnedModel 与 AnimationGraph），模型居中、等比缩放适配预览区，默认正面朝向相机

#### Scenario: 拖拽旋转角色
- **WHEN** 玩家在 3D 预览区按下鼠标左键并拖拽
- **THEN** 角色 Model 绕 Y 轴旋转，旋转角度与鼠标水平拖拽距离成正比；松开鼠标后保持当前角度

#### Scenario: 预览区尺寸变化
- **WHEN** 屏幕尺寸变化触发 `RefreshLayout`
- **THEN** 3D 预览的 RenderTexture 分辨率与预览控件尺寸同步调整，相机位置/视锥重新计算以保持角色居中

### Requirement: 六边形雷达图叠加展示
系统 SHALL 以六边形雷达图形式叠加展示五行属性与关键属性两组数据。

#### Scenario: 显示叠加雷达图
- **WHEN** 角色属性面板可见
- **THEN** 雷达图区域显示两组六边形数据：外层为五行（金/木/水/火/土，5 顶点映射到六边形 5 顶点 + 1 中心点或采用 6 顶点含"中和"），内层为关键属性（攻击/防御/气血/暴击/命中/闪避，6 顶点）；两组数据使用不同填充色与描边色区分

#### Scenario: 鼠标悬停顶点
- **WHEN** 鼠标悬停在雷达图某顶点附近
- **THEN** 显示该顶点对应属性的 `InkAttributeTooltip`（头部属性图标 + 中部属性名与数值 + 下部属性来源说明）

#### Scenario: 属性变化刷新
- **WHEN** 角色五行或关键属性因装备切换而改变
- **THEN** 雷达图带动画过渡到新数值（复用现有 `WuxingRadarChart` 的插值动画机制）

### Requirement: 属性 ToolBar 悬停提示
系统 SHALL 为所有属性项提供三段式 ToolBar 悬停提示。

#### Scenario: 悬停基础属性
- **WHEN** 鼠标悬停在基础属性（如气血/体魄/内力/身法/根骨/悟性）行上
- **THEN** 显示 `InkAttributeTooltip`，结构为：
  - 头部：属性图标（如气血对应朱红色心形图标）+ 属性中文名
  - 中部：当前数值 / 基础数值 / 装备加成（如 "3200 / 2800 / +400"）
  - 下部：附加信息（属性说明文本）+ 可追加内容占位（如来源装备列表，初始为空或显示"无加成来源"）

#### Scenario: 悬停进阶属性
- **WHEN** 鼠标悬停在进阶属性（暴击/抗暴/命中/闪避）上
- **THEN** 显示对应 ToolBar，中部展示百分比与具体数值（如 "23% (230/1000)"），下部展示计算公式说明

#### Scenario: 悬停五行属性
- **WHEN** 鼠标悬停在雷达图五行顶点或五行属性标签上
- **THEN** 显示 ToolBar，头部为元素图标（金/木/水/火/土），中部为亲和度数值与等级（如 "1520 · 中阶"），下部为元素相生相克关系说明

#### Scenario: 悬停装备槽
- **WHEN** 鼠标悬停在装备槽上
- **THEN** 显示 ToolBar，头部为装备图标，中部为装备名 + 品质 + 等级要求，下部为装备属性加成列表与套装状态

### Requirement: 装备双击切换
系统 SHALL 支持在角色属性面板内双击装备槽与背包物品进行装备切换，切换后实时更新角色属性。

#### Scenario: 双击背包物品装备
- **WHEN** 玩家双击背包中的可装备物品
- **THEN** 该物品装备到对应槽位；若该槽位已有装备，原装备返回背包；角色属性面板立即追加该装备的属性加成（BaseStats 与 WuxingBonus）；3D 预览角色外观更新（若装备影响外观）；装备槽与背包格子状态同步刷新

#### Scenario: 双击装备槽卸下
- **WHEN** 玩家双击已装备的装备槽
- **THEN** 该装备卸下并返回背包（背包有空位时）；角色属性面板立即移除该装备的属性加成；装备槽显示为空槽；3D 预览角色外观更新

#### Scenario: 背包已满无法卸下
- **WHEN** 玩家双击装备槽但背包已满
- **THEN** 不执行卸下操作，显示提示"背包已满，无法卸下"

#### Scenario: 等级不足无法装备
- **WHEN** 玩家双击背包中需求等级高于当前角色等级的装备
- **THEN** 不执行装备操作，显示提示"等级不足，需要 Lv.X"

### Requirement: 顶部信息扩展（昵称+等级+阶段）
系统 SHALL 在角色属性面板顶部展示角色昵称、等级与成长阶段。

#### Scenario: 显示完整顶部信息
- **WHEN** 角色属性面板可见
- **THEN** 顶部导航栏依次显示：返回按钮 + 头像 + 昵称（大字号）+ 等级标签（Lv.X）+ 门派 + 阶段标签（武侠/仙侠/玄幻能力，使用不同颜色区分）

#### Scenario: 阶段根据数据动态显示
- **WHEN** `CharacterAttributesComponent.CurrentStage` 为 `Wuxia`/`Xianxia`/`Xuanhuan`
- **THEN** 阶段标签分别显示"武侠能力"（青铜色）/"仙侠能力"（翡翠色）/"玄幻能力"（紫金色），并附带阶段描述 tooltip

### Requirement: 燕云十六声 + 魔兽世界风格融合
系统 SHALL 融合燕云十六声的水墨视觉语言与魔兽世界的纸娃娃布局骨架。

#### Scenario: 整体视觉风格
- **WHEN** 角色属性面板渲染
- **THEN** 采用魔兽世界布局骨架（左侧纸娃娃 3D 预览 + 装备槽环绕 + 右侧属性列表 + 底部背包），叠加燕云十六声视觉元素（水墨晕染背景、金属蚀刻边框、五行色彩、繁体竖排标题）

## MODIFIED Requirements

### Requirement: MenuCharAttributesV2Page 左侧预览面板
**原有**: 左侧预览面板仅显示角色名/等级/门派/称号文字 Label。

**修改为**: 左侧预览面板顶部为 3D 角色实时预览控件（`CharacterPreview3D`），底部叠加角色名/等级/门派/称号/阶段信息；3D 预览支持拖拽旋转。

### Requirement: MenuCharAttributesV2Page 右侧属性面板
**原有**: 右侧属性面板包含战力数值 + 6 项基础属性 InkBar + 4 项进阶属性 + 6 槽装备摘要 + 3 项武学摘要，装备摘要仅显示装备名 Label。

**修改为**: 右侧属性面板重新组织为：
- 顶部战力数值（保留）
- 六边形雷达图叠加区（`HexRadarChartOverlay`，替代原五行雷达图位置）
- 基础属性 6 项（保留，每项绑定 `InkAttributeTooltip`）
- 进阶属性 4 项（保留，每项绑定 `InkAttributeTooltip`）
- 装备槽区（6-8 槽 `InkEquipmentSlot`，支持双击卸下，替代原装备摘要 Label）
- 背包格子区（`InkBackpackGrid`，支持双击装备，新增）
- 武学摘要 3 项（保留）

### Requirement: WuxingRadarChart
**原有**: 五边形雷达图，仅展示五行属性（金木水火土）。

**修改为**: 升级为 `HexRadarChartOverlay`（或新增控件并废弃旧控件），支持六边形双图层叠加：外层五行 + 内层关键属性，两组数据使用不同色彩区分，顶点支持悬停 Tooltip。

### Requirement: 装备切换交互
**原有**: 装备切换需跳转到 `MenuEquipmentPage`，通过单击选中 + "更换装备"按钮完成。

**修改为**: 装备切换直接在 `MenuCharAttributesV2Page` 内通过双击完成（双击背包装备 / 双击装备槽卸下），无需跳转页面；`MenuEquipmentPage` 保留作为高级装备管理入口（筛选/对比/套装详情）。

## REMOVED Requirements
无
