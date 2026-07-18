# 燕云水墨 UI 落地（基础与核心垂直切片）Spec

## Why
当前 MMORPG 客户端（Flax Engine）的 UI 主题为 `ChineseClassicalTheme`（黛青色/WoW 石质风），视觉语言不统一且与目标产品定位不符。`hundun-yy-ui` 设计方案已完成一套完整的燕云十六声风格水墨武侠 UI（55 页 + 设计 Token + 组件类），但仅以 HTML/CSS 原型存在，未在客户端落地。本 spec 将该设计方案的**地基与核心垂直切片**移植到 Flax C# 客户端，建立可复用的 `InkWashTheme` 主题与 `Ink` 组件库，并落地一条贯穿"加载→章节过场→战斗 HUD 枢纽→菜单→弹窗→奖励→设置"的完整纵向链路（约 12 页），验证设计语言在客户端的可行性，为剩余 43 页作为后续 spec delta 落地铺路。

## What Changes
- 新增 `InkWashTheme` 主题系统（C# 静态类），承载燕云设计 Token：深墨黑底/鎏金主色/朱红战斗色/纸色面板/古铜辅色/品质五级色，与现有 `ChineseClassicalTheme` 并存，旧 UI 不受影响
- 新增 `Ink` 组件库（Flax `ContainerControl` 派生控件）：`InkPanel`/`InkPaperPanel`/`InkBrushBorder`/`InkCornerDeco`/`InkPanelTitle`/`InkButton`/`InkTag`/`InkBar`/`InkCell`/`InkListItem`/`InkAvatar`/`InkDivider`/`InkSplash`/`InkBackButton`/`InkVerticalTitle`/`InkBackgroundLayer`/`InkVignette`
- 新增 `InkWashFontAtlas`/字体加载策略：注册 Ma Shan Zheng（书法大标题）、Noto Serif SC（宋体副标题）、Noto Sans SC（黑体正文）、DIN（数值），缺失时降级到系统中文字体
- 新增 `InkPageShell`（页面外壳）与 `InkPageRouter`（页面路由）：统一承载背景层/暗角/内容层，按 `data-dom-id` 导航契约在页面间切换，支持返回战斗 HUD
- 落地战斗 HUD（`combat-hud.html`）：左下角头像+竖排角色名+气血/体魄条、右下角技能槽+奇术、底部 buff 条、底部系统导航栏、顶部任务提示、右上角水墨指南针
- 落地加载页（`loading-1`/`loading-2`）与章节过场（`chapter-transition`）：竖排书法标题 + 水墨背景 + 进度条
- 落地 3 个代表菜单：角色属性（`menu-char-attributes`）、任务（`menu-quests`）、商店（`menu-shop`），覆盖面板/标题/列表/格子/进度条组件模式
- 落地 2 个代表弹窗：物品获得（`popup-item-acquired`）、留言（`popup-message`），覆盖弹层/纸色面板/遮罩模式
- 落地 2 个代表奖励：成就解锁（`reward-achievement`）、任务完成（`reward-quest-complete`），覆盖奖励庆祝/金光晕/居中模态模式
- 落地设置页（`settings`），覆盖设置列表/开关/滑块模式
- 导入 `hundun-yy-ui/assets` 下的水墨背景纹理与背景图到 Flax Content
- 接入 `MainUIManager`：在进入 World 场景后切换到 `InkPageRouter`，由 `InkPageRouter` 承载战斗 HUD 与各子页面

## Impact
- Affected specs:
  - `character-attribute-panel`（角色属性面板）：本 spec 落地的 `menu-char-attributes` 将采用燕云视觉语言，与该 spec 的 WoW 风面板并存；该 spec 的功能需求（装备槽/五行雷达/背包内嵌）不在本 spec 范围，由该 spec 自行实现
  - `character-equipment-system`：不受影响，独立演进
- Affected code:
  - `Source/Game/UI/StyleSystem/InkWashTheme.cs` — 新增主题 Token
  - `Source/Game/UI/Ink/` — 新增组件库目录（InkPanel.cs 等）
  - `Source/Game/UI/Ink/Pages/` — 新增 12 个页面控件
  - `Source/Game/UI/Ink/InkPageShell.cs` / `InkPageRouter.cs` — 新增页面外壳与路由
  - `Source/Game/UI/MainUIManager.cs` — 接入 `InkPageRouter`
  - `Source/Game/UI/Canvas.cs` — 可能扩展以承载 `InkPageRouter`
  - `Content/InkWash/` — 新增导入的背景纹理与字体资源
