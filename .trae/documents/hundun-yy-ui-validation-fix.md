# 混沌世界 MMORPG UI 系统 — 校验收尾修复计划

## 摘要

前序会话已完成 UI 系统的全部主体工作（19 个 HTML 页面、粒子动效系统、3 张视觉资源、4 份 API 契约文档、UI 设计规范与架构规范）。最终校验脚本 `scan-design-directory.mjs` 报告 **19 个渲染阻断错误**（全部为 `semantic-token-fallback` 缺失）和 **23 个软警告**（其中 1 个为 `orchestration-summary.json` 缺少 `skillProvenance` 字段，其余 22 个为预存的色彩/圆角/阴影软警告，属用户明确视觉需求，不阻断渲染）。

本计划仅聚焦于阻断性错误的修复，使校验通过、画布可正常渲染。

## 当前状态分析

### 已完成（无需改动）
- 19 个 HTML 页面：内容完整，`<main>` 结构完好
- `colors_and_type.css`：青色 Token 已调亮（#7EAB9E），含 TraeWork 基础 Token + 自定义水墨 Token
- 粒子系统：`ink-particles.css` + `ink-particles.js` 已注入全部 19 个 HTML 的 `<head>` 末尾
- 3 张视觉资源 + `.design` 注册（image-006/007/008）
- 4 份 API 契约文档 + UI 设计规范 + 架构规范 + 编排摘要

### 阻断错误（19 个）
全部 19 个 HTML 文件的 `<head>` 中缺少 `<style id="semantic-token-fallback">` 块。

**根因**：页面使用 Tailwind 语义类（`bg-card` / `text-foreground` / `border-border`），这些类依赖 `@theme inline` 编译。若画布 iframe 的 Tailwind 浏览器运行时编译失败，页面会降级为黑白默认样式。`semantic-token-fallback` 块提供 CSS 变量回退定义，确保即使编译失败也能正确渲染。

**现有 head 结构**（以 `compass.html` 为例）：
```
行 7:     <style id="theme-vars"> ... </style>        ← TraeWork + 水墨 Token
行 1308:  <script src="...tailwind browser..."></script>
行 1309:  <script src="...lucide..."></script>
行 1310:  <style type="text/tailwindcss"> @theme inline { ... } </style>
行 1326:  <style> .no-scrollbar ... [data-icon] ... </style>
行 1342:  <link rel="stylesheet" href="../assets/css/ink-particles.css">
行 1343:  <script src="../assets/js/ink-particles.js" defer></script>
行 1344: </head>
```

**缺失位置**：`<style id="semantic-token-fallback">` 应插入在 `</style>`（tailwindcss 块结束，行 1325）与 `<style>`（no-scrollbar 块开始，行 1326）之间——这正是 `fill-html-head.mjs` 的 `buildHead()` 函数生成该块的位置。

### 软警告（1 个需修复）
`orchestration-summary.json` 缺少 `skillProvenance` 字段。扫描脚本期望：
```json
"skillProvenance": {
  "name": "solo-design",
  "version": "2026.07.06.8",
  "version_source": "skill-version.json",
  "runtime_skill_dir": "...",
  "recorded_at": "ISO-8601",
  "read_status": "ok"
}
```
版本来源：`c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\skill-version.json` → `2026.07.06.8`。

### 软警告（22 个预存，不修复）
- `colors_and_type.css` 含 secondary/accent 品牌变量（`--accent-teal` 等）—— 用户明确要求金色强调 + 灰色中性，属设计需求
- `colors_and_type.css` 含超大圆角 Token（`--radius-20/24/32`）—— 武侠水墨风格需要柔和圆角
- 阴影 alpha > 0.05 —— 沉浸式暗色主题需要深阴影营造层次感
- 19 个页面引用 secondary/accent 变量 —— 同上

这些警告在用户明确视觉需求下可接受，不阻断渲染。

## 修复方案

### 步骤 1：生成 semantic-token-fallback 参考块

**为什么不直接用 `--replace-head`**：`fill-html-head.mjs --replace-head` 会重建整个 `<head>`，虽然能添加 semantic-token-fallback，但会**丢失**手动注入的粒子 `<link>` 和 `<script>` 标签（脚本只保留自定义 `<style>` 块，不保留 `<link>`/`<script>`）。

**方法**：用 `fill-html-head.mjs` Mode 1（生成新骨架）在临时目录生成一个包含完整 head 的参考文件，从中提取 `<style id="semantic-token-fallback">...</style>` 块。

