# 耕地 UI 视觉系统落地实施计划

## 一、概述

将 Batch 0 已规划的交易系统暗色专业风（gd 前缀 token，品牌蓝 `#2962ff`，背景 `#0a0e17`，涨绿跌红）从 HTML 原型落地到 Avalonia 项目（Horizon.Game.GengDi）。采用两阶段串行：阶段一在 HTML 原型中覆盖所有页面类型作为视觉确认，阶段二落地到 Avalonia XAML 代码。

- HTML 原型目录：`c:\Works\GitHubProjects\HundunWorld\gengdi-trading-ui-redesign\`
- Avalonia 项目目录：`c:\Works\GitHubProjects\HundunWorld\Horizon.Game.GengDi\`
- solo-design lane：`existing_edit_add_comparison`，canvas-first，每 HTML 页须在 `.design` 注册

## 二、现状分析

### 2.1 HTML 原型现状（Batch 0 已完成）
- 设计 token：`colors_and_type.css`（gd 前缀，品牌蓝 9 级色阶，4 类状态色，涨跌色，圆角 4/8/12px，Inter+Noto Sans SC / JetBrains Mono）
- 框架壳：`partials/project-shell.html`（水平顶栏 56px + 副导航 44px + 内容区，含 Tailwind v4 / Lucide / Chart.js）
- 已注册画布节点：`page-components`（组件展示）、`page-home-preview`（主界面预览）
- 验证状态：`validate-design-workspace` + `validate-finish-readiness` 均通过

### 2.2 Avalonia 项目现状（2026-07-27 实地探查确认）
当前视觉风格：暴雪战网暗色主题（Bnet 前缀，蓝色 `#7AA9FF`/`#00AEFF`，背景 `#141826`）。

项目根：`Horizon.Game.GengDi/`，视图位于 `Core/Views/`，控件位于 `Core/Controls/`。

样式文件分层（5 个）：
- `Assets/Styles/ThemeResources.xaml` — 旧版颜色（基本废弃，PrimaryColor `#7AA9FF`）
- `Assets/Styles/Styles.xaml` — 战网 Bnet 前缀（当前主用，含 BnetBlue/BnetDarkBg 等 + Button.BnetPrimary/.BnetInstall/.HeaderTab/.HeaderIconBtn/.HeaderPromo/.NavigationButton + Border.Card + TextBlock.Title/.Subtitle/.SectionHeader）
- `Assets/Styles/Resources.axaml` — ThemeDictionaries（Default/Dark），大量 BattleNet*/Login*/Social*/TitleBar*/SubNav* 前缀画刷 + LinearGradientBrush（Hero/PageHeader/TitleBar/SocialSidebar/LoginTitle 等）
- `Assets/Styles/ControlsGalleryStyles.axaml` — 控件样式模板（NavigationView.HorizonAppNav、Border.SurfaceCard/.SurfaceCard.subtle/.ListItemCard/.SurfaceCard.QuickAccess、TextBlock.PageEyebrow/.SectionTitle/.SupportingText、Button.PrimaryAction/.QuietAction/.TitleBarBrand/.TitleBarNavItem/.TitleBarIconButton/.TitleBarUserButton/.SubNavItem、controls.GalleryPage 控件模板含 PageHeader 渐变 88px + 104x104 图标 + 34px 大标题，双模板 IsPageScrollingEnabled True/False）
- `Assets/Styles/GdTheme.axaml` — **已新建**（gd token + 别名层，Dark/Default 双主题，已完成）

页面清单（41 视图 + 5 控件，实地确认）：
- 主框架（2）：MainWindow、MainView — `Core/Views/`
- 主页面（11）：Home、Games、News、Downloads、Settings、Security、Profile、Notification、Social、Login、Register — `Core/Views/`
- 花卉市场（14）：FlowerDashboard、FlowerShop、FlowerPlantingAdvice、FlowerCart、FlowerOrderCenter、FlowerAlertCenter、FlowerAIAssistant、FlowerDataScreen、FlowerMerchant、FlowerWorkbench、FlowerSpeciesDetail、FlowerProductDetail、FlowerAddress、FlowerProfile — `Core/Views/`
- 音乐（6）：MusicPlayer、MusicDiscover、MusicSearch、PlaylistManage、MusicStory、MiniPlayer — `Core/Views/`
- 抽屉页（7）：HourlyForecast、WeatherNews、WeatherDetail、LifeIndex、AirQuality、DishDetail、MusicStory — `Core/Views/`（*DrawerView.axaml）
- 弹窗（2）：ForwardMessageDialog（Window）、AvatarCropWindow（Window）— `Core/Views/`
- 自定义控件（5）：WeatherMapView、WeatherIconControl、VideoMessageCard、ToastContainer、LogisticsMapView — `Core/Controls/`
- 工具（1）：ExcelProcessorView — `Tools/ExcelProcessor/Views/`

