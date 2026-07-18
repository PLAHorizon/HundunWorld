# 混沌世界 HundunWorld · 水墨武侠沉浸式 UI 系统 — 实施计划

## 摘要

为混沌世界 MMORPG 设计一套仿《燕云十六声》的**纯水墨武侠沉浸式**全屏 UI 系统。采用 solo-design 的 create-project 工作流，新建一个 Free Explore 模式的原生水墨项目（不依赖任何设计系统覆盖层），Phase 1 交付 9 个覆盖全部四大模块的核心界面，Phase 2 文档化延期页面。视觉方向：深墨黑沉浸背景 + 鎏金/古铜双主色 + 毛笔书法字体（Ma Shan Zheng）+ 水墨笔触描边面板 + 极简隐藏式 HUD。

---

## 一、现状分析

### 1.1 工作区已有设计项目（已核实）

工作区存在 4 个 `.design` 项目：

| 项目 | 路径 | 说明 |
|------|------|------|
| `game-ui-system` | `c:\Works\GitHubProjects\HundunWorld\game-ui-system\` | 19 页面，水墨古风，**library-bound 绑定 TRAE Work + 覆盖层实现** |
| `hundun-login` | `c:\Works\GitHubProjects\HundunWorld\hundun-login\` | 登录界面设计 |
| `gengdi-login-redesign` | `c:\Works\GitHubProjects\HundunWorld\gengdi-login-redesign\` | 登录重设计 |
| `character-creation` | `c:\Works\GitHubProjects\HundunWorld\character-creation\` | 角色创建，teal/藏青配色 |

`game-ui-system` 已实现用户想要的几乎全部内容（19 页面覆盖战斗/角色/社交/经济/世界/进度各模块），且 `orchestration-summary.json` 的 `visualSpecExcerpt` 即为"仿燕云十六声水墨古风UI"。但其本质是 **TRAE Work 结构保留 + 水墨视觉覆盖**：

- `operatingMode: "library-bound"`，绑定 `dl_builtin_trae_work`
- 标题字体用 `STKaiti`（系统楷书），非真正毛笔书法字体
- 靠叠加 CSS 覆盖层改色（TRAE Work Light tokens + 暗色水墨 override），非原生水墨 token
- 受 TRAE Work 组件契约约束，无法自由偏离

### 1.2 "纯度差距"判断

用户明确选择"**纯**水墨武侠沉浸"，"纯"字指向原生实现而非覆盖层。现有项目三处不够纯：

1. **模式不纯**：library-bound 受 TRAE Work 约束，非 Free Explore
2. **字体不纯**：STKaiti 系统楷书 ≠ Ma Shan Zheng 毛笔书法
3. **token 不纯**：覆盖层叠加而非原生深墨黑+金铜 token 体系，TRAE Light 残留可能渗透

### 1.3 本次会话的引用设计系统

`<trae_command>` 引用豆包设计系统（`dl_builtin_doubao`，冷蓝 AI 仪表盘风）。豆包与水墨武侠美学根本冲突，用户已通过澄清明确选择"纯水墨武侠沉浸"作为视觉方向——这是对豆包（及任何 dashboard 类设计系统）视觉约束的显式覆盖。按 solo-design 优先级规则，进入 **Free Explore 模式**，生成原生水墨武侠品牌 CSS。

---

## 二、关键决策

### 决策 A — 执行路径：新建独立 Free Explore 项目

新建 `hundun-ink-wash-ui` 项目，而非 fork 现有项目或在现有项目上 customize-theme。理由：

- Free Explore 与 library-bound 互斥，无法在现有 library-bound 项目内"切换模式"
- 用户要"纯"水墨，需原生 token 而非覆盖层
- 保留现有 `game-ui-system` 作对照参考，不破坏已有成果（含 671+ 图标可复用）

> 复用策略：`game-ui-system/assets/icons/` 的 SVG 图标集可零成本复制到新项目 `assets/icons/`，作为水墨 UI 的功能图标基础（Free Explore 模式无 library 图标源，工作区项目级图标可复用）。

### 决策 B — 页数上限：弹性上限 9 页（Elastic Cap）

依 solo-design `long-requirement-parsing.md` §Elastic Cap Rules，4 项条件全满足：

| 条件 | 满足情况 |
|------|----------|
| 全 P0 | 9 页均为 MMORPG 核心系统界面，无 nice-to-have |
| 顺序依赖 | 以 combat-hud 为中枢，各面板构成完整游戏体验闭环 |
| 用户明确要完整 | 原文"设计一整套完整UI系统" |
| 单设备 | desktop 全屏沉浸式 |

→ 触发弹性上限，Phase 1 生成 9 页。其余页面文档化为 Phase 2（走 edit-project 添加页路径追加至同一项目）。

### 决策 C — 设备与模式

- `deviceType: "desktop"`（全屏沉浸式游戏 UI）
- `dashboardMode: false`（非数据大屏）
- `themeMode: "dark"`（深墨黑沉浸）
- `operatingMode: "free-explore"`

### 决策 D — 拓扑偏离声明

solo-design 默认 wiring 为线性单链。但游戏 HUD 底部导航栏是常驻全局导航（非业务"下一步"）。将 combat-hud 的 8 个系统入口注册为 `hideEdge: true` 隐藏交互，仅保留 1 条可见边 `combat-hud → character-panel` 作主流程示意。可见边总数 = 1 ≤ 9，满足约束。

---

## 三、Phase 1 核心页面清单（9 页）

页面规划覆盖用户选择的全部四大模块，融合燕云十六声研究结论（HUD 极简无跑马灯、任务栏隐藏、武学心法奇术三系、调律定音叠音、水墨山水大地图、真实身体机制）。

| # | 页面名 | slug | 模块 | 信息密度 | 功能要点 | visualNorthStar |
|---|--------|------|------|----------|----------|-----------------|
| 1 | 核心战斗HUD | `combat-hud` | 核心战斗 | task-driven | 极简沉浸式HUD；四角分布（左上血气条、右下8格技能槽、右上任务追踪、左下小地图）；隐藏任务栏与跑马灯；水墨渐变血条；技能冷却水墨晕染；底部8入口系统导航 | 全屏深墨黑，四角金线描边半透明面板，中央留空保留3D渲染区，底部技能槽水墨晕染冷却 |
| 2 | 角色属性装备 | `character-panel` | 核心角色 | information-dense | 左立绘+中属性+右装备三栏；五维属性（力/敏/体/气/神）竖向进度条；人形装备槽位图；调律/定音/叠音页签；造诣综合分 | 左栏水墨人物立绘剪影，中栏竖向五行属性条，右栏人形装备槽+调律定音叠音金线标签 |
| 3 | 武学心法奇术 | `skill-panel` | 核心角色 | information-dense | 武学/心法/奇术三标签切换；左列表右详情双栏；技能图标方格带品质色描边；天赋树节点连线；心法搭配槽 | 顶部三标签金线下划线，左侧技能列表品质边框，右侧招式描述+天赋树连线图 |
| 4 | 背包行囊 | `inventory` | 核心角色 | task-driven | 4类背包切换（道具/装备/材料/任务）；8列网格格子带品质色边框+数量角标；右栏物品详情；底部货币栏 | 左侧8列品质色边框格子网格，右侧物品详情卡，底部鎏金货币栏 |
| 5 | 任务日志 | `quest-log` | 世界进度 | information-dense | 5类任务（主线/支线/奇遇/日常/门派）；左列表右详情双栏；折叠分组；目标进度+奖励预览；水墨纸卷排版 | 左侧折叠分组任务列表，右侧水墨纸卷式描述+目标进度+奖励 |
| 6 | 世界地图 | `world-map` | 世界进度 | showcase | 水墨山水画大地图；区域标记（清河/开封等）；左侧竖排筛选；右上区域信息；下方搜索；金菱形界碑 | 中央水墨山水画地图，金菱形标记，左筛选+右上信息+下搜索 |
| 7 | 社交门派 | `social-guild` | 社交经济 | information-dense | 好友/门派/帮会三标签；左列表右详情双栏；好友在线/离线分组；门派信息；聊天多频道切换 | 左侧好友/门派列表带在线状态圆点，右侧详情/聊天多频道标签 |
| 8 | 商城商店 | `shop` | 社交经济 | task-driven | 商品4列网格+购物车双栏；品质色边框商品卡；货币余额；购买确认 | 左侧4列品质色边框商品网格，右侧购物车结算+货币余额 |
| 9 | NPC对话交互 | `npc-dialog` | 登录系统 | task-driven | 全屏沉浸式对话；底部水墨卷轴对话框+选项分支；左侧NPC立绘+好感度；右上任务关联；多选项分支树 | 中央NPC水墨立绘，底部水墨卷轴对话框+分支选项，左上NPC名+好感度 |

---

## 四、Phase 2 延期页面清单（13 页）

Phase 1 审阅通过后，走 edit-project 添加页路径追加至同一项目：

| 页面名 | slug | 说明 |
|--------|------|------|
| 罗盘司南 | `compass` | 古代司南/风水罗盘美学（天干地支+八卦） |
| 设置面板 | `settings` | 画质/音效/操作/社交设置 |
| 制造系统 | `crafting` | 手工制造/配方/材料消耗 |
| 成就图鉴 | `achievement` | 成就/称号收集 |
| 邮件系统 | `mail` | 游戏内邮件 |
| 聊天频道 | `chat` | 多频道聊天（综合/门派/私聊/世界） |
| 好友详情 | `friends` | 好友列表与详情 |
| 装备强化 | `equipment-enhance` | 叠音/调律/定音深度强化 |
| 副本入口 | `dungeon-entry` | 副本选择与进入 |
| 排行榜 | `leaderboard` | 武林造诣排行 |
| 坐骑宠物 | `mount-pet` | 坐骑与宠物管理 |
| 师徒系统 | `mentor` | 师徒拜师/传功 |
| 传统战斗HUD | `combat-hud-traditional` | combat-hud 对比态 |

> 登录界面、角色选择已有独立项目（`hundun-login`、`character-creation`），如需纯水墨版可在 Phase 2 一并迁移。

---

## 五、水墨武侠品牌 CSS Token 方案

**目标文件**：`c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\colors_and_type.css`

设计原则：Free Explore 直接生成原生深墨黑+金铜水墨 token，不内联任何 dashboard 设计系统 tokens；保留 CSS 变量命名规范一致性。同时定义 `--ink-*` 游戏语义 token 体系。

### 5.1 色彩 Token

**背景层（深墨黑）**
- `--bg-base-default: #0E1016`（主背景/墨黑）
- `--bg-base-secondary: #14171E`（面板背景）
- `--bg-base-tertiary: #1C1F28`（次级面板/纸面）
- `--ink-bg-panel: rgba(20,23,30,0.85)`（水墨半透明面板/毛玻璃）
- `--ink-bg-abyss: #0A0B10`（最深墨渊）

