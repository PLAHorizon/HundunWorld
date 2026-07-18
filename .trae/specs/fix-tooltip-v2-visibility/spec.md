# Tooltip 彻底修复与美化 V2 Spec

## Why

上一轮 `merge-attr-equip-tooltip` spec 虽已勾选完成，但用户实际运行反馈问题仍然存在：
1. **装备和装备槽还是没有 Tooltip** — 鼠标悬停装备槽/装备物品时 Tooltip 不出现
2. **ToolTip 位置还是不对** — Tooltip 没有精确出现在鼠标右下方 10 像素处
3. **美化不到位** — 需要严格参考 `popup-verification.html` 的纸色卷轴风格

经深度代码审查发现三个根本性缺陷：
- **`InkAttributeTooltip` 用 `new` 关键字隐藏基类 `Visible` 属性**（`public new bool Visible`），导致 Flax 引擎渲染管线访问基类 `Control.Visible` 与代码逻辑访问派生类 `_visible` 可能不一致，Tooltip 即使设置了位置也可能不绘制
- **Tooltip 缺少 `BringToFront()` 调用**，`AddChild` 后若页面继续添加其他子控件（装饰层、面板等），Tooltip 可能被遮挡或不在最顶层
- **坐标转换逻辑虽用 `PointToScreen` 但 `new Visible` 的副作用未被排查**，且边界溢出判断混用屏幕坐标与父控件本地坐标

## What Changes

- **移除 `InkAttributeTooltip` 的 `new Visible` 属性隐藏** — 直接使用基类 `Control.Visible`，消除渲染管线与代码逻辑的不一致
- **Tooltip 显示时调用 `BringToFront()`** — 确保 Tooltip 始终位于页面所有子控件的最顶层，不被后续添加的装饰层/面板遮挡
- **简化并校准坐标转换** — 确认 `Parent.PointToScreen(Float2.Zero)` 正确性，统一边界溢出判断坐标系，添加诊断日志辅助验证
- **装备槽命中测试验证** — 确认 `InkEquipmentSlot.OnMouseEnter` 实际被调用（添加诊断日志），排查是否存在尺寸为 0 或被透明控件遮挡的问题
- **Tooltip 美化对齐 popup-verification.html** — 调整四角 L 装饰尺寸 14→18px、装饰带线条 max-width 80→120px、band-dot 4px 圆点、opacity 0.6/0.7 对齐设计稿

## Impact

- 受影响代码：
  - `HundunWorld/Source/Game/UI/Ink/Components/InkAttributeTooltip.cs` — 移除 `new Visible`、添加 `BringToFront`、校准坐标转换、美化参数对齐设计稿
  - `HundunWorld/Source/Game/UI/Ink/Components/InkEquipmentSlot.cs` — 添加 `OnMouseEnter` 诊断日志（仅 Debug 配置）
  - `HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs` — 确认 Tooltip 添加时机和装备槽布局尺寸
- 不影响：网络同步、服务端、ECS、战斗逻辑、装备数据模型

## ADDED Requirements

### Requirement: Tooltip 可见性一致性

`InkAttributeTooltip` SHALL 直接使用基类 `Control.Visible` 属性，禁止用 `new` 关键字隐藏。渲染管线的 `Control.Visible` 与代码逻辑的可见性状态 MUST 完全一致。

#### Scenario: Show 后立即可见
- **WHEN** 调用 `_tooltip.Show(screenPos)` 设置 `Visible = true`
- **THEN** Flax 渲染管线在下一帧绘制 Tooltip（`base.Visible == true`）

#### Scenario: Hide 后立即隐藏
- **WHEN** 调用 `_tooltip.Hide()` 设置 `Visible = false`
- **THEN** Flax 渲染管线不再绘制 Tooltip（`base.Visible == false`）

### Requirement: Tooltip 置顶显示

`InkAttributeTooltip.Show` SHALL 在设置 `Visible = true` 后立即调用 `BringToFront()`，确保 Tooltip 在页面所有子控件中位于最顶层，不被后续添加的装饰层、面板、背景图遮挡。

#### Scenario: 装备槽悬停时 Tooltip 可见
- **WHEN** 鼠标悬停装备槽，调用 `Show` 后页面有其他子控件
- **THEN** Tooltip 完整可见，不被任何兄弟控件遮挡

### Requirement: Tooltip 精确跟随鼠标 V2

