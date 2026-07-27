# 耕地（Horizon.Game.GengDi）UI 系统重构计划

参照 `quant-trading-ui`（混沌AI量化交易系统）的暗色专业终端风格，重构 GengDi 全部 43 个 View 的 UI 系统。先出 HTML/CSS 原型（`.design` canvas 交付），用户逐批确认后再落地到 Avalonia XAML。

## 一、Summary 概述

把 Horizon.Game.GengDi 从现有"战网水平顶栏 + 水墨古风/鎏金/朱红"风格，整体重构为 quant-trading-ui 的"暗色专业终端 + 240px 左侧栏 + 56px 顶栏"风格。采用 solo-design 的 `complex_html_page` lane（P10），分 8 批产出 HTML 原型，每批独立确认，原型全量确认后再进入 Avalonia 落地阶段。

**成功标准**：
- 原型阶段：所有页面通过 `validate-design-workspace.mjs` 与 `validate-finish-readiness.mjs`；用户对每批 canvas 视觉确认通过。
- 落地阶段：每个 Avalonia View 的 token 全部取自统一 ResourceDictionary，无硬编码颜色；MainView 战网顶栏彻底替换为侧栏布局。

## 二、Current State Analysis 现状分析

### 2.1 参考设计 quant-trading-ui
- 路径：`C:\Works\GitHubProjects\HundunLive\quant-trading-ui`（只读参考，不在当前工作区）
- 风格：暗色专业终端，品牌前缀 `qt`
- 色板：品牌蓝 `#2962ff`，背景 `#0a0e17`，卡片 `#131722`，涨绿 `#26a69a` / 跌红 `#ef5350`
- 字体：`Inter` + `Noto Sans SC`（正文）/ `JetBrains Mono`（数值）
- 圆角：`4 / 8 / 12px`
- 布局：`240px` 左侧边栏 + `56px` 顶栏 + 主内容区
- 组件：`metric-card` / `data-table` / `badge` / `nav-item`（左侧 3px 蓝色指示条）/ `period-btn`
- 技术：Tailwind + Lucide 图标 + Chart.js
- 页面：dashboard / strategy / ai-analysis / backtest + project-shell partial

### 2.2 目标项目 Horizon.Game.GengDi
- 框架：Avalonia 11.3.12 + FluentAvalonia，MVVM 架构，类暴雪战网客户端
- 现有主题（`ThemeResources.xaml`）：深色 `#151826` 背景 + `#7AA9FF` 主色，已接近但未统一
- 现有 MainView：战网水平顶栏（Logo + 游戏/新闻/花卉市场/音乐/··· 水平导航 + 副导航条 18 项）
- View 清单：`Core/Views/` 42 个 + `Tools/ExcelProcessor/Views/` 1 个 = 43 个
- 现有水墨古风资产（`game-ui-system`、`hundun-yy-ui`、`gengdi-login-redesign`）与本次重构无关，本次采用纯交易系统风格

### 2.3 现有导航结构（需改造）
- 顶栏 `TitleBarHost`：Logo + 水平 TitleBarNavItem（游戏/新闻/花卉市场/音乐/···）+ 工具区（下载/通知/用户）
- 副导航 `SubNavBar`：18 个 SubNavItem 水平堆叠
- 底部：MiniPlayer
- 改造目标：水平顶栏 → 240px 左侧栏分组导航

## 三、Assumptions & Decisions 假设与决策

### 3.1 关键决策（建议默认值，Batch 0 启动前用户拍板）

| 决策项 | 选项 A（推荐） | 选项 B |
|---|---|---|
| 品牌前缀 | `gd`（GengDi） | `gengdi` |
| 品牌蓝 | `#2962ff`（与 quant 完全一致，零偏差） | `#7AA9FF`（现有接近色，更柔和） |
| 背景/卡片 | `#0a0e17` / `#131722`（quant 原版） | `#151826`（现有接近色） |
| 涨/跌色 | `#26a69a` / `#ef5350` | 自定义 |
| 暗色为默认 | 是（`html.dark` 主体验） | 否 |
| 图表方案（落地阶段） | LiveChartsCore | OxyPlot / ScottPlot |

下文以选项 A 为默认进行规划。

