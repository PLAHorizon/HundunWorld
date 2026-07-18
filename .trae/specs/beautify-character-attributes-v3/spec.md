# 角色属性页 V3 美化重设计 Spec

## Why

当前 `MenuCharAttributesV2Page` 虽已集成 3D 预览、雷达图、装备槽、背包等基础功能，但实际运行效果与"燕云十六声 + 魔兽世界"风格的角色属性页仍有明显差距：

- 左右两栏比例失衡，右侧属性区过宽、信息堆叠拥挤；
- 装备槽仅 6 个，无法支撑武侠 MMORPG 的装备体系；
- 整体色调偏暗沉闷，缺少柔和春色的点缀，视觉层次不够；
- 分区间距、字号对比、信息密度不够大气。

本次 Spec 对页面进行三栏重布局、扩展 15 装备槽、引入柔和阳刚的春色色调，并整体优化视觉层次与留白。

## What Changes

- **三栏布局重设计**：左侧属性面板（30%）+ 中间 3D 预览/战力区（40%）+ 右侧装备/背包/武学区（30%）。
- **扩展装备槽位**：`EquipmentSlot` 枚举从 8 个扩展至 15 个，覆盖头/颈/肩/背/身/腰/腿/足/右主手/左副手/右戒/左戒/右腕/左腕/面。
- **15 槽装备栏**：中间右侧区域采用人体拓扑布局（上中下三段），支持双击切换，带品质色发光边框。
- **色调升级**：保留鎏金主色的阳刚质感，背景由深墨黑向墨青过渡，玉色/春色作为点缀色，降低整体对比度使视觉柔和。
- **视觉层次优化**：战力数字放大居中、分区卡片化、增加内边距与分区标题装饰线、属性条更细长精致。
- **数据与逻辑适配**：扩展 mock 装备数据、更新 `RecalculateAttributes` 以支持更多槽位加成、保持 Tooltip 与双击切换逻辑。

**BREAKING**: `EquipmentSlot` 枚举新增 7 个槽位，现有保存数据若按整数值反序列化会错位。本次仅用于客户端 UI 展示与 mock 数据，不处理服务端持久化；正式上线前需确认服务端/存档数据兼容。

## Impact

- 受影响代码：
  - `HundunWorld/Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs`
  - `HundunWorld/Source/Game/Equipment/EquipmentData.cs`
  - `HundunWorld/Source/Game/Equipment/EquipmentDatabase.cs`
  - `HundunWorld/Source/Game/UI/StyleSystem/InkWashTheme.cs`（可能新增春色 Token）
  - `HundunWorld/Source/Game/UI/Ink/Components/InkEquipmentSlot.cs`（调整默认尺寸/边框发光）
- 不影响：网络同步、服务端 Grain、ECS 系统、战斗 HUD。

## ADDED Requirements

### Requirement: 三栏角色属性页布局

页面 SHALL 在 1920×1080 及以上分辨率下呈现稳定的三栏结构：

- **左侧属性区（约 30% 宽度）**：从上到下依次为 基础属性（6 项条形）、进阶属性（4 项）、六边形雷达图、武学摘要。
- **中间预览区（约 40% 宽度）**：顶部大字号战力、中央 `CharacterPreview3D`、底部角色名/等级/门派/称号/阶段。
- **右侧装备区（约 30% 宽度）**：顶部 15 装备槽人体拓扑布局、中部背包、底部武学/细节切换。

#### Scenario: 1920×1080 全屏
- **WHEN** 玩家从 CombatHud 点击头像进入属性页
- **THEN** 三栏区域边界清晰，无重叠，中间 3D 预览尺寸 ≥ 420×520，左右两侧 ≥ 340 宽度

### Requirement: 15 个装备槽位

`EquipmentSlot` SHALL 包含 15 个槽位：

`Head, Neck, Shoulder, Back, Body, Waist, Legs, Feet, RightHand, LeftHand, RightRing, LeftRing, RightWrist, LeftWrist, Face`

右侧装备栏 SHALL 按人体拓扑排列为 5 行 3 列，装备槽带槽位类型图标与品质色发光边框。

#### Scenario: 装备槽悬停/双击
- **WHEN** 鼠标悬停到任一装备槽
- **THEN** 显示该槽位当前装备详情 Tooltip
- **WHEN** 双击已装备槽位
- **THEN** 装备卸下并进入背包，属性实时刷新

### Requirement: 柔和阳刚的春色色调

页面 SHALL 使用升级后的色调系统：

- 背景层：由 `#0E1016` 向墨青 `#0E1318` 过渡，柔和不刺眼；
- 主强调色：保留鎏金 `#C8A858`（阳刚/古风）；
- 次强调色：玉色 `#5E8B7E`、春色芽绿 `#8FAE6B` 用于属性条、阶段标签、雷达图外层；
- 分割线/边框使用低透明度金色，避免过于沉重；
- 文字层级：标题金色、正文纸色、次要信息暗纸色。

## MODIFIED Requirements

### Requirement: 现有 `MenuCharAttributesV2Page` 数据绑定

保持现有 `BindCharacter(CharacterAttributesComponent)`、`RecalculateAttributes`、装备双击切换逻辑不变，仅调整 UI 布局与视觉表现。

## REMOVED Requirements

### Requirement: 旧版左右两栏布局
**Reason**: 被三栏布局替代，视觉层次与装备容量不足。
**Migration**: 删除 `PreviewPanelWidth`、`AttributesPanelRightMargin` 等旧布局常量，替换为三栏布局常量。
