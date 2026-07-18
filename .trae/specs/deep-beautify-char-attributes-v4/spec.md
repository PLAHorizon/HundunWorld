# 深度美化角色属性 UI V4 Spec

## Why

经深入研究 `menu-char-attributes-v2.html` 设计方案，发现当前 `MenuCharAttributesV2Page` 实现与设计稿存在多项视觉细节差距：卡片缺少渐变背景、战力数字缺少辉光阴影、装备槽/武学卡片缺少 hover 位移动效、属性图标未按五行分色、进阶属性缺少左边框强调、品质色内发光缺失。这些细节正是"美观大气、松紧有秩、色调柔和阳刚有春色"要求的关键体现。

## What Changes

- **战力区辉光强化** — 战力数字添加 `text-shadow: 0 0 16px rgba(200,168,88,0.2)` 金色辉光，字号对齐设计稿 48px，趋势标签用翡翠绿
- **基础属性卡片渐变背景** — 从纯色 `BaseTertiary(0.35)` 改为设计稿的 `linear-gradient(135deg, rgba(28,31,40,0.6), rgba(20,23,30,0.6))` 渐变
- **基础属性卡片 hover 效果** — 边框变金色、背景渐变更亮（0.7 alpha）
- **属性图标五行分色** — 28×28px 图标容器按五行属性分色（jade 翡翠/vermilion 朱红/cyan 青/gold 金），1px 边框 + 10% 背景色
- **进阶属性左边框强调** — 添加 `border-left: 2px solid`，hover 时左边框变金色
- **装备槽 hover 位移动效** — `transform: translateY(-2px)` 上浮 + 阴影加深
- **装备槽品质色内发光** — `box-shadow: inset 0 0 8px rgba(品质色,0.12)` 内发光
- **武学卡片 hover 横向位移** — `transform: translateX(2px)` 右移 + 渐变背景变亮
- **武学卡片图标容器** — 42×42px 图标容器，按品质分色边框

## Impact

- 受影响代码：
  - `HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs` — 战力区、基础属性卡片、进阶属性、装备槽、武学卡片的绘制与 hover 逻辑
  - `HundunWorld/Source/Game/UI/Ink/Components/InkEquipmentSlot.cs` — hover 位移动效、品质色内发光
- 不影响：数据绑定、装备逻辑、网络同步、Tooltip 功能

## ADDED Requirements

### Requirement: 战力区辉光与字号

战力数字 SHALL 使用 48px 字号、`GoldBright` 色、`text-shadow: 0 0 16px rgba(200,168,88,0.2)` 金色辉光阴影。趋势标签 SHALL 使用 `JadeBright` 翡翠绿色。战力标签 SHALL 使用 `PaperAged` 色、0.1em 字间距。

#### Scenario: 战力数字辉光
- **WHEN** 角色属性页显示战力数值
- **THEN** 数字呈金色辉光效果，字号 48px，视觉醒目

### Requirement: 基础属性卡片渐变背景

基础属性卡片 SHALL 使用 `linear-gradient(135deg, rgba(28,31,40,0.6), rgba(20,23,30,0.6))` 渐变背景（135度对角线，从三级背景色到次级背景色，0.6 透明度）。hover 时 SHALL 切换为更亮的渐变 `rgba(35,39,51,0.7) → rgba(28,31,40,0.7)` 且边框变 `BorderGold`。

#### Scenario: 卡片渐变背景
- **WHEN** 显示基础属性卡片
- **THEN** 卡片背景呈对角线渐变，从左上 `BaseTertiary(0.6)` 到右下 `BaseSecondary(0.6)`

#### Scenario: 卡片 hover 效果
- **WHEN** 鼠标悬停基础属性卡片
- **THEN** 背景渐变变亮（0.7 alpha）、边框变金色

### Requirement: 属性图标五行分色

基础属性图标 SHALL 使用 28×28px 容器，1px 边框，圆角 2px，按五行属性分色：
- 气血/体魄：jade 翡翠色（`JadeBright` 文字 + `JadePrimary` 边框 + `rgba(94,139,126,0.1)` 背景）
- 内力/根骨：cyan 青色（`Info` 文字 + 边框 + `rgba(74,126,168,0.1)` 背景）
- 身法：vermilion 朱红色（`VermilionBright` 文字 + `VermilionDeep` 边框 + `rgba(192,57,43,0.1)` 背景）
- 悟性：gold 金色（`GoldBright` 文字 + `GoldDeep` 边框 + `rgba(200,168,88,0.1)` 背景）

#### Scenario: 五行分色图标
- **WHEN** 显示基础属性卡片图标
- **THEN** 图标容器按属性五行语义显示对应颜色边框与淡色背景

### Requirement: 进阶属性左边框强调

进阶属性项 SHALL 添加 `border-left: 2px solid BorderNeutralL2`（2px 左边框）。hover 时左边框 SHALL 变为 `GoldPrimary` 色，背景从 `rgba(20,23,30,0.4)` 变为 `rgba(28,31,40,0.5)`。

#### Scenario: 进阶属性左边框
- **WHEN** 显示进阶属性项
- **THEN** 左侧有 2px 强调边框

#### Scenario: 进阶属性 hover
- **WHEN** 鼠标悬停进阶属性项
- **THEN** 左边框变金色，背景轻微变亮

### Requirement: 装备槽 hover 位移与内发光

装备槽 SHALL 在 hover 时上浮 2px（`translateY(-2px)`）并加深阴影（`0 4px 12px rgba(0,0,0,0.3)`）。已装备槽位 SHALL 按品质色添加内发光 `inset 0 0 8px rgba(品质色,0.12)`。

#### Scenario: 装备槽 hover 上浮
- **WHEN** 鼠标悬停装备槽
- **THEN** 槽位上浮 2px 并加深投影

#### Scenario: 品质色内发光
- **WHEN** 装备槽已装备传说品质物品
- **THEN** 槽位边框朱红色，内部有 `rgba(192,57,43,0.12)` 内发光

### Requirement: 武学卡片 hover 横向位移

武学卡片 SHALL 在 hover 时右移 2px（`translateX(2px)`），背景渐变变亮。武学图标容器 SHALL 为 42×42px，按品质分色：传说朱红边框、史诗金色边框。

#### Scenario: 武学卡片 hover 右移
- **WHEN** 鼠标悬停武学卡片
- **THEN** 卡片右移 2px，背景渐变更亮

#### Scenario: 武学图标容器品质分色
- **WHEN** 显示传说品质武学卡片
- **THEN** 图标容器边框 `VermilionDeep`、文字 `VermilionBright`

## MODIFIED Requirements

### Requirement: 基础属性卡片视觉

**原有**: 卡片使用纯色 `BaseTertiary(0.35)` 背景，无 hover 效果。

**修改为**: 卡片使用 135 度对角线渐变背景 `rgba(28,31,40,0.6) → rgba(20,23,30,0.6)`，hover 时渐变变亮且边框变金色。

### Requirement: 装备槽视觉

**原有**: 装备槽无 hover 位移动效，无品质色内发光。

**修改为**: 装备槽 hover 时上浮 2px 加深阴影，已装备槽位按品质色内发光。

### Requirement: 武学卡片视觉

**原有**: 武学卡片无 hover 位移动效。

**修改为**: 武学卡片 hover 时右移 2px，背景渐变变亮。
