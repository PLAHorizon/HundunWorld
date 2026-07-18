# 混沌世界 · 燕云融合 UI — 剩余批次实施计划（批次2-7）

## 概述

继续执行已批准的56页燕云融合UI项目。批次1（7页创角加载）已完成并通过 `fill-html-head` 注入。本计划覆盖批次2-7剩余42页的并行生成、token映射、wiring验证与最终交付。

所有设计决策已在上一会话确认：全量56页整合 / 新建独立项目 `hundun-yy-ui` / 融合两套配色优势。

---

## 一、当前状态分析

### 1.1 已完成

- 项目根路径：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui`
- `colors_and_type.css`：融合风格CSS完整（ink-深墨黑底 + yy-纸色面板 + 朱红战斗强调 + 鎏金主CTA），约645行
- `hundun-yy-ui.design`：56个page节点（data[0]-data[55]）+ 19个image节点（data[56]-data[74]）全部预注册；53条可见连线 + 54条隐藏交互全部预注册
- `orchestration-summary.json`：完整运行时上下文（styleContinuityAnchors + 56页pages数组 + 19图assets + wiringPlan + hiddenInteractionPlan）
- `generation-tree.json`：1 root + 7 shared-branch + 56 page-leaf
- 19张JPG图片已复制到 `assets/`
- 671个SVG图标已复制到 `assets/icons/`
- 批次1：7个HTML页面已生成（cc-naming, cc-face-customize, loading-1, loading-2, ink-anim-ref-1, ink-anim-ref-2, chapter-transition），`fill-html-head.mjs --replace-head` 已成功注入

### 1.2 待完成（42页，6批次）

| 批次 | 模块 | 页数 | yy-参考目录 | 图片资源 |
|------|------|------|-------------|----------|
| 2 | 核心战斗 | 7 | `yy-core-combat/pages/` | bg-ink-wash-scene, bg-element-vision, bg-ink-wash-death, ink-brush-stroke, bg-meridian-diagram |
| 3 | 角色菜单 | 7 | `yy-menu-character/pages/` | bg-char-preview, bg-sect-emblem |
| 4 | 任务菜单 | 7 | `yy-menu-quest/pages/` | bg-bestiary-illust, bg-letter-paper, bg-map-ink |
| 5 | 扩展菜单 | 7 | `yy-menu-extended/pages/` | bg-gacha-glow, bg-char-preview-v2 |
| 6 | 弹窗系统 | 8 | `yy-popup-system/pages/` | 无（纯CSS水墨纹理） |
| 7 | 奖励+杂项 | 13 | `yy-reward-system/pages/`(6) + `yy-misc-system/pages/`(7) | 无（纯CSS水墨纹理） |

### 1.3 关键路径

- SKILL_DIR: `c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design`
- fill-html-head.mjs: `{SKILL_DIR}/script/fill-html-head.mjs`
- scan-design-directory.mjs: `{SKILL_DIR}/script/scan-design-directory.mjs`
- 融合CSS: `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css`
- yy-参考原型根: `c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\yy-{module}\pages\{page}.html`
- 输出目录: `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\pages\`

---

## 二、Token与组件类映射表

Sub-Agent 生成页面时，必须将 yy- 原型中的 `--color-*` token 和组件类映射到融合 `--ink-*`/`--bg-*` 命名空间。

### 2.1 Token映射

| yy-原型Token | 融合Token | 语义 |
|---------------|-----------|------|
| `--color-ink-darkest: #0d0d0d` | `--bg-base-default: #0E1016` / `--ink-bg-void` | 主背景 |
| `--color-ink-dark: #1a1a1a` | `--bg-base-secondary: #14171E` / `--ink-bg-panel-solid` | 面板背景 |
| `--color-ink-medium: #2d2d2d` | `--bg-base-tertiary: #1C1F28` | 三级背景 |
| `--color-ink-light: #3d3d3d` | `--bg-base-elevated: #232733` | 升高背景 |
| `--color-ink-wash: #4a4a4a` | `--text-tertiary: #7A7468` | 次要文字/边框 |
| `--color-ink-faded: #6a6a6a` | `--text-tertiary: #7A7468` | 淡化文字 |
| `--color-paper-bright: #f5f0e8` | `--ink-paper-bright: #f5f0e8` | 纸色亮（面板/文字） |
| `--color-paper: #ebe5d8` | `--ink-paper: #ebe5d8` | 纸色 |
| `--color-paper-aged: #d4c9b8` | `--ink-paper-aged: #d4c9b8` | 旧纸色 |
| `--color-paper-faded: #c8bba8` | `--ink-paper-faded: #c8bba8` | 淡纸色 |
| `--color-paper-dark: #a89e8a` | `--ink-paper-dark: #a89e8a` | 暗纸色 |
| `--color-vermilion: #c0392b` | `--ink-vermilion-primary: #c0392b` | 朱红 |
| `--color-vermilion-dark: #a93226` | `--ink-vermilion-deep: #a93226` | 深朱红 |
| `--color-vermilion-light: #d9504a` | `--ink-vermilion-bright: #d9504a` | 亮朱红 |
| `--color-gold: #c9a84c` | `--ink-gold-primary: #C8A858` | 鎏金 |
| `--color-gold-bright: #d4af37` | `--ink-gold-bright: #E0C880` | 亮金 |
| `--color-gold-dark: #a08838` | `--ink-gold-deep: #8A7438` | 深金 |
| `--color-jade: #5e8b6e` | `--ink-jade-primary: #5E8B7E` | 玉色 |
| `--color-jade-dark: #4a7c59` | `--ink-jade-primary: #5E8B7E` | 深玉 |
| `--color-jade-light: #7aa889` | `--ink-jade-bright: #7EAE9E` | 亮玉 |
| `--color-cyan-gray: #6b7b8a` | `--state-info: #4A7EA8` | 青灰/信息 |

