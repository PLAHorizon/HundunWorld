# 混沌世界 UI 系统收尾计划

修复验证阻塞错误 + 补全缺失 token + 编写架构规范文档

## 摘要

前序会话已完成 9 个高保真 UI 页面、5 张素材图片、`.design` 画布文件和导航注册。当前 `scan-design-directory.mjs` 验证仅剩 1 个阻塞错误（`world-map.html` 硬编码颜色），但深入排查发现 `colors_and_type.css` 的水墨 override 块缺少 3 组共 11 个 CSS 自定义变量定义（`--ink-quality-*`、`--ink-element-*`、`--ink-font-number`），被 7+ 个页面共 93 处引用。本计划修复验证错误、补全缺失 token，并编写 UI 架构规范文档。

## 现状分析

### 已完成的工作

- 9 个 HTML 页面: combat-hud、character-panel、skill-panel、inventory、quest-log、world-map、social-guild、shop、compass
- 5 张生成图片: ink-wash-bg-dark、compass-ring、gold-filigree-corner、ink-wash-map、ink-divider
- `.design` 文件: 9 page 节点 + 5 image 节点，导航交互已注册（8 条 nav + 8 条 back-hud）
- `colors_and_type.css`: TRAE Work base tokens + scaffold 合并 + 水墨 override 块（`--ink-bg-*`、`--ink-gold-*`、`--ink-jade-*`、`--ink-blood-*`、`--ink-text-*`、`--ink-border-*`、`--ink-shadow-*`、`--ink-font-*`、`--ink-radius-*`、`--ink-blur-*`）
- `components.css`、`css.json`、`library-consumption.json`、671 个 SVG 图标
- `orchestration-summary.json` 完整项目元数据

### 阻塞验证错误

- `[FAIL] [html-quality] world-map.html: hardcoded colors are forbidden outside <style id="theme-vars">`
- 根因: `world-map.html` 第 1691 行 `.dot-herb, .qt-herb { background: #6FAE6A; }` 仍为硬编码 hex 颜色

### 缺失 token 定义（非阻塞但影响渲染）

`colors_and_type.css` 水墨 override 块（第 842-907 行）缺少以下 3 组变量，导致 7+ 个页面共 93 处 `var(--ink-*)` 引用失效：

| 缺失变量 | 引用页面数 | 引用次数 | 用途 |
|----------|-----------|---------|------|
| `--ink-quality-common` | 5 | ~20 | 普通品质边框/图标 |
| `--ink-quality-uncommon` | 5 | ~15 | 优良品质边框/图标 |
| `--ink-quality-rare` | 5 | ~15 | 稀有品质边框/图标 |
| `--ink-quality-epic` | 4 | ~10 | 史诗品质边框/图标 |
| `--ink-quality-legendary` | 5 | ~15 | 传说品质边框/图标 |
| `--ink-element-metal` | 2 | ~3 | 五行-金属性文字 |
| `--ink-element-wood` | 1 | ~2 | 五行-木属性背景 |
| `--ink-element-water` | 1 | ~1 | 五行-水属性背景 |
| `--ink-element-earth` | 1 | ~1 | 五行-土属性背景 |
| `--ink-element-fire` | 0 | 0 | 预留 |
| `--ink-font-number` | 2 | ~5 | 数字字体 |

## 变更方案

### 变更 1: 补全缺失 token 定义

文件: `game-ui-system/colors_and_type.css`

在第 898 行 `--ink-font-mono` 之后、第 900 行 `--ink-radius-none` 之前插入：

```css
  --ink-font-number: "DIN Alternate", "DIN", "Bebas Neue", monospace;

  --ink-quality-common: #8A8275;
  --ink-quality-uncommon: #6B8E5A;
  --ink-quality-rare: #4A7EA8;
  --ink-quality-epic: #8B5E9E;
  --ink-quality-legendary: #C8A858;

  --ink-element-metal: #D4C4A0;
  --ink-element-wood: #6B8E5A;
  --ink-element-water: #4A6E8A;
  --ink-element-fire: #B85638;
  --ink-element-earth: #8A7B5A;
```

原因: 子代理生成页面时引用了这些 token，但 override 块遗漏了定义。品质颜色对应游戏 EquipmentQuality 枚举（普通→神话），五行颜色对应五行属性体系。

### 变更 2: 修复硬编码颜色

文件: `game-ui-system/pages/world-map.html`

第 1691 行:
- 修改前: `.dot-herb, .qt-herb { background: #6FAE6A; }`
- 修改后: `.dot-herb, .qt-herb { background: var(--ink-element-wood); }`

原因: 验证器禁止 `<style id="theme-vars">` 块之外使用硬编码 hex 颜色。其余 4 个 dot 颜色已修复为 CSS 变量，仅此行遗漏。

### 变更 3: 重新验证

执行:
```
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs" "c:\Works\GitHubProjects\HundunWorld\game-ui-system" --expected-pages=9
```

预期: 0 errors, 9 warnings（Library-bound 自定义 CSS 类警告，非阻塞）

### 变更 4: 编写 UI 架构规范文档

文件: `game-ui-system/ui-architecture-spec.md`

内容结构:
1. 设计系统概述: 水墨古风视觉语言、TRAE Work 结构保留策略
2. Token 体系: `--ink-*` 变量分类表（bg/gold/jade/blood/text/border/shadow/font/radius/quality/element）
3. 9 个界面规格: 每个界面的布局结构、交互入口、数据绑定点
4. 导航流转: combat-hud 为中心 hub，8 个功能页通过 `data-dom-id` 导航
5. 品质与五行系统: 品质色阶（common→legendary）与五行色阶（金木水火土）的 token 映射
6. 前端集成指引: Flax Engine 中如何映射 HTML 原型到游戏 UI 组件

## 假设与决策

- 品质颜色值参考燕云十六声和主流 MMORPG 惯例: 灰（普通）→ 绿（优良）→ 蓝（稀有）→ 紫（史诗）→ 金（传说）
- 五行颜色值采用低饱和度古风配色，与水墨暗色背景协调
- `--ink-font-number` 使用 DIN 等宽数字字体，适用于属性数值显示
- 9 个 warnings（Library-bound 自定义 CSS 类）为非阻塞，无需修复
- UI 架构规范文档输出到 `game-ui-system/` 目录，与 `.design` 文件同级

## 验证步骤

1. 确认 `colors_and_type.css` 新增 11 个 token 定义，语法正确
2. 确认 `world-map.html` 第 1691 行已替换为 `var(--ink-element-wood)`
3. 运行 `scan-design-directory.mjs`，确认输出 `Validation Passed` / 0 errors
4. 确认 `ui-architecture-spec.md` 已创建且内容完整
5. 在画布中打开 `.design` 文件，确认 9 个页面 + 5 张图片正常渲染
