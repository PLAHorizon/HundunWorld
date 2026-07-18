# Tasks

- [x] Task 1: 创建 InkWashTheme 主题 Token 系统
  - [x] SubTask 1.1: 新建 `Source/Game/UI/StyleSystem/InkWashTheme.cs`，按 `colors_and_type.css` 的 `:root` 变量定义全部 `Color`/`float` 常量（背景层/鎏金/古铜/朱红/纸色/辅助/品质/状态/文字/边框/圆角/间距/控件高度）
  - [x] SubTask 1.2: 定义字体族字段 `FontDisplay`/`FontHeading`/`FontBody`/`FontNumber`，并提供 `GetFont(FontRole)` 方法，缺失时降级到系统中文字体
  - [x] SubTask 1.3: 提供辅助方法 `QualityColor(InkQuality)`、`SetBrushBorder(Control, float)`，供组件复用

- [x] Task 2: 导入燕云字体与背景纹理资源
  - [x] SubTask 2.1: 将 Ma Shan Zheng / Noto Serif SC / Noto Sans SC 字体导入 `Content/InkWash/Fonts/`（若授权允许，否则准备降级方案）
  - [x] SubTask 2.2: 将 `hundun-yy-ui/assets` 下需要的背景图（bg-loading-landscape、bg-loading-mountain、bg-chapter-ink、bg-ink-wash-scene、bg-cc-naming 等）复制到 `Content/InkWash/Textures/`
  - [x] SubTask 2.3: 在 `InkWashTheme` 中以资产路径常量记录纹理引用，避免硬编码散落

- [x] Task 3: 实现 Ink 组件库核心控件
  - [x] SubTask 3.1: `InkBackgroundLayer`（径向渐变 + noise overlay）与 `InkVignette`（暗角晕影），覆写 `OnDraw`
  - [x] SubTask 3.2: `InkPanel`/`InkPanelSolid`/`InkPanelElevated`（半透明毛玻璃/纯色/抬升阴影），支持 `BorderColor`/`Radius`/`BackgroundBlurred` 属性
  - [x] SubTask 3.3: `InkPaperPanel`（纸色卷轴面板 + 纸纹 overlay）与 `InkBrushBorder`（多层金线飞白描边）
  - [x] SubTask 3.4: `InkCornerDeco`（四角 L 型金角装饰），支持 `ShowTL/TR/BL/BR` 开关
  - [x] SubTask 3.5: `InkPanelTitle`（标题栏 + 左侧金竖线 + 宋体字 + 字间距）
  - [x] SubTask 3.6: `InkButton`（含 `InkButtonVariant` 枚举：Default/Primary/Vermilion/Ghost，支持 Sm/Md/Lg 尺寸，hover/press 状态过渡）
  - [x] SubTask 3.7: `InkTag`（含 `InkTagVariant`：Default/Brand/Vermilion + 品质色 `InkQuality` 变体）
  - [x] SubTask 3.8: `InkBar`（含 `InkBarFillVariant`：Gold/Jade/Blood/Vermilion，支持横向/竖向 `InkBarVertical`，宽度过渡动画）
  - [x] SubTask 3.9: `InkCell`（品质色边框格子，支持图标 + 数量徽章）
  - [x] SubTask 3.10: `InkListItem`（列表项，支持 active 态左侧金竖线）
  - [x] SubTask 3.11: `InkAvatar`/`InkDot`、`InkDivider`/`InkDividerVertical`、`InkSplash`（水墨晕染装饰）
  - [x] SubTask 3.12: `InkBackButton`（左上角返回按钮）、`InkVerticalTitle`（竖排书法标题）
  - [x] SubTask 3.13: `InkTextBlock`（统一文本控件，支持 `InkTextStyle`：Display/Heading/Subheading/Body/Caption/Number 预设，自动应用对应字体/字号/字色/字间距）

