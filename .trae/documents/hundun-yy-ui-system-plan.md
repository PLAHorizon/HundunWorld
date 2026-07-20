# 混沌世界 MMORPG「燕云十六声」风格 UI 系统实施计划

## 一、Summary 概要

基于已存在的 `game-ui-system/` 项目（19 个完整 HTML 页面 + 5 张图片资源 + 完整水墨古风 Dark 主题 Token 体系 + 组件库），扩展为一套「春意盎然青色 + 鎏金强调 + 灰阶调和」的沉浸式 MMORPG UI 高保真原型，配套 4 大子系统后端接口契约文档与 UI 设计规范，并统一注入粒子动效系统。

**交付物**：HTML/CSS 高保真原型（在 .design 画布上）+ 4 份接口契约文档 + 1 份 UI 设计规范 + 1 份苹果 HIG 参考约束 + 3 张补充视觉资源。不产出任何 .NET 代码，接口仅文档化。

**核心改动**：青色 Token 调亮 → 粒子动效系统注入 → 5 个页面审查增强 → 视觉资源补充与 .design 注册 → 6 份文档新建 → 画布全量校验。

---

## 二、Current State Analysis 现状分析（已实际验证）

### 2.1 已存在资源（经 LS/Glob/Read 实际验证）

| 类别 | 路径 | 状态 |
|---|---|---|
| .design 项目 | `game-ui-system/game-ui-system.design` | 含 19 个 page 节点 + 5 个 image 节点，`config.designLibrary` 绑定 TRAE Work |
| HTML 页面 | `game-ui-system/pages/*.html`（19 个） | 全部存在：combat-hud, character-panel, skill-panel, inventory, quest-log, world-map, social-guild, shop, compass, combat-hud-traditional, equipment-enhance, crafting, mount-pet, friends, mail, leaderboard, mentor, achievement, dungeon-entry |
| 图片资源 | `assets/textures/ink-wash-bg-dark.jpg`、`ink-wash-map.jpg`、`assets/ui-elements/compass-ring.jpg`、`assets/borders/gold-filigree-corner.jpg`、`ink-divider.jpg` | 5 张全部存在 |
| 主样式 | `colors_and_type.css` | 含完整水墨古风 Dark Theme Override（第705-920行），定义 `--ink-jade-primary: #5E8B7E` 等 token |
| 组件库 | `components.css` | 含 `ds-btn/ds-card/ds-input/ds-table/ds-tabs/ds-tag/ds-progress/ds-dialog/ds-menu/ds-pagination` 等完整组件 |
| 脚手架 | `scaffold.css`、`css.json` | 布局工具类 + 图标映射 |
| 图标库 | `assets/icons/`（数百个 SVG） | 含 game-ui-system 与 hundun-yy-ui 两套图标 |

### 2.2 缺失资源（经 Glob 验证不存在）

| 类别 | 说明 |
|---|---|
| `ui-architecture-spec.md` | **不存在**，需新建（含完整架构规范 + 19 页规格） |
| `orchestration-summary.json` | **不存在**，需新建（含 designReferenceLibraries） |
| `docs/` 目录 | **不存在**，需创建并放置接口契约文档 |
| `assets/css/`、`assets/js/` 目录 | **不存在**，需创建并放置粒子动效文件 |
| 粒子动效系统 | 全部 19 个 HTML 页面均无粒子动效（combat-hud 已有 17 处基础 transition/animation，但非粒子系统） |
| 接口契约文档 | 完全缺失 |
| UI 设计规范文档 | 完全缺失 |

### 2.3 苹果设计库参考（read-only 约束源）

路径 `c:\Users\33011\.trae-cn\design_libraries\dl_builtin_apple`，含 button/card/comparison-table/input/navigation/product-grid 组件定义，Apple HIG 风格（System Blue #007AFF、1.2rem 圆角、DM Sans、44px 触控目标）。作为组件规范的**补充参考约束**，不覆盖水墨 Dark 主题。

### 2.4 关键约束