### 3.2 核心假设
- 用户"全部页面"指 43 个 View 全部产出原型
- 原型阶段用 HTML/CSS + Chart.js，不阻塞 Avalonia 落地
- 落地阶段才处理 Avalonia 原生图表替换
- 放弃所有水墨/鎏金/朱红 token，不保留品牌色融合

## 四、Proposed Changes 拟议变更

### 4.1 设计系统规范

#### 4.1.1 Token 映射表（qt → gd）

| quant token | GengDi token | 色值 | 语义 |
|---|---|---|---|
| `--qt-brand-500` | `--gd-brand-500` | `#2962ff` | 品牌主色 |
| `--qt-brand-50..900` | `--gd-brand-50..900` | 同 quant 色阶 | 品牌色阶 |
| `--qt-background` | `--gd-background` | `#0a0e17` | 应用背景 |
| `--qt-foreground` | `--gd-foreground` | `#e0e6ed` | 主文本 |
| `--qt-card` | `--gd-card` | `#131722` | 卡片背景 |
| `--qt-popover` | `--gd-popover` | `#1a1f2e` | 浮层/抽屉 |
| `--qt-muted` | `--gd-muted` | `#1a1f2e` | 次级背景 |
| `--qt-muted-foreground` | `--gd-muted-foreground` | `#787b86` | 次级文本 |
| `--qt-border` | `--gd-border` | `#2a2e39` | 边框 |
| `--qt-input` | `--gd-input` | `#2a2e39` | 输入框 |
| `--qt-state-success` | `--gd-state-success` | `#26a69a` | 成功/涨 |
| `--qt-state-warning` | `--gd-state-warning` | `#ff9800` | 警告 |
| `--qt-state-error` | `--gd-state-error` | `#ef5350` | 错误/跌 |
| `--qt-up` / `--qt-down` | `--gd-up` / `--gd-down` | `#26a69a` / `#ef5350` | 涨跌 |
| `--qt-radius-sm/md/lg` | `--gd-radius-*` | `4/8/12px` | 圆角 |
| `--qt-font-sans` | `--gd-font-sans` | `Inter,Noto Sans SC` | 无衬线 |
| `--qt-font-mono` | `--gd-font-mono` | `JetBrains Mono` | 等宽数值 |

#### 4.1.2 组件清单

| 组件 | CSS 类 | 用途 |
|---|---|---|
| 布局根 | `.app-root` / `.app-sidebar` / `.app-main-area` / `.app-header` / `.app-content` | 240+56 布局 |
| 侧栏导航项 | `.nav-item` + `[data-active]::before` | 左侧 3px 蓝色指示条 |
| 指标卡 | `.metric-card` | KPI 展示 |
| 数据表 | `.data-table` | 列表 |
| 徽标 | `.badge` + `-success/-warning/-error/-info` | 状态标签 |
| 数值 | `.num` | 等宽 tabular-nums |
| 涨跌文本 | `.text-up` / `.text-down` | 涨跌色 |
| 周期按钮 | `.period-btn` + `.active` | 切换组 |
| 区块标题 | `.section-title` | 卡片标题 |

#### 4.1.3 导航框架改造方案

**侧栏分组设计**（把现有 18 个扁平 SubNavItem 收敛为侧栏分组）：

```
侧栏
├─ [品牌] 耕地 Logo
├─ 主导航
│   ├─ 总览          (Home)              layout-dashboard
│   ├─ 游戏          (Games)             library
│   ├─ 新闻          (News)              newspaper
│   └─ 下载          (Downloads)         download
├─ 花卉市场（分组标题）
│   ├─ 行情仪表盘    (FlowerDashboard)   gauge
│   ├─ 市场          (FlowerShop)        shopping-cart
│   ├─ 种植建议      (FlowerPlantingAdvice) sprout
│   ├─ 购物车        (FlowerCart)        shopping-bag
│   ├─ 订单          (FlowerOrderCenter) file-text
│   ├─ 预警          (FlowerAlertCenter) bell-ring
│   ├─ AI助手        (FlowerAIAssistant) brain
│   ├─ 数据大屏      (FlowerDataScreen)  bar-chart-3
│   ├─ 商家          (FlowerMerchant)    store
│   ├─ 工作台        (FlowerWorkbench)   layout-grid
│   └─ 品种详情      (FlowerSpeciesDetail) leaf
├─ 音乐（分组标题）
│   ├─ 发现          (MusicDiscover)     compass
│   ├─ 歌单          (PlaylistManage)    list-music
│   └─ 搜索          (MusicSearch)       search
├─ [footer] 在线状态 + 用户头像
顶栏右侧：通知(角标) / 主题切换 / 用户
```

