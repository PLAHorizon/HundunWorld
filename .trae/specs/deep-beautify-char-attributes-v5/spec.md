# 角色属性 UI 再度美化 V5 Spec

## Why

上一轮 `deep-beautify-char-attributes-v4` 完成了战力辉光、卡片渐变、五行分色、hover 位移等基础美化，但深入研究设计稿 `menu-char-attributes-v2.html` 后发现仍存在 **20 处视觉差距**，其中包含 3 处 P0 级语义错误（装备槽信息缺失、强化等级语义错误、等级显示退化）与 7 处 P1 级视觉不一致（渐变缺失、边框方向错误、称号/门派视觉降级、武学卡片缺外边框等）。本轮目标是在保持 V3 三栏布局（产品已扩展为属性+3D预览+装备）的前提下，逐一消除这些视觉差距，使运行时效果与设计稿高度对齐。

## What Changes

### P0 — 语义/功能修复
- **装备槽信息完整化**：每个已装备槽位下方新增"装备名"（11px PaperBright）与"装备类型"（10px TextTertiary）两行 Label；强化等级标签从 "Lv.{ItemLevel}" 改为 "+{EnhanceLevel}" 格式，并绘制带 1px GoldDeep 边框 + BaseDefault 背景 + 2px 圆角的胶囊样式，绝对定位右下角偏外（bottom: -6px, right: -4px）
- **等级显示两段式**：左侧预览区角色等级从单个 InkTag 拆为两段 — "Lv." 标签（18px Number GoldDeep）+ 数值（28px Number GoldBright 带 8px 金色辉光）

### P1 — 视觉风格对齐
- **顶栏背景渐变化**：顶部导航栏背景从纯色 `PanelSolid` 改为 90° 三段渐变 `rgba(14,16,22,0.98) → rgba(20,23,30,0.95) → rgba(14,16,22,0.98)`，边框从四周改为仅底边 1px 金线
- **底栏对齐设计稿**：高度从 56 改为 60，宽度跟随右面板（不再占全屏），背景改 180° 渐变，边框改仅顶边 1px 金线
- **面板背景渐变化**：左/右面板背景从纯色 `Panel` 改为 180° 渐变 `rgba(20,23,30,0.98) → rgba(14,16,22,0.98)`，边框从四周改为仅对应单边金线（左面板仅左边、右面板仅左边）
- **称号装饰线扩展**：称号两侧装饰线从 32px 纯色 `GoldPrimary` 扩展为 120px 三段渐变（透明 → GoldPrimary → 透明），模拟 `linear-gradient(90deg, transparent, var(--ink-gold-primary) 50%, transparent)`
- **门派徽章 InkTag 化**：中间预览区门派显示从普通 Label 改为 InkTag Brand 变体，前置 "⚔" Unicode 图标符号
- **武学卡片外边框 + 元信息双段**：MartialArtCard 增加 1px 中性外边框（`BorderNeutralL2`，hover 变 `BorderGold`）；元信息从单字符串 `"{type} · Lv.{level}"` 拆为两段 — 类型段（前置 "⚡" 图标 12px Info 色）+ 等级段（前置 "★" 图标 12px GoldPrimary 色）

### P2 — 细节打磨
- **GlowLabel 多层辉光**：战力值辉光从固定 2px 8 方向偏移扩展为 3 层不同半径（2/4/8px）叠加，更接近设计稿 `text-shadow: 0 0 16px rgba(200,168,88,0.2)` 的扩散感
- **DrawDiagonalGradient 真对角线**：基础属性卡片与武学卡片的渐变从 180° 水平条带改为真正的 135° 对角线条带（按对角线方向分段绘制）
- **进阶属性图标补充**：AdvancedAttrRow 增加 12×12 小图标 Label（前置 Unicode 符号，TextTertiary 色），对齐设计稿 `attr-adv-icon`
- **角色名 text-shadow**：左侧预览区角色名增加 8 方向 2px 偏移半透明黑色文字叠加，模拟 `text-shadow: 0 2px 12px rgba(0,0,0,0.6)`
- **门派标识 opacity**：底部门派标识 "青城" 设置 opacity 0.5（通过 TextColor alpha 实现），并前置 "⛰" Unicode 图标符号

## Impact

