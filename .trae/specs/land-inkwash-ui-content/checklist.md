# Checklist

## 专用组件库
- [x] InkMinimap 实现圆形墨色边框 + 实体点位绘制（玩家金色/友方翡翠/敌方朱红/NPC 金色）
- [x] InkMinimap 提供 AddEntity/ClearEntities 方法
- [x] InkMeridianDiagram 实现 SVG 人体轮廓 + 8 穴位可点击点
- [x] InkMeridianDiagram 提供 AcupointClicked 事件与 SetActiveAcupoint 方法
- [x] InkSkillSlotGrid 实现 8 槽圆形技能 + 冷却扇形遮罩 + 快捷键 + 奇术槽脉冲
- [x] InkSkillSlotGrid 提供 SetCooldown/SetSkillIcon 方法
- [x] InkDialClock 实现 12 时辰表盘 + 当前时辰指针
- [x] InkDialClock 提供 SetCurrentHour 方法

## 12 页数据绑定改造
- [x] CombatHudPage 气血条绑定 CharacterAttributesComponent.CurrentHealth/MaxHealth
- [x] CombatHudPage 体魄条绑定 CurrentStamina/MaxStamina
- [x] CombatHudPage 技能槽冷却绑定 SkillBase.GetCooldownProgress()
- [x] CombatHudPage 提供 BindCharacter(CharacterAttributesComponent) 方法
- [x] CombatHudPage 任务提示条支持 SetQuestProgress 方法（增强型 mock）
- [x] CombatHudPage buff 条支持 AddBuff/ClearBuffs 方法（增强型 mock）
- [x] MenuCharAttributesPage 五行 InkBar 绑定 CharacterAttributes.XxxAffinity/10000
- [x] MenuCharAttributesPage 装备槽绑定 AppearanceData.EquippedItems + EquipmentDatabase
- [x] MenuCharAttributesPage 属性列表字段映射正确（HP/Stamina/Energy/Defense/Constitution/Intelligence）
- [x] MenuCharAttributesPage 提供 BindData(CharacterAttributes, AppearanceData) 方法
- [x] SettingsPage 激活时从 GameConfigurationService 读取全屏/分辨率/画质/音量初值
- [x] SettingsPage 音量滑块变更回调 SetXxxAsync + GameAudioManager 实时应用
- [x] SettingsPage 分辨率/画质变更回调 SetXxxAsync
- [x] PopupItemAcquired 提供 ShowItem(itemId, count) 方法
- [x] PopupItemAcquired 从 EquipmentDatabase 查询物品名/品质映射到 InkQuality
- [x] MenuQuestsPage 任务点击交互：进度 +1，满则标记已完成
- [x] MenuQuestsPage 分类侧边栏切换过滤
- [x] MenuShopPage 购买交互：扣减 mock 金币，售罄标记，余额不足提示
- [x] MenuShopPage 分类切换过滤商品
- [x] RewardAchievementPage 提供 SetReward 方法
- [x] RewardQuestCompletePage 提供 SetReward 方法
- [x] LoadingPages 提供 SetProgress 方法 + ProgressComplete 事件

## P0 核心页落地
- [x] CombatHudV2Page 显示小地图/队伍状态/8 槽技能栏/道具栏
- [x] CombatHudV2Page 小地图使用 InkMinimap（mock 实体点位）
- [x] CombatHudV2Page 技能栏使用 InkSkillSlotGrid（冷却扇形 mock）
- [x] CombatHudV2Page 玩家 HP/MP/XP 绑定 CharacterAttributesComponent
- [x] MenuCharAttributesV2Page 显示顶部导航/左侧预览/右侧属性面板/底部操作栏
- [x] MenuCharAttributesV2Page 基础属性绑定 CharacterAttributesComponent
- [x] MenuCharAttributesV2Page 装备摘要绑定 EquipmentDatabase
- [x] MenuEquipmentPage 显示三列布局（背包/纸娃娃/属性对比）
- [x] MenuEquipmentPage 背包列表从 InventorySystem.GetAllItems 读取
- [x] MenuEquipmentPage 装备槽对应 EquipmentSlot 枚举（8 槽）
- [x] MenuEquipmentPage 点击物品可穿戴（调用 CharacterEquipmentManager）
- [x] MenuEquipmentPage 显示套装加成（mock）
- [x] DeathScreenPage 显示殒命标题/破招按钮/返回按钮/损失信息
- [x] DeathScreenPage 提供 ReviveRequested/ReturnRequested 事件
- [x] DialogueConfirmPage 显示纸色卷轴对话框/头像/竖排名称/对话内容/三选项/跳过
- [x] DialogueConfirmPage 提供 SetDialogue 方法与 DialogueConfirmed 事件
- [x] MenuBattlePrepPage 显示装备配置/武学搭配/战力评估/药品补给
- [x] MenuBattlePrepPage 装备槽从 EquippedItems 读取（当前为 mock + EquipmentDatabase.GetEquipment 查询，真实 EquippedItems 绑定待后续数据层就绪后接入）
- [x] AcupointPage 显示左 InkMeridianDiagram/右详情/竖排标题
- [x] AcupointPage 8 穴位 mock 数据完整（百会/太阳/风池/膻中/神阙/合谷/关元/涌泉）
- [x] AcupointPage 穴位点击切换详情 + 金色光晕
- [x] QtePage 显示水墨氛围/圆环计时器/按键提示/连击显示
- [x] QtePage 计时驱动 + QteFailed/QteSucceeded 事件
- [x] RewardLevelUpPage 显示居中模态/等级提升标题/属性对比/确认按钮
- [x] RewardLevelUpPage 提供 SetLevelUp 方法与 Confirmed 事件

## 路由与导航
- [x] InkPageDomIds 新增 9 个常量
- [x] InkPageRouter 注册 9 个新页面构造委托
- [x] 战斗 HUD 底部导航栏新增装备/战前备战/点穴入口
- [x] 新文件已追加到 Game.csproj 的 Compile Include

## 编译与回归
- [x] Flax.Build 编译 HundunWorld 项目 0 错误
- [ ] PIE 中 12 页数据绑定验证（角色属性真实五行/装备、设置页读写配置、HUD 气血/冷却同步）
- [ ] PIE 中 9 个 P0 新页面视觉与 HTML 原型一致
- [ ] 旧 12 页面无回归（数据绑定改造不破坏现有布局）