**关键改动**：
- 战网水平 TitleBarNavItem → 侧栏 nav-item（左侧蓝色指示条）
- SubNavBar 18 项水平条 → 删除，收敛进侧栏分组
- "更多"菜单(···)与用户下拉 → 侧栏 footer 用户区 + 顶栏工具按钮
- MiniPlayer 从底栏 → 主区底部固定条（保持）
- 抽屉覆盖层 OverlayHost 机制保留

### 4.2 分批策略（8 批）

| 批次 | 类型 | 页数 | 优先级 | 说明 |
|---|---|---|---|---|
| Batch 0 | 设计系统 + 框架壳 | 1 壳 + 1 组件页 | P0 | token + project-shell + 组件展示页 |
| Batch 1 | 核心骨架 + 认证 | 4 | P0 | MainView/MainWindow + Login/Register |
| Batch 2 | 基础导航页 | 5 | P0 | Home/Games/News/Notification/Downloads |
| Batch 3 | 花卉-数据/分析 | 5 | P0 | Dashboard/DataScreen/Alert/AI/Advice |
| Batch 4 | 花卉-市场交易 | 5 | P1 | Shop/ProductDetail/Cart/Order/Merchant |
| Batch 5 | 音乐模块 | 7 | P1 | Discover/Player/Search/Story×2/MiniPlayer/Playlist |
| Batch 6 | 用户中心 + 花卉管理 | 8 | P1/P2 | Profile/Security/Settings/Social + Flower 4页 |
| Batch 7 | 抽屉 + 对话框 + Excel | 9 | P2 | 6 抽屉 + 2 对话框/窗口 + Excel |

**依赖约束**：
- Batch 0 是所有后续批次的硬前置（token/shell/组件）
- Batch 1 的 MainView 是 Batch 2-7 的容器，必须先完成
- Batch 5 的 MiniPlayer 需与 Batch 1 MainView 底部布局对齐
- Batch 7 的抽屉依赖 MainView 的 OverlayHost 机制

### 4.3 每批页面清单

#### Batch 0 — 设计系统 + 框架壳（Main Agent 独立完成）
- `colors_and_type.css`（gd 前缀全量 token）
- `gengdi-trading-ui-redesign.design`（canvas 根）
- `partials/project-shell.html`（240+56 布局 + 分组导航 + 顶栏 + 内容插槽）
- `pages/_components.html`（全组件清单可视化）
- `generation-tree.json`

#### Batch 1 — 核心骨架 + 认证（4 页）
- `pages/main.html` ← MainView.axaml + MainWindow.axaml
- `pages/login.html` ← LoginView.axaml
- `pages/register.html` ← RegisterView.axaml

#### Batch 2 — 基础导航页（5 页）
- `pages/home.html` ← HomeView（对标 dashboard 仪表盘）
- `pages/games.html` ← GamesView（data-table + 卡片网格）
- `pages/news.html` ← NewsView
- `pages/notification.html` ← NotificationView
- `pages/downloads.html` ← DownloadsView

#### Batch 3 — 花卉-数据/分析（5 页，交易风格核心）
- `pages/flower-dashboard.html` ← FlowerDashboardView（K线/订单簿/持仓表/指标卡）
- `pages/flower-datascreen.html` ← FlowerDataScreenView（全屏指标墙）
- `pages/flower-alert.html` ← FlowerAlertCenterView
- `pages/flower-ai-assistant.html` ← FlowerAIAssistantView（对标 ai-analysis）
- `pages/flower-planting-advice.html` ← FlowerPlantingAdviceView

#### Batch 4 — 花卉-市场交易（5 页）
- `pages/flower-shop.html` ← FlowerShopView
- `pages/flower-product-detail.html` ← FlowerProductDetailView
- `pages/flower-cart.html` ← FlowerCartView
- `pages/flower-order.html` ← FlowerOrderCenterView
- `pages/flower-merchant.html` ← FlowerMerchantView

