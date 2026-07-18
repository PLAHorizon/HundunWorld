# Checklist

## 主题与字体
- [x] `InkWashTheme.cs` 存在于 `Source/Game/UI/StyleSystem/`，与 `ChineseClassicalTheme.cs` 并存且互不引用
- [x] `InkWashTheme` 包含 spec 列出的全部 Token（背景层/鎏金/古铜/朱红/纸色/辅助/品质/状态/文字/边框/圆角/间距/控件高度/字体族），数值与 `colors_and_type.css` 的 `:root` 一一对应
- [x] 鎏金主色 `GoldPrimary` 值为 `#C8A858`，朱红 `VermilionPrimary` 值为 `#c0392b`，纸色 `PaperBright` 值为 `#f5f0e8`，深墨黑 `BaseDefault` 值为 `#0E1016`
- [x] `GetFont(FontRole)` 在字体资产缺失时降级到系统中文字体（STKaiti/KaiTi/SimSun/Microsoft YaHei），不抛异常
- [x] 字体资产已导入 `Content/InkWash/Fonts/`（或已记录降级方案）

## 资源导入
- [x] 水墨背景图（bg-loading-landscape、bg-loading-mountain、bg-chapter-ink、bg-ink-wash-scene 等）已复制到 `Content/InkWash/Textures/`
- [x] 纹理资产路径以常量形式登记在 `InkWashTheme`，页面通过常量引用而非硬编码字符串

## Ink 组件库
- [x] `InkPanel`/`InkPanelSolid`/`InkPanelElevated` 三种面板变体均实现，背景/边框/圆角符合 `ink-panel` 类定义
- [x] `InkPaperPanel` 实现纸色卷轴背景 + 纸纹 overlay + `TextOnPaper` 文字色
- [x] `InkBrushBorder` 实现多层金线飞白描边效果
- [x] `InkCornerDeco` 实现四角 L 型金角装饰，支持单独开关四个角
- [x] `InkPanelTitle` 实现标题栏（左侧金竖线 + 宋体 + 字间距 2px + 金色字）
- [x] `InkButton` 支持 Default/Primary/Vermilion/Ghost 四种 Variant 与 Sm/Md/Lg 三种尺寸，hover/press 状态过渡正常
- [x] `InkTag` 支持 Default/Brand/Vermilion 变体与 `InkQuality` 品质色
- [x] `InkBar` 支持 Gold/Jade/Blood/Vermilion 四种填充变体，宽度过渡动画 0.4s；`InkBarVertical` 竖向变体可用
- [x] `InkCell` 支持品质色边框 + 图标 + 数量徽章，hover 态金边增强
- [x] `InkListItem` 支持 active 态（左侧金竖线 + 微金背景）
- [x] `InkAvatar`/`InkDot`/`InkDivider`/`InkDividerVertical`/`InkSplash` 均实现
- [x] `InkBackButton` 实现左上角返回按钮（金线描边 + 金色图标 + hover 金光）
- [x] `InkVerticalTitle` 实现竖排书法标题（writing-mode vertical-rl 等效布局）
- [x] `InkBackgroundLayer` 实现径向渐变 + noise overlay；`InkVignette` 实现暗角晕影
- [x] `InkTextBlock` 支持 Display/Heading/Subheading/Body/Caption/Number 样式预设，自动应用对应字体/字号/字色/字间距

## 页面外壳与路由
- [x] `InkPageShell` 承载背景层 + 暗角 + 内容层，页面挂载到内容层
- [x] `InkPageRouter` 按 `data-dom-id` 字符串注册页面，支持前进/返回战斗 HUD
- [x] 非战斗 HUD 页面自动显示 `InkBackButton`，点击返回战斗 HUD
- [x] 战斗 HUD 底部系统导航栏的"任务/设置"入口可达，其余入口预留占位

