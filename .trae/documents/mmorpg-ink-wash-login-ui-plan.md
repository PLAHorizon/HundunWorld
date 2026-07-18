# 混沌世界 MMORPG 登录界面 — 实现计划

## 摘要

为融合武侠、仙侠、玄幻三类题材的 MMORPG 游戏客户端设计一套水墨写意风格的登录界面。用户已确认：视觉风格为水墨写意融合（水墨笔触基底 + 仙侠云雾 + 玄幻光效，留白克制），功能范围为仅登录核心（单屏完成），主色调为青灰 + 水墨青。采用 Solo Design 技能 `create-project.md` 工作流，Free Explore 模式产出 `.design` 画布项目。

## 当前状态分析

### 运行环境

- 工作区：`c:\Works\GitHubProjects\HundunWorld`（已存在游戏项目代码）
- Solo Design 技能路径：`c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design`
- 参考设计库：TRAE Work（`c:\Users\33011\.trae-cn\design_libraries\dl_builtin_trae_work`）

### 设计库冲突判定

TRAE Work 是代码编辑器风格设计系统（Light 模式专用、中性色表面、IDE 组件 `.ds-btn`/`.ds-input` 等），与水墨仙侠游戏登录界面存在根本性风格冲突。判定为 **Free Explore 模式**，由用户明确选择的水墨风格方向主导设计，`colors_and_type.css` 自行生成。

### 设备与模式

- 设备类型：`desktop`（PC 游戏客户端）
- operatingMode：`free-explore`
- 响应式：默认 desktop + tablet + mobile（技能硬性默认）

## 设计定位

| 维度 | 决策 |
|------|------|
| Purpose | 武侠/仙侠/玄幻三题材融合 MMORPG 登录入口 |
| Tone | 清冷飘逸 / 文人写意 — 水墨留白的克制感 + 仙侠云雾的空灵 + 玄幻灵光的幽微 |
| Differentiation | 水墨笔触肌理 + 毛玻璃表单面板悬浮于云雾山水之上 + 书法体游戏标题配印章 |

**designDials**：`layoutVariance: 4`（非对称分栏）/ `motionIntensity: 2`（云雾缓动）/ `visualDensity: 2`（大量留白）

## 项目结构

```
c:\Works\GitHubProjects\HundunWorld\hundun-login\
├── hundun-login.design           # 画布入口文件
├── generation-tree.json           # 生成依赖树
├── orchestration-summary.json     # 运行时上下文摘要
├── colors_and_type.css            # 品牌CSS（水墨青灰主题）
├── assets/
│   └── login-bg.jpg               # 水墨山水云雾背景图
├── partials/
│   └── project-shell.html         # 共享外壳片段
└── pages/
    └── login.html                 # 登录页
```

### 生成树

```
gen-project-shell (partials/project-shell.html) — 先生成
  共享区域：全屏背景容器、水墨氛围遮罩、品牌标题区、辅助层骨架
  可变槽位：<!-- SLOT: pageTitle -->, <!-- SLOT: pageContent -->
  └── gen-page-login (pages/login.html) — 外壳确认后生成
        私有区域：登录表单卡片、记住密码/自动登录、注册找回入口
```

调度纪律：外壳与登录页为父子依赖，不可同批调度。外壳返回 + 文件确认 + 树状态更新后，方可调度登录页。

## 品牌 CSS 设计方向

### 色彩策略：单色相 + tint scale

- **品牌主色相 = 水墨青绿**（seedColor: `#4A7C7E`）— 用于登录按钮、输入聚焦、链接、激活态、玄幻灵光点缀
- **中性系统 = 青灰 tint scale** — 用于背景、表面、边框、正文文字（中性 tint 不计作品牌色相）

### 颜色 Token

**水墨青绿（品牌主色相）**

| Token | HEX | 用途 |
|-------|-----|------|
| `--hd-primary-50` | `#EEF4F4` | 最浅 tint，灵光底色 |
| `--hd-primary-100` | `#D6E5E5` | 浅 tint，聚焦辉光 |
| `--hd-primary-200` | `#ADCCCC` | 边框聚焦态 |
| `--hd-primary-300` | `#84B3B4` | 次要交互 |
| `--hd-primary-400` | `#5F9496` | hover 态 |
| `--hd-primary-500` | `#4A7C7E` | 核心色：登录按钮、激活态 |
| `--hd-primary-600` | `#3A6567` | 按钮 hover 加深 |
| `--hd-primary-700` | `#2D4E50` | 按下态 |
| `--hd-primary-800` | `#1F3839` | 深色强调 |
| `--hd-primary-900` | `#122324` | 最深 tint |

