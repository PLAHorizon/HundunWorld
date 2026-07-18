# Checklist

- [x] 战力数字字号 48px、`GoldBright` 色、金色辉光阴影（`GlowLabel` 8 方向偏移模拟 text-shadow）
- [x] 战力标签 `PaperAged` 色、字间距（"战 力"）
- [x] 战力趋势标签 `JadeBright` 翡翠绿色
- [x] 基础属性卡片使用 135 度对角线渐变背景 `BaseTertiary(0.6) → BaseSecondary(0.6)`（6 条带 Color.Lerp）
- [x] 基础属性卡片 hover 时渐变变亮（0.7 alpha）、边框变 `BorderGold`
- [x] 属性图标 28×28px 容器，按五行分色（jade/cyan/vermilion/gold）
- [x] 进阶属性项有 2px 左边框（`BorderNeutralL2`）
- [x] 进阶属性 hover 时左边框变 `GoldPrimary`，背景变亮
- [x] 装备槽 hover 上浮 2px（`yOffset = -2f`）
- [x] 装备槽 hover 时加深阴影（`Color(0,0,0,0.3)`）
- [x] 已装备槽位按品质色内发光（0.12 alpha 品质色内侧矩形）
- [x] 武学卡片 hover 右移 2px（`Location.X + 2`）
- [x] 武学卡片 hover 时背景渐变变亮
- [x] 武学图标容器 42×42px，按品质分色（传说朱红/史诗金色）
- [x] 编译通过，0 C# 错误（预存在警告不变）