**品牌主色（鎏金）**
- `--bg-brand: #C8A858`（鎏金主色）
- `--bg-brand-hover: #E0C880`（亮金）
- `--bg-brand-active: #8A7438`（深金）
- `--ink-gold-primary: #C8A858` / `--ink-gold-bright: #E0C880` / `--ink-gold-deep: #8A7438`

**铜色体系（辅助主色）**
- `--ink-bronze-primary: #B87333`（古铜）
- `--ink-bronze-bright: #D4944E`（亮铜）
- `--ink-bronze-deep: #7A4A20`（沉铜）
- `--ink-bronze-glow: rgba(184,115,51,0.4)`（铜光晕）

**辅助语义色**
- `--ink-jade-primary: #5E8B7E`（青玉/治疗）
- `--ink-blood-primary: #B85450`（朱砂/战斗危险）

**品质色（游戏语义，归 stateColors）**
- `--ink-quality-common: #8A8275`（凡）
- `--ink-quality-uncommon: #6B8E5A`（良）
- `--ink-quality-rare: #4A7EA8`（珍）
- `--ink-quality-epic: #8B5E9E`（史）
- `--ink-quality-legendary: #C8A858`（传/金）

**文字/边框**
- `--text-default: #F0EDE4`（主文字/宣纸白）
- `--text-secondary: #B8B0A0`（次要文字）
- `--border-neutral-l2: rgba(200,168,88,0.15)`（金线边框）
- `--ink-border-gold: rgba(200,168,88,0.35)`（金线描边）
- `--ink-border-bronze: rgba(184,115,51,0.3)`（铜色边框）

