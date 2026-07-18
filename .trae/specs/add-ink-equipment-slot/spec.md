# 装备槽控件 InkEquipmentSlot Spec

## Why
当前 `MenuEquipmentPage` 装备槽使用通用 `InkCell` 控件，仅支持图标 + 品质色边框 + 数量徽章，缺少装备槽专属的交互能力（双击卸下/换装、悬停查看详情、空槽类型提示）。为支撑燕云十六声风格的装备管理体验，需要一个专用的装备槽控件，统一承载已装备/空槽两种状态的视觉与交互，并通过事件把交互意图上抛给宿主页面。

## What Changes
- 新增 `InkEquipmentSlot` 控件，继承 `FlaxEngine.GUI.ContainerControl`，位于 `HundunWorld.Game.UI.Ink.Components` 命名空间
- 新增文件 `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs`
- 提供装备态绘制（图标 80% 区域 + 品质色边框 2px + 右下角等级标签）
- 提供空槽态绘制（暗色边框 1px + 槽位类型中文名居中）
- 提供双击检测（两次点击间隔 < 500ms 触发 `DoubleClicked` 事件）
- 提供悬停事件 `Hovered`（槽位类型 + 鼠标位置）/ `HoverEnded`
- 提供 `Refresh(EquipmentData)` 接口刷新槽位内容
- 将新文件追加到 `Source/Game.csproj` 的 `<Compile Include>`

## Impact
- Affected specs:
  - `land-inkwash-ui-foundation`（Ink 组件库，本控件为其补充装备槽专用控件）
  - `land-inkwash-ui-content`（`MenuEquipmentPage` 当前使用 `InkCell`，后续可选择性替换为 `InkEquipmentSlot`，本 spec 不强制改动现有页面）
  - `character-equipment-system`（提供 `EquipmentData` / `EquipmentSlot` 数据模型，本控件消费这些数据）
- Affected code:
  - `Source/Game/UI/Ink/Components/InkEquipmentSlot.cs` — 新建控件
  - `Source/Game.csproj` — 追加 `<Compile Include>` 条目
  - `Source/Game/UI/StyleSystem/InkWashTheme.cs` — 仅引用，不改动
  - `Source/Game/UI/Ink/InkBackgrounds.cs`（`InkRenderHelper`）— 仅引用 `GetFontRef`，不改动
  - `Source/Game/Equipment/EquipmentData.cs` — 仅引用数据模型，不改动

## ADDED Requirements

### Requirement: 装备槽控件数据与构造
系统 SHALL 提供一个继承 `ContainerControl` 的装备槽控件，默认尺寸 64×64，构造函数初始化背景色为 `InkWashTheme.BaseTertiary` 且 `ClipChildren = true`。

#### Scenario: 构造默认值
- **WHEN** `new InkEquipmentSlot()` 被调用
- **THEN** 控件 `Width=64`、`Height=64`、`BackgroundColor = InkWashTheme.BaseTertiary`、`ClipChildren = true`
- **AND** `SlotType` 默认为 `EquipmentSlot.Body`，`CurrentEquipment` 为 null，`Icon`/`EmptySlotIcon` 为 `SpriteHandle.Invalid`

### Requirement: 装备态绘制
系统 SHALL 在槽位持有装备时绘制装备图标（填充槽位内部 80% 区域）、品质色边框（2px）、右下角等级标签（`ItemLevel` 文本，Heading 字体）。

#### Scenario: 已装备绘制
- **WHEN** `CurrentEquipment` 非 null 且 `Icon.IsValid` 为 true
- **THEN** 调用 `Render2D.DrawSprite` 绘制图标，缩放填充以槽位中心为中心的 80% 区域
- **AND** 绘制 2px 品质色边框（颜色取 `InkWashTheme.QualityColor(映射后的 InkQuality)`）
- **AND** 在右下角绘制 `CurrentEquipment.ItemLevel` 文本标签
- **AND** 悬停时边框高亮为 `InkWashTheme.GoldBright`

### Requirement: 空槽态绘制
系统 SHALL 在槽位无装备时绘制暗色边框（1px，`InkWashTheme.BorderNeutralL3`）并在槽位中心绘制槽位类型中文名。

#### Scenario: 空槽绘制
- **WHEN** `CurrentEquipment` 为 null
- **THEN** 绘制 1px 暗色边框，背景保持 `BaseTertiary`
- **AND** 在槽位中心绘制 `SlotNames[SlotType]` 对应的中文名（`TextTertiary` 色，Heading 字体）
- **AND** 若 `EmptySlotIcon.IsValid` 为 true 则在中心绘制空槽图标（50% 区域）

### Requirement: 品质映射
系统 SHALL 将 `EquipmentData.Quality`（int，0-5）映射到 `InkWashTheme.InkQuality`（0-4 枚举），超界值钳制到 [0,4]。

#### Scenario: 品质钳制
- **WHEN** `equipment.Quality` 为 5（超出 InkQuality 上限 4）
- **THEN** 映射结果为 `InkQuality.Legendary`（4）
- **WHEN** `equipment.Quality` 为负数
- **THEN** 映射结果为 `InkQuality.Common`（0）

### Requirement: 双击检测
系统 SHALL 通过覆写 `OnMouseDown`/`OnMouseUp` 检测左键双击，两次按下间隔小于 500ms 时触发 `DoubleClicked` 事件。时间戳使用 `FlaxEngine.Time.UnscaledGameTime`。

#### Scenario: 双击触发
- **WHEN** 玩家在槽位范围内左键按下并释放，随后在 500ms 内再次按下
- **THEN** 第二次释放时触发 `DoubleClicked?.Invoke(SlotType, CurrentEquipment)` 事件
- **AND** 事件参数为当前槽位类型与装备数据（空槽时 CurrentEquipment 为 null）

#### Scenario: 单击不触发
- **WHEN** 两次按下间隔超过 500ms
- **THEN** 重置上次点击时间，不触发 `DoubleClicked`

### Requirement: 悬停事件
系统 SHALL 在鼠标进入时触发 `Hovered(SlotType, Float2)`，在鼠标离开时触发 `HoverEnded(SlotType)`。

#### Scenario: 悬停进入
- **WHEN** 鼠标移入控件范围
- **THEN** 触发 `Hovered?.Invoke(SlotType, location)`，并将边框高亮为 `InkWashTheme.GoldBright`

#### Scenario: 悬停离开
- **WHEN** 鼠标移出控件范围
- **THEN** 触发 `HoverEnded?.Invoke(SlotType)`，并恢复边框颜色（装备态恢复品质色，空槽态恢复暗色边框）

### Requirement: 刷新接口
系统 SHALL 提供 `public void Refresh(EquipmentData equipment)` 方法，更新 `CurrentEquipment` 并触发重绘。

#### Scenario: 装入装备
- **WHEN** 调用 `Refresh(equipment)` 且 equipment 非 null
- **THEN** `CurrentEquipment = equipment`，控件标记需要重绘
- **WHEN** 调用 `Refresh(null)`
- **THEN** `CurrentEquipment = null`，控件回到空槽态绘制

### Requirement: 绘制健壮性
系统 SHALL 用 `try-catch` 包裹 `Draw` 方法体，异常时静默忽略，避免渲染异常导致游戏崩溃。

#### Scenario: 绘制异常
- **WHEN** `Draw` 内任意绘制调用抛出异常
- **THEN** 异常被捕获并吞掉，控件不崩溃，下一帧继续尝试绘制

## MODIFIED Requirements
无（本 spec 为纯新增控件，不强制修改现有页面）。

## REMOVED Requirements
无。