**青灰中性系统**

| Token | HEX | 用途 |
|-------|-----|------|
| `--hd-ink-900` | `#1C2128` | 标题文字、最深层 |
| `--hd-ink-800` | `#2A323D` | 主标题 |
| `--hd-ink-700` | `#3A4654` | 正文文字 |
| `--hd-ink-600` | `#4A5664` | 次要文字 |
| `--hd-ink-500` | `#5C6878` | 辅助文字 |
| `--hd-ink-400` | `#7A8593` | 占位符 |
| `--hd-ink-300` | `#A8B0BC` | 禁用态 |
| `--hd-ink-200` | `#CDD3DB` | 边框 |
| `--hd-ink-100` | `#E5E9ED` | 分割线、浅边框 |
| `--hd-ink-50` | `#F2F4F6` | 表面浅底 |
| `--hd-paper` | `#F5F6F4` | 页面基底（微冷白） |

**状态色（仅语义状态）**：`--state-error: #B85450`（赭红）/ `--state-success: #5E8B5E`（苍绿）/ `--state-warning: #C49B5E`（赭石）/ `--state-info: #4A7C7E`

### 字体系统

一个 CJK 衬线标题族 + 一个无衬线正文族（Free Explore 排版策略合规）：

| 角色 | 字体栈 | 用途 |
|------|--------|------|
| 标题/展示 | `"Noto Serif SC", "Source Han Serif SC", "SimSun", serif` | 游戏名"混沌世界"、区块标题（宽字距 0.15em 书法舒朗感） |
| 正文/UI | `"Noto Sans SC", "PingFang SC", system-ui, sans-serif` | 表单标签、输入、按钮、辅助信息 |

字号阶梯：`.hd-display` 48px/56px/700（游戏名）/ `.hd-heading-lg` 28px/36px/600 / `.hd-body-lg` 16px/26px/400 / `.hd-body` 14px/22px/400 / `.hd-body-sm` 12px/18px/400

### 圆角策略

`radiusMax = 16`（Free Explore 默认上限），取中低区间：`--hd-radius-sm: 4px`（输入框）/ `--hd-radius-md: 8px`（按钮）/ `--hd-radius-lg: 12px`（表单面板）/ `--hd-radius-full: 9999px`（圆点）。不生成 xl/2xl/3xl。

### 阴影策略

静态表面（卡片、输入、按钮）优先边框 + 表面分层，阴影 alpha ≤ 0.05。仅表单毛玻璃浮层允许 `backdrop-filter: blur(20px)` + `background: rgba(245,246,244,0.72)` + 微阴影 `0 8px 40px rgba(28,33,40,0.12)`。

## 页面布局规划

### 整体构图：60/40 非对称分栏

避免"居中表单卡片"通用模板（Anti-Convergence），强化飘逸写意的入境感。画布 1920×1080。

```
┌──────────────────────────────────────────────────────────┐
│                    背景层 (login-bg.jpg)                    │
│   水墨山水 + 云雾 + 顶部渐淡 + 底部墨沉 + 玄幻灵光微闪        │
│                                                          │
│  ┌────────────────────────┐    ┌──────────────────────┐  │
│  │    品牌层 (左 60%)       │    │   表单层 (右 40%)     │  │
│  │                        │    │                      │  │
│  │   [印章]                │    │  ┌────────────────┐  │  │
│  │   混 沌 世 界            │    │  │ 欢迎语 / "登录" │  │  │
│  │   (书法体 display)       │    │  │ [账号输入框]    │  │  │
│  │   ── 水墨笔触分隔 ──     │    │  │ [密码输入框] 👁 │  │  │
│  │   "一花一世界            │    │  │ ☐记住密码 ☐自动登录│  │
│  │    一叶一菩提"           │    │  │ [  进 入 江 湖  ]│  │  │
│  │   [玄幻灵光粒子]         │    │  │ 注册账号  找回密码│  │  │
│  └────────────────────────┘    └──────────────────────┘  │
│                                                          │
│  ● 服务器: 烽火连城(流畅)        v1.0.0        ©混沌世界   │
└──────────────────────────────────────────────────────────┘
```

### 四层分区

**第一层 — 背景层**（z-index: 0）：全屏 `login-bg.jpg` + `linear-gradient` 遮罩（顶部云雾淡 → 底部墨色沉）+ 2-3 个 `radial-gradient` 水墨青绿微光斑（CSS `@keyframes` 8-12s 呼吸）+ 半透明云雾层极慢漂移（20s）