- 技术栈（仅用于接口契约文档描述）：.NET 10 C# + Orleans + SqlServer + Redis + EFCore + TouchSocket + MemoryPack
- 所有输出使用简体中文
- 基于现有项目扩展，不新建项目；通过 solo-design skill 的 `edit-project.md` 工作流落地
- Sub-Agent 不写 .design，由 Main Agent 统一注册节点
- 禁止创建临时脚本；独立子任务并行派发

---

## 三、Proposed Changes 提议变更

### 阶段 0：工作流接入（前置）

**动作**：Main Agent 调用 solo-design skill，声明使用 `edit-project.md` 工作流编辑现有项目 `c:\Works\GitHubProjects\HundunWorld\game-ui-system\`。确认 `fill-html-head.mjs`（路径 `{SKILL_DIR}/script/fill-html-head.mjs`）与 `scan-design-directory.mjs`（路径 `{SKILL_DIR}/script/scan-design-directory.mjs`）可用。

**验证**：skill 加载成功，工作流文档可读，脚本路径确认。

---

### 阶段 1：青色 Token 调亮（基础层，阻塞后续）

**目标**：将偏深的 `#5E8B7E` 调整为「春意盎然」的明亮春青色，为后续页面增强与新文档提供色彩基础。

**决策 D1：青色 Token 新色值梯度**（保持 3 级层次，整体向「春意」偏移）：
```
--ink-jade-primary: #5E8B7E → #7EAB9E  （春青，原 bright 提升为主色）
--ink-jade-bright:  #7EAB9E → #A8D4C4  （嫩绿青，更明亮的春芽色）
--ink-jade-deep:    #3E6B5E → #5E8B7E  （原主色降为深色，保持层次）
--ink-jade-glow:    rgba(94,139,126,0.4) → rgba(126,171,158,0.45)
--ink-jade-dim:     rgba(94,139,126,0.5) → rgba(126,171,158,0.55)
--ink-jade-faint:   rgba(94,139,126,0.12) → rgba(126,171,158,0.15)
--ink-text-jade:    #7EAB9E → #A8D4C4  （跟随 bright）
```

**修改文件清单**：
1. `game-ui-system/colors_and_type.css`
   - 第860-865行：按 D1 新值替换 6 个 jade token
   - 第879行：`--ink-text-jade` 跟随 bright 调整为 `#A8D4C4`
   - 第811行附近：若 `--status-success-default` 系列引用了 jade 基色则同步调亮
2. `game-ui-system/pages/*.html`（19 个）
   - 每个文件内嵌的 `<style id="theme-vars">` 块中，定位 6 个 jade token 定义，按 D1 新值替换
   - 同步更新 `--ink-text-jade`

**并行策略**：19 个 HTML 内嵌样式块修改无依赖，由 Sub-Agent 并行处理（每批 4-5 个文件），不得修改 .design。

**验证**：
- `Grep --pattern="--ink-jade-primary:\s*#5E8B7E"` 应返回 0 结果
- `Grep --pattern="--ink-jade-primary:\s*#7EAB9E"` 应返回 20 结果（1 CSS + 19 HTML）
- 画布预览 combat-hud.html，确认青色元素视觉为春青色而非深青色

---

### 阶段 2：粒子动效系统（依赖阶段 1）

**目标**：建立可复用的粒子动效基础设施，注入全部 19 个页面。

**决策 D2：粒子动效采用「共享 CSS+JS 注入」方案**，4 种粒子类型：
- **金粉飘落**（`data-particle="gold-burst"`）：按钮点击触发，从点击坐标向外扩散 12-16 颗金粉，时长 800ms
- **墨韵涟漪**（`data-particle="ink-ripple"`）：面板切换触发，从面板中心扩散 2 圈水墨环，时长 1200ms
- **青玉萤光**（`data-particle="jade-firefly"`）：信息提示出现时从边缘飘出 6-8 颗青玉光点，时长 1000ms
- **环境水墨微粒**（`data-particle="ambient"`）：页面加载后持续，背景缓慢飘动 20 颗微粒