### 2.2 组件类映射

| yy-原型类 | 融合类 | 用途 |
|-----------|--------|------|
| `ink-panel` | `ink-panel` | 水墨面板（不变，但背景改用融合token） |
| `ink-button` | `ink-btn` | 按钮基类 |
| `ink-button-primary` | `ink-btn-vermilion` | 朱红按钮（战斗/危险操作） |
| `ink-button-gold` | `ink-btn-primary` | 鎏金按钮（主CTA） |
| `ink-button-secondary` | `ink-btn-ghost` | 幽灵按钮 |
| `ink-progress` | `ink-bar` | 进度条容器 |
| `ink-progress-bar` | `ink-bar-fill` | 进度条填充（鎏金渐变） |
| `ink-progress-bar-vermilion` | `ink-bar-fill-vermilion` | 朱红进度条 |
| `ink-progress-bar-gold` | `ink-bar-fill` | 金色进度条（同默认） |
| `ink-progress-bar-jade` | `ink-bar-fill-jade` | 玉色进度条 |
| `vertical-title` | `ink-vertical-title` | 竖排书法标题 |
| `ink-badge` | `ink-tag` | 标签/徽章 |
| `ink-badge-gold` | `ink-tag-brand` | 金色标签 |
| `ink-badge-vermilion` | `ink-tag` + style | 朱红标签 |
| `ink-divider` | `ink-divider` | 分割线（不变） |
| `ink-splash` | `ink-splash` | 水墨晕染（不变） |
| `paper-texture` | `ink-paper-panel` | 纸色卷轴面板 |
| `side-slide-panel` | (保持，但token映射) | 侧滑面板 |
| `ink-tooltip` | `ink-tooltip` | 悬浮提示（不变） |
| `ink-loading` | (页面级，保持结构) | 加载画面 |
| `chapter-transition` | (页面级，保持结构) | 章节过场 |

### 2.3 html元素差异

- yy-原型：`<html lang="zh-CN" class="light">`
- 融合项目：`<html lang="zh-CN" class="dark">`
- `fill-html-head.mjs --prefix=ink --theme=dark` 会自动处理 `<head>` 注入

---

## 三、各批次Wiring要求（data-dom-id）

每个页面必须在对应交互元素上添加 `data-dom-id` 属性。可见连线（visible）和隐藏交互（hidden）均需注册。

