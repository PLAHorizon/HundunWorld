# 燕云水墨 UI 内容填充与 P0 核心页落地 Spec

## Why
`land-inkwash-ui-foundation` 已落地 12 页燕云水墨 UI 骨架（InkWashTheme + Ink 组件库 + InkPageRouter + 12 个页面控件），但全部使用硬编码 mock 数据，与游戏真实系统脱节——角色属性页不显示玩家实际属性、技能槽不反映真实冷却、设置页不读写实际配置。同时设计方案 `hundun-yy-ui/pages` 共 55 页，剩余 43 页未落地，其中 9 页 P0 核心玩法页面（战斗 HUD v2、装备管理、阵亡、NPC 对话、战前备战、点穴、QTE、等级提升奖励、角色属性 v2）是玩家核心玩法闭环的必需 UI。本 spec 作为 `land-inkwash-ui-foundation` 的 delta，将 12 页骨架绑定到真实游戏数据系统，并落地 9 页 P0 核心页面，使客户端 UI 具备实际可用内容。

## What Changes
- **内容填充（12 页）**：将已落地 12 页的 mock 数据替换为真实数据绑定
  - `CombatHudPage`：气血/体魄条绑定 `CharacterAttributesComponent`，技能冷却绑定 `SkillBase.GetCooldownProgress()`，任务提示条用增强型 mock
  - `MenuCharAttributesPage`：五行数据绑定 `CharacterAttributesComponent.XxxAffinity`，装备槽绑定 `AppearanceData.EquippedItems` + `EquipmentDatabase`，属性条做字段映射
  - `MenuQuestsPage`：使用增强型 mock（点击任务可推进进度、切换分类过滤）
  - `MenuShopPage`：使用增强型 mock（点击购买扣减 mock 金币、商品售罄状态）
  - `PopupItemAcquired`：基于 `InventorySystem` 事件触发，从 `EquipmentDatabase` 查询物品名/品质
  - `SettingsPage`：全量绑定 `GameConfigurationService.Instance.GetXxx()`，控件变更回调 `SetXxxAsync`
  - `RewardPages`/`LoadingPages`/`PopupMessage`：保持 mock 但增加交互响应
- **P0 核心页落地（9 页）**：新增 9 个页面控件
  - `CombatHudV2Page`：升级版战斗 HUD（小地图 + 队伍状态 + 8 槽技能栏 + 道具栏）
  - `MenuCharAttributesV2Page`：升级版角色属性（战力/基础/进阶/装备摘要/武学摘要）
  - `MenuEquipmentPage`：装备管理（三列布局：背包/纸娃娃/属性对比 + 套装加成）
  - `DeathScreenPage`：阵亡界面（破招复活/返回安全区）
  - `DialogueConfirmPage`：NPC 对话确认（纸色卷轴 + 三选项）
  - `MenuBattlePrepPage`：战前备战（装备配置 + 武学搭配 + 战力评估 + 药品补给）
  - `AcupointPage`：点穴系统（人体穴位图 + 详情面板）
  - `QtePage`：QTE 千钧一发（圆环计时器 + 按键提示）
  - `RewardLevelUpPage`：等级提升奖励（属性提升对比）
- **新增专用组件（4 个）**：`InkMinimap`（小地图）、`InkMeridianDiagram`（人体穴位图）、`InkDialClock`（时辰表盘，为后续 menu-time 预留）、`InkSkillSlotGrid`（8 槽技能栏网格）
- **扩展 InkPageRouter**：注册 9 个新页面 `data-dom-id`，战斗 HUD 底部导航栏"闲趣/异闻/成就/留影"保持占位（P1 页面不在本 spec 范围）
- **不做**：P1/P2/P3 共 34 页（组队/邮箱/门派/活动/创角/收集/引导/休闲/抽卡/多人/拍照等）、3D 角色预览、网络数据同步外观、任务/商店/成就系统的真实数据层（用增强型 mock）