#### Batch 5 — 音乐模块（7 页）
- `pages/music-discover.html` ← MusicDiscoverView
- `pages/music-player.html` ← MusicPlayerView
- `pages/music-search.html` ← MusicSearchView
- `pages/music-story.html` ← MusicStoryView
- `pages/music-story-drawer.html` ← MusicStoryDrawerView（右侧抽屉）
- `pages/mini-player.html` ← MiniPlayerView（MainView 子件）
- `pages/playlist-manage.html` ← PlaylistManageView

#### Batch 6 — 用户中心 + 花卉管理（8 页）
- `pages/profile.html` ← ProfileView
- `pages/security.html` ← SecurityView
- `pages/settings.html` ← SettingsView
- `pages/social.html` ← SocialView
- `pages/flower-profile.html` ← FlowerProfileView
- `pages/flower-workbench.html` ← FlowerWorkbenchView
- `pages/flower-address.html` ← FlowerAddressView
- `pages/flower-species-detail.html` ← FlowerSpeciesDetailView

#### Batch 7 — 抽屉 + 对话框 + Excel（9 页）
- `pages/weather-detail-drawer.html` ← WeatherDetailDrawerView
- `pages/air-quality-drawer.html` ← AirQualityDrawerView
- `pages/hourly-forecast-drawer.html` ← HourlyForecastDrawerView
- `pages/life-index-drawer.html` ← LifeIndexDrawerView
- `pages/weather-news-drawer.html` ← WeatherNewsDrawerView
- `pages/dish-detail-drawer.html` ← DishDetailDrawerView
- `pages/forward-message-dialog.html` ← ForwardMessageDialog
- `pages/avatar-crop.html` ← AvatarCropWindow
- `pages/excel-processor.html` ← ExcelProcessorView

> 抽屉类统一用右侧滑入覆盖层模式，共享 `partials/drawer-shell.html`。

### 4.4 HTML 原型目录结构

根目录：`c:\Works\GitHubProjects\HundunWorld\gengdi-trading-ui-redesign\`

```
gengdi-trading-ui-redesign/
├── gengdi-trading-ui-redesign.design      # canvas 根（Main Agent 独占）
├── colors_and_type.css                    # gd token（全量 dark/light）
├── generation-tree.json                   # 生成树
├── runtime-dispatch-manifest.json         # 分派清单
├── runtime-orchestration-summary.json     # 编排汇总
├── validation-report.json                 # 验证结果
├── partials/
│   ├── project-shell.html                 # 240+56 框架壳
│   └── drawer-shell.html                  # 抽屉共享壳
├── pages/
│   ├── _components.html                   # 组件展示页
│   ├── main.html ... login.html ...       # 各业务页
│   └── weather-detail-drawer.html ... excel-processor.html
└── assets/
    ├── icons/                             # 自定义 SVG
    └── img/                               # 业务图片占位
