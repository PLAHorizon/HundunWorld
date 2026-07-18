# 合并装备与角色属性功能 + Tooltip 修复与美化 Spec

## Why

当前存在三个功能重叠的菜单页面：`MenuCharAttributesPage`（V1 旧版）、`MenuCharAttributesV2Page`（V2，已集成装备槽/背包/武学/3D预览）、`MenuEquipmentPage`（独立装备页，8 槽老设计）。导航入口分散、功能重复，且 `MenuCharAttributesV2Page` 已实质包含装备管理能力，`MenuEquipmentPage` 已冗余。

与此同时，Tooltip 存在两个未解决的严重问题：
1. **位置不准确** — 多次修改坐标转换逻辑后仍偏离鼠标右下方 10 像素的目标位置；
2. **装备槽与装备物品无 Tooltip** — 鼠标悬停装备槽/背包格子时 Tooltip 不出现，用户无法查看装备详情。

## What Changes

- **废弃 `MenuEquipmentPage`** — 移除独立装备页的页面注册与导航入口，装备管理统一由 `MenuCharAttributesV2Page` 承载。
- **废弃 `MenuCharAttributesPage`（V1）** — 移除旧版角色属性页注册，统一使用 V2。
- **系统性修复 Tooltip 定位** — 重构 `InkAttributeTooltip.Show` 的坐标转换逻辑，确保 Tooltip 左上角精确位于鼠标屏幕坐标右下方 10 像素处，不受控件嵌套层级影响。
- **修复装备槽/背包 Tooltip 不触发** — 诊断并修复鼠标事件传递链路，确保所有装备槽（含空槽）与背包格子（含空格）悬停时均显示 Tooltip。
- **Tooltip 视觉美化** — 严格参考 `popup-verification.html` 的纸色卷轴面板风格：纸色背景 + 四角金色 L 装饰 + 顶/底装饰带 + 品质色边框 + 纸色质感纹理。

**BREAKING**: 移除 `MenuEquipmentPage` 和 `MenuCharAttributesPage` 的页面注册，若存在外部导航到这两个页面的 dom-id 调用需改为 `NavCharacterV2`。

## Impact

- 受影响代码：
  - `HundunWorld/Source/Game/UI/MainUIManager.cs` — 移除 `MenuEquipmentPage`、`MenuCharAttributesPage` 的注册与创建方法
  - `HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuEquipmentPage.cs` — 废弃（保留文件但不再注册）
  - `HundunWorld/Source/Game/UI/Ink/Pages/MenuCharAttributesPage.cs` — 废弃（保留文件但不再注册）
  - `HundunWorld/Source/Game/UI/Ink/Components/InkAttributeTooltip.cs` — 重构坐标转换与视觉绘制
  - `HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs` — 修复事件绑定与 Tooltip 调用
  - `HundunWorld/Source/Game/UI/Ink/Components/InkEquipmentSlot.cs` — 确认事件触发
  - `HundunWorld/Source/Game/UI/Ink/Components/InkBackpackGrid.cs` — 确认事件冒泡
- 不影响：网络同步、服务端 Grain、ECS 系统、战斗逻辑、装备数据模型。

## ADDED Requirements

### Requirement: 统一角色菜单入口

系统 SHALL 只保留一个角色菜单页面入口（`NavCharacterV2`），承载角色属性 + 装备管理 + 背包 + 武学的全部功能，不再提供独立装备页导航。

#### Scenario: 导航到角色菜单
- **WHEN** 玩家从 HUD 点击角色/装备入口
- **THEN** 统一打开 `MenuCharAttributesV2Page`，页面内含属性面板、3D 预览、15 装备槽、背包、武学摘要

#### Scenario: 旧入口兼容
- **WHEN** 代码中存在指向旧装备页 dom-id 的导航调用
- **THEN** 编译期或运行期将其重定向到 `NavCharacterV2`，不出现空页面或异常

### Requirement: Tooltip 精确跟随鼠标

`InkAttributeTooltip.Show(Float2 screenPosition)` SHALL 将控件左上角定位到鼠标屏幕坐标右下方精确 10 像素处，不受以下因素影响：
- Tooltip 父控件的嵌套层级
- 父控件的 Location 偏移
- 屏幕分辨率

#### Scenario: 任意层级悬停
- **WHEN** 鼠标悬停在任意可交互控件（基础属性、进阶属性、装备槽、背包格子、雷达图、阶段标签）上
- **THEN** Tooltip 左上角出现在鼠标光标右下方 10 像素处