## Impact
- Affected specs:
  - `land-inkwash-ui-foundation`：本 spec 复用其 InkWashTheme/Ink 组件库/InkPageShell/InkPageRouter，修改 12 个已落地页面文件的数据源，扩展 Pages 目录与 Router 注册
  - `character-attribute-panel`：本 spec 的 `MenuCharAttributesV2Page` 采用燕云视觉语言，与该 spec 的 WoW 风格面板并存
  - `character-equipment-system`：本 spec 的 `MenuEquipmentPage` 对接其 `CharacterEquipmentManager`/`EquipmentData`，仅消费数据
- Affected code:
  - `Source/Game/UI/Ink/Pages/CombatHudPage.cs` — 数据绑定改造
  - `Source/Game/UI/Ink/Pages/MenuCharAttributesPage.cs` — 数据绑定改造
  - `Source/Game/UI/Ink/Pages/MenuQuestsPage.cs` — 增强型 mock
  - `Source/Game/UI/Ink/Pages/MenuShopPage.cs` — 增强型 mock
  - `Source/Game/UI/Ink/Pages/PopupPages.cs` — 物品弹窗数据源改造
  - `Source/Game/UI/Ink/Pages/SettingsPage.cs` — 全量绑定 GameConfigurationService
  - `Source/Game/UI/Ink/Pages/RewardPages.cs` — 交互响应增强
  - `Source/Game/UI/Ink/Pages/LoadingPages.cs` — 交互响应增强
  - `Source/Game/UI/Ink/Pages/CombatHudPage.cs` — 任务提示条增强型 mock
  - `Source/Game/UI/Ink/Pages/Character/` — 新增目录，放置 P0 角色相关页面
  - `Source/Game/UI/Ink/Pages/Combat/` — 新增目录，放置 P0 战斗相关页面
  - `Source/Game/UI/Ink/Components/` — 新增目录，放置 4 个专用组件
  - `Source/Game/UI/Ink/InkPageRouter.cs` — 扩展注册 9 个新页面
  - `Source/Game/UI/MainUIManager.cs` — 可能更新页面注册逻辑
  - `Source/Game.csproj` — 追加新文件 Compile Include

## ADDED Requirements

### Requirement: CombatHudPage 真实数据绑定
系统 SHALL 将战斗 HUD 的气血/体魄条绑定到 `CharacterAttributesComponent` 的真实数据，技能槽绑定真实冷却进度。

#### Scenario: 气血体魄实时更新
- **WHEN** 玩家受到伤害或恢复
- **THEN** 气血条 `InkBar` 的 Value 立即更新为 `CharacterAttributesComponent.CurrentHealth / MaxHealth`，体魄条更新为 `CurrentStamina / MaxStamina`，数值标签用 DIN 字体显示具体数值

#### Scenario: 技能冷却同步
- **WHEN** 玩家施放技能进入冷却
- **THEN** 对应技能槽的冷却扇形遮罩进度更新为 `SkillBase.GetCooldownProgress()`，冷却结束时遮罩消失

#### Scenario: 任务提示条增强型 mock
- **WHEN** 战斗 HUD 可见
- **THEN** 顶部任务提示条显示 mock 任务文本与进度（如"寻访江湖名士 3/10"），进度可由外部事件推进

### Requirement: MenuCharAttributesPage 真实数据绑定
系统 SHALL 将角色属性页的五行数据绑定到 `CharacterAttributesComponent`，装备槽绑定到 `AppearanceData.EquippedItems`。

#### Scenario: 五行数据展示
- **WHEN** 角色属性页激活
- **THEN** 右侧 5 个五行 `InkBar` 的 Value 分别为 `MetalAffinity/10000`、`WoodAffinity/10000`、`WaterAffinity/10000`、`FireAffinity/10000`、`EarthAffinity/10000`（原始值 0-10000 映射到 0-1）

#### Scenario: 装备槽展示
- **WHEN** 角色属性页激活
- **THEN** 右侧 6 个装备 `InkCell` 从 `CharacterPersistenceService.LoadAppearanceAsync(characterId)` 获取 `EquippedItems`，通过 `EquipmentDatabase.GetEquipment(itemId)` 查询名称/品质/图标，未装备的槽位显示空格