**第二层 — 品牌层**（z-index: 10，左 60%）：印章（`--hd-primary-700` 底 + 反白篆字"混沌"）/ 游戏名"混沌世界"（`.hd-display` 宽字距）/ 水墨分隔笔触（SVG/CSS 渐变不规则边缘）/ 引导诗句 `"一花一世界 · 一叶一菩提"` / 玄幻灵光粒子

**第三层 — 表单层**（z-index: 20，右 40%，毛玻璃浮层）：

| 元素 | 规格 | 交互 |
|------|------|------|
| 欢迎语 | "欢迎归来" `.hd-heading-lg` | — |
| 账号输入 | 底线样式，placeholder "请输入账号" | 聚焦底线变 `--hd-primary-400` + 微辉光 |
| 密码输入 | 底线样式，右侧眼睛图标切换显隐 | 同上 |
| 记住密码 | 自定义复选框（圆点），与自动登录并排 | 勾选填充 `--hd-primary-500` |
| 自动登录 | 同上 | 同上 |
| 登录按钮 | 全宽，宽字距"进 入 江 湖"，`--hd-primary-500` 实色，`--hd-radius-md` | hover → 600；active → 700；微缩放 0.98 |
| 注册账号 | 文字链接，`--hd-primary-600` | hover 下划线浮现 |
| 找回密码 | 文字链接，与注册分列两端 | 同上 |

**第四层 — 辅助层**（z-index: 10，底部通栏）：服务器状态（`● 服务器：烽火连城 · 流畅`）/ 版本号 `v1.0.0` / 版权 `© 混沌世界`

### data-dom-id 规划

- `btn-login` — 登录按钮（主 CTA）
- `link-register` — 注册入口（`hideEdge: true`）
- `link-forgot` — 找回密码入口（`hideEdge: true`）
- `toggle-remember` / `toggle-auto-login` / `toggle-password` — 页内态，不布线

### 布线计划

单页项目，可见布线数 0（无后续页面），符合"总可见布线 ≤ 页数"约束。注册/找回为 `hideEdge` 入口保留语义，未来扩展可指向新页。

## 图片素材规划

### 素材清单

| 文件名 | 角色 | 区段 | 用途 |
|--------|------|------|------|
| `login-bg.jpg` | `critical-hero` | `background` | 全屏水墨山水云雾背景 |

### 背景图生成提示词

```
Chinese ink wash painting (水墨写意), misty mountains and floating clouds landscape,
xianxia ethereal atmosphere with subtle spirit light wisps, traditional shanshui
composition with abundant negative space (留白), blue-gray slate tone palette with
muted teal-blue-green mineral pigment accents (青灰 + 水墨青绿), rice paper texture
base, ink brush stroke textures, distant peaks fading into mist, no characters,
no text, no typography, no letters, no words, atmospheric depth, cool ethereal mood,
high quality, cinematic wide landscape
```

横版 16:9，无文字/无字母/无水印，青灰色调主导 + 青绿点缀，大量留白。

### 降级策略

若生成失败，使用 CSS 多层 `radial-gradient` + `linear-gradient` 模拟水墨青灰山水氛围。GenerateImage 成功后不重生成/不后处理。

## 实现步骤

### 步骤 1 — 确认设计风格

- 跳过风格询问（用户已明确指定水墨方向 + 青灰色调）
- 生成完整 `colors_and_type.css`（上述全部 token），brandPrefix 定为 `hd`
- 遵守 Free Explore 四项 blocking 策略：单色相 tint、圆角 ≤16、单一字体系统、静态阴影 alpha ≤0.05

### 步骤 2 — 准备设计项目

- 创建目录结构：`hundun-login/assets/`、`hundun-login/pages/`、`hundun-login/partials/`
- 写入 `colors_and_type.css`
- 写入 `hundun-login.design`，含 1 个 page 骨架节点（`id: "page-login"`、`htmlSrc: "pages/login.html"`、`deviceType: "desktop"`、`designLibrary: null`）

### 步骤 3 — 编排摘要

- 写入 `orchestration-summary.json`：project 上下文、designSource（`operatingMode: "free-explore"`）、pages[0] 计划、assets 计划、wiringPlan、styleContinuityAnchors

### 步骤 4 — 准备页面素材

- 派发 1 个图片生成 Sub-Agent，输出 `assets/login-bg.jpg`
- 生成成功后 Main Agent 在 `.design` 追加 `image-001` 节点

### 步骤 5 — 设计页面

**5a — 生成共享外壳片段**（先调度）：生成 `partials/project-shell.html`，含全屏背景容器、品牌标题区骨架、辅助层骨架、全局水墨氛围，暴露 `<!-- SLOT: pageTitle -->` 和 `<!-- SLOT: pageContent -->` 槽位

