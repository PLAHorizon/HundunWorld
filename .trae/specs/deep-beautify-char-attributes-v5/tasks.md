# Tasks

- [x] Task 1: 装备槽完整信息呈现（InkEquipmentSlot.cs 独立修改）
  - [x] SubTask 1.1: 读取 `InkEquipmentSlot.cs` 当前实现，定位 Draw 方法、字段定义、子控件构建
  - [x] SubTask 1.2: 新增 `EquipmentNameLabel`（11px Body PaperBright，居中）与 `EquipmentTypeLabel`（10px Body TextTertiary，居中）两个子 Label，在已装备时显示于图标下方，空槽时隐藏
  - [x] SubTask 1.3: 新增 `EnhanceLevel` 字段（int），替换原 `ItemLevel` 显示逻辑；强化等级标签改为 "+{EnhanceLevel}" 格式
  - [x] SubTask 1.4: 强化等级胶囊标签样式：10px Number GoldBright 文本，1px GoldDeep 边框，BaseDefault 背景，2px 圆角，绝对定位（bottom: -6px, right: -4px，相对装备图标右下角偏外）
  - [x] SubTask 1.5: 在 `Refresh` 数据绑定方法中同步设置 EnhanceLevel、装备名、装备类型
  - [x] SubTask 1.6: 调整装备槽容器尺寸（DefaultSlotHeight=78）以容纳下方两行 Label
  - [x] SubTask 1.7: 编译验证 0 C# 错误

- [x] Task 2: 顶栏/底栏/面板背景渐变化与单边框（MenuCharAttributesV2Page.cs）
  - [x] SubTask 2.1: 新建 `GradientBarPanel` 嵌套类，支持渐变方向、渐变色数组、单边框方向与边框色
  - [x] SubTask 2.2: 顶栏改用 `GradientBarPanel`：90° 三段渐变，仅 Bottom 边框 1px BorderGold
  - [x] SubTask 2.3: 底栏改用 `GradientBarPanel`：高度 56→60，宽度跟随右面板，180° 两段渐变，仅 Top 边框
  - [x] 2.3.1: 调整 `BottomBarHeight` 常量为 60f
  - [x] 2.3.2: 调整底栏 Location 与 Size 对齐右面板
  - [x] SubTask 2.4: 左面板改用 `GradientBarPanel`：180° 两段渐变，仅 Left 边框
  - [x] SubTask 2.5: 右面板改用 `GradientBarPanel`：180° 两段渐变，仅 Left 边框
  - [x] SubTask 2.6: 中间预览面板保持不变
  - [x] SubTask 2.7: 编译验证

- [x] Task 3: 角色等级两段式 + 称号装饰线渐变 + 门派徽章 InkTag + 角色名 text-shadow + 门派标识 opacity
  - [x] SubTask 3.1: 定位中间预览面板角色信息容器构建代码
  - [x] SubTask 3.2: 等级显示拆分：移除单个 InkTag，改为 "Lv." Label + 等级数值 Label 水平排列
  - [x] 3.2.1: 新建 `GlowLevelLabel` 嵌套类，8 方向 4px 偏移半透明金色辉光
  - [x] SubTask 3.3: 门派徽章改用 `InkTag` Brand 变体，前缀 "⚔ "
  - [x] SubTask 3.4: 称号装饰线扩展 32→120，三段渐变（透明→GoldPrimary→透明）
  - [x] 3.4.1: 新建 `GradientLine` 嵌套类，支持方向、起止色、中点色
  - [x] SubTask 3.5: 新建 `ShadowedNameLabel` 嵌套类，8 方向 2px 偏移半透明黑色 text-shadow
  - [x] SubTask 3.6: 底部门派标识改为 "⛰ 青城"，opacity 0.5
  - [x] SubTask 3.7: 编译验证

- [x] Task 4: 武学卡片外边框 + 元信息双段拆分
  - [x] SubTask 4.1: 定位 `MartialArtCard` 嵌套类与 `BuildMartialArtsSummary`
  - [x] SubTask 4.2: MartialArtCard.Draw 增加外边框绘制（默认 BorderNeutralL2，hover BorderGold）
  - [x] SubTask 4.3: 元信息行拆为 4 个 Label（⚡类型图标+类型文本+★等级图标+等级文本）
  - [x] 4.3.1: 调整 `LayoutMartialArtsSummary` 中元信息行布局
  - [x] SubTask 4.4: 武学名 Label 设置 Scale 1.02 模拟 1px 字距
  - [x] SubTask 4.5: 编译验证

- [x] Task 5: GlowLabel 多层辉光 + DrawDiagonalGradient 真对角线 + 进阶属性图标
  - [x] SubTask 5.1: 定位 `GlowLabel` 与 `DrawDiagonalGradient`
  - [x] SubTask 5.2: GlowLabel.Draw 扩展为 3 层辉光（2px 0.15 + 4px 0.10 + 8px 0.05）
  - [x] 5.2.1: 用 WithAlpha 动态计算每层 alpha
  - [x] SubTask 5.3: DrawDiagonalGradient 重写为 6×6 网格对角线插值（135° 真对角线）
  - [x] SubTask 5.4: 定位 `AdvancedAttrRow` 与 `BuildAdvancedAttributes`
  - [x] SubTask 5.5: AdvancedAttrRow 增加图标 Label（12×12，TextTertiary 色）
  - [x] 5.5.1: 分配 Unicode 图标（✦暴击/◈抗暴/◉命中/∽闪避）
  - [x] SubTask 5.6: 编译验证

- [x] Task 6: 整体编译验证与回归检查
  - [x] SubTask 6.1: 关闭 Flax Editor（避免 DLL 锁定）
  - [x] SubTask 6.2: 执行 `dotnet build HundunWorld/Source/Game.csproj -c Editor.Windows.Development -p:Platform=x64 -t:Rebuild`
  - [x] SubTask 6.3: 确认 0 C# 错误（97 个预存在警告可忽略）
  - [x] SubTask 6.4: `Game.CSharp.dll` 已重新生成
  - [x] SubTask 6.5: 代码审查确认无回归：Tooltip/装备双击/3D 预览/雷达图 hover 逻辑均保留

# Task Dependencies

- [Task 1] 独立（InkEquipmentSlot.cs）— 与 Task 2-5 并行完成
- [Task 2] [Task 3] [Task 4] [Task 5] 均修改 MenuCharAttributesV2Page.cs，由同一 Sub-Agent 顺序处理
- [Task 6] 依赖 [Task 1-5] 全部完成 — 已通过 0 错误编译验证