- 不做：剩余 43 页 UI、拖拽换装、3D 角色预览、网络数据绑定（页面用 mock 数据验证视觉）、替换现有 `ChineseClassicalTheme`、迁移旧 UI 控件

## ADDED Requirements

### Requirement: InkWashTheme 设计 Token 系统
系统 SHALL 提供一个 `InkWashTheme` 静态类，承载燕云水墨设计 Token，供所有 `Ink` 组件与页面引用，与 `ChineseClassicalTheme` 并存互不干扰。

#### Scenario: 组件引用主题色
- **WHEN** 任意 `Ink` 组件需要鎏金主色
- **THEN** 通过 `InkWashTheme.GoldPrimary` 获取 `#C8A858` 对应 `Color`，无需硬编码

#### Scenario: Token 完整性
- **WHEN** 检查 `InkWashTheme`
- **THEN** 包含：背景层（BaseDefault/BaseSecondary/BaseTertiary/BaseElevated/Panel/PanelSolid/Void/Abyss/Scrim）、鎏金系（GoldPrimary/Bright/Deep/Glow）、古铜系（BronzePrimary/Bright/Deep）、朱红系（VermilionPrimary/Bright/Deep/Faded/Glow）、纸色系（PaperBright/Paper/PaperAged/PaperFaded/PaperDark）、辅助语义色（Jade/Blood）、品质色（QualityCommon/Uncommon/Rare/Epic/Legendary）、状态色（Success/Warning/Error/Info）、文字色（Default/Secondary/Tertiary/Disabled/Brand/OnBrand/OnPaper/Vermilion）、边框色（BorderGold/BorderGoldStrong/BorderBronze/BorderVermilion/Divider）、圆角（RadiusNone/Sm/Md/Lg/Full）、间距（Space1~8）、控件高度（ControlHSm/Md/Lg）、字体族（FontDisplay/FontHeading/FontBody/FontNumber）

### Requirement: 字体加载与降级
系统 SHALL 在启动时注册燕云字体族，并在字体资源缺失时降级到系统中文字体，确保文案可读。

#### Scenario: 字体可用
- **WHEN** Ma Shan Zheng / Noto Serif SC / Noto Sans SC 字体已导入 Content
- **THEN** `InkWashTheme.FontDisplay` 等引用对应字体资产，大标题以毛笔书法呈现

#### Scenario: 字体缺失降级
- **WHEN** 某字体资产未导入或加载失败
- **THEN** 降级到 STKaiti/KaiTi/SimSun/Microsoft YaHei 等系统中文字体，不抛异常，文案正常显示

### Requirement: Ink 组件库
系统 SHALL 提供一组 Flax `ContainerControl` 派生的 `Ink` 组件，对应 `colors_and_type.css` 中的组件类，作为页面构建积木。

#### Scenario: 面板组件
- **WHEN** 页面需要一个半透明毛玻璃金线描边面板
- **THEN** 使用 `InkPanel`，自动应用 `InkWashTheme.Panel` 背景 + `BorderGold` 边框 + `RadiusMd` 圆角

#### Scenario: 纸色卷轴面板
- **WHEN** 页面需要一个浅色卷轴/信笺面板
- **THEN** 使用 `InkPaperPanel`，应用 `PaperPanelBg` 背景 + `TextOnPaper` 文字色

#### Scenario: 按钮变体
- **WHEN** 页面需要主操作按钮
- **THEN** 使用 `InkButton` 并设置 `Variant = InkButtonVariant.Primary`，呈现鎏金渐变 + 金光晕

#### Scenario: 进度条变体
- **WHEN** 页面需要气血条
- **THEN** 使用 `InkBar` 并设置 `FillVariant = InkBarFillVariant.Vermilion`，呈现朱红渐变

#### Scenario: 品质格子
- **WHEN** 页面需要装备格子
- **THEN** 使用 `InkCell` 并设置 `Quality = InkQuality.Legendary`，呈现朱红边框 + 光晕

#### Scenario: 四角装饰
- **WHEN** 面板需要四角金角装饰
- **THEN** 通过 `InkCornerDeco` 在面板四角绘制 L 型金线

