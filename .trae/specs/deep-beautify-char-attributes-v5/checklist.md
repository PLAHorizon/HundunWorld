# Checklist

## P0 — 装备槽信息完整化（Task 1）
- [x] 每个已装备槽位图标下方显示装备名 Label（11px PaperBright，居中，不换行）
- [x] 装备名下方显示装备类型 Label（10px TextTertiary，居中）
- [x] 图标右下角偏外（bottom: -6px, right: -4px）显示 "+{EnhanceLevel}" 胶囊标签
- [x] 胶囊标签样式：10px Number GoldBright 文本，1px GoldDeep 边框，BaseDefault 背景，2px 圆角
- [x] 空装备槽仅显示槽位类型文字，不显示装备名/类型/强化等级
- [x] EnhanceLevel 字段与 ItemLevel 字段语义分离，不再混淆显示 "Lv.{ItemLevel}"

## P0 — 角色等级两段式（Task 3）
- [x] 等级显示拆为 "Lv." 前缀（18px Number GoldDeep）+ 数值（28px Number GoldBright）
- [x] 等级数值带 8 方向 4px 偏移半透明金色辉光（模拟 text-shadow: 0 0 8px rgba(200,168,88,0.3)）
- [x] 等级两段水平排列，基线对齐

## P1 — 顶栏/底栏/面板渐变与单边框（Task 2）
- [x] 顶栏背景为 90° 三段渐变 `rgba(14,16,22,0.98) → rgba(20,23,30,0.95) → rgba(14,16,22,0.98)`
- [x] 顶栏仅底部绘制 1px BorderGold 线，其余三边无边框
- [x] 底栏高度从 56 改为 60
- [x] 底栏宽度跟随右面板（不再占全屏）
- [x] 底栏背景为 180° 两段渐变 `rgba(20,23,30,0.95) → rgba(14,16,22,0.98)`
- [x] 底栏仅顶部绘制 1px BorderGold 线
- [x] 左面板背景为 180° 两段渐变 `rgba(20,23,30,0.98) → rgba(14,16,22,0.98)`
- [x] 左面板仅左侧绘制 1px BorderGold 线
- [x] 右面板背景为 180° 两段渐变同上
- [x] 右面板仅左侧绘制 1px BorderGold 线

## P1 — 称号装饰线渐变化（Task 3）
- [x] 称号两侧装饰线宽度从 32px 扩展为 120px
- [x] 装饰线为三段渐变（透明 → GoldPrimary 50% → 透明），模拟 `linear-gradient(90deg, transparent, var(--ink-gold-primary) 50%, transparent)`
- [x] 左侧装饰线方向：左透明 → 右金色 → 左透明（与右侧对称）

## P1 — 门派徽章 InkTag 化（Task 3）
- [x] 中间预览区门派显示从普通 Label 改为 InkTag Brand 变体
- [x] 门派徽章文本前缀 "⚔ " Unicode 符号

## P1 — 武学卡片外边框与元信息双段（Task 4）
- [x] MartialArtCard 绘制 1px BorderNeutralL2 外边框（默认）
- [x] MartialArtCard hover 时外边框变 BorderGold
- [x] 元信息行拆为两段：类型段（"⚡" 图标 12px Info 色 + "{type}" 11px PaperAged）
- [x] 元信息行拆为两段：等级段（"★" 图标 12px GoldPrimary 色 + "Lv.{level}" 11px PaperAged）
- [x] 武学名 Label 模拟 1px 字距（Scale 1.02）

## P2 — GlowLabel 多层辉光（Task 5）
- [x] GlowLabel.Draw 绘制 3 层辉光叠加（2px alpha 0.15 + 4px alpha 0.10 + 8px alpha 0.05）
- [x] 战力值辉光视觉范围更接近 16px blur 扩散感

## P2 — DrawDiagonalGradient 真对角线（Task 5）
- [x] DrawDiagonalGradient 按 135° 对角线方向分段绘制（6×6 网格对角线插值）
- [x] 基础属性卡片背景渐变方向与设计稿 `linear-gradient(135deg, ...)` 一致
- [x] 武学卡片背景渐变方向同上

## P2 — 进阶属性图标（Task 5）
- [x] AdvancedAttrRow 增加图标 Label 子控件（12×12）
- [x] 图标使用 Unicode 符号（✦◈◉∽），TextTertiary 色
- [x] 布局为：图标 + 间距 4 + 标签 + 数值

## P2 — 角色名 text-shadow（Task 3）
- [x] 角色名 Label 在主文字下绘制 8 方向 2px 偏移半透明黑色（rgba(0,0,0,0.6)）文字叠加
- [x] 模拟 `text-shadow: 0 2px 12px rgba(0,0,0,0.6)` 效果

## P2 — 门派标识 opacity（Task 3）
- [x] 底部门派标识 "青城" 文本改为 "⛰ 青城"
- [x] TextColor 通过 alpha 0.5 实现 opacity 效果

## 编译与回归（Task 6）
- [x] `dotnet build -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild` 0 C# 错误
- [x] `Game.CSharp.dll` 已重新生成
- [x] 现有 Tooltip 显示功能不受影响（OnMouseEnter/MouseScreenPosition 逻辑保留）
- [x] 现有装备双击换装功能不受影响（双击检测逻辑保留）
- [x] 现有 3D 预览旋转功能不受影响（CharacterPreview3D 未修改）
- [x] 现有雷达图 hover Tooltip 功能不受影响（HexRadarChartOverlay 未修改）