- **Affected specs**: `deep-beautify-char-attributes-v4`（已完成，本轮在其基础上深化）、`merge-attr-equip-tooltip`（装备槽信息扩展不影响 Tooltip 逻辑）、`enhance-character-attribute-ui`（3D 预览与雷达图不动）
- **Affected code**:
  - [MenuCharAttributesV2Page.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Pages\Character\MenuCharAttributesV2Page.cs) — 顶栏/底栏/面板 Draw 重写、等级两段式、称号渐变线、门派徽章、武学卡片外边框+元信息拆分、GlowLabel 多层辉光、DrawDiagonalGradient 对角线、进阶属性图标、角色名 text-shadow、门派标识 opacity
  - [InkEquipmentSlot.cs](file:///c:\Works\GitHubProjects\HundunWorld\HundunWorld\Source\Game\UI\Ink\Components\InkEquipmentSlot.cs) — 装备名/类型 Label 子控件、强化等级胶囊标签

## ADDED Requirements

### Requirement: 装备槽完整信息呈现

系统 SHALL 在每个已装备的装备槽下方显示装备名（11px PaperBright）与装备类型（10px TextTertiary）两行 Label，并在装备图标右下角偏外位置（bottom: -6px, right: -4px）显示强化等级胶囊标签（"+X" 格式，10px Number GoldBright，1px GoldDeep 边框，BaseDefault 背景，2px 圆角）。

#### Scenario: 已装备槽位显示完整信息
- **WHEN** 装备槽已装备物品
- **THEN** 图标下方显示装备名 Label（11px PaperBright，居中，不换行）
- **AND** 装备名下方显示装备类型 Label（10px TextTertiary，居中）
- **AND** 图标右下角偏外显示 "+{EnhanceLevel}" 胶囊标签

#### Scenario: 空装备槽保持简洁
- **WHEN** 装备槽未装备物品
- **THEN** 仅显示槽位类型文字（如"头"/"颈"），不显示装备名/类型/强化等级

### Requirement: 顶部/底部栏与面板渐变背景

系统 SHALL 为顶部导航栏绘制 90° 三段渐变背景（深-浅-深）与仅底边 1px 金线；为底部操作栏绘制 180° 两段渐变背景与仅顶边 1px 金线；为左/右面板绘制 180° 两段渐变背景与仅左边 1px 金线。

#### Scenario: 顶栏渲染
- **WHEN** 角色属性页显示
- **THEN** 顶部导航栏背景为 90° 渐变 `rgba(14,16,22,0.98) → rgba(20,23,30,0.95) → rgba(14,16,22,0.98)`
- **AND** 仅底部绘制 1px `BorderGold` 线，其余三边无边框

#### Scenario: 面板渲染
- **WHEN** 左/右面板可见
- **THEN** 面板背景为 180° 渐变 `rgba(20,23,30,0.98) → rgba(14,16,22,0.98)`
- **AND** 仅左侧绘制 1px `BorderGold` 线

### Requirement: 角色等级两段式显示

系统 SHALL 在左侧预览区将角色等级显示拆分为两段："Lv." 前缀（18px Number GoldDeep）+ 等级数值（28px Number GoldBright，带 8px 金色辉光阴影）。

#### Scenario: 等级渲染
- **WHEN** 角色已绑定
- **THEN** 等级行显示 "Lv." 前缀（18px Number GoldDeep）
- **AND** 紧随其后显示等级数值（28px Number GoldBright）
- **AND** 等级数值带有 8px 金色辉光阴影（多层偏移叠加模拟 text-shadow: 0 0 8px rgba(200,168,88,0.3)）

### Requirement: 武学卡片完整边框与元信息双段

系统 SHALL 为武学卡片绘制 1px 中性外边框（hover 变金色），并将元信息行拆为类型段（前置图标）与等级段（前置图标）两段独立 Label。

#### Scenario: 武学卡片渲染
- **WHEN** 武学摘要显示
- **THEN** 每张武学卡片有 1px `BorderNeutralL2` 外边框（hover 变 `BorderGold`）
- **AND** 元信息行显示两段：类型段（"⚡ {type}"，12px Info 色图标 + 11px PaperAged 文本）+ 等级段（"★ Lv.{level}"，12px GoldPrimary 色图标 + 11px PaperAged 文本）

## MODIFIED Requirements

### Requirement: GlowLabel 多层辉光

战力值 GlowLabel SHALL 使用 3 层不同半径（2/4/8px）的 8 方向偏移半透明金色文字叠加，模拟 CSS `text-shadow: 0 0 16px rgba(200,168,88,0.2)` 的扩散辉光效果，而非单一 2px 偏移。

### Requirement: DrawDiagonalGradient 真对角线

`DrawDiagonalGradient` 辅助方法 SHALL 按 135° 对角线方向分段绘制渐变（而非当前的 180° 水平条带），使基础属性卡片与武学卡片背景渐变方向与设计稿 `linear-gradient(135deg, ...)` 一致。

### Requirement: 称号装饰线渐变化

称号两侧装饰线 SHALL 从 32px 纯色 `GoldPrimary` 扩展为 120px 三段渐变（透明 → GoldPrimary 50% → 透明），模拟 `linear-gradient(90deg, transparent 0%, var(--ink-gold-primary) 50%, transparent 100%)`。