### 3.1 批次2：核心战斗（7页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 data-dom-id → 目标 |
|------|---------------------------|---------------------------|
| combat-hud | `link-guide-hud` → guide-hud; `nav-character` → menu-char-attributes | `nav-quests` → menu-quests; `nav-casual` → menu-casual-mode; `nav-bestiary-popup` → popup-bestiary-side; `nav-reward` → reward-achievement; `nav-photo` → photo-mode; `nav-settings` → settings |
| guide-hud | `link-element-vision` → element-vision | `back-hud` → combat-hud |
| element-vision | `link-death-screen` → death-screen | `back-hud` → combat-hud |
| death-screen | `link-qte` → qte | `back-hud` → combat-hud |
| qte | `link-acupoint` → acupoint | `back-hud` → combat-hud |
| acupoint | `link-dialogue-confirm` → dialogue-confirm | `back-hud` → combat-hud |
| dialogue-confirm | （无可见出口） | `back-hud` → combat-hud |

### 3.2 批次3：角色菜单（7页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| menu-char-attributes | `nav-equipment` → menu-equipment | `back-hud` |
| menu-equipment | `nav-appearance` → menu-appearance | `back-hud` |
| menu-appearance | `nav-battle-prep` → menu-battle-prep | `back-hud` |
| menu-battle-prep | `nav-sect` → menu-sect | `back-hud` |
| menu-sect | `nav-personal-info` → menu-personal-info | `back-hud` |
| menu-personal-info | `nav-time` → menu-time | `back-hud` |
| menu-time | `shortcut-quests` → menu-quests | `back-hud` |

### 3.3 批次4：任务菜单（7页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| menu-quests | `nav-bestiary` → menu-bestiary | `back-hud` |
| menu-bestiary | `nav-martial-record` → menu-martial-record | `back-hud` |
| menu-martial-record | `nav-livelihood` → menu-livelihood | `back-hud` |
| menu-livelihood | `nav-team` → menu-team | `back-hud` |
| menu-team | `nav-mail` → menu-mail | `back-hud` |
| menu-mail | `nav-shop` → menu-shop | `back-hud` |
| menu-shop | `shortcut-casual-mode` → menu-casual-mode | `back-hud` |

### 3.4 批次5：扩展菜单（7页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| menu-casual-mode | `nav-gacha` → menu-gacha | `back-hud` |
| menu-gacha | `nav-activities` → menu-activities | `back-hud` |
| menu-activities | `nav-battle-pass` → menu-battle-pass | `back-hud` |
| menu-battle-pass | `nav-char-attributes-v2` → menu-char-attributes-v2 | `back-hud` |
| menu-char-attributes-v2 | `nav-shop-rare-items` → shop-rare-items | `back-hud` |
| shop-rare-items | `nav-settings` → settings | `back-hud` |
| settings | `shortcut-popup-bestiary` → popup-bestiary-side | `back-hud` |

### 3.5 批次6：弹窗系统（8页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| popup-bestiary-side | `link-popup-message` → popup-message | `back-hud` |
| popup-message | `link-popup-martial-detail` → popup-martial-detail | `back-hud` |
| popup-martial-detail | `link-popup-guide-side` → popup-guide-side | `back-hud` |
| popup-guide-side | `link-popup-martial-arts` → popup-martial-arts | `back-hud` |
| popup-martial-arts | `link-popup-item-acquired` → popup-item-acquired | `back-hud` |
| popup-item-acquired | `link-popup-skill-realization` → popup-skill-realization | `back-hud` |
| popup-skill-realization | `link-popup-verification` → popup-verification | `back-hud` |
| popup-verification | `shortcut-reward-achievement` → reward-achievement | `back-hud` |

### 3.6 批次7：奖励+杂项（13页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| reward-achievement | `link-reward-teleport` → reward-teleport-unlock | `back-hud` |
| reward-teleport-unlock | `link-reward-map` → reward-map-unlock | `back-hud` |
| reward-map-unlock | `link-reward-level-up` → reward-level-up | `back-hud` |
| reward-level-up | `link-reward-congratulations` → reward-congratulations | `back-hud` |
| reward-congratulations | `link-reward-quest-complete` → reward-quest-complete | `back-hud` |
| reward-quest-complete | `shortcut-photo-mode` → photo-mode | `back-hud` |
| photo-mode | `link-settings-audio` → settings-audio | `back-hud` |
| settings-audio | `link-mini-game-complete` → mini-game-complete | `back-hud` |
| mini-game-complete | `link-guide-action` → guide-action | `back-hud` |
| guide-action | `link-guide-martial` → guide-martial | `back-hud` |
| guide-martial | `link-multiplayer` → multiplayer | `back-hud` |
| multiplayer | `link-combat-hud-v2` → combat-hud-v2 | `back-hud` |
| combat-hud-v2 | （无可见出口） | `back-hud` |