```

**约束**：
- 每个 `pages/*.html` head 由 `apply-html-head-contract.mjs` 注入（`--title="页面名" --lang="zh-CN" --prefix="gd"`，图表页加 `--charts`）
- Page Sub-Agent 禁止写 `.design`、`runtime-*.json`、`validation-report.json`
- 每页完成后跑 `record-dispatch-completion.mjs`

### 4.5 后续落地到 Avalonia XAML 的映射策略（原型确认后执行）

#### Token 层
- `colors_and_type.css` 的 `--gd-*` → `Assets/Styles/Resources.axaml` 的 `ThemeDictionaries`（Default/Dark/HighContrast）
- 废弃：`BattleNetHeroBackgroundBrush`、`BattleNetPrimaryBrush=#FF5C7A3A`（军绿）、`LoginTitleGradientBrush`、`TitleBarBackgroundBrush`、`SubNavBarBackgroundBrush`、所有 `LoginInfoCard*Brush`/`LoginInput*Brush`
- 新增：`GdBrand500/600` / `GdBackground` / `GdCard` / `GdPopover` / `GdMuted` / `GdBorder` / `GdState*` / `GdUp` / `GdDown` / `GdRadius*` / `GdFontSans` / `GdFontMono`

#### 布局层
| HTML | Avalonia XAML |
|---|---|
| `.app-root` flex | `<Grid ColumnDefinitions="240,*">` |
| `.app-sidebar` 240 | `<Border Width="240" Classes="gd-sidebar">` |
| `.nav-item` + `::before` | `<Button Classes="gd-nav-item">` + 左 3px Border active 态 |
| `.app-header` 56h | `<Grid Height="56" Classes="gd-header">` |
| Tailwind `grid-cols-4` | `<Grid ColumnDefinitions="1*,1*,1*,1*">` |

#### 组件层
| HTML 组件 | Avalonia 实现 |
|---|---|
| `.metric-card` | `<Border Class="gd-metric-card">` |
| `.data-table` | `<DataGrid Class="gd-data-table">` 或手写 Grid |
| `.badge` 变体 | `<Border Class="gd-badge gd-badge-success">` |
| `.num` 等宽 | `<TextBlock FontFamily="{StaticResource GdFontMono}" FontFeatures="tnum"/>` |
| Lucide `data-lucide` | FluentAvalonia `SymbolIconSource` 或 `PathIcon` SVG |
| Chart.js canvas | LiveChartsCore `CartesianChart` / `CandlesticksSeries` |

#### MainView 落地步骤
1. 备份现 `MainView.axaml`
2. `Grid RowDefinitions="Auto,*,Auto"` → `Grid ColumnDefinitions="240,*"`
3. 左侧新建 `SidebarView`（品牌区 + 分组导航 + footer 用户区）
4. 右上新建 `HeaderView`（56h，页面标题 + 通知/主题/用户）
5. 右下保留 `TransitioningContentControl` + `OverlayHost`
6. 删除 `TitleBarHost`、`SubNavBar` 整块
7. MiniPlayer → 主区底部 `Grid RowDefinitions="*,Auto"`
8. `MainWindow.axaml` 配色对齐 `GdBackground`

## 五、Verification Steps 验证步骤

### 5.1 每批结束三段式验证

**段 1 — 工作区结构验证**（Main Agent）：
```
node {SKILL_DIR}/shared-runtime/deterministic-tooling/validate-design-workspace.mjs "c:\Works\GitHubProjects\HundunWorld\gengdi-trading-ui-redesign"
```
校验：`.design` 存在、`colors_and_type.css` 存在、`project-shell.html` 存在、`generation-tree.json` 节点与 pages 一致、各页 head 已注入。

**段 2 — 完成就绪验证**（Main Agent）：
```
node {SKILL_DIR}/shared-runtime/deterministic-tooling/validate-finish-readiness.mjs "c:\Works\GitHubProjects\HundunWorld\gengdi-trading-ui-redesign" --check=all
```
校验：所有已规划 page-leaf 节点 `status=completed`、`record-dispatch-completion` 均已登记、无遗留 forbidden write。

**段 3 — 用户视觉确认门**：
- 用 integrated_browser MCP 逐页截图汇总
- 用户对每批输出"确认通过 / 需返工"
- 返工点：颜色偏差、组件类未对齐、导航分组不合理、token 硬编码
- **未通过不进下一批**；通过后该批 canvas 节点冻结

### 5.2 全量收尾验证（Batch 7 后）
- 跑全量 `validate-design-workspace` + `validate-finish-readiness`
- 输出 `finish-readiness-report.json`，43 页全部 `completed`
- 用户整体一致性最终确认 → 进入 Avalonia 落地阶段

### 5.3 升级/停止条件
- 某批验证连续 2 次未过且属 token 缺陷 → 回 Batch 0 修订设计系统
- 用户对核心风格（Batch 0/1）不满意 → 停止后续批次，重定 token 与 shell

## 六、关键风险与对策

| 风险 | 对策 |
|---|---|
| 18 项副导航收敛为侧栏分组后页面入口变深 | 顶栏保留快速操作区，常用页放主导航顶层 |
| 花卉模块语义与"交易"风格不完全契合 | 复用 metric-card/data-table 容器，语义文案保留花卉/种植，涨跌色用于价格波动 |
| 抽屉/对话框数量多（Batch 7 共 9 件） | 共享 `DrawerShell` 组件，各抽屉只填内容区 |
| 落地阶段 Chart.js 无对应 | 原型不阻塞；落地选 LiveChartsCore，先做 1 个图表 View 验证再推广 |
| FluentAvalonia 主题与 gd token 冲突 | `CustomAccentColor` 指向 `#2962ff`，覆盖 `Resources.axaml` 的 `ThemeDictionaries` 收敛 |