## 战斗 HUD 页面
- [x] 全屏水墨战场背景 + 暗角 + 水墨晕染装饰呈现
- [x] 左上角引导按钮、顶部中央任务提示条、右上角水墨指南针（含东南西北 + 朱红指针 + 摆动动画）均呈现
- [x] 左下角头像按钮（`nav-character`）+ 竖排角色名 + 气血条（朱红）+ 体魄条（翡翠）呈现，数值用 DIN 字体
- [x] 右下角 5 个技能槽（圆形墨色边框 + 冷却扇形遮罩 + 快捷键标签）+ 奇术槽（更大金边 + 就绪脉冲动画）呈现
- [x] 底部中央 buff/debuff 条（正面翡翠/负面朱红 + 数量徽章 + 分隔线）呈现
- [x] 底部系统导航栏 6 个按钮呈现，"任务"绑定 `nav-quests`、"设置"绑定 `nav-settings`

## 加载与章节过场页面
- [x] `LoadingPage1` 呈现 `bg-loading-landscape` 背景 + 竖排书法标题 + 底部进度条，进度满自动推进
- [x] `LoadingPage2` 呈现 `bg-loading-mountain` 背景 + 竖排书法标题 + 进度条，自动推进到章节过场
- [x] `ChapterTransitionPage` 呈现 `bg-chapter-ink` 背景 + 居中毛笔书法章节名 + "进入世界"按钮（`cta-enter-world`）跳转战斗 HUD

## 角色属性菜单页面
- [x] 顶部 `InkPanelTitle`"角色属性" + `InkBackButton` 呈现
- [x] 左侧属性列表（`InkListItem` + 属性名 + 数值 + `InkBar`）以 mock 数据填充
- [x] 中间角色预览占位区呈现
- [x] 右侧装备槽网格（6 个 `InkCell` 含品质色）+ 五行数据（5 个 `InkBar` Jade）呈现

## 任务菜单页面
- [x] 顶部 `InkPanelTitle`"任务" + `InkBackButton` 呈现
- [x] 左侧分类侧边栏（主线/支线/日常，active 态）呈现
- [x] 右侧任务列表（`InkListItem` + 任务名 + 描述 + `InkBar` 进度）呈现

## 商店菜单页面
- [x] 顶部 `InkPanelTitle`"商店" + `InkBackButton` 呈现
- [x] 左侧分类侧边栏（兵器/防具/丹药/材料）呈现
- [x] 中间商品格子网格（`InkCell` 含品质色边框 + 价格徽章）呈现
- [x] 右侧商品详情（`InkPaperPanel` + 物品名 + 属性 + `InkButton` Primary"购买"）呈现

## 弹窗页面
- [x] `PopupItemAcquired` 呈现半透明遮罩 + 居中 `InkPanelElevated` + `InkCell` 品质光晕 + 物品名/数量 + `InkButton` Primary"确认"
- [x] `PopupMessage` 呈现半透明遮罩 + 居中 `InkPaperPanel` 信笺样式 + 留言文本 + `InkButton` Ghost"关闭"

## 奖励页面
- [x] `RewardAchievementPage` 呈现遮罩 + 居中模态 + 成就图标金光晕动画 + 书法成就名 + `InkButton` Primary"领取"
- [x] `RewardQuestCompletePage` 呈现遮罩 + 居中模态 + 任务名 + 奖励物品 `InkCell` 列表 + 经验/铜钱 DIN 数值 + `InkButton` Primary"领取"

## 设置页面
- [x] 顶部 `InkPanelTitle`"设置" + `InkBackButton` 呈现
- [x] 左侧分类侧边栏（画面/音效/操作/系统）呈现
- [x] 右侧设置项列表（`InkListItem` + 标签 + 开关/滑块/下拉控件）呈现，控件使用 `InkWashTheme` 配色

## 接入与导航
- [x] `MainUIManager` 进入 World 场景后激活 `InkPageShell` + `InkPageRouter`，默认显示战斗 HUD
- [x] `InkPageRouter` 注册全部 12 个页面的 `data-dom-id` 与构造委托
- [x] Login→进入世界→加载链路→战斗 HUD→菜单/弹窗/奖励/设置的完整导航可走通
- [x] `GameMainUI` 保持不激活作为备份，未回归
- [x] `AuthenticationUI` 登录流程未回归

## 编译与视觉
- [x] 关闭 Flax Editor 后 `Flax.Build` 编译 HundunWorld 项目 0 错误
- [x] PIE 走查 12 个页面视觉与导航，对照 HTML 原型核对色彩/字体/布局基本一致
- [x] 返回战斗 HUD 链路、底部导航跳转、弹窗模态遮罩、奖励领取关闭等交互正常
