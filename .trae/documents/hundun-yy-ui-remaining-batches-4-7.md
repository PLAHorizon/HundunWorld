# 混沌世界 · 燕云融合 UI — 剩余批次执行计划（批次4-7，35页）

## 概述

继续执行已批准的 56 页燕云融合 UI 项目。批次 1-3（21 页）已完成并通过 `fill-html-head` 注入验证。本计划覆盖批次 4-7 剩余 35 页的并行生成、token 映射、wiring 验证与最终交付。

所有设计决策已在之前会话确认并沿用：全量 56 页整合 / 新建独立项目 `hundun-yy-ui` / 融合两套配色优势（ink- 深墨黑沉浸底 + yy- 纸色面板 + 朱红战斗/传世强调 + 鎏金主 CTA）。

---

## 一、当前状态分析

### 1.1 已完成（21 页，批次 1-3）

- 项目根路径：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui`
- `colors_and_type.css`：融合风格 CSS 完整（~645 行），含 ink- 深墨黑底 + yy- 纸色面板 + 朱红战斗强调 + 鎏金主 CTA + 全套融合 token + 组件类
- `hundun-yy-ui.design`：56 个 page 节点 + 19 个 image 节点全部预注册；53 条可见连线 + 54 条隐藏交互全部预注册
- `orchestration-summary.json`：完整运行时上下文（styleContinuityAnchors + 56 页 pages 数组 + 19 图 assets + wiringPlan + hiddenInteractionPlan）
- `generation-tree.json`：1 root + 7 shared-branch + 56 page-leaf（status 需更新）
- 19 张 JPG 图片已就位（`assets/` 下 16 张 + `assets/` 根 3 张 ink-* 共 19 张）
- 671 个 SVG 图标已就位（`assets/icons/`）
- 批次 1（7 页）：cc-naming, cc-face-customize, loading-1, loading-2, ink-anim-ref-1, ink-anim-ref-2, chapter-transition
- 批次 2（7 页）：combat-hud, guide-hud, element-vision, death-screen, qte, acupoint, dialogue-confirm
- 批次 3（7 页）：menu-char-attributes, menu-equipment, menu-appearance, menu-battle-prep, menu-sect, menu-personal-info, menu-time
- 以上 21 页均已完成 `fill-html-head.mjs --replace-head --prefix=ink --theme=dark` 注入

### 1.2 待完成（35 页，4 批次）

| 批次 | 模块 | 页数 | yy- 参考目录 | 图片资源 |
|------|------|------|-------------|----------|
| 4 | 任务菜单 | 7 | `yy-menu-quest/pages/` | bg-bestiary-illust, bg-letter-paper, bg-map-ink |
| 5 | 扩展菜单 | 7 | `yy-menu-extended/pages/` | bg-gacha-glow, bg-char-preview-v2 |
| 6 | 弹窗系统 | 8 | `yy-popup-system/pages/` | 无（纯 CSS 水墨纹理） |
| 7 | 奖励+杂项 | 13 | `yy-reward-system/pages/`(6) + `yy-misc-system/pages/`(7) | 无（纯 CSS 水墨纹理） |

所有 yy- 参考原型 HTML 已确认存在：
- `yy-menu-quest/pages/`: menu-quests, menu-bestiary, menu-martial-record, menu-livelihood, menu-team, menu-mail, menu-shop（7 文件确认）
- `yy-menu-extended/pages/`: menu-casual-mode, menu-gacha, menu-activities, menu-battle-pass, menu-char-attributes-v2, shop-rare-items, settings（7 文件确认）
- `yy-popup-system/pages/`: popup-bestiary-side, popup-message, popup-martial-detail, popup-guide-side, popup-martial-arts, popup-item-acquired, popup-skill-realization, popup-verification（8 文件确认）
- `yy-reward-system/pages/`: reward-achievement, reward-congratulations, reward-level-up, reward-map-unlock, reward-quest-complete, reward-teleport-unlock（6 文件确认）
- `yy-misc-system/pages/`: combat-hud-v2, guide-action, guide-martial, mini-game-complete, multiplayer, photo-mode, settings-audio（7 文件确认）

### 1.3 关键路径

- SKILL_DIR: `c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design`
- fill-html-head.mjs: `{SKILL_DIR}/script/fill-html-head.mjs`
- scan-design-directory.mjs: `{SKILL_DIR}/script/scan-design-directory.mjs`
- 融合 CSS: `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css`
- yy- 参考原型根: `c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\yy-{module}\pages\{page}.html`
- 输出目录: `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\pages\`

---

## 二、Token 与组件类映射表（Sub-Agent 必带）

### 2.1 Token 映射

| yy- 原型 Token | 融合 Token | 语义 |
|----------------|-----------|------|
| `--color-ink-darkest: #0d0d0d` | `--bg-base-default: #0E1016` / `--ink-bg-void` | 主背景 |
| `--color-ink-dark: #1a1a1a` | `--bg-base-secondary: #14171E` / `--ink-bg-panel-solid` | 面板背景 |
| `--color-ink-medium: #2d2d2d` | `--bg-base-tertiary: #1C1F28` | 三级背景 |
| `--color-ink-light: #3d3d3d` | `--bg-base-elevated: #232733` | 升高背景 |
| `--color-ink-wash: #4a4a4a` | `--text-tertiary: #7A7468` | 次要文字/边框 |
| `--color-ink-faded: #6a6a6a` | `--text-tertiary: #7A7468` | 淡化文字 |
| `--color-paper-bright: #f5f0e8` | `--ink-paper-bright: #f5f0e8` | 纸色亮 |
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