#### Scenario: 边界溢出自动调整
- **WHEN** 鼠标位于屏幕右下角，Tooltip 右下方 10 像素处会超出屏幕
- **THEN** Tooltip 自动向左/向上偏移到鼠标左侧/上方，确保完整可见

### Requirement: 装备槽与背包格子 Tooltip 全覆盖

所有装备槽（含空槽）与背包格子（含空格）SHALL 在鼠标悬停时显示对应 Tooltip：
- 已装备槽位：显示装备名称、品质、属性加成、来源（品质色边框）
- 空装备槽：显示槽位名称、"未装备"、可装备物品类型提示
- 已存放背包格子：显示装备详情（品质色边框）
- 空背包格子：显示"空格子"、"未存放物品"提示

#### Scenario: 悬停已装备槽位
- **WHEN** 鼠标悬停在已装备的头部槽位
- **THEN** Tooltip 显示头盔名称、品质色边框、属性加成、来源信息

#### Scenario: 悬停空装备槽
- **WHEN** 鼠标悬停在未装备的戒指槽位
- **THEN** Tooltip 显示"右戒指 / 未装备 / 可装备：戒指"

#### Scenario: 鼠标离开隐藏
- **WHEN** 鼠标离开悬停目标
- **THEN** Tooltip 立即隐藏，不使用定时器延迟

### Requirement: Tooltip 纸色卷轴视觉风格

Tooltip SHALL 采用 `popup-verification.html` 的纸色卷轴面板视觉风格：

- 背景：纸色半透明 `PaperPanelBg` (#f5f0e8, 0.92 alpha)
- 纸色质感：顶/底边缘轻微老化渐变
- 外边框：2px，品质色优先（装备类），否则默认金色 `BorderGold`
- 内描边：距外边框 1px，`GoldBright` 1px
- 四角装饰：14px 金色 L 型角饰（70% alpha）
- 顶部装饰带：中心金点 + 两侧金色短线
- 底部装饰带：中心金点 + 两侧金色短线（与顶部呼应）
- 头部分隔线：三段渐变（淡-实-淡）模拟水墨分隔
- 阴影：右下偏移 4px 半透明黑色
- 文字色：标题/正文用 `TextOnPaper`，附加信息用 `PaperDark`

#### Scenario: 装备品质色边框
- **WHEN** Tooltip 显示传说品质装备
- **THEN** 外边框采用朱红 `QualityLegendary`，占位圆点同色

#### Scenario: 属性 Tooltip 默认边框
- **WHEN** Tooltip 显示基础属性（非装备）
- **THEN** 外边框采用默认金色 `BorderGold`

## MODIFIED Requirements

### Requirement: MainUIManager 页面注册

**原有**: `MainUIManager` 注册 `NavCharacterV2`、`NavEquipment`（或等价装备入口）、旧版角色属性页三个独立页面。

**修改为**: `MainUIManager` 仅注册 `NavCharacterV2` 一个角色菜单页面，移除 `MenuEquipmentPage` 和 `MenuCharAttributesPage` 的注册与创建方法。所有角色/装备相关导航统一指向 `NavCharacterV2`。

### Requirement: InkAttributeTooltip 坐标转换

**原有**: `Show` 方法直接将屏幕坐标赋值给 `Location`，或使用 `Parent.ScreenToLocal` 转换，但在多层嵌套下定位偏差。

**修改为**: `Show` 方法使用根 `UICanvas` 的屏幕到本地坐标转换（`RootCanvas.ScreenToLocal` 或递归累加父级 Location），确保任意嵌套层级下 Tooltip 左上角精确对应鼠标屏幕坐标 + (10, 10) 偏移。

## REMOVED Requirements

### Requirement: 独立装备页 MenuEquipmentPage
**Reason**: `MenuCharAttributesV2Page` 已完整覆盖装备管理功能（15 槽位 + 背包 + 双击换装），独立装备页冗余。
**Migration**: 移除 `MainUIManager` 中的 `MenuEquipmentPage` 注册与 `CreateMenuEquipment` 方法；文件保留但不再引用。

### Requirement: 旧版角色属性页 MenuCharAttributesPage
**Reason**: V2 已完全替代 V1，V1 不再使用。
**Migration**: 移除 `MainUIManager` 中的 `MenuCharAttributesPage` 注册与 `CreateMenuCharAttributes` 方法；文件保留但不再引用。

### Requirement: Tooltip 自动隐藏定时器
**Reason**: 用户要求 Tooltip 在鼠标离开悬停目标后才消失，而非定时自动隐藏。
**Migration**: 已在上一轮移除 `Update` 中的定时器逻辑，本轮确认所有悬停目标均绑定 `HoverEnded`/`MouseLeave`/`TooltipEnded` 事件。