**新建文件**：
1. `game-ui-system/assets/css/ink-particles.css`
   - 定义 4 种 keyframes：`@keyframes gold-burst`、`@keyframes ink-ripple`、`@keyframes jade-firefly`、`@keyframes ambient-drift`
   - 粒子基础类：`.ink-particle`、`.ink-particle--gold`、`.ink-particle--jade`、`.ink-ripple-ring`、`.ink-ambient`
   - 粒子容器：`.ink-particle-layer`（`pointer-events:none; position:fixed; z-index:9999`）
   - 颜色引用 D1 新值：`var(--ink-jade-bright)`、`var(--ink-gold-bright)`
2. `game-ui-system/assets/js/ink-particles.js`
   - IIFE 封装，暴露 `window.InkParticles` 对象
   - 方法：`burst(x, y, type)`、`ripple(el)`、`firefly(el)`、`startAmbient()`、`stopAmbient()`
   - 事件委托：`document` 的 `click` 事件匹配 `[data-particle]` 或 `.ds-btn` 触发 burst
   - 自定义事件监听：`panel:show` → ripple，`toast:show` → firefly
   - 页面 `DOMContentLoaded` 后自动 `startAmbient()`
   - 动效曲线统一：`cubic-bezier(0.16, 1, 0.3, 1)`（ease-out，参考苹果 HIG）

**修改文件**：19 个 HTML，在每个文件的 `</style>`（theme-vars 块结束）后、`</head>` 前追加：
```html
<link rel="stylesheet" href="../assets/css/ink-particles.css">
<script src="../assets/js/ink-particles.js" defer></script>
```

**并行策略**：19 个 HTML 注入无依赖，Sub-Agent 并行处理。

**验证**：
- 浏览器打开任一页面，点击按钮观察金粉粒子
- 控制台执行 `InkParticles.burst(500, 500, 'gold')` 应可见粒子
- 画布预览 5 个核心页面确认粒子不遮挡核心信息

---

### 阶段 3：5 个重点页面审查-增强（依赖阶段 1、2）

**目标**：对 5 个核心子系统入口页面进行质量审查与增强（这些页面已存在，不重写）。

**审查清单**：

| 页面 | 审查重点 | 增强动作（若需） |
|---|---|---|
| `character-panel.html` | 「侠」字占位角色剪影；五维属性条动画；装备槽 `data-dom-id` | 替换「侠」字为更精细的 SVG 人物轮廓；属性条加 `transition: height 0.6s ease-out` |
| `social-guild.html` | 门派列表、成员表、门派事件流 | 校验 `ds-table` 用法；成员在线状态用青玉萤光点缀 |
| `equipment-enhance.html` | 强化材料槽、成功率进度条、强化预览 | 成功率条用 `--ink-jade-bright` 渐变；强化按钮加 `data-particle="gold-burst"` |
| `combat-hud-traditional.html` | 与 combat-hud 的 `toggle-immersive` 交互；传统技能栏布局 | 校验 `toggle-immersive` 的 `data-dom-id` 存在；技能栏快捷键提示 |
| `dungeon-entry.html` | 秘境分类、难度标签、奖励预览、进入按钮 | 作为基准模板不大幅改动；进入按钮加 `data-particle="gold-burst"` |

**验证**：
- `.design` 中 5 个页面节点 interactions 注册的 `data-dom-id` 在对应 HTML 中均可 Grep 命中
- 画布从 combat-hud 跳转至这 5 个页面无白屏

---

### 阶段 4：视觉资源补充 + .design 节点注册（依赖阶段 2）

**目标**：生成 3 张新资源并注册为 .design image 节点。

**决策 D6：补充 3 张视觉资源**：
1. `assets/textures/ink-wash-bg-spring.jpg`：春意青色调水墨背景（1920×1080，深色底 + 青色水墨晕染 + 少量金粉），用于页面 body 背景层
2. `assets/ui-elements/particle-sprite-gold.png`：金粉粒子贴图（512×512，透明背景）
3. `assets/ui-elements/particle-sprite-jade.png`：青玉萤光粒子贴图（512×512，透明背景）