| yy- 原型类 | 融合类 | 用途 |
|-----------|--------|------|
| `ink-panel` | `ink-panel` | 水墨面板（不变，背景改用融合 token） |
| `ink-button` | `ink-btn` | 按钮基类 |
| `ink-button-primary` | `ink-btn-vermilion` | 朱红按钮（战斗/危险操作） |
| `ink-button-gold` | `ink-btn-primary` | 鎏金按钮（主 CTA） |
| `ink-button-secondary` | `ink-btn-ghost` | 幽灵按钮 |
| `ink-progress` | `ink-bar` | 进度条容器 |
| `ink-progress-bar` | `ink-bar-fill` | 进度条填充（鎏金渐变） |
| `ink-progress-bar-vermilion` | `ink-bar-fill-vermilion` | 朱红进度条 |
| `ink-progress-bar-gold` | `ink-bar-fill` | 金色进度条 |
| `ink-progress-bar-jade` | `ink-bar-fill-jade` | 玉色进度条 |
| `vertical-title` | `ink-vertical-title` | 竖排书法标题 |
| `ink-badge` | `ink-tag` | 标签/徽章 |
| `ink-badge-gold` | `ink-tag-brand` | 金色标签 |
| `ink-badge-vermilion` | `ink-tag` + inline style 朱红 | 朱红标签 |
| `ink-divider` | `ink-divider` | 分割线（不变） |
| `ink-splash` | `ink-splash` | 水墨晕染（不变） |
| `paper-texture` | `ink-paper-panel` | 纸色卷轴面板 |
| `side-slide-panel` | 保持类名，token 映射 | 侧滑面板 |
| `ink-tooltip` | `ink-tooltip` | 悬浮提示（不变） |

### 2.3 html 元素差异

- yy- 原型：`<html lang="zh-CN" class="light">`
- 融合项目：`<html lang="zh-CN" class="dark">`
- `fill-html-head.mjs --prefix=ink --theme=dark` 自动处理 `<head>` 注入

---

## 三、各批次 Wiring 要求（data-dom-id）

每个页面必须在对应交互元素上添加 `data-dom-id` 属性。

### 3.1 批次 4：任务菜单（7 页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| menu-quests | `nav-bestiary` → menu-bestiary | `back-hud` |
| menu-bestiary | `nav-martial-record` → menu-martial-record | `back-hud` |
| menu-martial-record | `nav-livelihood` → menu-livelihood | `back-hud` |
| menu-livelihood | `nav-team` → menu-team | `back-hud` |
| menu-team | `nav-mail` → menu-mail | `back-hud` |
| menu-mail | `nav-shop` → menu-shop | `back-hud` |
| menu-shop | `shortcut-casual-mode` → menu-casual-mode | `back-hud` |

### 3.2 批次 5：扩展菜单（7 页）

| 页面 | 可见出口 data-dom-id → 目标 | 隐藏出口 |
|------|---------------------------|---------|
| menu-casual-mode | `nav-gacha` → menu-gacha | `back-hud` |
| menu-gacha | `nav-activities` → menu-activities | `back-hud` |
| menu-activities | `nav-battle-pass` → menu-battle-pass | `back-hud` |
| menu-battle-pass | `nav-char-attributes-v2` → menu-char-attributes-v2 | `back-hud` |
| menu-char-attributes-v2 | `nav-shop-rare-items` → shop-rare-items | `back-hud` |
| shop-rare-items | `nav-settings` → settings | `back-hud` |
| settings | `shortcut-popup-bestiary` → popup-bestiary-side | `back-hud` |