#### Scenario: 组件清单
- **WHEN** 检查 `Ink` 组件库
- **THEN** 至少包含：`InkPanel`/`InkPanelSolid`/`InkPanelElevated`/`InkPaperPanel`/`InkBrushBorder`/`InkCornerDeco`/`InkPanelTitle`/`InkButton`/`InkTag`/`InkBar`/`InkBarVertical`/`InkCell`/`InkListItem`/`InkAvatar`/`InkDot`/`InkDivider`/`InkDividerVertical`/`InkSplash`/`InkBackButton`/`InkVerticalTitle`/`InkBackgroundLayer`/`InkVignette`/`InkTextBlock`（含 Display/Heading/Subheading/Body/Caption/Number 样式预设）

### Requirement: InkPageShell 页面外壳
系统 SHALL 提供统一的 `InkPageShell`，承载全局水墨背景层、暗角晕影、内容层，所有页面以子控件形式挂载到内容层。

#### Scenario: 页面挂载
- **WHEN** 路由器激活某页面
- **THEN** 该页面控件被添加到 `InkPageShell` 的内容层，背景层与暗角层保持不变

#### Scenario: 返回按钮
- **WHEN** 页面（非战斗 HUD）被激活
- **THEN** 左上角显示 `InkBackButton`，点击返回战斗 HUD

### Requirement: InkPageRouter 页面路由
系统 SHALL 提供 `InkPageRouter`，按 `data-dom-id` 导航契约管理页面栈，支持前进/返回/切换。

#### Scenario: 前进导航
- **WHEN** 用户点击带 `data-dom-id` 的导航按钮（如 `nav-character`）
- **THEN** 路由器激活目标页面，旧页面被隐藏或卸载

#### Scenario: 返回战斗 HUD
- **WHEN** 用户在任意子页面点击返回按钮（`back-hud`）
- **THEN** 路由器返回战斗 HUD 页面

#### Scenario: 战斗 HUD 为枢纽
- **WHEN** 战斗 HUD 被激活
- **THEN** 底部系统导航栏可见，点击各入口跳转到对应子页面

### Requirement: 战斗 HUD 页面
系统 SHALL 落地 `combat-hud.html` 对应的战斗 HUD，作为游戏内 UI 枢纽。

#### Scenario: HUD 布局
- **WHEN** 玩家进入 World 场景
- **THEN** 显示战斗 HUD：左上角引导按钮、顶部中央任务提示、右上角水墨指南针、左下角头像+竖排角色名+气血/体魄条、右下角技能槽+奇术、底部中央 buff 条、底部系统导航栏

#### Scenario: 气血/体魄条
- **WHEN** HUD 可见
- **THEN** 气血条使用 `InkBar` 朱红变体，体魄条使用 `InkBar` 翡翠变体，数值用 DIN 字体

#### Scenario: 技能槽
- **WHEN** HUD 可见
- **THEN** 技能槽为圆形墨色边框，支持冷却扇形遮罩与快捷键标签；奇术槽更大且金边，就绪时有脉冲动画

#### Scenario: 系统导航
- **WHEN** 玩家点击底部导航栏的"任务/闲趣/异闻/成就/留影/设置"按钮
- **THEN** 路由器跳转到对应页面（本 spec 范围内仅"任务/设置"可达，其余入口预留但跳转到占位页或提示"待落地"）

### Requirement: 加载与章节过场页面
系统 SHALL 落地 `loading-1`、`loading-2`、`chapter-transition` 三个页面，作为进入世界的过场。

#### Scenario: 加载页布局
- **WHEN** 加载页激活
- **THEN** 全屏水墨背景图 + 竖排书法标题 + 底部加载进度条（`InkBar`）

#### Scenario: 章节过场布局
- **WHEN** 章节过场激活
- **THEN** 章节标题以毛笔书法居中呈现，配水墨背景，提供"进入世界"按钮跳转战斗 HUD

#### Scenario: 自动推进
- **WHEN** 加载进度满
- **THEN** 自动推进到下一页（loading-1→loading-2→chapter-transition）

### Requirement: 角色属性菜单页面
系统 SHALL 落地 `menu-char-attributes.html` 对应的角色属性菜单，验证面板/标题/列表/属性条模式。

#### Scenario: 布局
- **WHEN** 角色属性菜单激活
- **THEN** 左侧属性列表（`InkListItem`）、中间角色预览占位、右侧装备槽（`InkCell`）+ 五行数据，顶部 `InkPanelTitle`"角色属性"