---

## 四、实施步骤

### 步骤1：批次2 — 核心战斗（7页）

并行分发7个Sub-Agent，每个生成1个页面：

1. 读取对应 yy- 参考HTML：`c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\yy-core-combat\pages\{page}.html`
2. 读取融合CSS：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css`
3. 读取 solo-design 约束文件（Sub-Agent自行读取 `{SKILL_DIR}/workflows/page-generation-template.md` 指定的文件）
4. 将 yy- HTML 中的 `--color-*` token 映射为融合 `--ink-*`/`--bg-*` token
5. 将 yy- 组件类映射为融合组件类（见第二节映射表）
6. 添加 `data-dom-id` 属性（见第三节Wiring表）
7. html元素改为 `class="dark"`
8. 输出到 `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\pages\{page}.html`
9. 运行 `node {SKILL_DIR}/script/fill-html-head.mjs c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css pages/{page}.html --replace-head --prefix=ink --theme=dark`

页面清单：combat-hud, guide-hud, element-vision, death-screen, qte, acupoint, dialogue-confirm

**Sub-Agent dispatch payload 必须包含**：
- 风格连续性锚点（从 orchestration-summary.json styleContinuityAnchors 展开粘贴）
- yy-参考HTML路径
- 输出路径
- Token映射表（第二节）
- 组件类映射表（第二节）
- Wiring data-dom-id（第三节对应批次）
- SKILL_DIR绝对路径
- fill-html-head.mjs完整命令

### 步骤2：批次3 — 角色菜单（7页）

同步骤1模式，参考目录 `yy-menu-character/pages/`。
页面清单：menu-char-attributes, menu-equipment, menu-appearance, menu-battle-prep, menu-sect, menu-personal-info, menu-time

### 步骤3：批次4 — 任务菜单（7页）

同步骤1模式，参考目录 `yy-menu-quest/pages/`。
页面清单：menu-quests, menu-bestiary, menu-martial-record, menu-livelihood, menu-team, menu-mail, menu-shop

### 步骤4：批次5 — 扩展菜单（7页）

同步骤1模式，参考目录 `yy-menu-extended/pages/`。
页面清单：menu-casual-mode, menu-gacha, menu-activities, menu-battle-pass, menu-char-attributes-v2, shop-rare-items, settings

### 步骤5：批次6 — 弹窗系统（8页）

同步骤1模式，参考目录 `yy-popup-system/pages/`。
页面清单：popup-bestiary-side, popup-message, popup-martial-detail, popup-guide-side, popup-martial-arts, popup-item-acquired, popup-skill-realization, popup-verification

### 步骤6：批次7 — 奖励+杂项（13页）

同步骤1模式，参考目录分为两个：
- `yy-reward-system/pages/`（6页）：reward-achievement, reward-congratulations, reward-level-up, reward-map-unlock, reward-quest-complete, reward-teleport-unlock
- `yy-misc-system/pages/`（7页）：combat-hud-v2, guide-action, guide-martial, mini-game-complete, multiplayer, photo-mode, settings-audio

批次7共13页，可拆分为7a（6页奖励）+ 7b（7页杂项）两个子批次并行执行。

### 步骤7：最终验证

全部56页生成后执行：

```bash
node c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui --expected-pages=56
```

- 退出码必须为0（warnings非阻塞可接受）
- 验证项：目录结构、.design格式、HTML基础设施、质量规则、assets覆盖、所有interactions注册

### 步骤8：generation-tree.json 状态更新

每批次完成后，将对应 page-leaf 节点的 `status` 从 `"planned"` 更新为 `"generated"`。全部完成后将 root 和 shared-branch 也更新为 `"generated"`。

---

## 五、风格连续性锚点（Sub-Agent必带payload）

以下锚点必须完整粘贴到每个Sub-Agent的dispatch query中：

**colorSystem**: 鎏金#C8A858主CTA+选中态+面板标题强调；朱红#c0392b战斗操作+危险提示+传世品质+气血条；纸色#f5f0e8卷轴信笺面板与深底形成水墨纸面对比；品质色凡/良/珍/史/传五级，传世=朱红

**shapeSystem**: 古风硬朗克制：圆角max8px，面板4px，按钮2px，血条999px；金线描边+四角L型金角装饰；水墨笔触边框多层box-shadow错位模拟飞白；纸色面板用纸纹SVG overlay

**typographySystem**: Ma Shan Zheng毛笔书法仅用于页面大标题/品牌字/页名(32px)；Noto Serif SC宋体用于面板标题/副标题(14-18px,weight600)；Noto Sans SC黑体用于UI正文(13px)；DIN等宽用于数值；标题字间距1-4px

**spacingSystem**: 4/8/12/16/24/32/48px阶梯；面板内边距16px；列表项内边距12px 16px；控件高度28/36/44px

**componentLanguage**: ink-panel半透明毛玻璃面板+金线描边；ink-paper-panel纸色卷轴面板；ink-btn金线描边按钮(主/幽灵/朱红)；ink-cell品质色边框格子；ink-bar金/朱红渐变进度条；ink-vertical-title竖排书法标题；ink-splash水墨晕染装饰；统一data-dom-id导航

**surfaceAndDepth**: 静态面板以金线描边+背景层级分层为主,shadow alpha<=0.05；浮动层(弹窗/抽屉)用ink-shadow-panel深elevation+毛玻璃；金光晕仅限传世品质与关键CTA；朱红光晕仅限战斗操作

**imageryAndIconography**: 水墨晕染深色背景纹理(SVG noise+径向渐变)；远山雾霭/古建亭台/人物剪影水墨画风；功能图标复用671个SVG图标集；金菱形界碑标记；纸色面板用纸纹SVG overlay

**interactionTone**: 沉静厚重，hover态金线增强+微亮，选中态左侧金竖线+背景微金；朱红用于战斗反馈与危险提示；过渡0.15-0.4s ease；无弹跳/无跑马灯

---

## 六、假设与决策

### 关键假设
1. 19张现有yy-原型图可直接复用，无需新生成图片
2. yy- HTML中的 `--color-*` token可通过映射表完整转换到融合 `--ink-*` 命名空间
3. 所有56个page节点和107条交互已在 .design 中预注册，无需再修改 .design 文件
4. `fill-html-head.mjs --prefix=ink --theme=dark` 可正确生成 `@theme inline` 桥接块
5. 批次间无依赖关系，可串行执行各批次；批次内各页无依赖关系，可并行生成

### 设计决策（沿用已批准计划）
- CSS前缀 `ink-`：复用完整组件系统
- 朱红命名 `--ink-vermilion-*`：避免触发验证警告
- 纸色面板 `ink-paper-panel`：与 `ink-panel` 深色面板并存
- 传世品质色朱红 `#c0392b`
- 可见连线拓扑 combat-hud 2出口分支
- 参考页 ink-anim-ref-1/2 无wiring