### 3.3 批次 6：弹窗系统（8 页）

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

### 3.4 批次 7：奖励+杂项（13 页）

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

## 四、风格连续性锚点（每个 Sub-Agent dispatch 必须完整粘贴）

**colorSystem**: 鎏金 #C8A858 主 CTA + 选中态 + 面板标题强调；朱红 #c0392b 战斗操作 + 危险提示 + 传世品质 + 气血条；纸色 #f5f0e8 卷轴信笺面板与深底形成水墨纸面对比；品质色凡/良/珍/史/传五级，传世 = 朱红

**shapeSystem**: 古风硬朗克制：圆角 max 8px，面板 4px，按钮 2px，血条 999px；金线描边 + 四角 L 型金角装饰；水墨笔触边框多层 box-shadow 错位模拟飞白；纸色面板用纸纹 SVG overlay

**typographySystem**: Ma Shan Zheng 毛笔书法仅用于页面大标题/品牌字/页名(32px)；Noto Serif SC 宋体用于面板标题/副标题(14-18px, weight600)；Noto Sans SC 黑体用于 UI 正文(13px)；DIN 等宽用于数值；标题字间距 1-4px

**spacingSystem**: 4/8/12/16/24/32/48px 阶梯；面板内边距 16px；列表项内边距 12px 16px；控件高度 28/36/44px

**componentLanguage**: ink-panel 半透明毛玻璃面板 + 金线描边；ink-paper-panel 纸色卷轴面板；ink-btn 金线描边按钮(主/幽灵/朱红)；ink-cell 品质色边框格子；ink-bar 金/朱红渐变进度条；ink-vertical-title 竖排书法标题；ink-splash 水墨晕染装饰；统一 data-dom-id 导航

**surfaceAndDepth**: 静态面板以金线描边 + 背景层级分层为主，shadow alpha <= 0.05；浮动层(弹窗/抽屉)用 ink-shadow-panel 深 elevation + 毛玻璃；金光晕仅限传世品质与关键 CTA；朱红光晕仅限战斗操作

**imageryAndIconography**: 水墨晕染深色背景纹理(SVG noise + 径向渐变)；远山雾霭/古建亭台/人物剪影水墨画风；功能图标复用 671 个 SVG 图标集；金菱形界碑标记；纸色面板用纸纹 SVG overlay

**interactionTone**: 沉静厚重，hover 态金线增强 + 微亮，选中态左侧金竖线 + 背景微金；朱红用于战斗反馈与危险提示；过渡 0.15-0.4s ease；无弹跳/无跑马灯

---

## 五、Sub-Agent Dispatch 规范

每个 Sub-Agent（general_purpose_task 类型）的 dispatch query 必须包含以下内容：

1. **约束文件路径**（Sub-Agent 自行读取）：`c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\workflows\page-generation-template.md`（Standard Mode，Sub-Agent 按模板指示自行读取所有约束文件）
2. **yy- 参考 HTML 路径**：`c:\Works\GitHubProjects\HundunWorld\hundun-ink-wash-ui\yy-{module}\pages\{page}.html`
3. **融合 CSS 路径**：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css`
4. **风格连续性锚点**：第四节完整粘贴（8 个锚点）
5. **Token 映射表**：第 2.1 节完整粘贴
6. **组件类映射表**：第 2.2 节完整粘贴
7. **html 元素要求**：`<html lang="zh-CN" class="dark">`
8. **Wiring data-dom-id**：第三节对应批次的该页条目
9. **输出路径**：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\pages\{page}.html`
10. **fill-html-head 命令**：`node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\fill-html-head.mjs" "c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css" "pages/{page}.html" --replace-head --prefix=ink --theme=dark`（cwd = `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui`）
11. **页面图片资源**（如有）：该页使用的背景图文件名（已存在于 `assets/`）

### Sub-Agent 6 步执行流程

1. 读取 `page-generation-template.md`（Standard Mode），按模板指示读取所有约束文件
2. 读取 yy- 参考 HTML，分析其布局结构、组件、交互
3. 读取融合 CSS `colors_and_type.css`，理解可用 token 和组件类
4. 生成融合 HTML：
   - `<html lang="zh-CN" class="dark">`
   - 将 yy- 的 `--color-*` token 映射为融合 `--ink-*`/`--bg-*` token
   - 将 yy- 组件类映射为融合组件类
   - 添加 `data-dom-id` 属性到交互元素
   - 保持页面功能完整，视觉风格与锚点一致
   - 输出到 `pages/{page}.html`