#### Scenario: 数据展示
- **WHEN** 页面可见
- **THEN** 以 mock 数据填充属性名/数值/进度条，验证字体与色彩

### Requirement: 任务菜单页面
系统 SHALL 落地 `menu-quests.html` 对应的任务菜单，验证分类侧边栏 + 任务列表模式。

#### Scenario: 布局
- **WHEN** 任务菜单激活
- **THEN** 左侧分类侧边栏（主线/支线/日常）、右侧任务列表（`InkListItem`），每项含任务名/描述/进度（`InkBar`）

### Requirement: 商店菜单页面
系统 SHALL 落地 `menu-shop.html` 对应的商店菜单，验证商品格子 + 品质色 + 购买按钮模式。

#### Scenario: 布局
- **WHEN** 商店菜单激活
- **THEN** 左侧分类、中间商品格子网格（`InkCell` 含品质色边框）、右侧商品详情面板（`InkPaperPanel`），底部购买按钮（`InkButton` Primary）

### Requirement: 物品获得弹窗
系统 SHALL 落地 `popup-item-acquired.html` 对应的物品获得弹窗，验证居中模态 + 品质光晕模式。

#### Scenario: 弹出
- **WHEN** 触发物品获得
- **THEN** 屏幕中央弹出 `InkPanelElevated`，含物品图标（`InkCell` 品质色）、物品名、数量、"确认"按钮（`InkButton` Primary），背景半透明遮罩

### Requirement: 留言弹窗
系统 SHALL 落地 `popup-message.html` 对应的留言弹窗，验证纸色卷轴面板 + 竖排文字模式。

#### Scenario: 弹出
- **WHEN** 触发留言
- **THEN** 居中弹出 `InkPaperPanel`，以信笺样式呈现留言文本，底部"关闭"按钮（`InkButton` Ghost）

### Requirement: 成就解锁奖励页
系统 SHALL 落地 `reward-achievement.html` 对应的成就解锁奖励页，验证金光晕庆祝模式。

#### Scenario: 布局
- **WHEN** 成就解锁
- **THEN** 居中模态，成就图标带金光晕动画，成就名以书法字体呈现，底部"领取"按钮（`InkButton` Primary）

### Requirement: 任务完成奖励页
系统 SHALL 落地 `reward-quest-complete.html` 对应的任务完成奖励页，验证奖励列表 + 领取模式。

#### Scenario: 布局
- **WHEN** 任务完成奖励激活
- **THEN** 居中模态，显示任务名、奖励物品列表（`InkCell`）、经验/铜钱数值（DIN 字体），底部"领取"按钮

### Requirement: 设置页面
系统 SHALL 落地 `settings.html` 对应的设置页面，验证设置列表/开关/滑块模式。

#### Scenario: 布局
- **WHEN** 设置页激活
- **THEN** 左侧分类侧边栏（画面/音效/操作/系统）、右侧设置项列表，每项含标签 + 控件（开关/滑块/下拉）

### Requirement: 资源导入
系统 SHALL 将 `hundun-yy-ui/assets` 下的水墨背景纹理与背景图导入 Flax Content，供页面引用。

#### Scenario: 背景图可用
- **WHEN** 加载页或战斗 HUD 需要水墨背景
- **THEN** 可从 `Content/InkWash/Textures/` 加载对应纹理资产（如 `bg-loading-landscape`、`bg-ink-wash-scene`）

### Requirement: MainUIManager 接入
系统 SHALL 在 `MainUIManager` 进入 World 场景后，激活 `InkPageRouter` 承载战斗 HUD 与子页面。

#### Scenario: 进入世界
- **WHEN** 玩家完成角色创建并进入 World 场景
- **THEN** `MainUIManager` 显示 `InkPageRouter`，路由器默认激活战斗 HUD 页面

## MODIFIED Requirements

### Requirement: MainUIManager UI 切换
**原有**: `MainUIManager` 在 Login 场景显示 `AuthenticationUI`，进入游戏后显示 `GameMainUI`（WoW 风格 HUD）。

**修改为**: `MainUIManager` 在 Login 场景显示 `AuthenticationUI`（保持不变），进入 World 场景后激活 `InkPageRouter` 并默认显示战斗 HUD。`GameMainUI` 暂时保留不激活，作为旧 UI 备份，避免回归。

## REMOVED Requirements
无