- [x] Task 4: 实现 InkPageShell 与 InkPageRouter
  - [x] SubTask 4.1: `InkPageShell`（承载 `InkBackgroundLayer` + `InkVignette` + 内容层 `ContainerControl`，提供 `LoadPage`/`UnloadPage` 接口）
  - [x] SubTask 4.2: `InkPageRouter`（页面栈管理，按 `data-dom-id` 字符串注册与跳转，支持前进/返回战斗 HUD，提供 `RegisterPage(string domId, Func<Control>)`）
  - [x] SubTask 4.3: 在 `InkPageShell` 中为非枢纽页面自动添加 `InkBackButton` 并绑定 `back-hud` 导航

- [x] Task 5: 落地战斗 HUD 页面（`CombatHudPage`）
  - [x] SubTask 5.1: 全屏水墨战场背景 + `InkVignette` + 水墨晕染装饰
  - [x] SubTask 5.2: 左上角引导按钮（`InkButton` Ghost + 图标），`data-dom-id="link-guide-hud"`（跳转占位提示）
  - [x] SubTask 5.3: 顶部中央任务提示条（`InkPanel` + 任务文本 + 进度计数）
  - [x] SubTask 5.4: 右上角水墨指南针（圆形 `InkPanel` + 东南西北字 + 朱红指针 + 摆动动画）
  - [x] SubTask 5.5: 左下角头像按钮（`data-dom-id="nav-character"`）+ `InkVerticalTitle` 角色名 + 气血条（`InkBar` Vermilion）+ 体魄条（`InkBar` Jade）
  - [x] SubTask 5.6: 右下角技能槽（5 个圆形技能 + 冷却扇形遮罩 + 快捷键标签）+ 奇术槽（更大金边 + 就绪脉冲动画）
  - [x] SubTask 5.7: 底部中央 buff/debuff 图标条（正面翡翠/负面朱红 + 数量徽章 + 分隔线）
  - [x] SubTask 5.8: 底部系统导航栏（6 个 `InkButton`：任务/闲趣/异闻/成就/留影/设置，"任务"绑定 `nav-quests`、"设置"绑定 `nav-settings`，其余预留占位）

- [x] Task 6: 落地加载与章节过场页面
  - [x] SubTask 6.1: `LoadingPage1`（`bg-loading-landscape` 背景 + `InkVerticalTitle`"江湖初启" + 底部 `InkBar` 进度条，进度满自动推进）
  - [x] SubTask 6.2: `LoadingPage2`（`bg-loading-mountain` 背景 + `InkVerticalTitle`"远峰在望" + 进度条，自动推进到章节过场）
  - [x] SubTask 6.3: `ChapterTransitionPage`（`bg-chapter-ink` 背景 + 居中毛笔书法章节名 + `InkButton` Primary"进入世界"，`data-dom-id="cta-enter-world"` 跳转战斗 HUD）

- [x] Task 7: 落地角色属性菜单页面（`MenuCharAttributesPage`）
  - [x] SubTask 7.1: 顶部 `InkPanelTitle`"角色属性" + `InkBackButton`
  - [x] SubTask 7.2: 左侧属性列表（`InkPanel` + 多个 `InkListItem`，每项含属性名 + 数值 + `InkBar`）
  - [x] SubTask 7.3: 中间角色预览占位（`InkPanel` + 提示文字"预览"，本 spec 不做 3D）
  - [x] SubTask 7.4: 右侧装备槽网格（6 个 `InkCell` 含品质色）+ 五行数据（5 个 `InkBar` Jade）

- [x] Task 8: 落地任务菜单页面（`MenuQuestsPage`）
  - [x] SubTask 8.1: 顶部 `InkPanelTitle`"任务" + `InkBackButton`
  - [x] SubTask 8.2: 左侧分类侧边栏（主线/支线/日常，`InkListItem` active 态）
  - [x] SubTask 8.3: 右侧任务列表（`InkListItem` × N，每项含任务名 + 描述 + `InkBar` 进度 + 奖励缩略）