```powershell
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\fill-html-head.mjs" "c:\Works\GitHubProjects\HundunWorld\game-ui-system\colors_and_type.css" "c:\Users\33011\.trae-cn\work\6a5ce69cd75e7451510c6641\temp-skeleton.html" --title="Temp"
```

从生成的 `temp-skeleton.html` 中提取 `<style id="semantic-token-fallback">` 到 `</style>` 的完整块，存为参考字符串。

### 步骤 2：向 19 个 HTML 批量插入 semantic-token-fallback 块

对每个 HTML 文件，使用 `SearchReplace` 在以下锚点之间插入回退块：

**搜索锚点**（所有 19 个文件结构一致）：
```
    </style>
    <style>
      .no-scrollbar::-webkit-scrollbar { display: none; }
```

**替换为**：
```
    </style>
    <style id="semantic-token-fallback">
{从步骤 1 提取的回退 CSS 内容}
    </style>
    <style>
      .no-scrollbar::-webkit-scrollbar { display: none; }
```

**19 个文件列表**：
1. `achievement.html` 2. `character-panel.html` 3. `combat-hud-traditional.html` 4. `combat-hud.html` 5. `compass.html` 6. `crafting.html` 7. `dungeon-entry.html` 8. `equipment-enhance.html` 9. `friends.html` 10. `inventory.html` 11. `leaderboard.html` 12. `mail.html` 13. `mentor.html` 14. `mount-pet.html` 15. `quest-log.html` 16. `shop.html` 17. `skill-panel.html` 18. `social-guild.html` 19. `world-map.html`

**验证点**：插入后每个文件应包含 `id="semantic-token-fallback"` 且粒子 `<link>`/`<script>` 仍在原位。

### 步骤 3：更新 orchestration-summary.json

在 `orchestration-summary.json` 的 JSON 对象中添加 `skillProvenance` 字段（与 `project` 同级）：

```json
"skillProvenance": {
  "name": "solo-design",
  "version": "2026.07.06.8",
  "version_source": "skill-version.json",
  "runtime_skill_dir": "c:\\Users\\33011\\.trae-cn\\builtin\\design\\default\\skills\\solo-design",
  "recorded_at": "2026-07-19T16:30:00.000Z",
  "read_status": "ok"
}
```

### 步骤 4：重新运行校验

```powershell
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs" "c:\Works\GitHubProjects\HundunWorld\game-ui-system"
```

**期望结果**：`renderBlockingErrorCount: 0`，`softWarningCount: 22`（预存软警告保留）。`skillProvenance.read_status` 应为 `"ok"`。

### 步骤 5：画布预览抽检

通过本地 HTTP 服务器或直接打开文件，抽检 3 个关键页面确认渲染正常：
- `combat-hud.html`（核心战斗 HUD，粒子动效密集）
- `character-panel.html`（角色面板，语义 Token 使用密集）
- `dungeon-entry.html`（副本入口，data-particle 属性验证）

确认：水墨青色主题正确、金色强调色正常、粒子动效触发正常、无黑白降级样式。

## 假设与决策

| ID | 决策 | 理由 |
|---|---|---|
| D1 | 采用手动插入而非 `--replace-head` | `--replace-head` 会丢失粒子 `<link>`/`<script>` 注入；手动插入只添加缺失块，零副作用 |
| D2 | 用临时文件生成参考回退块 | `buildSemanticFallbackCSS` 的输出依赖 CSS 解析逻辑，直接运行脚本生成是最可靠的获取方式 |
| D3 | 保留 22 个预存软警告 | 用户明确要求金色强调色、灰色中性色、柔和圆角、深阴影——这些软警告是设计需求的有意体现 |
| D4 | skillProvenance 版本取自 `skill-version.json` | 扫描脚本以该文件为权威版本来源 |

## 验证步骤

1. 运行 `scan-design-directory.mjs`，确认 `renderBlockingErrorCount: 0`
2. 确认 `skillProvenance.read_status: "ok"` 且 `version: "2026.07.06.8"`
3. 在浏览器中打开 `compass.html`，确认 Tailwind 语义类（`bg-card`/`text-foreground`）正确渲染为水墨主题色，而非黑白
4. 在浏览器中打开 `combat-hud.html`，确认粒子动效仍正常加载（`ink-particles.css`/`js` 未丢失）