**动作**：
1. 通过 solo-design skill 的 GenerateImage 生成 3 张图片（在 skill 工作流内调用，不绕过 skill）
2. Main Agent 编辑 `game-ui-system.design`，在 `data` 数组追加 3 个 image 节点：
   - `image-006`：水墨春意背景纹理，`assets/textures/ink-wash-bg-spring.jpg`，canvasData `{x:3100, y:3393}`
   - `image-007`：金粉粒子贴图，`assets/ui-elements/particle-sprite-gold.png`，canvasData `{x:3720, y:3393}`
   - `image-008`：青玉萤光粒子贴图，`assets/ui-elements/particle-sprite-jade.png`，canvasData `{x:4340, y:3393}`
3. 可选：`ink-particles.js` 的环境微粒改用 `particle-sprite-gold.png` 贴图（通过 `new Image()` 预加载）

**验证**：
- 3 张图片文件存在于对应路径
- `.design` 中 image-006/007/008 节点 JSON 合法
- `scan-design-directory.mjs` 无缺失资源告警

---

### 阶段 5：文档交付（与阶段 2-4 可部分并行）

**目标**：交付 4 份接口契约 + 1 份 UI 设计规范 + 1 份苹果 HIG 参考 + 1 份架构规范 + 1 份编排摘要。

**决策 D4：接口契约文档统一结构**（每份固定 8 节）：
1. 子系统概述与领域边界
2. Orleans Grain 接口定义（`IGrainWithGuidKey`，含方法签名、返回类型、GrainKey 策略）
3. Grain 状态与持久化（Redis Hash 结构、TTL、回写策略）
4. TouchSocket 消息协议（消息类型枚举、请求/响应消息体、推送消息体）
5. MemoryPack 序列化 DTO（`[MemoryPackable]` C# record，含字段顺序、类型、注释）
6. EFCore 实体与 SqlServer 表结构（实体类、表名、索引、关系）
7. Redis 缓存键命名规范（`hundun:{subsystem}:{grainid}:{field}` 模式）
8. 前端调用时序图（文字描述：UI 事件 → TouchSocket → Grain → Redis/SqlServer → 响应）

**新建文件**：

1. `game-ui-system/docs/api-contracts/01-combat-hud-api.md`
   - 覆盖：战斗 HUD 实时数据（血量/内力/怒气/技能冷却/Buff/Debuff/目标信息/伤害飘字/小队状态）
   - Grain：`ICombatHudGrain`（GetHudState、SubscribeDamage、Unsubscribe）、`IPartyCombatGrain`
   - TouchSocket 消息：`CombatStatePush`、`DamageFloatPush`、`BuffUpdatePush`、`SkillCooldownPush`
   - MemoryPack DTO：`CombatHudStateDto`、`DamageFloatDto`、`BuffDto`、`SkillCooldownDto`
   - EFCore：`CombatSnapshot`（快照表）、`DamageLog`（日志表）
   - Redis：`hundun:combat:{playerId}:hud`（Hash, TTL 30s）、`hundun:combat:{playerId}:cooldowns`（Sorted Set）

2. `game-ui-system/docs/api-contracts/02-character-inventory-api.md`
   - 覆盖：角色五维属性、装备槽、背包格子、物品操作、装备强化、制造
   - Grain：`ICharacterGrain`、`IInventoryGrain`、`IEquipmentGrain`、`ICraftingGrain`
   - TouchSocket 消息：`InventoryUpdatePush`、`EquipmentChangePush`、`CraftingProgressPush`
   - MemoryPack DTO：`CharacterDto`、`InventorySlotDto`、`EquipmentDto`、`CraftingRecipeDto`
   - EFCore：`Character`、`InventoryItem`、`EquipmentInstance`、`CraftingRecipe`、`CraftingQueue`
   - Redis：`hundun:char:{playerId}`（Hash）、`hundun:inv:{playerId}`（Hash, TTL 5min）

