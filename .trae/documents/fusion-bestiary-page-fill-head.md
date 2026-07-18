# 计划：完成博物志融合风格 HTML 页面的 fill-html-head 步骤

## 概述

用户要求生成一个融合风格 HTML 页面（博物志菜单页），基于 yy- 参考原型的布局和功能，应用完整的 Token 映射和组件类映射到融合 CSS 体系。当前步骤 1-4 已完成，需执行步骤 5（fill-html-head）和步骤 6（确认报告）。

## 当前状态分析

已完成的步骤（1-4）：
- 约束文件、参考原型、融合 CSS 均已读取分析
- 融合 HTML 已生成并写入 `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\pages\menu-bestiary.html`（520 行）
- HTML 使用 FullHtmlReplaceHead 模式：`<head>` 仅含 meta + title，自定义 `<style>` 块放在 `<body>` 内

验证通过的检查项：
- `<html lang="zh-CN" class="dark">` — 第 2 行，符合要求
- `data-dom-id="back-hud"` — 第 16 行，在返回按钮上
- `data-dom-id="nav-martial-record"` — 第 44 行，在武林录导航项上
- 背景图 `bg-bestiary-illust.jpg` — 已确认存在于 `assets/` 目录，HTML 中以 `../assets/bg-bestiary-illust.jpg` 引用
- `colors_and_type.css` — 包含所有页面使用的组件类（ink-back-btn、ink-bar、ink-bar-fill、ink-splash、ink-tag、ink-tag-brand、ink-tag-vermilion、ink-divider、ink-vignette、ink-vertical-title 等）
- `fill-html-head.mjs` 脚本 — 已确认存在于指定路径

待执行的步骤：
- 步骤 5：运行 fill-html-head.mjs 脚本
- 步骤 6：确认输出并报告

## 实施步骤

### 步骤 5：运行 fill-html-head

执行以下命令（cwd = `c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui`）：

```
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\fill-html-head.mjs" "c:\Works\GitHubProjects\HundunWorld\hundun-yy-ui\colors_and_type.css" "pages/menu-bestiary.html" --replace-head --prefix=ink --theme=dark
```

此命令将：
- 读取 `colors_and_type.css` 的全部内容
- 将其注入到 `pages/menu-bestiary.html` 的 `<head>` 中，替换原有的最小化 `<head>`
- 生成 `<style id="theme-vars">` 块（含设计 Token、组件类）
- 注入 Tailwind v4 浏览器脚本和 Lucide 图标脚本
- 生成 `@theme inline` 桥接块（ink 前缀 → Tailwind 语义令牌）
- 预期输出：`Success: 1 file(s), Failed: 0`

### 步骤 6：确认与报告

确认 fill-html-head 输出 "Success: 1 file(s), Failed: 0"。

最终回复中报告：
1. HTML 文件行数（fill-html-head 后行数会增加，预计约 580-600 行）
2. fill-html-head 是否成功
3. `<html>` 元素是否为 `class="dark"`
4. `data-dom-id` 属性是否已放置（back-hud + nav-martial-record）
5. 是否有任何问题

## 假设与决策

- fill-html-head 的 `--replace-head` 模式会保留 `<html>` 标签原样（含 `lang="zh-CN"` 和 `class="dark"`），仅替换 `<head>` 内容
- fill-html-head 不会修改 `<body>` 内的内容（自定义 `<style>` 块和 HTML 结构保持不变）
- 与同项目的 `menu-quests.html` 页面（已成功应用 fill-html-head）保持一致的模式

## 验证步骤

1. 命令执行后检查输出是否包含 "Success: 1 file(s), Failed: 0"
2. 读取生成的 HTML 文件，确认 `<head>` 已被替换为包含 `<style id="theme-vars">`、Tailwind 脚本、Lucide 脚本的完整 head
3. 确认 `<html lang="zh-CN" class="dark">` 保持不变
4. 确认 `data-dom-id="back-hud"` 和 `data-dom-id="nav-martial-record"` 仍然存在
5. 确认 `<body>` 内的自定义 `<style>` 块和页面结构未被修改
