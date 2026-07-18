# 混沌世界 HundunWorld · 燕云融合 UI 实施计划

## 概述

基于已有 8 组 `yy-` 前缀设计原型（共 56 页 + 19 图），深度仿照燕云十六声 UI 系统，整合到一个全新独立 `.design` 项目 `hundun-yy-ui` 中。视觉风格融合两套配色优势：以 `ink-` 深墨黑沉浸为底，吸收 `yy-` 原型的纸色面板与朱红强调色。

用户三项决策（已确认）：全量 56 页整合 / 新建独立项目 / 融合两套配色优势。

---

## 一、项目命名与目录结构

**项目名**：`hundun-yy-ui`
**设计文件**：`hundun-yy-ui.design`
**根路径**：`c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui`

```
hundun-yy-ui/
├── hundun-yy-ui.design              # 主设计文件（56页+19图节点）
├── colors_and_type.css              # 融合风格品牌CSS
├── orchestration-summary.json       # 运行时上下文摘要
├── generation-tree.json             # 生成树SSOT
├── assets/
│   ├── icons/                       # 复用671个SVG图标（从hundun-ink-wash-ui/assets/icons/复制）
│   └── (19张yy-原型图片)
├── partials/                        # 共享HTML片段
│   ├── project-shell.html           # 全局框架
│   ├── character-loading-shell.html # 创角加载共享壳
│   ├── core-combat-shell.html       # 核心战斗共享壳
│   ├── menu-character-shell.html    # 角色菜单共享壳
│   ├── menu-quest-shell.html        # 任务菜单共享壳
│   ├── menu-extended-shell.html     # 扩展菜单共享壳
│   ├── popup-system-shell.html      # 弹窗系统共享壳
│   ├── reward-system-shell.html     # 奖励系统共享壳
│   └── misc-system-shell.html       # 杂项系统共享壳
└── pages/                           # 56个HTML页面
```

配置：`free-explore` 模式、`desktop`、`dark` 主题、`dashboardMode=false`。不改动现有 `hundun-ink-wash-ui.design` 的 9 页。

---

## 二、融合风格 Token 方案（colors_and_type.css）

### 2.1 融合策略

| 维度 | ink- 系统 | yy- 系统 | 融合决策 | 理由 |
|------|-----------|----------|----------|------|
| 底色 | `#0E1016` 深墨黑 | `#1a1a1a` 墨色 | 沿用 ink- `#0E1016` | 更沉浸，符合硬核武侠 |
| 面板 | 半透明毛玻璃金线描边 | `#f5f0e8` 纸色卷轴 | 双轨并存：深色面板用 ink-，浅色卷轴/信笺/对话框用 yy- 纸色 | 水墨纸面对比 |
| 主CTA | 鎏金 `#C8A858` | 朱红 `#c0392b` | 鎏金为主CTA | 金色更适合通用操作 |
| 战斗/危险 | `#B85450` 血色 | `#c0392b` 朱红 | 升级为朱红 `#c0392b` | 更饱和，战斗辨识度高 |
| 传世品质 | 鎏金 `#C8A858` | 朱红 `#c0392b` | 朱红 `#c0392b` | 稀缺感更强 |
| 字体 | Ma Shan Zheng + Noto Serif SC + Noto Sans SC + DIN | 无书法体 | 保留 ink- 全套字体 | Ma Shan Zheng 书法大标题是独有优势 |
| 圆角 | max 8px | max 16px | max 8px | 古风克制更符合武侠 |
| 前缀 | `ink-` | `color-` | `ink-` 前缀 | 复用 ink- 完整组件系统 |

### 2.2 新增 Token（相对 ink- 原系统的增量）

```css
/* 纸色系（yy- 引入，用于浅色卷轴/对话框/信笺面板）*/
--ink-paper-bright: #f5f0e8;
--ink-paper: #ebe5d8;
--ink-paper-aged: #d4c9b8;
--ink-paper-faded: #c8bba8;
--ink-paper-dark: #a89e8a;

/* 朱红系（yy- 引入，战斗/危险/传世品质强调）*/
--ink-vermilion-primary: #c0392b;
--ink-vermilion-bright: #d9504a;
--ink-vermilion-deep: #a93226;
--ink-vermilion-faded: #8b2a20;
--ink-vermilion-glow: rgba(192, 57, 43, 0.4);

/* 品质色升级：传世 = 朱红 */
--ink-quality-legendary: #c0392b;

/* 状态色升级：错误 = 朱红 */
--state-error: #c0392b;

/* 纸色面板专用 */
--ink-paper-panel-bg: rgba(245, 240, 232, 0.92);
--ink-paper-panel-border: rgba(168, 158, 138, 0.4);
--ink-paper-panel-shadow: 0 4px 16px rgba(0, 0, 0, 0.12);

/* 纸色面板文字 */
--text-on-paper: #2a2520;
--text-vermilion: #d9504a;

/* 朱红边框/阴影 */
--ink-border-vermilion: rgba(192, 57, 43, 0.35);
--ink-shadow-vermilion: 0 0 24px rgba(192, 57, 43, 0.2);
```