3. `game-ui-system/docs/api-contracts/03-quest-skill-api.md`
   - 覆盖：任务日志（主线/支线/日常/周常）、任务进度、技能树、心法、奇术、技能装备
   - Grain：`IQuestGrain`、`ISkillGrain`、`ISkillTreeGrain`
   - TouchSocket 消息：`QuestProgressPush`、`QuestCompletePush`、`SkillLearnedPush`、`SkillEquippedPush`
   - MemoryPack DTO：`QuestDto`、`QuestObjectiveDto`、`SkillDto`、`SkillTreeNodeDto`
   - EFCore：`Quest`、`PlayerQuestProgress`、`Skill`、`PlayerSkill`、`SkillTree`
   - Redis：`hundun:quest:{playerId}:active`（Sorted Set）、`hundun:skill:{playerId}`（Hash）

4. `game-ui-system/docs/api-contracts/04-social-shop-api.md`
   - 覆盖：门派、好友、邮件、师徒、排行榜、成就、商城、副本入口
   - Grain：`IGuildGrain`、`IFriendGrain`、`IMailGrain`、`IMentorGrain`、`ILeaderboardGrain`、`IAchievementGrain`、`IShopGrain`、`IDungeonGrain`
   - TouchSocket 消息：`GuildMemberOnlinePush`、`MailReceivedPush`、`FriendStatusPush`、`ShopPurchasePush`、`DungeonMatchPush`
   - MemoryPack DTO：`GuildDto`、`FriendDto`、`MailDto`、`MentorDto`、`LeaderboardEntryDto`、`AchievementDto`、`ShopItemDto`、`DungeonDto`
   - EFCore：`Guild`、`GuildMember`、`Friend`、`Mail`、`Mentorship`、`LeaderboardScore`、`Achievement`、`PlayerAchievement`、`ShopItem`、`ShopOrder`、`DungeonRecord`
   - Redis：`hundun:guild:{guildId}`（Hash）、`hundun:leaderboard:{type}`（Sorted Set）、`hundun:mail:{playerId}:unread`（List）

5. `game-ui-system/ui-design-guidelines.md`（5 章）
   - 第1章 色彩使用指南：引用 D1 新值，给出「主青/亮青/深青/金/血」使用比例（建议 40/15/10/25/10），品质色阶（common/uncommon/rare/epic/legendary），五行色（金木水火土）
   - 第2章 字体与排版：STKaiti 标题、Noto Sans SC 正文、DIN Alternate 数字、JetBrains Mono 代码；字号阶梯 10/11/12/14/18/22/24/28/32px
   - 第3章 动效规范：4 种粒子的触发条件、时长（burst 800ms、ripple 1200ms、firefly 1000ms、ambient 持续）、曲线 `cubic-bezier(0.16,1,0.3,1)`、面板进出场（fade+slide 200ms）
   - 第4章 组件库规范：`ds-btn` 5 状态、`ds-card` 3 变体、`ds-input` 4 状态、`ds-table` 行高 36px、`ds-tabs` 下划线指示器、`ds-progress` 渐变填充、`ds-dialog` 模态遮罩 `blur(8px)`
   - 第5章 交互原则：沉浸式 HUD 信息层级（中央渲染区无遮挡、四角功能集群）、触控目标 ≥44px（引用苹果 HIG）、键盘 Tab 可达、危险操作二次确认

6. `game-ui-system/docs/apple-hig-reference.md`
   - 提取苹果设计库组件规范的可借鉴项
   - 8pt 间距网格、圆角阶梯 4/8/12px、触控目标 44×44px、动效曲线 ease-out、信息层级 3 级、对比度 WCAG AA
   - 明确标注：作为「补充约束」叠加到水墨主题，不替换 token 值