- [x] Task 9: 落地商店菜单页面（`MenuShopPage`）
  - [x] SubTask 9.1: 顶部 `InkPanelTitle`"商店" + `InkBackButton`
  - [x] SubTask 9.2: 左侧分类侧边栏（兵器/防具/丹药/材料）
  - [x] SubTask 9.3: 中间商品格子网格（`InkCell` × N，含品质色边框 + 价格徽章）
  - [x] SubTask 9.4: 右侧商品详情（`InkPaperPanel` + 物品名 + 属性 + `InkButton` Primary"购买"）

- [x] Task 10: 落地物品获得与留言弹窗
  - [x] SubTask 10.1: `PopupItemAcquired`（半透明遮罩 + 居中 `InkPanelElevated` + `InkCell` 品质光晕 + 物品名/数量 + `InkButton` Primary"确认"）
  - [x] SubTask 10.2: `PopupMessage`（半透明遮罩 + 居中 `InkPaperPanel` 信笺样式 + 留言文本 + `InkButton` Ghost"关闭"）

- [x] Task 11: 落地成就解锁与任务完成奖励页
  - [x] SubTask 11.1: `RewardAchievementPage`（遮罩 + 居中模态 + 成就图标金光晕动画 + 书法成就名 + `InkButton` Primary"领取"）
  - [x] SubTask 11.2: `RewardQuestCompletePage`（遮罩 + 居中模态 + 任务名 + 奖励物品 `InkCell` 列表 + 经验/铜钱 DIN 数值 + `InkButton` Primary"领取"）

- [x] Task 12: 落地设置页面（`SettingsPage`）
  - [x] SubTask 12.1: 顶部 `InkPanelTitle`"设置" + `InkBackButton`
  - [x] SubTask 12.2: 左侧分类侧边栏（画面/音效/操作/系统）
  - [x] SubTask 12.3: 右侧设置项列表（`InkListItem` × N，每项含标签 + 开关/滑块/下拉控件，使用 `InkWashTheme` 配色）

- [x] Task 13: 接入 MainUIManager 与页面注册
  - [x] SubTask 13.1: 在 `MainUIManager` 进入 World 场景的逻辑中，创建 `InkPageShell` + `InkPageRouter`，挂载到根 `UICanvas`
  - [x] SubTask 13.2: 在 `InkPageRouter` 注册全部 12 个页面的 `data-dom-id` 与构造委托
  - [x] SubTask 13.3: 默认激活战斗 HUD；`GameMainUI` 保持不激活作为备份
  - [x] SubTask 13.4: 验证 Login→进入世界→加载链路→战斗 HUD→菜单/弹窗/奖励/设置的完整导航

- [x] Task 14: 编译验证与视觉走查
  - [x] SubTask 14.1: 关闭 Flax Editor，执行 `Flax.Build` 编译 HundunWorld 项目，确保 0 错误
  - [ ] SubTask 14.2: 启动编辑器 PIE，走查 12 个页面的视觉与导航，对照 HTML 原型核对色彩/字体/布局（需用户在 Flax Editor 中手动 PIE 验证）
  - [ ] SubTask 14.3: 走查返回战斗 HUD 链路、底部导航跳转、弹窗模态遮罩、奖励领取关闭等交互（需用户在 Flax Editor 中手动 PIE 验证）
  - [ ] SubTask 14.4: 确认旧 `AuthenticationUI`/`GameMainUI` 未回归（需用户在 Flax Editor 中手动 PIE 验证）

# Task Dependencies
- [Task 3] 依赖 [Task 1]（组件引用 InkWashTheme Token）
- [Task 2] 依赖 [Task 1]（资源路径常量在 Theme 中登记）
- [Task 4] 依赖 [Task 3]（Shell/Router 使用 Ink 组件）
- [Task 5]~[Task 12] 依赖 [Task 3] 与 [Task 4]（页面使用 Ink 组件与 Shell/Router）
- [Task 13] 依赖 [Task 5]~[Task 12]（接入前所有页面需就绪）
- [Task 14] 依赖 [Task 13]
- [Task 5]~[Task 12] 之间相互独立，可并行实现
