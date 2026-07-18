# Tasks

- [x] Task 1: 新增 4 个专用 Ink 组件
  - [x] SubTask 1.1: 创建 `Source/Game/UI/Ink/Components/InkMinimap.cs`（圆形墨色边框 + 实体点位绘制 + `AddEntity(type, relativeX, relativeZ)`/`ClearEntities()` 方法 + `InkMinimapEntityType` 枚举：Player/Friendly/Enemy/NPC）
  - [x] SubTask 1.2: 创建 `Source/Game/UI/Ink/Components/InkMeridianDiagram.cs`（SVG 风格人体轮廓 `Draw()` + 8 穴位可点击点 + `AcupointClicked(int index)` 事件 + `SetActiveAcupoint(index)` 方法 + 穴位名称常量数组）
  - [x] SubTask 1.3: 创建 `Source/Game/UI/Ink/Components/InkSkillSlotGrid.cs`（8 圆形技能槽 + 冷却扇形遮罩 `Draw()` + 快捷键标签 + 奇术槽脉冲动画 `Update()` + `SetCooldown(slotIndex, progress)`/`SetSkillIcon(slotIndex, texture)` 方法）
  - [x] SubTask 1.4: 创建 `Source/Game/UI/Ink/Components/InkDialClock.cs`（圆形表盘 `Draw()` + 12 时辰刻度 + 当前时辰指针 + `SetCurrentHour(int hour)` 方法）

- [x] Task 2: CombatHudPage 真实数据绑定改造
  - [x] SubTask 2.1: 新增 `BindCharacter(CharacterAttributesComponent)` 方法，气血条绑定 `CurrentHealth/MaxHealth`，体魄条绑定 `CurrentStamina/MaxStamina`，数值标签 DIN 字体显示具体值
  - [x] SubTask 2.2: 技能槽冷却绑定 `SkillBase.GetCooldownProgress()`，新增 `BindSkills(SkillBase[] slots)` 方法，每帧 `Update()` 中刷新冷却扇形
  - [x] SubTask 2.3: 任务提示条改为增强型 mock：新增 `SetQuestProgress(string name, int current, int target)` 方法，外部可推进进度
  - [x] SubTask 2.4: buff 条改为增强型 mock：新增 `AddBuff(string name, bool isDebuff)`/`ClearBuffs()` 方法，支持动态增减

- [x] Task 3: MenuCharAttributesPage 真实数据绑定改造
  - [x] SubTask 3.1: 新增 `BindData(CharacterAttributes attributes, AppearanceData appearance)` 方法
  - [x] SubTask 3.2: 五行 InkBar 绑定 `attributes.Metal/Wood/Water/Fire/Earth`（除以 10000 映射 0-1）
  - [x] SubTask 3.3: 装备槽绑定 `appearance.EquippedItems`，通过 `EquipmentDatabase.GetEquipment(itemId)` 查询名称/品质/图标，未装备显示空格
  - [x] SubTask 3.4: 属性列表字段映射：气血→HP、体魄→Stamina、内力→Energy、身法→PhysicalDefense、根骨→Constitution、悟性→Intelligence，InkBar 显示对应进度

- [x] Task 4: SettingsPage 全量配置绑定改造
  - [x] SubTask 4.1: 页面激活时从 `GameConfigurationService.Instance` 读取全屏/分辨率/画质/主音量/音效音量/操作模式初值
  - [x] SubTask 4.2: 主音量/音效音量滑块变更时调用 `SetMasterVolumeAsync/SetSFXVolumeAsync` 保存 + `GameAudioManager.Instance.MasterVolume/SfxVolume` 实时应用
  - [x] SubTask 4.3: 分辨率/画面质量变更时调用对应 `SetXxxAsync` 保存
  - [x] SubTask 4.4: 分辨率列表从 `Screen.GetSupportedDisplayModes` 获取（若 FlaxEngine 支持），否则保持 mock 列表

- [x] Task 5: PopupPages 物品弹窗数据源改造
  - [x] SubTask 5.1: `PopupItemAcquired` 新增 `ShowItem(ulong itemId, int count)` 方法，从 `EquipmentDatabase.GetEquipment(itemId)` 查询名称/品质映射到 `InkQuality` 枚举
  - [x] SubTask 5.2: `PopupMessage` 保持 mock 但增加 `ShowMessage(title, content)` 方法供外部调用

- [x] Task 6: MenuQuestsPage 与 MenuShopPage 增强型 mock
  - [x] SubTask 6.1: `MenuQuestsPage` 新增任务点击交互：点击任务项进度 +1，进度满标记"已完成"显示 mock 奖励，分类侧边栏切换过滤
  - [x] SubTask 6.2: `MenuShopPage` 新增购买交互：点击购买扣减 mock 金币余额（初始 1000 两），商品售罄标记，余额不足显示朱红提示，分类切换过滤商品