#### Scenario: 属性字段映射
- **WHEN** 角色属性页激活
- **THEN** 左侧属性列表映射：气血→`CurrentHealth/MaxHealth`、体魄→`CurrentStamina/MaxStamina`、内力→`CurrentEnergy/MaxEnergy`、身法→`PhysicalDefense`（临时映射）、根骨→`Constitution`、悟性→`Intelligence`（临时映射），每项 `InkBar` 显示对应进度

### Requirement: SettingsPage 全量配置绑定
系统 SHALL 将设置页所有控件绑定到 `GameConfigurationService`，控件变更立即异步保存。

#### Scenario: 读取配置
- **WHEN** 设置页激活
- **THEN** 全屏模式控件读取 `GetVSync()`，分辨率控件读取 `GetResolution()`，画面质量读取 `GetGraphicsQuality()`，主音量读取 `GetMasterVolume() * 100`，音效音量读取 `GetSFXVolume() * 100`，操作模式读取 `GetKeyBinding` 体系

#### Scenario: 变更保存
- **WHEN** 玩家拖动主音量滑块
- **THEN** 调用 `GameConfigurationService.Instance.SetMasterVolumeAsync(value / 100f)` 异步保存，同时调用 `GameAudioManager.Instance.MasterVolume = value / 100f` 实时应用

#### Scenario: 分辨率切换
- **WHEN** 玩家选择新分辨率
- **THEN** 调用 `GameConfigurationService.Instance.SetResolutionAsync(width, height)` 保存配置

### Requirement: PopupItemAcquired 真实物品数据
系统 SHALL 将物品获得弹窗绑定到 `InventorySystem` 事件，显示真实物品信息。

#### Scenario: 物品弹窗触发
- **WHEN** `InventorySystem` 检测到新物品入包
- **THEN** 触发 `PopupItemAcquired` 弹窗，物品名从 `EquipmentDatabase.GetEquipment(itemId).Name` 获取，品质从 `.Quality` 获取映射到 `InkQuality` 枚举，数量从 `InventoryItem.Count` 获取

### Requirement: 增强型 mock 交互
系统 SHALL 为缺失数据系统（任务/商店/成就）的页面提供增强型 mock，支持点击交互与状态变化。

#### Scenario: 任务进度推进
- **WHEN** 玩家在 `MenuQuestsPage` 点击某任务项
- **THEN** 该任务进度 +1（如 3/10→4/10），进度满时标记为"已完成"并显示 mock 奖励

#### Scenario: 商店购买交互
- **WHEN** 玩家在 `MenuShopPage` 点击"购买"按钮
- **THEN** mock 金币余额扣减商品价格，商品格子标记"已购买"或售罄，余额不足时显示朱红提示

### Requirement: CombatHudV2Page 战斗 HUD v2
系统 SHALL 落地 `combat-hud-v2.html` 对应的升级版战斗 HUD，包含小地图、队伍状态、8 槽技能栏。

#### Scenario: HUD v2 布局
- **WHEN** 玩家进入战斗或组队状态
- **THEN** 显示 `CombatHudV2Page`：右上角 `InkMinimap` 小地图（AOI 实体点位）、左侧队伍成员状态卡（HP/MP，最多 3 名，用 mock 数据）、右下角 `InkSkillSlotGrid` 8 槽技能栏（含冷却扇形 + 快捷键 + 奇术槽脉冲）、底部道具栏（mock 4 格）

#### Scenario: 小地图实体点位
- **WHEN** AOI 范围内有实体
- **THEN** `InkMinimap` 绘制点位：玩家中心金色、友方翡翠、敌方朱红、NPC 金色，点位位置基于实体相对坐标

### Requirement: MenuCharAttributesV2Page 角色属性 v2
系统 SHALL 落地 `menu-char-attributes-v2.html` 对应的升级版角色属性页。