5. 运行 `fill-html-head.mjs` 命令注入 `<style id="theme-vars">` 和 `@theme inline` 桥接块
6. 确认 fill-html-head 输出 "Success: 1 file(s), Failed: 0"，报告完成

### Main Agent 验证（每批次后）

- 确认每个 Sub-Agent 返回中包含 fill-html-head 成功
- 对未完成的页面（Sub-Agent 停在第 4 步未运行 fill-html-head 的情况），Main Agent 手动运行 fill-html-head.mjs
- 确认 `pages/` 目录下该批次的 HTML 文件已生成且非空

---

## 六、实施步骤

### 步骤 1：批次 4 — 任务菜单（7 页）

并行分发 7 个 Sub-Agent，每个生成 1 个页面。

页面清单与 yy- 参考：
| 页面 | yy- 参考路径 | 背景图 |
|------|-------------|--------|
| menu-quests | `yy-menu-quest/pages/menu-quests.html` | bg-map-ink |
| menu-bestiary | `yy-menu-quest/pages/menu-bestiary.html` | bg-bestiary-illust |
| menu-martial-record | `yy-menu-quest/pages/menu-martial-record.html` | 无 |
| menu-livelihood | `yy-menu-quest/pages/menu-livelihood.html` | 无 |
| menu-team | `yy-menu-quest/pages/menu-team.html` | 无 |
| menu-mail | `yy-menu-quest/pages/menu-mail.html` | bg-letter-paper |
| menu-shop | `yy-menu-quest/pages/menu-shop.html` | 无 |

Wiring：见第 3.1 节。

### 步骤 2：批次 5 — 扩展菜单（7 页）

并行分发 7 个 Sub-Agent。

页面清单与 yy- 参考：
| 页面 | yy- 参考路径 | 背景图 |
|------|-------------|--------|
| menu-casual-mode | `yy-menu-extended/pages/menu-casual-mode.html` | 无 |
| menu-gacha | `yy-menu-extended/pages/menu-gacha.html` | bg-gacha-glow |
| menu-activities | `yy-menu-extended/pages/menu-activities.html` | 无 |
| menu-battle-pass | `yy-menu-extended/pages/menu-battle-pass.html` | 无 |
| menu-char-attributes-v2 | `yy-menu-extended/pages/menu-char-attributes-v2.html` | bg-char-preview-v2 |
| shop-rare-items | `yy-menu-extended/pages/shop-rare-items.html` | 无 |
| settings | `yy-menu-extended/pages/settings.html` | 无 |

Wiring：见第 3.2 节。

### 步骤 3：批次 6 — 弹窗系统（8 页）

并行分发 8 个 Sub-Agent。

页面清单与 yy- 参考：
| 页面 | yy- 参考路径 |
|------|-------------|
| popup-bestiary-side | `yy-popup-system/pages/popup-bestiary-side.html` |
| popup-message | `yy-popup-system/pages/popup-message.html` |
| popup-martial-detail | `yy-popup-system/pages/popup-martial-detail.html` |
| popup-guide-side | `yy-popup-system/pages/popup-guide-side.html` |
| popup-martial-arts | `yy-popup-system/pages/popup-martial-arts.html` |
| popup-item-acquired | `yy-popup-system/pages/popup-item-acquired.html` |
| popup-skill-realization | `yy-popup-system/pages/popup-skill-realization.html` |
| popup-verification | `yy-popup-system/pages/popup-verification.html` |

无背景图，使用纯 CSS 水墨纹理（SVG noise + 径向渐变）。

Wiring：见第 3.3 节。

### 步骤 4：批次 7a — 奖励系统（6 页）

并行分发 6 个 Sub-Agent。

页面清单与 yy- 参考：
| 页面 | yy- 参考路径 |
|------|-------------|
| reward-achievement | `yy-reward-system/pages/reward-achievement.html` |
| reward-teleport-unlock | `yy-reward-system/pages/reward-teleport-unlock.html` |
| reward-map-unlock | `yy-reward-system/pages/reward-map-unlock.html` |
| reward-level-up | `yy-reward-system/pages/reward-level-up.html` |
| reward-congratulations | `yy-reward-system/pages/reward-congratulations.html` |
| reward-quest-complete | `yy-reward-system/pages/reward-quest-complete.html` |

无背景图，使用纯 CSS 水墨纹理。

Wiring：见第 3.4 节前 6 行。

