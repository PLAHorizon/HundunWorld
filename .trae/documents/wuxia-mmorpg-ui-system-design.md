# 混沌世界 MMORPG UI 系统设计计划

仿燕云十六声水墨古风风格 - 高保真视觉原型 + UI 架构规范

## 摘要

为 HundunWorld 武侠 MMORPG 设计一套完整的 UI 系统，视觉风格仿照燕云十六声的极简沉浸式水墨古风美学，功能覆盖核心战斗 HUD、角色装备、武学心法奇术、背包、任务、地图、社交、商城、指南针共 9 个界面。产出包括 `.design` 画布高保真 HTML 原型 + UI 架构规范文档。视觉设计基于 TRAE Work 设计系统的组件结构，覆盖为水墨古风 token 系统。

## 现状分析

### 项目技术栈

- 客户端: Flax Engine (C#)
- 服务端: .NET 10 + Orleans 分布式框架
- 网络: TouchSocket + MemoryPack 序列化
- 存储: SqlServer + EFCore + Redis
- 现有场景: Login.scene、Character.scene、Main.scene、World.scene、RootScene.scene

### 已有游戏系统 (后端 Grain + 数据模型)

| 系统 | 关键 Grain | 关键枚举/模型 |
|------|-----------|--------------|
| 角色系统 | CharacterGrain | CharacterEntity (等级/经验/境界/战力/五维属性/外观定制) |
| 战斗系统 | CombatGrain + CombatCalculator | CombatStateKind, DamageType, EffectType |
| 技能系统 | SkillGrain | SkillType (主动/被动/特殊/控制/位移/终结技), SkillLevel (初/中/高/大师), SkillBookEntity |
| 装备系统 | InventoryGrain | EquipmentSlot (头/胸/腿/手/脚/武器/饰品/主副手/戒指/项链/耳环), EquipmentQuality (普通→神话), ItemEntity |
| 背包系统 | InventoryGrain | BagEntity (主背包/材料/任务/时装 4种), ItemCategory, CurrencyType |
| 任务系统 | QuestGrain | QuestCategory (主线/支线/日常/周常/活动), QuestStatus |
| 社交系统 | SocialGrain + IMUserGrain | ChatKind (世界/队伍/公会/私聊/系统/区域), GuildEntity |
| 门派系统 | - | Profession (少林/武当/峨眉等 15 派 + 无门派) |
| 交易系统 | TradeGrain + MarketGrain | TradeLogEntity |
| 排行榜 | RankingGrain | LeaderboardCategory (等级/战斗/财富) |
| 制作系统 | CraftingGrain | - |
| 副本系统 | DungeonGrain | - |
| 成就系统 | AchievementGrain | AchievementCategory, AchievementStatus |

### 燕云十六声 UI 研究要点

- **极简沉浸式 HUD**: 去除传统网游的跑马灯/活动弹窗，任务栏默认隐藏，最大化画面沉浸感
- **五维属性体系**: 体(气血)/御(防御)/敏(会心)/势(会意)/劲(外功攻击)
- **武学+心法+奇术**: 可同时装备 2 武学 + 4 心法 + 4 奇术
- **装备调律系统**: 左4件套+右4件套+弓+玦，调律/定音/叠音/武库四重强化
- **地图系统**: 界碑传送、蹊跷收集、宝箱(需天赋)、采集标记、单/多人世界切换
- **指南针**: 融合古代司南/风水罗盘美学

### 设计约束

- **Solo Design 技能**: create-project 工作流，Library-Bound 模式 (TRAE Work 为参考设计系统)
- **TRAE Work 特性**: Light mode、`--bg-brand` 主色、`.ds-*` 组件类、token-first 原则
- **风格冲突处理**: 用户明确要求燕云十六声水墨古风，与 TRAE Work 的浅色科技风冲突。采用"结构保留 + 视觉覆盖"策略: 保留 `.ds-*` 组件结构和间距系统，通过自定义 `colors_and_type.css` 覆盖颜色/字体/纹理 token 为水墨古风
- **设备类型**: desktop (PC 游戏 UI)
- **响应式**: 游戏内 UI 以 1920x1080 为基准，桌面端为主

## 提议变更

### 1. 创建 .design 画布项目

**路径**: `c:\Works\GitHubProjects\HundunWorld\game-ui-system\`

**结构**:
```
game-ui-system/
├── game-ui-system.design          # .design 画布项目 (含9个页面节点)
├── colors_and_type.css            # 水墨古风 token 覆盖 CSS
├── pages/                         # 9个界面 HTML
│   ├── 01-combat-hud.html
│   ├── 02-character-panel.html
│   ├── 03-skill-panel.html
│   ├── 04-inventory.html
│   ├── 05-quest-log.html
│   ├── 06-world-map.html
│   ├── 07-social-guild.html
│   ├── 08-shop.html
│   └── 09-compass.html
├── assets/                        # 古风装饰素材
│   ├── textures/                  # 水墨纹理
│   ├── borders/                   # 鎏金边框
│   ├── icons/                     # 系统图标
│   └── ui-elements/               # UI 组件素材
└── ui-architecture-spec.md        # UI 架构规范文档
```

**原因**: 这是 Solo Design 技能的标准画布项目交付格式，HTML 原型提供高保真视觉预览，`.design` 文件管理页面间导航关系。

### 2. 水墨古风 Token 系统 (`colors_and_type.css`)

在 TRAE Work 的 `colors_and_type.css` 基础上，新增 `--ink-*` 前缀的视觉覆盖变量，实现水墨古风美学:

**色板**:
- 背景层: `--ink-bg-void` (#08090C) → `--ink-bg-paper` (#1C1F28) → `--ink-bg-mist` (#232732)，深色水墨渐变
- 主色鎏金: `--ink-gold-primary` (#C8A858)，用于标题/强调/激活态
- 辅色青玉: `--ink-jade-primary` (#5E8B7E)，用于次要强调/增益效果
- 品质色: 凡(灰褐)/良(青玉)/稀(青蓝)/珍(紫)/传(鎏金)/神(朱红)
- 五行色: 金/木/水/火/土 各对应古风色调

**字体**:
- 标题: STKaiti / KaiTi / Noto Serif SC (楷书衬线)
- 正文: Noto Sans SC / PingFang SC / Microsoft YaHei (黑体无衬线)
- 数字: DIN Alternate / Roboto Mono (等宽数字)

**圆角**: 克制古风 (0px 直角 → 8px 大圆角，不使用超过 8px 的圆角)

**边框**: 鎏金线系列 (rgba(200,168,88, 0.08~0.5) 不同透明度)

**阴影**: 深色水墨投影 + 鎏金/玉色微光晕

**纹理**: CSS 背景实现的水墨晕染/宣纸纤维/暗角渐变

### 3. 九个界面 HTML 原型设计

每个页面引用 `colors_and_type.css` 中的 `--ink-*` token，使用 `.ds-*` 组件类作为结构基础，叠加古风装饰元素。

#### 3.1 核心战斗 HUD (`01-combat-hud.html`)

- **布局**: 全屏 1920x1080，四角分布 UI 元素，中央保留 3D 渲染区
- **左上**: 角色头像(八角形金线描边) + HP/MP/体力条(水墨渐变) + 等级 + 门派徽章
- **上中**: 任务追踪器(默认隐藏，水墨纸卷造型，点击展开)
- **右上**: 圆形小地图(水墨晕染边缘) + 指南针指针 + 标记系统(NPC金/怪物红/队友玉)
- **左下**: Buff/Debuff 竖向列表(增益玉色/减益朱红，剩余时间倒计时)
- **下中**: 武器切换 + 5-8技能槽(金线描边，冷却扇形遮罩+数字) + 终结技(鎏金充能条)
- **右下**: 10格快捷物品栏(1-0键)
- **交互**: 技能拖拽重排、武器切换水墨过渡、任务追踪器卷轴展开

#### 3.2 角色属性装备面板 (`02-character-panel.html`)

- **左侧**: 3D角色立绘预览(水墨纸卷边框) + 外观切换按钮
- **中上**: 五维属性面板(体/御/敏/势/劲，竖向进度条，楷书属性名+等宽数字)
- **中下**: 基础属性(攻击/防御/暴击/命中/闪避) + 战斗属性(战力/境界/门派/阵营/侠义值)
- **右上**: 人形装备槽位图(对应 EquipmentSlot 全部槽位，品质色光晕)
- **右下**: 装备详情(图标+品质+强化+五行+耐久) + **调律区域**(无相攻击加成) + **定音区域**(词条列表) + **叠音区域**(层数进度) + 套装效果 + 宝石槽

#### 3.3 武学心法奇术面板 (`03-skill-panel.html`)

- **顶部标签**: 武学/心法/奇术 三页切换(鎏金下划线+水墨晕染)
- **左侧**: 技能列表(按 SkillType 分组，品质色描边，等级角标) + 秘籍库(完整秘籍/残卷/心得/传承玉简)
- **右侧**: 技能详情(演示区+名称+门派+类型+等级) + 技能效果(伤害/冷却/消耗/范围) + 天赋树(节点连线图)
- **左下**: 心法槽位(4个) + 奇术槽位(3个)
- **操作**: 升级/装备/卸下按钮

#### 3.4 背包行囊 (`04-inventory.html`)

- **顶部标签**: 主背包/材料/任务/时装 (对应 BagEntity 4种类型) + 整理/仓库按钮
- **左侧**: 8列网格格子区(64x64px，品质色边框+数量角标+强化角标)
- **右侧**: 物品详情(大图+品质光晕+完整属性+绑定+耐久+套装效果) + 使用/装备/丢弃按钮
- **左下**: 货币栏(铜币/银两/金锭/元宝，对应 CurrencyType)
- **交互**: 拖拽支持(背包内移动/装备穿戴/交易拖入)、右键菜单

#### 3.5 任务日志 (`05-quest-log.html`)

- **顶部标签**: 主线/支线/日常/周常/活动 (对应 QuestCategory) + 进行中数量角标
- **左侧**: 任务列表(按分类折叠，状态图标: ★进行中/○未开始/✓已完成/✗已失败)
- **右侧**: 任务详情(标题+分类+难度+描述+目标列表+进度+奖励预览) + 放弃/追踪按钮

#### 3.6 世界地图 (`06-world-map.html`)

- **顶部**: 区域切换标签 + 单人/多人世界切换
- **左侧竖排筛选**: 界碑/蹊跷/宝箱/采集标记(草药/树木/走兽/飞禽/矿物，最多5种)/江湖故人
- **中央**: 水墨山水画风格地图，标记系统(对应 MarkerType): 界碑传送(金菱形)、蹊跷(昆虫形)、宝箱、NPC(金点)、怪物(红点)、玩家(星形+朝向)
- **右上**: 区域信息(名称+坐标+名望+众生任务)
- **下方**: 标点搜索栏 + 采集标记选择

#### 3.7 社交好友门派 (`07-social-guild.html`)

- **顶部标签**: 好友/门派/聊天/队伍
- **左侧**: 好友列表(在线/离线分组) + 门派信息(15派之一+职位+贡献) + 帮会信息(等级1-5/50-5000人+公告)
- **右侧**: 玩家信息卡(头像+名称+等级+门派+战力) + 操作(私聊/组队/交易/删除) + 聊天窗口(世界/队伍/门派/私聊/区域频道，对应 ChatKind) + 消息输入框

#### 3.8 商城商店 (`08-shop.html`)

- **顶部标签**: 推荐/武器/护甲/消耗品/材料/特殊 (对应 ShopCategory)
- **左侧**: 4列商品网格(图标+名称+价格+限购标识，品质色边框)
- **右侧**: 购物车(已选列表+合计) + 货币余额(金币/钻石/荣誉) + 确认购买

#### 3.9 指南针系统 (`09-compass.html`)

- **中央**: 圆形罗盘(外圈天干地支甲乙丙丁/子丑寅卯，内圈八卦方位，中心玩家星标+朝向指针) + 附近兴趣点方向标记
- **下方**: 方位信息(朝向角度+三维坐标+区域名称+附近兴趣点列表含距离方向)
- **底部**: 模式切换(罗盘/地图) + 追踪目标选择

### 4. 古风装饰素材生成

通过 GenerateImage 生成水墨古风装饰素材，注册为 `.design` 的 `type: "image"` 节点:

**纹理素材** (4张):
- `ink-wash-bg-dark.jpg` (1920x1080): 浓墨晕染深色背景
- `ink-wash-panel.jpg` (800x600): 面板水墨纸纹纹理
- `rice-paper-overlay.png` (256x256): 宣纸纤维叠层(可平铺)
- `vignette-ink.png` (1920x1080): 水墨暗角晕染

**边框素材** (6张):
- `gold-filigree-corner-{tl,tr,bl,br}.png` (64x64): 鎏金镂空转角(回纹/卷草纹)
- `ink-divider-horizontal.png` (600x4): 水墨横向分割线
- `ink-divider-vertical.png` (4x600): 水墨纵向分割线

**UI 元素素材** (4张):
- `health-bar-bg.png` / `mana-bar-bg.png` (200x20): 血条/蓝条凹槽背景
- `skill-cd-overlay.png` (64x64): 技能冷却扇形遮罩
- `compass-ring.png` (400x400): 鎏金罗盘圆环(天干地支刻度)

### 5. UI 架构规范文档 (`ui-architecture-spec.md`)

独立 Markdown 文档，包含:

1. **概述**: 项目背景、技术栈、设计约束
2. **设计系统规范**: Token 系统(颜色/字体/间距/圆角/边框/阴影/纹理)、组件库映射、品质色系表、五行色系表
3. **界面架构**: 界面层级管理(SceneType 映射)、界面状态机(GameState/TransitionPhase)、导航关系图、加载策略(SceneLoadStrategy)
4. **组件规范**: 基础组件(按钮/输入/滑块/标签页/面板)、复合组件(物品格子/技能槽/装备槽位图/血条)、装饰组件(水墨边框/鎏金转角/分割线)
5. **数据绑定规范**: Grain→UI 数据流(Orleans Grain→TouchSocket→MemoryPack→UI)、各界面 Grain 接口映射、实时同步策略(HP/MP/Buff/位置)
6. **交互规范**: 键盘快捷键映射、拖拽规范、动画过渡(AnimationType/EasingType)、错误处理(ErrorHandlingStrategy)
7. **Flax Engine 集成**: HTML 原型→Flax UICanvas 转换指南、`.ds-*`→Flax 控件映射、CSS 变量→C# 常量映射

## 实现步骤 (遵循 Solo Design create-project 工作流)

### 步骤 1: 确认设计风格

- Library-Bound 模式: 读取 TRAE Work 的 SKILL.md/css.json 提取 token 和组件类
- 生成 `colors_and_type.css`: 定义全部 `--ink-*` 水墨古风 token 覆盖变量
- 提取 TRAE Work 的 `.ds-*` 组件清单用于页面结构基础
- 将 TRAE Work 图标库复制到项目 `assets/icons/` (利用已有 SVG 资源)

### 步骤 2: 初始化 .design 项目

- 创建 `game-ui-system/` 目录结构
- 写入 `game-ui-system.design` 文件: 包含 9 个页面节点骨架(预分配 ID 和 htmlSrc)
- 写入 `orchestration-summary.json`: 记录 token 引用、页面计划、组件计划、素材计划
- 页面节点配置: `deviceType: "desktop"`，每页 `pageIndex` 1-9

### 步骤 3: 并行生成古风素材

- 使用 GenerateImage 并行生成全部纹理/边框/UI 元素素材(共约 14 张)
- 每张素材生成后注册为 `.design` 的 `type: "image"` 节点
- 素材放置在 `assets/` 对应子目录

### 步骤 4: 并行生成 9 个页面 HTML

- **串行先生成**: `01-combat-hud.html` (作为视觉基准，其他页面继承其风格)
- **并行生成剩余 8 页**: 每个子智能体读取 Solo Design 约束文件 + token 引用 + 组件计划，生成 HTML
- 每个 HTML 引用 `colors_and_type.css` (通过 `fill-html-head.mjs` 内联)
- 使用 Tailwind CDN + `.ds-*` 组件类 + `--ink-*` token 变量 + 古风装饰素材

### 步骤 5: 页面排序 + 导航注册

- 主智能体按逻辑顺序排列 9 个页面节点
- 注册页面间导航交互: HUD → 各面板 → 返回 HUD 的线性导航链
- 设置 `hideEdge: true` 的隐藏交互(如全局菜单跳转)

### 步骤 6: 阻塞验证

- 运行 `scan-design-directory.mjs` 验证:
  - `.design` 文件 JSON 有效性
  - 9 个页面节点的 `htmlSrc` 指向真实存在的 HTML 文件
  - 所有图片素材注册为 image 节点
  - 节点 ID 唯一性
  - 交互定义完整性
- 修复所有验证错误后方可继续

### 步骤 7: 编写 UI 架构规范文档

- 生成 `ui-architecture-spec.md`，涵盖上述第 5 节全部章节
- 包含数据模型映射表、Grain 接口对照表、Flax Engine 集成指南

### 步骤 8: 预览交付

- 通知用户打开 `.design` 文件预览
- 输出页面摘要表

## 假设与决策

### 决策 1: 视觉风格覆盖策略

- **假设**: TRAE Work 的浅色科技风不适合武侠游戏 UI
- **决策**: 采用"结构保留 + 视觉覆盖"策略。保留 `.ds-*` 组件类和间距系统，通过 `colors_and_type.css` 中 `--ink-*` 变量覆盖颜色/字体/纹理。这是用户对 Library 视觉约束的明确覆盖，仅覆盖视觉维度，组件结构和布局规则仍遵循 TRAE Work

### 决策 2: 五维属性映射

- **假设**: 燕云十六声的 体/御/敏/势/劲 五维体系需映射到项目 `AttributeType` 枚举
- **决策**: UI 层做映射转换，不修改后端枚举: Constitution→体, Defense→御, Agility→敏, Luck→势, Strength→劲

### 决策 3: 调律/定音/叠音系统 UI

- **假设**: 项目后端已有相关消息定义，但 UI 需参考燕云十六声的实际玩法设计
- **决策**: 在角色装备面板的详情区域设置三个子区域: 调律(无相攻击加成)、定音(词条列表+激活状态)、叠音(层数进度+共鸣效果)

### 决策 4: 页面生成顺序

- **决策**: 先生成 `01-combat-hud.html` 作为视觉基准(串行)，其余 8 页并行生成。因为 HUD 定义了整体视觉风格(配色/字体/装饰元素)，其他页面需继承

### 决策 5: 指南针系统独立页面

- **决策**: 指南针作为独立全屏页面设计(融合古代司南/风水罗盘美学)，同时在 HUD 小地图中嵌入简化版指南针指针

### 决策 6: Flax Engine 集成路径

- **决策**: HTML 原型作为视觉规范和交互参考，不直接转换为 Flax 代码。在 UI 架构规范文档中提供 HTML→Flax UICanvas 的映射指南(`.ds-panel`→`UIPanel`, CSS Grid→`UGridPanel` 等)

### 决策 7: 现有 .design 项目

- **假设**: 工作区可能已有登录/角色创建等 .design 项目
- **决策**: 本次创建独立的 `game-ui-system` 新项目，不修改现有项目。如需整合可在后续迭代中处理

## 验证步骤

1. **设计文件验证**: 运行 `scan-design-directory.mjs` 确认 `.design` JSON 有效、9 个页面节点完整、所有 htmlSrc 指向真实文件
2. **素材注册验证**: 确认所有 `assets/` 下的图片文件都注册为 `.design` 的 `type: "image"` 节点
3. **Token 覆盖验证**: 在浏览器中预览每个 HTML 页面，确认水墨古风视觉风格一致(深色背景/鎏金强调/楷书标题/水墨纹理)
4. **组件一致性验证**: 确认所有页面使用统一的 `.ds-*` 组件类和 `--ink-*` token 变量
5. **数据映射验证**: 检查 UI 架构规范文档中每个界面的 Grain 接口映射和数据模型对应关系
6. **交互完整性验证**: 确认页面间导航交互定义完整，线性导航链无断链
7. **响应式验证**: 确认页面在 1920x1080 和 1280x720 下均正常渲染