#### Scenario: v2 布局
- **WHEN** 角色属性 v2 页激活
- **THEN** 显示顶部导航 + 左侧角色预览（名称/等级/门派/称号 mock）+ 右侧属性面板（战力数值 DIN、基础属性 6 项、进阶属性 4 项、装备摘要、武学摘要）+ 底部操作栏
- **THEN** 基础属性数据从 `CharacterAttributesComponent` 读取，装备摘要从 `EquipmentDatabase` 读取

### Requirement: MenuEquipmentPage 装备管理
系统 SHALL 落地 `menu-equipment.html` 对应的装备管理页面。

#### Scenario: 三列布局
- **WHEN** 装备管理页激活
- **THEN** 显示三列：左侧装备背包列表（从 `InventorySystem.GetAllItems` 获取）、中间纸娃娃装备槽（8 槽对应 `EquipmentSlot` 枚举）、右侧属性总览对比
- **THEN** 点击背包物品可穿戴，调用 `CharacterEquipmentManager.EquipBody` 等

#### Scenario: 套装加成显示
- **WHEN** 穿戴多件同套装装备
- **THEN** 右侧显示套装加成激活状态（mock 套装数据）

### Requirement: DeathScreenPage 阵亡界面
系统 SHALL 落地 `death-screen.html` 对应的阵亡界面。

#### Scenario: 阵亡显示
- **WHEN** 玩家 HP 归零
- **THEN** 显示 `DeathScreenPage`：殒命标题（书法字体）+ 朱红"破招"按钮 + 幽影"返回"按钮 + 损失信息（mock 经验/铜钱损失）
- **THEN** 点击"破招"触发复活流程（mock），点击"返回"回到安全区（mock）

### Requirement: DialogueConfirmPage NPC 对话确认
系统 SHALL 落地 `dialogue-confirm.html` 对应的 NPC 对话确认页面。

#### Scenario: 对话显示
- **WHEN** 玩家与 NPC 交互触发对话
- **THEN** 显示 `DialogueConfirmPage`：底部纸色卷轴对话框 + NPC 头像（mock）+ 竖排 NPC 名称 + 对话内容（mock 文本）+ 三选项按钮（接受/拒绝/询问）+ 跳过按钮
- **THEN** 选项回调通过 `DialogueConfirmed` 事件暴露给外部

### Requirement: MenuBattlePrepPage 战前备战
系统 SHALL 落地 `menu-battle-prep.html` 对应的战前备战页面。

#### Scenario: 备战布局
- **WHEN** 战前备战页激活
- **THEN** 显示装备配置面板（8 槽从 `EquippedItems` 读取）+ 武学搭配（主动/被动 mock 列表）+ 战力评估（`InkBar` 进度 + 达成率百分比 DIN）+ 药品补给列表（mock 4 格）

### Requirement: AcupointPage 点穴系统
系统 SHALL 落地 `acupoint.html` 对应的点穴系统页面。

#### Scenario: 穴位图布局
- **WHEN** 点穴页激活
- **THEN** 显示左侧 `InkMeridianDiagram` 人体穴位图（SVG 风格轮廓 + 8 穴位可点击点：百会/太阳/风池/膻中/神阙/合谷/关元/涌泉）+ 右侧穴位详情面板（名称/效果/修炼等级 mock）+ 竖排标题"点穴"

#### Scenario: 穴位点击
- **WHEN** 玩家点击某穴位
- **THEN** 右侧详情面板更新为该穴位信息，穴位点亮金色光晕

### Requirement: QtePage QTE 千钧一发
系统 SHALL 落地 `qte.html` 对应的 QTE 页面。

#### Scenario: QTE 触发
- **WHEN** 触发 QTE 事件
- **THEN** 显示 `QtePage`：水墨氛围背景 + 圆环计时器（3 秒倒数）+ 按键提示（如"J"）+ 连击显示
- **THEN** 计时结束未响应则失败，响应则成功并消失

### Requirement: RewardLevelUpPage 等级提升奖励
系统 SHALL 落地 `reward-level-up.html` 对应的等级提升奖励页面。