### 5.2 字体 Token

- `--ink-font-display: "Ma Shan Zheng","STKaiti","KaiTi",serif`（毛笔书法大标题/品牌字，Google Fonts）
- `--ink-font-heading: "Noto Serif SC","Source Han Serif SC","SimSun",serif`（宋体衬线副标题/面板标题）
- `--ink-font-body: "Noto Sans SC","PingFang SC","Microsoft YaHei",sans-serif`（UI 正文黑体）
- `--ink-font-number: "DIN Alternate","DIN","Bebas Neue",monospace`（数值等宽）

CSS 头部 `@import` 加载 Google Fonts。Ma Shan Zheng（毛笔装饰标题）与 Noto Serif SC（宋体副标题/正文）角色不重叠，符合 Free Explore "一个 CJK serif 标题族 + 一个 body 栈"约束。

### 5.3 圆角 Token（古风克制）

- `--ink-radius-none: 0px`
- `--ink-radius-sm: 2px`
- `--ink-radius-md: 4px`
- `--ink-radius-lg: 8px`
- `--radius-full: 999px`（仅血条/圆点）

`radiusMax: 8`，不生成 12px 以上大圆角，符合古风硬朗气质与 Free Explore radius policy。

### 5.4 阴影/深度 Token

- `--ink-shadow-panel: 0 8px 32px rgba(0,0,0,0.6),0 2px 8px rgba(0,0,0,0.4)`（浮动面板/弹窗/抽屉，允许深 elevation）
- `--ink-shadow-soft: 0 1px 0 rgba(0,0,0,0.05)`（静态面板微阴影，alpha ≤ 0.05）
- `--ink-shadow-gold: 0 0 24px rgba(200,168,88,0.2)`（金光晕，仅关键 CTA/传世品质，用户明确要求的氛围特征）
- `--ink-shadow-inset: inset 0 1px 0 rgba(200,168,88,0.08)`（面板顶部金线高光）
- `--ink-blur-panel: blur(8px)`（水墨半透明面板毛玻璃）