App.axaml 当前加载顺序（待改）：
```
FluentAvaloniaTheme → Styles.xaml → ControlsGalleryStyles.axaml
```
Resources.axaml 已在 Application.Resources.MergedDictionaries 加载。GdTheme.axaml 和 GdControls.axaml 尚未加载。

关键复杂度：页面大量 `DynamicResource` 引用 BattleNet/Login/Social/TitleBar 前缀画刷；大量内联硬编码颜色（`#FFFFFF88`、`#1A7AA9FF` 等）；GalleryPage 控件模板（104 图标+渐变头）与新水平顶栏终端风格冲突；Styles.xaml 中 Bnet 前缀 Color 与 Resources.axaml 中 BattleNet 前缀 Brush 并存。

## 三、关键决策

### D1：两阶段串行，批次编号对齐
阶段一按"页面类型"分批产出 HTML 范式（不要求 41 页各做一个 HTML，每批 1-2 个 HTML 承载同类型多页范式与变体）。阶段二批次编号对齐，保证视觉确认与代码落地一一对应。

### D2：别名层 + 渐进迁移双轨兼容（核心风险控制）
不采用一次性全量改 41 页（风险不可控），也不采用只改资源不改页面（遗留技术债）。两步走：
1. **别名层先行**：新建 `GdTheme.axaml`，定义 gd token，并把所有旧键（BnetBlue、BattleNetHeroBackgroundBrush、TitleBarBackgroundBrush、LoginFormBackgroundBrush 等）重新声明为指向 gd token 的别名。完成后全 App 一夜切换新风格，页面代码不动。
2. **逐批迁移**：每批把该批页面旧键引用与内联硬编码颜色替换为 gd token，消除别名依赖。

### D3：GalleryPage 控件模板重写
当前 GalleryPage 模板（PageHeader 渐变 + 104x104 图标 + 34px 大标题）与新水平顶栏终端风格冲突。重写为轻量页面头（标题 + 副标题 + 右侧操作区，背景 GdBackground，去渐变与大图标）。业务页面可选保留 GalleryPage 或改纯 Grid。

### D4：内联硬编码颜色统一治理
建立 gd token 的 alpha 变体资源（如 `GdForegroundMutedBrush` = #E0E6ED at 60%、`GdBorderSubtleBrush` = #2A2E39 at 50%、`GdOverlayBrush` = #80000000），覆盖页面常见 `#FFFFFF88`/`#FFFFFFBB`/`#1A7AA9FF` 模式。每批迁移时 grep 强制清零内联颜色。

### D5：FluentAvalonia NavigationView 保留不扩展
主框架已用自定义 Button 实现水平导航。NavigationView.HorizonAppNav 样式经别名层自动换色，但不新增用法。落地前 grep 排查残留使用。

## 四、阶段一：HTML 原型分批计划（6 批）

每批目标：产出类型视觉范式 HTML，在 `.design` 注册画布节点，作为阶段二视觉验收基线。所有新 HTML 放 `gengdi-trading-ui-redesign/pages/`，复用 `partials/project-shell.html` 框架壳。

### Batch 1 — 覆盖层·弹窗与 Toast
- 覆盖：ForwardMessageDialog、AvatarCropWindow、ToastContainer
- 新增：`pages/overlays-dialogs.html`
- 内容：模态对话框范式（遮罩 `rgba(0,0,0,0.5)` + 居中卡 + 标题/搜索/列表/操作区，圆角 12px）、独立工具窗口范式（自带标题栏 + 裁剪预览 + 操作区）、Toast 通知范式（4 类状态色 + 位置 + 自动消失）
- 验收：按钮用 gd-primary，Toast 用 gd-state-* surface