保留不变的 ink- token：背景层、鎏金、古铜、玉色、字体、圆角、间距、控件尺寸、阴影、Tailwind v4 语义别名（`--ink-background` 到 `--ink-radius-xlarge`）。

### 2.3 新增融合组件类

- `ink-paper-panel`：纸色卷轴面板（纸纹 SVG overlay + 纸色背景），用于对话框/信笺/卷轴
- `ink-btn-vermilion`：朱红渐变按钮，用于战斗/危险操作
- `ink-bar-fill-vermilion`：朱红渐变进度条，用于气血/战斗条
- `ink-vertical-title`：竖排书法标题（writing-mode: vertical-rl + Ma Shan Zheng），用于加载页/章节过场
- `ink-splash`：水墨晕染装饰（径向渐变 + blur），用于氛围层

---

## 三、56 页分批次交付计划

### 批次总览

| 批次 | 模块 | 页数 | 优先级 | 页面清单 |
|------|------|------|--------|----------|
| 1 | 角色创建与加载 | 7 | P0 | cc-naming, cc-face-customize, loading-1, loading-2, ink-anim-ref-1, ink-anim-ref-2, chapter-transition |
| 2 | 核心战斗 | 7 | P0 | combat-hud, guide-hud, element-vision, death-screen, qte, acupoint, dialogue-confirm |
| 3 | 角色菜单 | 7 | P1 | menu-char-attributes, menu-equipment, menu-appearance, menu-battle-prep, menu-sect, menu-personal-info, menu-time |
| 4 | 任务菜单 | 7 | P1 | menu-quests, menu-bestiary, menu-martial-record, menu-livelihood, menu-team, menu-mail, menu-shop |
| 5 | 扩展菜单 | 7 | P2 | menu-casual-mode, menu-gacha, menu-activities, menu-battle-pass, menu-char-attributes-v2, shop-rare-items, settings |
| 6 | 弹窗系统 | 8 | P2 | popup-bestiary-side, popup-message, popup-martial-detail, popup-guide-side, popup-martial-arts, popup-item-acquired, popup-skill-realization, popup-verification |
| 7 | 奖励+杂项 | 13 | P3 | reward-achievement, reward-teleport-unlock, reward-map-unlock, reward-level-up, reward-congratulations, reward-quest-complete, photo-mode, settings-audio, mini-game-complete, guide-action, guide-martial, multiplayer, combat-hud-v2 |

总计：7+7+7+7+7+8+13 = 56 页。批次 7 如 Sub-Agent 并发受限可拆分为 7a（奖励 6 页）+ 7b（杂项 7 页）。

### 各批次图片资源

- 批次1（7图）：bg-cc-naming, bg-cc-preview, bg-loading-landscape, bg-loading-mountain, ink-anim-spread, ink-anim-brush, bg-chapter-ink
- 批次2（5图）：bg-ink-wash-scene, bg-element-vision, bg-ink-wash-death, ink-brush-stroke, bg-meridian-diagram
- 批次3（2图）：bg-char-preview, bg-sect-emblem
- 批次4（3图）：bg-bestiary-illust, bg-letter-paper, bg-map-ink
- 批次5（2图）：bg-gacha-glow, bg-char-preview-v2
- 批次6（0图）、批次7（0图）：纯 CSS 水墨纹理 + SVG noise 实现氛围

决策：不新增生成图片。19 张现有图片已覆盖所有需要背景图的页面。

---

## 四、页面间 Wiring 计划

### 4.1 可见连线（53条）

形成以 combat-hud 为分叉点的有向无环链。combat-hud 拥有 2 条可见出口（战斗分支 + 菜单分支），其余每页 1 条可见出口或 0 条。

**创角加载链（5条）**：cc-naming → cc-face-customize → loading-1 → loading-2 → chapter-transition → combat-hud

**战斗分支（6条）**：combat-hud → guide-hud → element-vision → death-screen → qte → acupoint → dialogue-confirm

**菜单分支（21条）**：combat-hud → menu-char-attributes → menu-equipment → menu-appearance → menu-battle-prep → menu-sect → menu-personal-info → menu-time → menu-quests → menu-bestiary → menu-martial-record → menu-livelihood → menu-team → menu-mail → menu-shop → menu-casual-mode → menu-gacha → menu-activities → menu-battle-pass → menu-char-attributes-v2 → shop-rare-items → settings

**弹窗链（7条）**：settings → popup-bestiary-side → popup-message → popup-martial-detail → popup-guide-side → popup-martial-arts → popup-item-acquired → popup-skill-realization → popup-verification

**奖励链（5条）**：popup-verification → reward-achievement → reward-teleport-unlock → reward-map-unlock → reward-level-up → reward-congratulations → reward-quest-complete

**杂项链（6条）**：reward-quest-complete → photo-mode → settings-audio → mini-game-complete → guide-action → guide-martial → multiplayer → combat-hud-v2