- [x] Task 7: RewardPages 与 LoadingPages 交互增强
  - [x] SubTask 7.1: `RewardAchievementPage`/`RewardQuestCompletePage` 增加 `SetReward(name, description, items[])` 方法供外部填充
  - [x] SubTask 7.2: `LoadingPages` 进度条改为可外部驱动：新增 `SetProgress(float value)` 方法，进度满触发 `ProgressComplete` 事件

- [x] Task 8: 落地 CombatHudV2Page（P0 战斗 HUD v2）
  - [x] SubTask 8.1: 创建 `Source/Game/UI/Ink/Pages/Combat/CombatHudV2Page.cs`
  - [x] SubTask 8.2: 右上角 `InkMinimap` 小地图（mock 实体点位，玩家中心 + 2 友方 + 2 敌方 + 1 NPC）
  - [x] SubTask 8.3: 左侧队伍成员状态卡（3 名 mock 成员，HP/MP 条 + 名称 + 职业图标）
  - [x] SubTask 8.4: 右下角 `InkSkillSlotGrid` 8 槽技能栏（含冷却扇形 mock + 快捷键 + 奇术槽脉冲）
  - [x] SubTask 8.5: 底部道具栏（4 格 mock）
  - [x] SubTask 8.6: 玩家 HP/MP/XP 条绑定 `CharacterAttributesComponent`（复用 CombatHudPage 绑定逻辑）

- [x] Task 9: 落地 MenuCharAttributesV2Page（P0 角色属性 v2）
  - [x] SubTask 9.1: 创建 `Source/Game/UI/Ink/Pages/Character/MenuCharAttributesV2Page.cs`
  - [x] SubTask 9.2: 顶部导航 + 左侧角色预览（名称/等级/门派/称号 mock）+ 底部操作栏
  - [x] SubTask 9.3: 右侧属性面板：战力数值 DIN（mock 计算）+ 基础属性 6 项（绑定 `CharacterAttributesComponent`）+ 进阶属性 4 项 mock + 装备摘要（绑定 `EquipmentDatabase`）+ 武学摘要 mock

- [x] Task 10: 落地 MenuEquipmentPage（P0 装备管理）
  - [x] SubTask 10.1: 创建 `Source/Game/UI/Ink/Pages/Character/MenuEquipmentPage.cs`
  - [x] SubTask 10.2: 三列布局：左侧装备背包列表（`InkListItem` 从 `InventorySystem.GetAllItems` 读取）+ 中间纸娃娃装备槽（8 槽 `InkCell` 对应 `EquipmentSlot` 枚举）+ 右侧属性总览对比
  - [x] SubTask 10.3: 排序筛选栏（品质/类型筛选 mock）+ 套装加成显示（mock 套装数据）
  - [x] SubTask 10.4: 点击背包物品可穿戴（调用 `CharacterEquipmentManager.EquipBody` 等，mock 交互）

- [x] Task 11: 落地 DeathScreenPage（P0 阵亡界面）
  - [x] SubTask 11.1: 创建 `Source/Game/UI/Ink/Pages/Combat/DeathScreenPage.cs`
  - [x] SubTask 11.2: 全屏遮罩 + 殒命标题（书法字体 `InkTextBlock` Display）+ 损失信息（mock 经验/铜钱损失）
  - [x] SubTask 11.3: 朱红"破招"按钮（`InkButton` Vermilion）+ 幽影"返回"按钮（`InkButton` Ghost）
  - [x] SubTask 11.4: `ReviveRequested`/`ReturnRequested` 事件暴露给外部

- [x] Task 12: 落地 DialogueConfirmPage（P0 NPC 对话确认）
  - [x] SubTask 12.1: 创建 `Source/Game/UI/Ink/Pages/Social/DialogueConfirmPage.cs`（注：Social 目录为 P0 对话页放置，与后续 P1 社交页共享）
  - [x] SubTask 12.2: 底部纸色卷轴对话框（`InkPaperPanel`）+ NPC 头像占位（`InkCell`）+ 竖排 NPC 名称（`InkVerticalTitle`）
  - [x] SubTask 12.3: 对话内容文本（`InkTextBlock` Body + `TextWrapping.WrapWords`）+ 三选项按钮（接受 `InkButton` Primary / 拒绝 `InkButton` Vermilion / 询问 `InkButton` Ghost）+ 跳过按钮
  - [x] SubTask 12.4: `SetDialogue(npcName, content)` 方法 + `DialogueConfirmed(option)` 事件