阴影策略：静态面板以金线描边 + surface 分层为主，shadow alpha ≤ 0.05；浮动层允许深 elevation；金光晕为用户明确要求的"金铜点缀"氛围特征，仅限关键强调元素。

### 5.5 水墨笔触边框工具类

- `.ink-brush-border`：`border: 1px solid var(--ink-border-gold)` + 多层 `box-shadow` 错位金线模拟毛笔飞白
- `.ink-panel`：`background: var(--ink-bg-panel); backdrop-filter: var(--ink-blur-panel); border: 1px solid var(--ink-border-gold); box-shadow: var(--ink-shadow-inset)`

---

## 六、图片资产生成清单

上限：`min(pageCount+1, 6) = min(10, 6) = 6` 张。Step 2.5 同一轮并行派发生成至 `c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\assets\`。

| # | 文件名 | 角色 | 归属页 | 用途 |
|---|--------|------|--------|------|
| 1 | `ink-wash-bg-dark.jpg` | shared-brand | 全页复用 | 浓墨晕染深色全局背景纹理 |
| 2 | `ink-wash-map.jpg` | supporting-content | world-map | 水墨山水画风格世界地图底图 |
| 3 | `character-silhouette.png` | supporting-content | character-panel | 水墨人物立绘剪影 |
| 4 | `ink-mountain-mist.jpg` | shared-brand | combat-hud/skill-panel | 水墨远山雾霭氛围背景 |
| 5 | `ancient-pavilion.jpg` | supporting-content | shop/social-guild | 古建亭台水墨剪影 |
| 6 | `npc-portrait.png` | supporting-content | npc-dialog | NPC 水墨半身立绘 |

每张生成后由 Main Agent 在 Step 2.5d 注册为 `type: "image"` 节点写入 `.design`（1 图 1 节点）。

---

## 七、项目结构与文件清单

新项目根目录：`c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\`

```
hundun-ink-wash-ui/
├── hundun-ink-wash-ui.design          # .design 入口（9 page 骨架节点 + 6 image 节点）
├── colors_and_type.css                # 水墨武侠品牌 CSS（原生 token）
├── orchestration-summary.json         # 编排摘要（free-explore 模式）
├── pages/
│   ├── combat-hud.html
│   ├── character-panel.html
│   ├── skill-panel.html
│   ├── inventory.html
│   ├── quest-log.html
│   ├── world-map.html
│   ├── social-guild.html
│   ├── shop.html
│   └── npc-dialog.html
└── assets/
    ├── ink-wash-bg-dark.jpg
    ├── ink-wash-map.jpg
    ├── character-silhouette.png
    ├── ink-mountain-mist.jpg
    ├── ancient-pavilion.jpg
    ├── npc-portrait.png
    └── icons/                        # 从 game-ui-system 复用的 SVG 图标集