### Batch 2 — 覆盖层·抽屉
- 覆盖：7 个 DrawerView
- 新增：`pages/overlays-drawer-weather.html`（天气类：逐时预报+天气详情+生活指数+空气质量+天气资讯）、`pages/overlays-drawer-content.html`（内容类：菜谱详情+音乐故事）
- 内容：右侧滑出抽屉范式（宽 420-500px，左边框 gd-border，头部标题+关闭按钮，内容滚动）、天气折线/柱状/网格、内容时间线/图文混排
- 验收：抽屉背景 gd-popover `#1a1f2e`，关闭按钮用 `header-icon-btn`，数据卡用 `metric-card`

### Batch 3 — 固定栏与特殊控件
- 覆盖：MiniPlayerView、WeatherMapView、WeatherIconControl、VideoMessageCard、LogisticsMapView、ExcelProcessorView
- 新增：`pages/chrome-fixedbar.html`（固定底栏播放器+物流地图+视频消息卡）、`pages/chrome-specialty.html`（天气地图+天气图标矩阵+Excel 工具）
- 内容：固定底栏播放器（64px，背景 gd-card，曲目信息+播放控制+进度音量）、物流地图、视频消息卡、天气地图+图层切换+图例、天气图标 7 状态矩阵、Excel 拖拽区+列映射+预览表
- 验收：固定栏不随内容滚动，地图描边 gd-border，表格用 `data-table`

### Batch 4 — 花卉市场·交易与行情（8 页）
- 覆盖：FlowerDashboard、FlowerDataScreen、FlowerAlertCenter、FlowerShop、FlowerProductDetail、FlowerCart、FlowerOrderCenter、FlowerAddress
- 新增：`pages/flower-market-data.html`（行情仪表盘+数据大屏+预警中心）、`pages/flower-trade-flow.html`（商店+商品详情+购物车+订单+地址）
- 内容：行情仪表盘（3 列指标卡+走势图+Top 品种表）、数据大屏（全屏深色+多图表网格+实时滚动条）、预警中心（列表+筛选+徽章）、商店（分类筛选+商品网格卡）、商品详情（大图+价格走势+参数表）、购物车（数量步进+小计+优惠+合计+结算）、订单（状态标签页+列表+徽章）、地址（卡列表+表单+默认标记）
- 验收：涨跌色严格 `text-up`/`text-down`，数字用 `num` 类，表格用 `data-table`，卡片用 `metric-card`

### Batch 5 — 花卉管理分析 + 音乐模块（6+6 页）
- 覆盖（花卉管理）：FlowerMerchant、FlowerWorkbench、FlowerPlantingAdvice、FlowerAIAssistant、FlowerProfile、FlowerSpeciesDetail
- 覆盖（音乐）：MusicPlayer、MusicDiscover、MusicSearch、PlaylistManage、MusicStory、MusicStoryDrawer（抽屉已在 Batch 2）
- 新增：`pages/flower-manage.html`（商家+工作台+种植建议+AI 助手+个人中心+品种详情）、`pages/music-module.html`（播放器+发现+搜索+歌单+故事）
- 内容：商家管理表、工作台看板、种植建议卡+农事日历+IoT 数据、AI 对话气泡、个人资料卡、品种百科、播放器大图+歌词+队列、发现轮播+推荐网格、搜索+热搜+结果 tab、歌单列表+拖拽排序、故事图文+音频
- 验收：AI 气泡区分进出（gd-info-surface/gd-muted），音乐进度条 gd-primary，歌词高亮 gd-primary

### Batch 6 — 其他主页与认证页（10 页）
- 覆盖：Games、News、Downloads、Settings、Security、Profile、Notification、Social、Login、Register（Home 已由 home-preview 覆盖）
- 新增：`pages/main-pages-content.html`（游戏+新闻+下载+设置+安全+资料+通知+社交）、`pages/main-pages-auth.html`（登录+注册）
- 内容：游戏库网格卡+启动按钮、新闻图文卡列表、下载任务进度条、设置分组面板、安全项+徽章、资料表单、通知列表、社交好友+聊天面板、登录左 hero 右表单、注册分步表单
- 验收：设置用 SurfaceCard 分组，社交气泡 gd-info-surface(出)/gd-muted(入)，登录 hero 用 gd-brand 渐变，表单 focus 态 gd-ring