- [x] Task 13: 落地 MenuBattlePrepPage（P0 战前备战）
  - [x] SubTask 13.1: 创建 `Source/Game/UI/Ink/Pages/Combat/MenuBattlePrepPage.cs`
  - [x] SubTask 13.2: 装备配置面板（8 槽 `InkCell`，通过 `EquipmentDatabase.GetEquipment` mock 读取已装备物品）+ 属性加成摘要（攻防血速 4 项）
  - [x] SubTask 13.3: 武学搭配（主动技能 4 格 + 被动技能 4 格 mock）+ 战力评估（`InkBar` 朱红进度 + 达成率百分比 DIN 字体）
  - [x] SubTask 13.4: 药品补给列表（4 格 mock `InkCell` + `InkCell.Badge` 数量徽章，`InkPaperPanel` 纸色卷轴）

- [x] Task 14: 落地 AcupointPage（P0 点穴系统）
  - [x] SubTask 14.1: 创建 `Source/Game/UI/Ink/Pages/Combat/AcupointPage.cs`
  - [x] SubTask 14.2: 左侧 `InkMeridianDiagram` 人体穴位图 + 竖排标题"点穴"（`InkVerticalTitle`）
  - [x] SubTask 14.3: 右侧穴位详情面板（`InkPanel` + 穴位名 `InkTextBlock` Heading + 效果 `InkTextBlock` Body + 修炼等级 `InkBar`）
  - [x] SubTask 14.4: 8 穴位 mock 数据（百会/太阳/风池/膻中/神阙/合谷/关元/涌泉，含效果文本与修炼等级）
  - [x] SubTask 14.5: 穴位点击切换详情 + 穴位点亮金色光晕

- [x] Task 15: 落地 QtePage（P0 QTE 千钧一发）
  - [x] SubTask 15.1: 创建 `Source/Game/UI/Ink/Pages/Combat/QtePage.cs`
  - [x] SubTask 15.2: 水墨氛围背景（半透明遮罩 + `InkSplash` 装饰）
  - [x] SubTask 15.3: 圆环计时器（`Draw()` 绘制 3 秒倒数圆环 + 朱红进度）+ 按键提示（大字号 `InkTextBlock` Display）+ 连击显示
  - [x] SubTask 15.4: `Update()` 驱动计时，计时结束触发 `QteFailed` 事件，按键触发 `QteSucceeded` 事件

- [x] Task 16: 落地 RewardLevelUpPage（P0 等级提升奖励）
  - [x] SubTask 16.1: 创建 `Source/Game/UI/Ink/Pages/Reward/RewardLevelUpPage.cs`（注：Reward 目录与现有 RewardPages.cs 并存，或合并到 RewardPages.cs）
  - [x] SubTask 16.2: 居中模态 + "等级提升"标题（书法字体）+ 金色光晕装饰
  - [x] SubTask 16.3: 属性提升分区（前/后对比，如"攻击力 120→135"，用 `InkTextBlock` Number + 朱红箭头）
  - [x] SubTask 16.4: `SetLevelUp(int newLevel, AttributeChange[] changes)` 方法 + `Confirmed` 事件

- [x] Task 17: 扩展 InkPageRouter 注册 9 个新页面
  - [x] SubTask 17.1: 在 `InkPageDomIds` 新增 9 个常量：`CombatHudV2`/`NavCharacterV2`/`NavEquipment`/`DeathScreen`/`DialogueConfirm`/`NavBattlePrep`/`Acupoint`/`Qte`/`RewardLevelUp`
  - [x] SubTask 17.2: 在 `RegisterInkPages` 注册 9 个新页面的构造委托
  - [x] SubTask 17.3: 更新战斗 HUD 底部导航栏：角色属性按钮可切换 v1/v2（或直接替换为 v2），新增装备/战前备战/点穴入口
  - [x] SubTask 17.4: 追加新文件到 `Source/Game.csproj` 的 `<Compile Include>`

- [x] Task 18: 编译验证与 PIE 走查
  - [x] SubTask 18.1: 关闭 Flax Editor，执行 `Flax.Build` 编译 HundunWorld 项目，确保 0 错误
  - [ ] SubTask 18.2: PIE 中验证 12 页数据绑定（角色属性显示真实五行/装备、设置页读写配置、战斗 HUD 气血/技能冷却同步）
  - [ ] SubTask 18.3: PIE 中验证 9 个 P0 新页面视觉与导航，对照 HTML 原型核对布局
  - [ ] SubTask 18.4: 验证旧 12 页面无回归（数据绑定改造不破坏现有布局）

# Task Dependencies
- [Task 8/9/10/14/15/16] 依赖 [Task 1]（专用组件库就绪）
- [Task 2/3/4/5/6/7] 相互独立，可并行（12 页数据绑定改造互不依赖）
- [Task 8/9/10/11/12/13/14/15/16] 相互独立，可并行（P0 页面互不依赖）
- [Task 17] 依赖 [Task 8-16]（所有 P0 页面就绪后注册）
- [Task 18] 依赖 [Task 17]（注册后 PIE 验证）