7. `game-ui-system/ui-architecture-spec.md`（新建，含完整架构规范 + 19 页规格）
   - 第1章 系统总览：4 大子系统划分、页面流转图、导航拓扑
   - 第2章 设计 Token 体系：背景层/品牌色/文本/图标/边框/状态/水墨自定义 token（引用 D1 新值）
   - 第3章 19 个页面规格：每页含布局结构、关键组件、数据绑定、Token 使用、交互锚点（`data-dom-id`）
   - 第4章 品质与五行系统：品质色阶、五行相生相克
   - 第5章 前端集成指引：HTML 结构约定、CSS 加载顺序、JS 事件协议（`panel:show`/`toast:show`）

8. `game-ui-system/orchestration-summary.json`（新建）
   - `project` 字段：项目元信息
   - `pages` 数组：19 个页面状态（初始全 `completed`）
   - `tokens` 字段：引用 colors_and_type.css 的关键 token
   - `componentPlan` 字段：引用 components.css 的组件清单
   - `imagePlan` 字段：8 张图片清单
   - `wiringPlan` 字段：可见交互图 + 隐藏交互计划
   - `designSource.libraryIdentity`：TRAE Work（主绑定）
   - `designReferenceLibraries` 数组：
     ```json
     [
       {
         "name": "苹果",
         "id": "dl_builtin_apple",
         "scope": "built-in-global",
         "path": "c:\\Users\\33011\\.trae-cn\\design_libraries\\dl_builtin_apple",
         "role": "supplementary-reference",
         "note": "Apple HIG 组件规范作为补充约束，不覆盖水墨主题"
       }
     ]
     ```

**并行策略**：4 份接口契约文档相互独立，由 4 个 Sub-Agent 并行撰写；`ui-design-guidelines.md` 与 `apple-hig-reference.md` 独立并行；`ui-architecture-spec.md` 与 `orchestration-summary.json` 需在阶段 1-3 完成后撰写（需引用新色值与新页面状态）。

**验证**：
- 每份接口契约文档含 8 节，Grep 可命中「Orleans」「TouchSocket」「MemoryPack」「EFCore」「Redis」5 个关键词
- `ui-design-guidelines.md` 含 5 章，Grep 可命中 D1 新色值 `#7EAB9E`、`#A8D4C4`
- `apple-hig-reference.md` 含触控目标 44px、ease-out 曲线
- `orchestration-summary.json` JSON 合法，`designReferenceLibraries` 字段存在

---

### 阶段 6：画布校验与收尾（依赖全部前置阶段）

**目标**：全量验证，确保画布无悬空、无白屏、无资源缺失。

**动作**：
1. 运行 solo-design skill 的 `scan-design-directory.mjs` 校验：
   - 19 个 page 节点的 `htmlSrc` 指向的文件均存在
   - 8 个 image 节点的 `imageSrc` 指向的文件均存在
   - interactions 的 `targetPageId` 均指向有效节点
2. 逐页抽检画布预览（至少 combat-hud、character-panel、dungeon-entry、shop、skill-panel）：
   - 青色为春意色
   - 按钮点击有金粉粒子
   - 跳转无白屏
3. 更新 `orchestration-summary.json` 的 `pages` 数组，确保 19 个页面状态均为 `completed`

**验证**：
- `scan-design-directory.mjs` 退出码 0，无告警
- 画布全览无白屏节点
- 全部文档就位

---

## 四、Assumptions & Decisions 假设与决策

### 关键决策