**5b — 生成登录页叶节点**（外壳确认后调度）：生成 `pages/login.html`，装配外壳 + 填充表单面板，传入 free-explore 连续性锚点展开块、`pageType: "task-driven"`、compositionPattern（非对称 60/40）、可见布线表 + 隐藏交互表

### 步骤 6 — 配置页面导航

- 汇总 domId 列表，校验设计意图/动效证据
- 注册 interactions（单页项目：注册/找回为 `hideEdge: true` 但无目标页）
- 自检：每页可见出口 ≤ 2，总可见布线 ≤ 页数

### 步骤 7 — 最终检查

执行验证脚本：
```bash
node "c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs" "c:\Works\GitHubProjects\HundunWorld\hundun-login" --expected-pages=1
```
退出码 1 时按修复流程处理，最多 3 轮。

### 步骤 8 — 完成预览

输出页面摘要表，不含 Markdown 链接/裸路径，依赖宿主渲染的画布条目。

## 验证方法

### 技能验证脚本

`scan-design-directory.mjs` 一次性完整验证，覆盖：JSON 合法性、`data` 非空数组、节点必填字段完整、ID 唯一、HTML 文件存在、图片文件存在、image 节点已注册、页面数匹配。

### 人工视觉验证

| 检查项 | 合格标准 |
|--------|---------|
| 水墨意境 | 背景图呈现青灰山水云雾，有留白 |
| 三题材融合 | 水墨笔触（武侠）+ 云雾空灵（仙侠）+ 微光灵气（玄幻）三者可辨 |
| 主色调 | 青灰冷调为主，水墨青绿仅用于按钮/聚焦/链接点缀 |
| 单屏完成 | 全部功能在一屏内，无滚动 |
| 精美简洁 | 毛玻璃面板精致，留白克制，无冗余装饰 |
| 字体气质 | 游戏名宋体宽字距有书法舒朗感 |
| 圆角克制 | 卡片 ≤12px，输入 ≤4px |
| 交互反馈 | 输入聚焦/按钮 hover/复选勾选均有水墨青绿反馈 |
| 响应式 | desktop 为主，tablet/mobile 断点下表单面板居中适配 |

### Free Explore 策略合规自检

| 策略 | 合规验证 |
|------|---------|
| 单色相 | 仅 `--hd-primary-*` 一个色相 tint，无 secondary/accent |
| 圆角上限 | 最大 `--hd-radius-lg: 12px` ≤ 16 |
| 字体系统 | 仅 1 个 CJK 衬线标题族 + 1 个无衬线正文族 |
| 静态阴影 | 静态表面阴影 alpha ≤ 0.05；仅表单浮层允许更深阴影 |

## 假设与决策

1. **Free Explore 模式**：TRAE Work 设计库（代码编辑器风格）与水墨仙侠游戏风格根本冲突，切换为 Free Explore 模式由用户明确方向主导
2. **单色相合规**：以水墨青绿为唯一品牌色相（CTA/交互），青灰归入中性 tint 系统（非品牌色相），既满足 Free Explore 单色相策略又还原用户"青灰+青绿"双调诉求
3. **60/40 非对称分栏**：避免居中表单通用模板，强化飘逸写意入境感
4. **单页项目**：用户明确"仅登录核心、单屏完成"，不扩展注册/选服/公告页
5. **书法字体近似**：CDN 加载 Noto Serif SC + 宽字距 + 重字重模拟书法舒朗；印章用 CSS 方块反白篆字近似
6. **毛玻璃兼容性**：画布预览基于 Chromium 原生支持 `backdrop-filter`，同时提供实色 fallback

## 关键文件路径

- 设计项目根目录：`c:\Works\GitHubProjects\HundunWorld\hundun-login\`
- 画布入口：`c:\Works\GitHubProjects\HundunWorld\hundun-login\hundun-login.design`
- 品牌 CSS：`c:\Works\GitHubProjects\HundunWorld\hundun-login\colors_and_type.css`
- 登录页：`c:\Works\GitHubProjects\HundunWorld\hundun-login\pages\login.html`
- 背景素材：`c:\Works\GitHubProjects\HundunWorld\hundun-login\assets\login-bg.jpg`
- 外壳片段：`c:\Works\GitHubProjects\HundunWorld\hundun-login\partials\project-shell.html`
- Solo Design 技能根：`c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design`
- 验证脚本：`c:\Users\33011\.trae-cn\builtin\design\default\skills\solo-design\script\scan-design-directory.mjs`