#### Scenario: 升级显示
- **WHEN** 玩家经验值溢出触发升级
- **THEN** 显示 `RewardLevelUpPage`：居中模态 + "等级提升"标题（书法字体）+ 属性提升分区（前/后对比，如攻击力 120→135）+ 确认按钮
- **THEN** 属性数据从 `CharacterAttributesComponent` 读取前/后值

### Requirement: 专用组件库扩展
系统 SHALL 新增 4 个专用 Ink 组件支撑 P0 页面。

#### Scenario: InkMinimap 小地图
- **WHEN** combat-hud-v2 需要小地图
- **THEN** 使用 `InkMinimap`：圆形墨色边框面板，支持实体点位绘制（玩家中心金色/友方翡翠/敌方朱红/NPC 金色），点位位置基于相对坐标

#### Scenario: InkMeridianDiagram 人体穴位图
- **WHEN** acupoint 页面需要经络图
- **THEN** 使用 `InkMeridianDiagram`：SVG 风格人体轮廓 + 8 个可点击穴位点，穴位点击触发 `AcupointClicked` 事件

#### Scenario: InkSkillSlotGrid 技能槽网格
- **WHEN** combat-hud-v2 需要 8 槽技能栏
- **THEN** 使用 `InkSkillSlotGrid`：8 个圆形技能槽 + 冷却扇形遮罩 + 快捷键标签 + 奇术槽（更大金边 + 脉冲动画），支持 `SetCooldown(slotIndex, progress)` 方法

#### Scenario: InkDialClock 时辰表盘
- **WHEN** 需要时辰显示（本 spec 内 acupoint 经络提示可选用）
- **THEN** 使用 `InkDialClock`：圆形表盘 + 12 时辰刻度（子/丑/寅/卯/辰/巳/午/未/申/酉/戌/亥）+ 当前时辰指针

### Requirement: InkPageRouter 扩展注册
系统 SHALL 在 `InkPageRouter` 注册 9 个新 P0 页面的 `data-dom-id`。

#### Scenario: 注册完整
- **WHEN** 检查 `InkPageRouter`
- **THEN** 包含 9 个新 `data-dom-id`：`combat-hud-v2`、`nav-character-v2`、`nav-equipment`、`death-screen`、`dialogue-confirm`、`nav-battle-prep`、`acupoint`、`qte`、`reward-level-up`

#### Scenario: 导航可达
- **WHEN** 玩家从战斗 HUD 点击导航按钮
- **THEN** 可跳转到对应 P0 页面（角色属性 v2、装备管理、战前备战、点穴），返回按钮回到战斗 HUD

## MODIFIED Requirements

### Requirement: CombatHudPage 数据源
**原有**: `CombatHudPage` 使用硬编码 mock 数据（HP 8500/10000、体魄 600/1000、5 个技能冷却 `{0, 0.3, 0, 0.7, 0}`、6 个 buff）。

**修改为**: `CombatHudPage` 气血/体魄条从 `CharacterAttributesComponent` 读取真实数据，技能冷却从 `SkillBase.GetCooldownProgress()` 读取，buff 条与任务提示条保持增强型 mock。提供 `BindCharacter(CharacterAttributesComponent)` 方法供外部绑定数据源。

### Requirement: MenuCharAttributesPage 数据源
**原有**: `MenuCharAttributesPage` 使用硬编码 mock 属性（气血/体魄/内力/身法/根骨/悟性）与 mock 装备格。

**修改为**: `MenuCharAttributesPage` 五行数据从 `CharacterAttributesComponent.XxxAffinity` 读取，装备槽从 `AppearanceData.EquippedItems` + `EquipmentDatabase` 读取，属性列表做字段映射。提供 `BindData(CharacterAttributes, AppearanceData)` 方法。

### Requirement: SettingsPage 数据源
**原有**: `SettingsPage` 使用硬编码 mock 值（全屏/分辨率/画质/主音量 80/音效音量 60/操作模式）。

**修改为**: `SettingsPage` 全量绑定 `GameConfigurationService.Instance.GetXxx()`，控件变更回调 `SetXxxAsync` 并实时应用（音量通过 `GameAudioManager`，分辨率通过 `Screen`）。

## REMOVED Requirements
无