| ID | 决策 | 理由 |
|---|---|---|
| D1 | 青色 Token 调亮为 `#7EAB9E`（主）/`#A8D4C4`（亮）/`#5E8B7E`（深） | 原 `#5E8B7E` 偏深不符合「春意盎然」；新值保持 3 级层次不破坏对比度 |
| D2 | 粒子动效采用共享 CSS+JS 注入方案 | 避免重复实现；IIFE 封装不污染全局；事件委托不覆盖现有监听 |
| D3 | 5 个页面审查-增强而非重写 | 实际验证 5 个 HTML 均已完整存在（character-panel 1853行、social-guild 1703行、equipment-enhance 1750行、combat-hud-traditional 1810行、dungeon-entry 2552行） |
| D4 | 接口契约文档统一 8 节结构 | 覆盖 .NET 10 + Orleans + TouchSocket + MemoryPack + EFCore + Redis 全技术栈，标准化便于查阅 |
| D5 | UI 设计规范分 5 章 | 色彩/字体/动效/组件/交互覆盖设计落地所需全部维度 |
| D6 | 补充 3 张视觉资源 | 现有 5 张不足支持粒子系统；春意背景 + 2 张粒子贴图补齐 |
| D7 | 苹果设计库不替换 TRAE Work 绑定 | 苹果库为 Light/HIG 风格，直接替换会导致 19 个页面水墨 Dark token 失效；改为文档化引用 + 补充约束 |
| D8 | 新建 `ui-architecture-spec.md` 与 `orchestration-summary.json`（而非扩充） | 经 Glob 验证两文件均不存在 |

### 假设

- 现有 19 个 HTML 页面质量基本达标，仅 5 个核心页面需增强（已通过 LS 验证文件存在）
- 现有 `colors_and_type.css` 的水墨古风 Dark Theme Override 是权威 Token 来源
- `components.css` 的组件库满足 19 个页面需求，无需新增组件
- 苹果设计库的组件定义可读取（已验证路径存在）
- 用户接受「不替换 .design 绑定，改为文档化引用苹果 HIG」的折中方案

---

## 五、Verification Steps 验证步骤

### 阶段 1 验证
- `Grep --pattern="--ink-jade-primary:\s*#5E8B7E" --path=game-ui-system` 返回 0 结果
- `Grep --pattern="--ink-jade-primary:\s*#7EAB9E" --path=game-ui-system` 返回 20 结果
- 画布预览 combat-hud.html，青色元素为春青色

### 阶段 2 验证
- 浏览器打开任一页面，点击 `.ds-btn` 观察金粉粒子
- 控制台执行 `InkParticles.burst(500, 500, 'gold')` 可见粒子
- 19 个 HTML 均含 `<link rel="stylesheet" href="../assets/css/ink-particles.css">`

### 阶段 3 验证
- 5 个页面的 `data-dom-id` 在 HTML 中均可 Grep 命中
- 画布从 combat-hud 跳转至 5 个页面无白屏

### 阶段 4 验证
- 3 张图片文件存在于对应路径
- `.design` 中 image-006/007/008 节点 JSON 合法
- `scan-design-directory.mjs` 无缺失资源告警

### 阶段 5 验证
- 4 份接口契约文档各含 8 节，Grep 命中 5 个技术栈关键词
- `ui-design-guidelines.md` 含 5 章，Grep 命中 `#7EAB9E`、`#A8D4C4`
- `apple-hig-reference.md` 含 44px、ease-out
- `orchestration-summary.json` JSON 合法，含 `designReferenceLibraries`

### 阶段 6 验证（最终）
- `node {SKILL_DIR}/script/scan-design-directory.mjs c:\Works\GitHubProjects\HundunWorld\game-ui-system --expected-pages=19 --report-json=c:\Works\GitHubProjects\HundunWorld\game-ui-system\validation-report.json` 退出码 0
- `validation-report.json` 记录 `success: true`
- 画布全览 19 个页面 + 8 个图片节点无白屏、无悬空

---

## 六、文件清单总览

### 新建文件（13 个）