```

---

## 八、页面生成依赖顺序与并行策略

### 8.1 依赖分析

9 页均为独立全屏系统面板，无 `stateGroupId`（非交互态组），无 base/derived 生成树依赖。各页面独立设计全屏界面，不需"复制某页 HTML 再改可变区"。因此 9 页真正独立，满足 Parallel Dispatch Principle。

### 8.2 执行步骤

| 步骤 | 动作 | 执行者 | 并行性 |
|------|------|--------|--------|
| Step 1 | 生成水墨武侠品牌 CSS（上述 token 方案） | Main Agent 直接 | - |
| Step 2 | 创建目录骨架 + 写 `.design`（9 page 骨架节点预分配 nodeId/htmlSrc/pageIndex，interactions=[]）+ 复制图标集 | Main Agent 直接 | - |
| Step 2.2 | 写 `orchestration-summary.json`（每页 visualNorthStar/compositionPattern/continuityAnchors/componentPlan/imagePlan） | Main Agent 直接 | - |
| Step 2.5 | 6 张图片全部并行派发 | 同一轮 6 子代理并行 | 并行 |
| Step 3 | 9 页全部并行派发 | 同一轮 9 子代理并行 | 并行 |
| Step 3.5 | 重排页面节点 + 注册 wiring interactions | Main Agent 直接 | - |
| Step 4 | 运行 `scan-design-directory.mjs` 验证 | Main Agent 直接 | - |
| Step 5 | 引导预览 | Main Agent | - |

### 8.3 子代理派发要点

每个页面子代理任务须包含：
- `{SKILL_DIR}` 展开为绝对路径 `c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design`
- 约束文件路径（page-generation-template.md 指定的 Sub-Agent 读取清单）
- `fill-html-head.mjs` 完整命令
- `operatingMode: "free-explore"` 标签
- 该页 `pageType`（信息密度类型）
- **Free-Explore 风格连续性锚点**（必须显式粘贴展开）：`["水墨笔触金线描边面板","深墨黑背景+水墨晕染纹理","Ma Shan Zheng毛笔书法标题","金/铜双主色点缀","水墨半透明毛玻璃面板","极简隐藏式HUD"]`
- 该页 visualNorthStar + compositionPattern
- 品牌 CSS 路径 + 实际 token 名引用清单

---

## 九、导航配置（Wiring）方案

Step 3.5 由 Main Agent 统一注册 interactions 到 `.design`：

**可见边（1 条，主流程引导）**
- `page-combat-hud` → `page-character-panel`（domId: `nav-character`，可见）

**隐藏交互（hideEdge: true，全局导航 + 返回控制）**

| 源页 | domId | 目标页 | 说明 |
|------|-------|--------|------|
| combat-hud | nav-skill | skill-panel | 系统导航 |
| combat-hud | nav-inventory | inventory | 系统导航 |
| combat-hud | nav-quest | quest-log | 系统导航 |
| combat-hud | nav-map | world-map | 系统导航 |
| combat-hud | nav-social | social-guild | 系统导航 |
| combat-hud | nav-shop | shop | 系统导航 |
| combat-hud | nav-dialog | npc-dialog | 系统导航 |
| character-panel | back-hud | combat-hud | 返回主界面 |
| skill-panel | back-hud | combat-hud | 返回主界面 |
| inventory | back-hud | combat-hud | 返回主界面 |
| quest-log | back-hud | combat-hud | 返回主界面 |
| world-map | back-hud | combat-hud | 返回主界面 |
| social-guild | back-hud | combat-hud | 返回主界面 |
| shop | back-hud | combat-hud | 返回主界面 |
| npc-dialog | back-hud | combat-hud | 返回主界面 |

可见边总数 = 1 ≤ 9 ✓。

---

## 十、验证步骤

Step 4 阻塞式验证（Main Agent 执行，失败则阻塞不得交付）：

```bash
node c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui --expected-pages=9 --require-interactions=nav-character:pages/character-panel.html,nav-skill:pages/skill-panel.html,nav-inventory:pages/inventory.html,nav-quest:pages/quest-log.html,nav-map:pages/world-map.html,nav-social:pages/social-guild.html,nav-shop:pages/shop.html,nav-dialog:pages/npc-dialog.html,back-hud:pages/combat-hud.html
```

**验收标准**：
1. `.design` 存在且为有效 JSON，`data` 非空数组，含 ≥9 个 page 节点 + 6 个 image 节点
2. 每个 page 节点 `devMetadata.htmlSrc` 指向的 HTML 文件实际存在
3. 每个 image 节点 `devMetadata.imageSrc` 指向的图片文件实际存在；`assets/` 下无未注册图片
4. 所有节点 `id` 唯一，`devMetadata` 字段完整
5. `--expected-pages=9` 断言通过
6. `--require-interactions` 列出的 domId→页面交互均已注册
7. Free Explore 模式：`.design.config.designLibrary` 为 `null` 或省略

**失败修复**：依 `main-agent-repair-flow.md`，区分"补注册节点/补文件"（Main Agent 修复）与"页面质量问题"（重派子代理）；子代理不得直接调用 validate-design-file.mjs（避免并行死锁）。

---

## 十一、假设与说明

1. **技术栈分层**：用户技术栈（.NET 10/Orleans/SqlServer/Redis/EFCore/TouchSocket/MemoryPack）属游戏服务端实现层；本次产出为 `.design` 画布设计稿（HTML+Tailwind+品牌 CSS），属视觉蓝本层，与服务端技术选型不直接耦合。设计稿交付后可作为 Flax Engine UI 落地的视觉参考。
2. **现有项目保留**：不修改/删除 `game-ui-system`、`hundun-login`、`character-creation` 等现有项目，仅复用其图标集。
3. **字体加载**：Ma Shan Zheng / Noto Serif SC / Noto Sans SC 通过 Google Fonts CDN 加载，画布预览需联网。
4. **Phase 2 交付**：13 个延期页面在 Phase 1 审阅通过后追加，沿用同一项目目录与品牌 CSS。
5. **金光晕合规**：`--ink-shadow-gold` 为用户明确要求的"金铜点缀"氛围特征（User Instruction Priority 优先），仅限关键 CTA/传世品质标识，不大面积使用。

---

## 十二、关键风险与缓解

| 风险 | 缓解 |
|------|------|
| Ma Shan Zheng 仅 400 字重，大段正文不可读 | 限制仅用于大标题/品牌字/页名；正文用 Noto Serif SC/Noto Sans SC |
| 水墨笔触边框纯 CSS 难逼真 | 多层 box-shadow 错位金线 + border-image 渐变模拟飞白，continuityAnchors 锚定统一实现 |
| 9 页并行 token 漂移 | 每个子代理任务显式粘贴展开 styleContinuityAnchors + 实际 token 名清单 |
| 金光晕违反 Free Explore shadow policy | 标注为用户明确要求的氛围特征，仅限关键元素，alpha 控制 |
| 现有项目误覆盖 | 新建独立 `hundun-ink-wash-ui` 目录，不动现有项目任何文件 |