### 风险与缓解
- **yy- HTML token不兼容**：所有yy- HTML需重写token引用，按第二节映射表执行
- **跨批次wiring引用**：page骨架节点在初始化阶段已全部预注册，targetPageId指向已存在节点ID
- **批次7的13页并发**：如Sub-Agent并发受限，拆分为7a(6页)+7b(7页)
- **fill-html-head WARN（custom style block moved to body）**：非阻塞，页面特定样式被重定位到body内，可接受

---

## 七、验证步骤

### 每批次后验证
1. 确认该批次所有HTML文件已生成且非空
2. 确认 `fill-html-head.mjs` 对每个文件输出 "Success"（0 failures）
3. 中间验证（可选）：`scan-design-directory.mjs --expected-pages={累计页数}` — 预期会有错误（未生成页面的HTML not found），这是分批交付的正常状态

### 最终验证（全部56页完成后）
1. `scan-design-directory.mjs --expected-pages=56` → 退出码0，0 errors
2. 确认 `pages/` 目录下有56个HTML文件
3. 确认 `assets/` 目录下有19个JPG + 671个SVG
4. 确认 `.design` 文件中56个page节点的 `devMetadata.htmlSrc` 均指向存在的HTML文件
5. 确认所有107条交互（53 visible + 54 hidden）的 source 和 target 节点均存在
6. 确认 `generation-tree.json` 中所有节点 status 为 `"generated"`