`InkAttributeTooltip.Show(Float2 screenPosition)` SHALL 将控件左上角定位到鼠标屏幕坐标右下方精确 10 像素处。坐标转换 SHALL 使用 `Parent.PointToScreen(Float2.Zero)` 获取父控件原点屏幕坐标，`本地坐标 = screenPosition - parentScreenOrigin + (10, 10)`。

边界溢出检测 SHALL 统一使用屏幕坐标系：
- 右溢出：`screenPosition.X + 10 + Width > Screen.Size.X` → 向左偏移
- 下溢出：`screenPosition.Y + 10 + Height > Screen.Size.Y` → 向上偏移
- 防负：本地坐标 < 0 时钳制为 0

#### Scenario: 屏幕中央悬停
- **WHEN** 鼠标位于屏幕中央 (960, 540)
- **THEN** Tooltip 左上角位于 (970, 550)

#### Scenario: 屏幕右下角悬停
- **WHEN** 鼠标位于屏幕右下角附近 (1900, 1060)，Tooltip 尺寸 240×200
- **THEN** Tooltip 自动向左偏移到鼠标左侧 (1900 - 240 - 10, 1060 + 10) 或向上偏移

### Requirement: 装备槽 Tooltip 触发可诊断

`InkEquipmentSlot.OnMouseEnter` SHALL 在 Debug 配置下输出诊断日志，确认鼠标进入事件是否触发。`OnEquipmentSlotHovered` SHALL 在 Debug 配置下输出日志确认 Tooltip 调用链路完整。

#### Scenario: 鼠标进入装备槽
- **WHEN** 鼠标进入任一装备槽
- **THEN** 日志输出 `[InkEquipmentSlot] OnMouseEnter slot=Head` 和 `[MenuCharAttributesV2Page] OnEquipmentSlotHovered slot=Head`

### Requirement: Tooltip 美化对齐 popup-verification.html

Tooltip 视觉 SHALL 严格对齐 `popup-verification.html` 的设计参数：

- 四角 L 装饰（corner-ornament）：尺寸 18px（非 14px），线宽 1px，金色 `GoldPrimary`，opacity 0.6（非 0.7）
- 顶部装饰带（modal-top-band）：中心图标/圆点 + 两侧渐变线，线条 max-width 120px（非 80px），渐变 `transparent → GoldDeep 50% → transparent`，整体 opacity 0.7
- band-dot：4×4px 圆点，`GoldDeep` 色
- 底部装饰带（modal-bottom-band）：与顶部呼应，相同参数
- 纸色背景：`PaperPanelBg` (rgba(245,240,232,0.92))
- 纸色边框：`PaperPanelBorder` (rgba(168,158,138,0.4)) 1px
- 外边框：品质色优先 2px，否则默认金色
- 阴影：`0 4px 16px rgba(0,0,0,0.12)`（纸色面板阴影，非 4px 偏移）

#### Scenario: 视觉参数与设计稿一致
- **WHEN** Tooltip 绘制时
- **THEN** 四角 L 装饰 18px、装饰带线条 max-width 120px、band-dot 4px 圆点、opacity 0.6/0.7 与 popup-verification.html 一致

## MODIFIED Requirements

### Requirement: InkAttributeTooltip 可见性管理

**原有**: 用 `public new bool Visible` 隐藏基类属性，内部维护 `_visible` 字段，setter 同步 `base.Visible`。

**修改为**: 移除 `new Visible` 属性和 `_visible` 字段，直接使用基类 `Control.Visible`。所有内部代码中 `_visible` 引用改为 `Visible`（基类属性）。`Draw` 方法的 `!_visible` 判断改为 `!Visible`。

### Requirement: InkAttributeTooltip.Show 坐标转换

**原有**: 使用 `Parent.PointToScreen(Float2.Zero)` 转换坐标，边界检测混用屏幕坐标与父控件本地坐标。

**修改为**: 保持 `Parent.PointToScreen(Float2.Zero)` 转换，但边界溢出检测统一基于屏幕坐标判断，溢出后的偏移量计算基于父控件本地坐标系。添加 `BringToFront()` 调用确保置顶。

## REMOVED Requirements

### Requirement: InkAttributeTooltip 的 new Visible 属性隐藏
**Reason**: `new` 隐藏基类 `Visible` 导致渲染管线与代码逻辑可能不一致，是 Tooltip 不显示的潜在根因。
**Migration**: 移除 `public new bool Visible` 和 `_visible` 字段，所有引用改为基类 `Visible`。
