# Tasks

- [x] Task 1: 战力区辉光与字号对齐设计稿
  - [x] SubTask 1.1: 定位战力数字绘制代码（`BuildCombatPowerArea` 中 `_combatPowerValue`）
  - [x] SubTask 1.2: 战力数字字号改为 48px，颜色用 `GoldBright`，添加金色辉光阴影（新建 `GlowLabel` 嵌套类，8 方向偏移半透明金色文字模拟 text-shadow）
  - [x] SubTask 1.3: 战力标签用 `PaperAged` 色、字间距（"战 力"）
  - [x] SubTask 1.4: 趋势/增量标签用 `JadeBright` 翡翠绿色
  - [x] SubTask 1.5: 编译验证

- [x] Task 2: 基础属性卡片渐变背景与 hover 效果
  - [x] SubTask 2.1: 定位基础属性卡片构建（`BuildBasicAttributes`）
  - [x] SubTask 2.2: 卡片背景改为 135 度对角线渐变（新建 `BasicAttrCard` 嵌套类，6 条带 `Color.Lerp` 模拟渐变）
  - [x] SubTask 2.3: hover 效果：边框变 `BorderGold`、渐变变亮为 `BaseElevated(0.7) → BaseTertiary(0.7)`
  - [x] SubTask 2.4: 编译验证

- [x] Task 3: 属性图标五行分色
  - [x] SubTask 3.1: 定位基础属性图标构建代码
  - [x] SubTask 3.2: 图标容器 28×28px，圆角 2px，1px 边框（新建 `BorderedIcon` 嵌套类）
  - [x] SubTask 3.3: 按五行属性分色（气血/体魄=Jade，内力/根骨=Cyan，身法=Vermilion，悟性=Gold）
  - [x] SubTask 3.4: 编译验证

- [x] Task 4: 进阶属性左边框强调与 hover
  - [x] SubTask 4.1: 定位进阶属性构建（`BuildAdvancedAttributes`）
  - [x] SubTask 4.2: 添加 2px 左边框（新建 `AdvancedAttrRow` 嵌套类）
  - [x] SubTask 4.3: hover 效果：左边框变 `GoldPrimary`，背景从 `BaseSecondary(0.4)` 变为 `BaseTertiary(0.5)`
  - [x] SubTask 4.4: 编译验证

- [x] Task 5: 装备槽 hover 位移与品质色内发光
  - [x] SubTask 5.1: 读取 `InkEquipmentSlot.cs` 的 Draw 方法和 OnMouseEnter/OnMouseLeave
  - [x] SubTask 5.2: hover 上浮 2px：Draw 中 `yOffset = _isHovered ? -2f : 0f`，所有绘制 Y 加 yOffset
  - [x] SubTask 5.3: hover 时加深阴影：绘制 `Rectangle(2f, 4f, Width, Height)` 半透明黑色 `Color(0,0,0,0.3)`
  - [x] SubTask 5.4: 已装备槽位品质色内发光：边框内侧绘制 0.12 alpha 品质色矩形
  - [x] SubTask 5.5: 编译验证

- [x] Task 6: 武学卡片 hover 横向位移与图标容器品质分色
  - [x] SubTask 6.1: 定位武学卡片构建（`BuildMartialArtsSummary`）
  - [x] SubTask 6.2: hover 右移 2px（新建 `MartialArtCard` 嵌套类，记录 `_originalX` 复位）
  - [x] SubTask 6.3: hover 时背景渐变变亮（0.7 alpha）
  - [x] SubTask 6.4: 武学图标容器 42×42px，按品质分色（传说朱红/史诗金色/默认中性）
  - [x] SubTask 6.5: 编译验证

# Task Dependencies
- [Task 1] [Task 2] [Task 3] [Task 4] 已并行完成（同一 agent 顺序处理避免文件冲突）
- [Task 5] 独立完成（另一 agent 处理 InkEquipmentSlot.cs）
- [Task 6] 依赖 [Task 2] — 已完成（复用渐变绘制方法 `DrawDiagonalGradient`）
- 所有 Task 编译验证通过（0 C# 错误）