| 路径 | 类型 | 阶段 |
|---|---|---|
| `game-ui-system/assets/css/ink-particles.css` | 样式 | 2 |
| `game-ui-system/assets/js/ink-particles.js` | 脚本 | 2 |
| `game-ui-system/assets/textures/ink-wash-bg-spring.jpg` | 图片 | 4 |
| `game-ui-system/assets/ui-elements/particle-sprite-gold.png` | 图片 | 4 |
| `game-ui-system/assets/ui-elements/particle-sprite-jade.png` | 图片 | 4 |
| `game-ui-system/ui-design-guidelines.md` | 文档 | 5 |
| `game-ui-system/ui-architecture-spec.md` | 文档 | 5 |
| `game-ui-system/orchestration-summary.json` | 文档 | 5 |
| `game-ui-system/docs/apple-hig-reference.md` | 文档 | 5 |
| `game-ui-system/docs/api-contracts/01-combat-hud-api.md` | 文档 | 5 |
| `game-ui-system/docs/api-contracts/02-character-inventory-api.md` | 文档 | 5 |
| `game-ui-system/docs/api-contracts/03-quest-skill-api.md` | 文档 | 5 |
| `game-ui-system/docs/api-contracts/04-social-shop-api.md` | 文档 | 5 |

### 修改文件（21 个）

| 路径 | 修改内容 | 阶段 |
|---|---|---|
| `game-ui-system/colors_and_type.css` | 青色 Token 调亮（D1） | 1 |
| `game-ui-system/pages/*.html`（19 个） | 内嵌 theme-vars 青色调亮 + 注入粒子 CSS/JS | 1+2 |
| `game-ui-system/game-ui-system.design` | 追加 3 个 image 节点（image-006/007/008） | 4 |

---

## 七、执行顺序依赖图

```
阶段0 (skill 接入)
  └─ 阶段1 (青色调亮) ──────────────┐
       ├─ 阶段2 (粒子系统)           │
       │    └─ 阶段3 (5 页审查增强)    │
       │         └─ 阶段4 (资源+.design)│
       │              └─ 阶段6 (校验)  │
       └─ 阶段5 (文档，可与 2-4 并行) ──┘
```

阶段 1 是阻塞前置（色值基础）；阶段 2 依赖阶段 1（粒子用新色）；阶段 3 依赖阶段 1+2；阶段 4 依赖阶段 2（粒子贴图）；阶段 5 可与阶段 2-4 并行（文档不依赖代码）；阶段 6 依赖全部。

---

## 八、风险与应对

| 风险 | 概率 | 应对 |
|---|---|---|
| 19 个 HTML 内嵌样式块定位不一致（部分文件可能无 `--ink-jade` 定义） | 中 | 阶段 1 前先 Grep 确认每个文件含 `--ink-jade-primary` 定义，缺失则跳过该文件并记录 |
| 粒子 JS 注入后与现有页面 JS 冲突 | 低 | `ink-particles.js` 用 IIFE 封装，仅挂载 `window.InkParticles`，不污染全局；事件用委托不覆盖现有监听 |
| 苹果设计库 Light 风格「污染」水墨 Dark 主题 | 中 | D7 决策已规避：不替换绑定，仅文档化引用；`ui-design-guidelines.md` 明确「苹果 HIG 仅约束交互与尺寸，不约束色彩」 |
| 接口契约文档过度详细导致篇幅失控 | 中 | 每份文档控制在 800-1200 行；DTO 仅列字段名与类型，不写完整 C# 实现；时序图用文字步骤而非图形 |
| `scan-design-directory.mjs` 报 interactions 悬空 | 低 | 阶段 3 已校验 `data-dom-id`；若仍报错，补齐 HTML 中缺失的 `data-dom-id` 属性 |
| GenerateImage 生成的粒子贴图背景不透明 | 低 | 生成提示词明确「transparent background, PNG, alpha channel」 |

---

## 九、关键参考文件位置（执行时引用）

- 现有主样式：`game-ui-system/colors_and_type.css`（第860-865行青玉组）
- 现有组件库：`game-ui-system/components.css`
- 画布定义：`game-ui-system/game-ui-system.design`
- 基准页面模板：`game-ui-system/pages/dungeon-entry.html`（2552 行，最完整）
- 苹果设计库：`c:\Users\33011\.trae-cn\design_libraries\dl_builtin_apple`（metadata.json、components/*.json、SKILL.md）
- Solo Design Skill 脚本：`{SKILL_DIR}/script/fill-html-head.mjs`、`{SKILL_DIR}/script/scan-design-directory.mjs`