### 步骤 5：批次 7b — 杂项系统（7 页）

并行分发 7 个 Sub-Agent。

页面清单与 yy- 参考：
| 页面 | yy- 参考路径 |
|------|-------------|
| photo-mode | `yy-misc-system/pages/photo-mode.html` |
| settings-audio | `yy-misc-system/pages/settings-audio.html` |
| mini-game-complete | `yy-misc-system/pages/mini-game-complete.html` |
| guide-action | `yy-misc-system/pages/guide-action.html` |
| guide-martial | `yy-misc-system/pages/guide-martial.html` |
| multiplayer | `yy-misc-system/pages/multiplayer.html` |
| combat-hud-v2 | `yy-misc-system/pages/combat-hud-v2.html` |

无背景图，使用纯 CSS 水墨纹理。

Wiring：见第 3.4 节后 7 行。

### 步骤 6：最终验证

全部 56 页生成后执行：

```bash
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs" "c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui" --expected-pages=56
```

验证项：
- 退出码 0（warnings 非阻塞可接受）
- 目录结构完整
- .design 格式有效
- 56 个 page 节点的 htmlSrc 均指向存在的 HTML
- 19 个 image 节点的 imageSrc 均指向存在的图片
- 所有 107 条交互（53 visible + 54 hidden）的 source 和 target 节点存在
- 质量规则通过

### 步骤 7：generation-tree.json 状态更新

每批次完成后，将对应 page-leaf 节点的 `status` 从 `"planned"` 更新为 `"generated"`。全部完成后将 root 和所有 shared-branch 也更新为 `"generated"`。

---

## 七、假设与决策

### 关键假设
1. 19 张现有 yy- 原型图可直接复用，无需新生成图片
2. yy- HTML 中的 `--color-*` token 可通过映射表完整转换到融合 `--ink-*` 命名空间
3. 所有 56 个 page 节点和 107 条交互已在 .design 中预注册，无需再修改 .design 文件
4. `fill-html-head.mjs --prefix=ink --theme=dark` 可正确生成 `@theme inline` 桥接块
5. 批次间无依赖关系，可串行执行各批次；批次内各页无依赖关系，可并行生成
6. 批次 7 拆分为 7a(6 页) + 7b(7 页) 两个子批次并行执行

### 设计决策（沿用已批准计划）
- CSS 前缀 `ink-`：复用完整组件系统
- 朱红命名 `--ink-vermilion-*`：避免触发验证警告
- 纸色面板 `ink-paper-panel`：与 `ink-panel` 深色面板并存
- 传世品质色朱红 `#c0392b`
- 可见连线拓扑 combat-hud 2 出口分支
- 参考页 ink-anim-ref-1/2 无 wiring
- 无图页面用 CSS SVG noise + 径向渐变

### 风险与缓解
- **yy- HTML token 不兼容**：所有 yy- HTML 需重写 token 引用，按第 2.1 节映射表执行
- **跨批次 wiring 引用**：page 骨架节点在初始化阶段已全部预注册，targetPageId 指向已存在节点 ID
- **Sub-Agent 未完成 fill-html-head**：已知风险（批次 3 menu-equipment 曾发生），Main Agent 需验证每个 Sub-Agent 返回，对未完成的页面手动运行 fill-html-head.mjs
- **批次 7 的 13 页并发**：已拆分为 7a(6 页) + 7b(7 页) 两个子批次
- **fill-html-head WARN（custom style block moved to body）**：非阻塞，页面特定样式被重定位到 body 内，可接受

---

## 八、验证步骤

### 每批次后验证
1. 确认该批次所有 HTML 文件已生成且非空
2. 确认 `fill-html-head.mjs` 对每个文件输出 "Success"（0 failures）
3. 抽查 1-2 个 HTML 文件，确认 `<html lang="zh-CN" class="dark">`、`<style id="theme-vars">` 已注入、data-dom-id 属性已放置

### 最终验证（全部 56 页完成后）
1. `scan-design-directory.mjs --expected-pages=56` → 退出码 0，0 errors
2. 确认 `pages/` 目录下有 56 个 HTML 文件
3. 确认 `assets/` 目录下有 19 个 JPG + 671 个 SVG
4. 确认 `.design` 文件中 56 个 page 节点的 `devMetadata.htmlSrc` 均指向存在的 HTML 文件
5. 确认所有 107 条交互（53 visible + 54 hidden）的 source 和 target 节点均存在
6. 确认 `generation-tree.json` 中所有节点 status 为 `"generated"`