### 阶段一画布注册与验证
每批完成：
1. 在 `gengdi-trading-ui-redesign.design` 的 `data[]` 追加新页面节点（id、title、type=page、canvasData 坐标、devMetadata.htmlSrc）
2. 更新 `generation-tree.json` 的 children 列表
3. 跑 `validate-design-workspace.mjs --report-json=<设计项目路径>/validation-report.json`，要求 `success=true`
4. 全部 6 批完成后跑 `validate-finish-readiness.mjs <设计项目路径> --check=all`

## 五、阶段二：Avalonia 落地策略

### 5.1 样式系统重构（Batch A0，前置）

**新建 `Assets/Styles/GdTheme.axaml`** — gd token 资源层 + 别名层：
- ThemeDictionaries（Dark 为主，Default/Light 次之）定义 gd 语义资源：
  - 色彩：GdBrand500(#FF2962FF) 等品牌色阶、GdBackgroundBrush(#FF0A0E17)、GdCardBrush(#FF131722)、GdPopoverBrush(#FF1A1F2E)、GdForegroundBrush(#FFE0E6ED)、GdMutedBrush(#FF1A1F2E)、GdMutedForegroundBrush(#FF787B86)、GdBorderBrush(#FF2A2E39)、GdPrimaryBrush(=GdBrand500)、GdUpBrush(#FF26A69A)、GdDownBrush(#FFEF5350)、GdSuccessBrush/GdWarningBrush/GdErrorBrush/GdInfoBrush + 各自 surface 半透明变体
  - 圆角：GdRadiusSm(4)/GdRadiusMd(8)/GdRadiusLg(12)
  - alpha 辅助：GdForegroundMutedBrush(#99E0E6ED)、GdBorderSubtleBrush(#802A2E39)、GdOverlayBrush(#80000000)
- 别名层（把旧键指向 gd token）：
  - Bnet 系：BnetBlue→GdBrand500、BnetDarkBg→GdBackground、BnetCardBg→GdCard、BnetBorder→GdBorder、BnetTextPrimary→GdForeground、BnetTextSecondary/Muted→GdMutedForeground、BnetGreen→GdSuccess、BnetRed→GdError
  - BattleNet 系：BattleNetHeroBackgroundBrush→GdBackgroundBrush(纯色去渐变)、BattleNetPrimaryBrush→GdPrimaryBrush、BattleNetOutlineBrush→GdBrand400、BattleNetSurfaceTintBrush→GdInfoSurfaceBrush
  - TitleBar 系：TitleBarBackgroundBrush→GdCardBrush(去渐变)、TitleBarBorderBrush→GdBorderBrush、TitleBarTextBrush→GdForegroundBrush、TitleBarTextSecondaryBrush→GdMutedForegroundBrush、TitleBarNavItemHoverBrush→GdMutedBrush、TitleBarNavItemSelectedBrush→GdPrimaryBrush
  - SubNav 系：SubNavBarBackgroundBrush→GdCardBrush、SubNavItemSelectedBorderBrush→GdPrimaryBrush
  - Login 系：LoginFormBackgroundBrush→GdCardBrush、LoginFormTextBrush→GdForegroundBrush、LoginFormMutedBrush→GdMutedForegroundBrush、LoginInfoCardBackgroundBrush→GdPopoverBrush、LoginInputBorderBrush→GdBorderBrush、LoginErrorBrush→GdErrorBrush
  - Social 系：在线→GdSuccess、离开→GdWarning、忙碌→GdError、未读→GdError、头像底→GdSecondary、气泡出→GdInfoSurface、入→GdMuted

**新建 `Assets/Styles/GdControls.axaml`** — gd 风格控件样式层：
- Button.GdPrimary（gd-primary 背景+白字+radius-md）、Button.GdGhost（透明+hover gd-muted）、Button.GdIcon（36x36）
- Border.GdCard（gd-card+gd-border+radius-lg+padding 20）、Border.GdMetricCard
- TextBlock.GdTitle/.GdSubtitle/.GdSectionHeader/.GdNum（mono+tabular-nums）
- Button.GdTab（水平顶栏导航项，active 下边框 gd-primary）、Button.GdSubNav（副导航项）
- Border.GdDrawer（右侧抽屉：宽 420+左边框 gd-border+gd-popover）、Border.GdDialog（模态：gd-popover+radius-lg+shadow-xl）
- controls|GalleryPage 新模板（轻量头：标题+副标题+操作区，背景 GdBackground，去 104 图标与渐变）

**修改 `App.axaml`** 加载顺序：
```
FluentAvaloniaTheme → GdTheme.axaml(新增) → Resources.axaml → Styles.xaml → ControlsGalleryStyles.axaml → GdControls.axaml(新增，最后)
```

**验收**：编译通过 + 启动应用全局换肤（背景 #0a0e17、主色 #2962ff），无资源解析异常，41 页无 `Resource not found` 日志。此步不改正文 XAML。

### 5.2 主框架壳落地（Batch A1）
- 目标：MainWindow、MainView、GalleryPage 模板、MiniPlayer 底栏
- 改动：MainWindow Background→GdBackgroundBrush；顶栏 Border→GdCardBrush+底边框 GdBorderBrush；内联 `#FFFFFF`→GdForegroundBrush；副导航 Border→GdCardBrush；GalleryPage 由 GdControls 覆盖；MiniPlayer 底栏→GdCardBrush+上边框 GdBorderBrush
- 验收：与 home-preview.html 1:1 视觉对比，明暗主题切换不崩

### 5.3 页面逐批落地（Batch A2-A7，对齐阶段一）

每批改动模式：
1. `DynamicResource` 旧键引用改为 gd 新键（如 LoginFormBackgroundBrush→GdCardBrush）
2. 内联硬编码颜色改为 gd token alpha 变体
3. 旧样式类按需替换（SurfaceCard→GdCard、QuietAction→GdGhost、PrimaryAction→GdPrimary；别名层已让旧类换色，此步消除旧类依赖）
4. grep 检查该批文件无残留 `#` 硬编码颜色、无残留旧键引用

| 批次 | 对应阶段一 | 覆盖页面 | 特殊处理 |
|---|---|---|---|
| A2 | Batch 1 | ForwardMessageDialog、AvatarCropWindow、ToastContainer | 弹窗 Window 背景 GdPopover，按钮 GdPrimary；Toast 4 类状态色 |
| A3 | Batch 2 | 7 个 DrawerView | 统一 GdDrawer 样式；天气折线/柱状改 GdUp/GdBrand；替换 SolidBackgroundFillColorBaseBrush 等默认键 |
| A4 | Batch 3 | WeatherMapView、WeatherIconControl、VideoMessageCard、LogisticsMapView、ExcelProcessorView | 控件模板颜色替换；地图描边 GdBorder |
| A5 | Batch 4 | 花卉 8 页（行情/数据/预警/商店/商品详情/购物车/订单/地址） | 涨跌色强制 GdUp/GdDown；FlowerDashboard 去 GalleryPage 改纯 Grid |
| A6 | Batch 5 | 花卉管理 6 页 + 音乐 6 页 | AI 气泡 GdInfoSurface/GdMuted；音乐进度条 GdPrimary；歌词高亮 GdPrimary |
| A7 | Batch 6 | 主页 10 页 | Settings 用 GdCard 分组；Social 气泡；Login hero 用 GdBrand 渐变 |

### 5.4 最终清理（Batch A8）
- grep 全项目确认无残留旧键引用（Bnet/BattleNet/Login*/Social*/TitleBar*，除别名层自身定义外）
- grep 确认无内联 `#` 硬编码颜色（白名单除外）
- 删除 GdTheme.axaml 别名层（全部页面迁移后），仅保留 gd token
- 评估删除旧 Styles.xaml 中 Bnet 样式类（全部页面改用 Gd* 类后）

## 六、风险与缓解

| 风险 | 等级 | 缓解 |
|---|---|---|
| 41 页工作量大 | 高 | 别名层先行一夜换肤，降低单批紧迫性；每批独立可验收；允许"别名层换肤+暂不迁移"作为中间稳态 |
| 新旧样式键兼容 | 高 | 别名层统一映射，避免逐页遗漏；每批 grep 该批文件旧键清零 |
| GalleryPage 模板重构破坏布局 | 中 | 双轨保留新轻量模板+旧模板分支；A1 先单独验证所有引用页面不崩 |
| 内联硬编码颜色遗漏 | 中 | 每批 Grep 扫描 `#[0-9A-Fa-f]{6,8}` 强制清零；alpha 变体 token 覆盖常见模式 |
| Avalonia 与 HTML 渲染差异 | 中 | 阶段二每批实机截图与 HTML 原型对比；字体确认 Inter/Noto Sans SC 打包或回退 |
| 明暗主题切换别名层行为 | 中 | GdTheme.axaml 用 ThemeDictionaries（Default/Dark），别名在两字典分别映射；A1 验收明暗切换 |

## 七、验证策略

### 阶段一（HTML 原型）
每批：`validate-design-workspace.mjs` 通过（success=true）+ 浏览器渲染检查（gd token 生效、涨跌色正确、字体加载）+ .design 节点注册完整
全部：`validate-finish-readiness.mjs --check=all` 通过

### 阶段二（Avalonia 落地）
每批：`dotnet build` 编译通过 + grep 旧键/内联颜色清零 + 启动应用逐页视觉对比 HTML 原型 + 明暗主题切换不崩
A0 额外：全局换肤生效 + 41 页无资源解析异常
A1 额外：与 home-preview.html 1:1 对比 + 窗口操作不破坏布局
A8 额外：全项目旧键清零 + 全项目内联颜色清零 + 全页面回归截图

交叉验证：阶段二每批落地前，对应阶段一 HTML 原型须已通过 validate-design-workspace。HTML 原型作为视觉验收基线，偏差超阈值（色彩 ΔE>3、间距 ±2px、圆角 ±1px）需记录决策。

## 八、交付物清单

**阶段一**（`gengdi-trading-ui-redesign/`）：
- 9-10 个新 HTML 文件（pages/ 下）
- 更新的 .design 文件（8-10 个新画布节点）
- 更新的 generation-tree.json
- validation-report.json + finish-readiness 通过证据

**阶段二**（`Horizon.Game.GengDi/`）：
- 新建 Assets/Styles/GdTheme.axaml（token + 别名层）
- 新建 Assets/Styles/GdControls.axaml（gd 控件样式 + GalleryPage 新模板）
- 修改 App.axaml（加载顺序）
- 修改 Resources.axaml（ThemeDictionaries 颜色引用）
- 41 视图 + 5 控件 axaml 逐批迁移
- 最终清理 Styles.xaml / ThemeResources.xaml

## 九、实施顺序建议

1. 阶段一 Batch 1-6（HTML 原型，每批验证通过后进入下一批）
2. 阶段二 Batch A0（样式系统重构 + 别名层，一夜换肤验证）
3. 阶段二 Batch A1（主框架壳，与 home-preview 对比验证）
4. 阶段二 Batch A2-A7（页面逐批落地，对齐阶段一批次）
5. 阶段二 Batch A8（最终清理）

每批完成后更新 todo 状态，阶段一每批需通过 solo-design 验证门，阶段二每批需通过编译 + grep + 视觉对比。

## 十、阶段二实施进度（2026-07-27 探查确认）

### 10.1 已完成批次

| 批次 | 内容 | 验证 |
|---|---|---|
| A0 | GdTheme.axaml（gd token + 别名层，Dark/Default 双主题）+ GdControls.axaml（GdPrimary/Ghost/Icon/Danger/Tab/SubNav/Card/MetricCard/ListItem/Drawer/Dialog/DrawerHeader/Divider/Input + GalleryPage 新轻量模板）+ App.axaml 加载顺序 | 编译 0 错误 |
| A1 | MainWindow.axaml（0 内联色）、MainView.axaml（0 内联色，51 处旧键经别名层换肤）、GalleryPage 新模板、MiniPlayer 底栏 | grep 内联色清零 |
| A2 | ForwardMessageDialog.axaml（GdPrimary/GdGhost/GdPopover）、AvatarCropWindow.axaml（GdPrimary/GdGhost/GdCard）、ToastContainer.axaml（4 类状态色 Converter + gd 键，剩 1 处 BoxShadow #33000000 可保留）、ToastConverters.cs（硬编码 gd Dark 值） | 编译 0 错误 |

### 10.2 剩余工作量统计（实地 grep 确认）

| 批次 | 覆盖文件 | 内联硬编码色 | 旧键引用 | 说明 |
|---|---|---|---|---|
| A3 | 7 DrawerView | 98 处（6 文件） | 15 处（4 文件） | WeatherDetail 46 最多 |
| A4 | 5 控件 + ExcelProcessor | 44 处（VideoCard5+WeatherMap4+Excel34） | — | Toast BoxShadow 1 处可选 |
| A5 | 8 花卉交易页 | 100 处（10 文件） | 482 处（14 文件） | FlowerMerchant 213 旧键最大 |
| A6 | 音乐 6 页 | 5 处 | 35 处（MusicSearch12+Player10+Playlist8+Discover5） | 花卉管理 6 页已含 A5 |
| A7 | 10 主页 | ~150 处 | ~300 处（Login67+Register50+Social24+Home26+News32） | 含 MiniPlayer13 旧键 |
| A8 | 全项目 | — | 809 处全量 | grep 清零 + 评估删别名层 |

合计：内联色约 360+ 处，旧键 809 处（别名层已兜底换肤，非阻塞）。

### 10.3 关键判断

1. **别名层已让 809 处旧键自动换肤**：当前全 App 视觉已是 gd 风格，旧键引用不是阻塞性问题，A8 统一替换或随批替换均可。
2. **内联硬编码颜色是真正风险源**：360+ 处不受别名层控制，仍是旧战网色（#7AA9FF/#FFFFFF88/#1A7AA9FF 等），必须逐批替换。
3. **GalleryPage 新模板已就绪**：21 个引用页面自动获得轻量头，无需逐页改模板。
4. **每批独立可验证**：编译 + grep 该批内联色清零 + 该批旧键清零（可选）。

### 10.4 剩余批次执行细则

#### Batch A3 — 抽屉页落地（当前进行中）
- 文件：AirQuality(21) / WeatherDetail(46) / DishDetail(10) / HourlyForecast(10) / LifeIndex(4) / WeatherNews(7) / MusicStory(0 内联,1 旧键)
- 改动模式：
  1. 根容器套 `Border.GdDrawer`（宽 440 + 左边框 + gd-popover + shadow）
  2. 头部套 `Border.GdDrawerHeader`（标题 + GdIcon 关闭按钮）
  3. 内联色映射：`#FFFFFF`→GdForegroundBrush、`#FFFFFFBB/#FFFFFF88`→GdForegroundMutedBrush、`#1A7AA9FF`→GdInfoSurfaceBrush、`#2A2E39`→GdBorderBrush、`#131722`→GdCardBrush、`#0a0e17`→GdBackgroundBrush
  4. 天气折线/柱状颜色：蓝系→GdBrand400/GdInfo，绿系→GdUp，红/警示→GdError/GdWarning
  5. 旧键引用（15 处）一并替换为 gd 键
- 验收：7 文件 grep 内联色=0 + 旧键=0 + 编译通过

#### Batch A4 — 固定栏与特殊控件落地
- 文件：VideoMessageCard(5) / WeatherMapView(4) / ExcelProcessorView(34) / WeatherIconControl / LogisticsMapView / ToastContainer(1 可选)
- 改动模式：
  1. VideoMessageCard：卡片背景→GdCardBrush，播放按钮→GdPrimary，时长标签→GdMutedForeground
  2. WeatherMapView：地图描边→GdBorderBrush，图例色→gd 状态色，图层切换按钮→GdGhost
  3. ExcelProcessorView：拖拽区虚线框→GdBorderBrush，列映射表→GdListItem，预览表头→GdSectionHeader，进度条→GdPrimary
  4. WeatherIconControl/LogisticsMapView：检查代码内 Color.FromArgb，改 gd 值或 DynamicResource
- 验收：5 控件 grep 内联色=0 + 编译通过

#### Batch A5 — 花卉市场交易页落地（8 页，工作量最大）
- 文件：FlowerDashboard / FlowerDataScreen / FlowerAlertCenter / FlowerShop / FlowerProductDetail / FlowerCart / FlowerOrderCenter / FlowerAddress
- 改动模式：
  1. 涨跌色强制：所有上涨数字→GdUpBrush，下跌→GdDownBrush，平→GdMutedForeground（grep `#26A69A/#EF5350/#00C853` 等）
  2. FlowerDashboard 去 GalleryPage 改纯 Grid（或保留 GalleryPage 用新模板，二选一）
  3. 指标卡→Border.GdMetricCard，列表→Border.GdListItem，表格头→GdSectionHeader
  4. 内联色 100 处逐个替换，旧键 24+7+4+29+45+28+3+11 处替换为 gd 键
  5. 数字类 TextBlock 加 Classes="GdNum"（mono+tabular-nums）
- 验收：8 文件 grep 内联色=0 + 涨跌色正确 + 编译通过

#### Batch A6 — 花卉管理 + 音乐模块落地（12 页）
- 花卉管理 6 页：FlowerMerchant(213 旧键,41 内联) / FlowerWorkbench(21,10) / FlowerPlantingAdvice(67,34) / FlowerAIAssistant(14,0) / FlowerProfile(5,0) / FlowerSpeciesDetail(11,2)
  - FlowerMerchant 是全项目最大单页，建议单独一个子任务，重点替换 213 处旧键
  - AI 助手气泡：出→GdInfoSurfaceBrush，入→GdMutedBrush
- 音乐 6 页：MusicPlayer(2 内联,10 旧键) / MusicDiscover(1,5) / MusicSearch(2,12) / PlaylistManage(0,8) / MusicStory(0,2) / MiniPlayer(0,13)
  - 进度条→GdPrimary，歌词高亮→GdPrimary，封面占位→GdSecondary
- 验收：12 文件 grep 内联色=0 + 编译通过

#### Batch A7 — 主页与认证页落地（10 页）
- 文件：Home(26 内联) / Games(9) / News(32) / Downloads / Settings(2) / Security / Profile / Notification / Social(24) / Login(67 旧键) / Register(50 旧键)
- 改动模式：
  1. Login hero 渐变→GdBrand500→GdBrand700 线性渐变，表单卡→GdCard，输入框→GdInput
  2. Social 气泡：出→GdInfoSurfaceBrush+GdInfo 边框，入→GdMutedBrush+GdBorder 边框
  3. Settings 分组→Border.GdCard，News 图文卡→Border.GdListItem
  4. 内联色 + 旧键逐个替换
- 验收：10 文件 grep 内联色=0 + 旧键=0 + 编译通过

#### Batch A8 — 最终清理
- grep 全项目 `Bnet|BattleNet|TitleBar[A-Z]|Login[A-Z]|Social[A-Z]|SubNav[A-Z]` 确认清零（除 GdTheme.axaml 别名层定义）
- grep 全项目 `#[0-9A-Fa-f]{6,8}` 确认清零（白名单：GdTheme/GdControls 自身定义、BoxShadow 阴影色、图标 Path Fill 中的纯白/纯黑可保留）
- 评估删除 GdTheme.axaml 别名层（全部页面迁移后，旧键无引用即可删）
- 评估删除 Styles.xaml 中 Bnet 样式类（全部页面改用 Gd* 类后）
- 全页面回归编译 + 启动视觉检查

### 10.5 风险与缓解（更新）

| 风险 | 缓解 |
|---|---|
| FlowerMerchant 213 旧键工作量大 | 单独子任务，可用脚本辅助批量替换（BnetBlue→GdPrimaryBrush 等固定映射） |
| 内联色遗漏 | 每批结束 grep `#[0-9A-Fa-f]{6,8}` 强制清零才进下一批 |
| 替换破坏布局 | 每批编译通过 + 启动应用抽检该批页面渲染 |
| GalleryPage 去化影响 | A5 优先保留 GalleryPage+新模板，仅 FlowerDashboard 试改纯 Grid 验证 |

### 10.6 执行顺序

A3（进行中）→ A4 → A5（含 FlowerMerchant 子任务）→ A6 → A7 → A8

每批完成更新 todo，编译通过 + grep 清零后进入下一批。