无可见连线页面：ink-anim-ref-1、ink-anim-ref-2（参考分镜页，无业务流）。

### 4.2 隐藏交互（约50条，hideEdge:true）

- combat-hud 系统导航栏：6 条隐藏交互指向菜单/弹窗/奖励/杂项入口
- 各菜单/弹窗/奖励/杂项页返回 combat-hud：约 40 条 `back-hud` 隐藏交互
- 菜单侧边栏内部导航：每模块侧边栏导航项均注册为隐藏交互
- 弹窗关闭/取消：每个弹窗页关闭按钮返回源页面

### 4.3 data-dom-id 命名规范

侧边栏导航 `nav-{target}`、CTA按钮 `cta-{target}`、卡片链接 `link-{target}`、快捷跳转 `shortcut-{target}`、返回控制 `back-hud`。

---

## 五、图片资源规划

19 张 yy- 原型图从各自 `yy-*/assets/` 复制到新项目 `assets/`，注册为 `image-001` 到 `image-019`，canvasData 中不含 group 字段（image 节点不可有 group）。671 个 SVG 图标从 `hundun-ink-wash-ui/assets/icons/` 复制到 `assets/icons/`。

---

## 六、orchestration-summary.json 与 generation-tree.json

- orchestration-summary.json：含 project（含完整 styleContinuityAnchors 融合描述）、designSource（free-explore、brandPrefix=ink、themeMode=dark）、pages（56个page对象）、assets（19个）、wiringPlan（53条visible）、hiddenInteractionPlan（约50条hidden）
- generation-tree.json：1 个 root（gen-project-shell）+ 8 个 shared-branch（对应8个模块）+ 56 个 page-leaf

---

## 七、每批次验证步骤

每批次完成后执行：

1. **fill-html-head 注入**：`node fill-html-head.mjs colors_and_type.css pages/{page}.html --replace-head --prefix=ink --theme=dark`，将融合 CSS 注入 `<style id="theme-vars">`，生成 `@theme inline` 桥接块
2. **scan-design-directory 全量验证**：`node scan-design-directory.mjs c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui --expected-pages={当前累计页数}`，验证目录结构/.design格式/HTML基础设施/质量规则/assets覆盖
3. **零错误确认**：验证脚本退出码为0（warnings 非阻塞可接受）
4. **wiring 注册**：根据 wiring map 和 hidden interaction plan 填充各 page 节点的 interactions 数组

---

## 八、假设与决策

### 关键假设
1. 现有 19 张 yy- 原型图可直接复用，风格与新融合 CSS 兼容；如执行中发现色调冲突可补充生成新氛围图
2. ink- 前缀组件系统可直接扩展，仅新增 yy- 组件类；yy- HTML 中 `color-` 前缀 token 需统一映射到 `ink-` 命名空间
3. free-explore 模式的 secondary/accent 警告非阻塞可接受
4. 批次7的13页在单批次弹性上限内（9-15页）；如并发受限可拆分为7a+7b

### 设计决策
- CSS 前缀 `ink-`：复用完整组件系统，减少重构量
- 朱红命名 `--ink-vermilion-*`：避免触发验证警告，语义清晰
- 纸色面板 `ink-paper-panel`：与 `ink-panel` 深色面板并存，按场景选择
- 传世品质色朱红 `#c0392b`：比 ink- 鎏金传世更稀缺感
- 可见连线拓扑 combat-hud 2出口分支：满足枢纽页需求，遵守每页出口≤2
- 无图页面用 CSS SVG noise + 径向渐变：符合图片克制原则
- 参考页 ink-anim-ref-1/2 无 wiring：分镜参考页不属于业务流

### 执行顺序约束
1. 初始化阶段一次性写入全部 56 个 page 骨架节点到 .design（含预分配 nodeId/htmlSrc/title/pageIndex）
2. 图片复制和 image 节点注册一次性完成（image-001 到 image-019）
3. generation-tree.json 一次性写入完整树
4. 每批次按依赖顺序生成：project-shell → shared-branch → 并行 page-leaf
5. wiring 注册在每批次完成后增量执行，或全部完成后统一执行
6. scan-design-directory.mjs 每批次完成后执行，0 错误才进入下一批次

### 风险与缓解
- **yy- HTML token 不兼容**：所有 yy- HTML 需重写 token 引用（`--color-ink-dark` → `--bg-base-default`、`--color-paper-bright` → `--ink-paper-bright`、`--color-vermilion` → `--ink-vermilion-primary` 等）
- **跨批次 wiring 引用**：page 骨架节点在初始化阶段已全部预注册，targetPageId 指向已存在节点 ID，无需等待 HTML 生成
- **56页 .design 文件过大**：canvas SDK 支持大文件，JSON 格式紧凑，可接受
- **fill-html-head --prefix 参数**：使用 `--prefix=ink`，脚本生成 `--ink-*` 前缀的 @theme inline 桥接
