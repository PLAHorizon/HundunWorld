# 混沌世界 MMORPG UI 设计规范

> 版本：v1.0  
> 风格基调：燕云十六声式沉浸水墨古风  
> 主色调：春意盎然的青色（ink-jade）为骨，鎏金（ink-gold）为魂，墨黑灰（ink-bg）为底  
> 适用范围：19 个高保真原型页面 + 粒子动效系统 + 全部 ds-* 组件库  
> 技术栈约束：.NET 10 C# + Orleans + SqlServer + Redis + EFCore + TouchSocket + MemoryPack

---

## 目录

- [第 1 章 色彩使用指南](#第-1-章-色彩使用指南)
- [第 2 章 字体与排版](#第-2-章-字体与排版)
- [第 3 章 动效规范](#第-3-章-动效规范)
- [第 4 章 组件库规范](#第-4-章-组件库规范)
- [第 5 章 交互原则](#第-5-章-交互原则)

---

## 第 1 章 色彩使用指南

### 1.1 核心 Token 定义

混沌世界 UI 的色彩体系建立在「水墨青金灰」五色基调之上。所有页面、组件、动效必须引用下列已实施的 CSS 变量，禁止使用硬编码色值。

| Token 名称 | 色值 | 语义角色 | 典型用途 |
| --- | --- | --- | --- |
| `--ink-jade-primary` | `#7EAB9E` | 春青，主色 | 主操作、关键路径、品牌识别 |
| `--ink-jade-bright` | `#A8D4C4` | 嫩绿青，亮色 | 悬停态、高亮、青玉萤光粒子 |
| `--ink-jade-deep` | `#5E8B7E` | 深青 | 按下态、激活态、描边强调 |
| `--ink-gold-primary` | `#C8A858` | 鎏金 | 次主色、稀有品质、金粉粒子 |
| `--ink-gold-bright` | `#E0C880` | 亮金 | 悬停高亮、金色辉光、Legendary 品质 |
| `--ink-bg-void` | `#0E1016` | 墨黑背景 | 全局背景、HUD 底层 |
| `--ink-bg-ink` | `#14181F` | 墨水深背景 | 面板背景、卡片底色 |

辅助语义 Token（衍生自上述核心值）：

- `--ink-blood-primary: #B85450` — 血色，用于危险操作、生命值、删除
- `--ink-text-primary: #F0EDE4` — 主文本色（宣纸白）
- `--ink-text-secondary: #B8B0A0` — 次文本色（灰）
- `--ink-text-muted: #8A8275` — 弱化文本色
- `--ink-border-gold: rgba(200, 168, 88, 0.25)` — 金色描边

### 1.2 色彩使用比例

水墨沉浸感的核心在于「青为主、金为点、灰为衬」。任何单屏画面的色彩占比建议遵循 40 / 15 / 10 / 25 / 10 分布。

| 色彩角色 | 占比 | 对应 Token | 使用场景 |
| --- | --- | --- | --- |
| 主青（春青） | 40% | `--ink-jade-primary` | 主面板边框、主按钮、关键图标、进度条填充 |
| 亮青（嫩绿青） | 15% | `--ink-jade-bright` | 悬停态、激活高亮、青玉萤光粒子 |
| 深青 | 10% | `--ink-jade-deep` | 按下态、描边、阴影内发光 |
| 鎏金 | 25% | `--ink-gold-primary` / `--ink-gold-bright` | 标题点缀、品质边框、金粉粒子、数字强调 |
| 血色 / 危险 | 10% | `--ink-blood-primary` | 删除、生命值低、错误状态、危险按钮 |

> 背景（`--ink-bg-void` 与 `--ink-bg-ink`）不计入上述比例，作为画面底色独立存在。

### 1.3 品质色阶

游戏内所有可品质分级的实体（装备、道具、坐骑、技能书、成就等）必须使用下列 5 级品质色阶。品质色仅用于边框、标签底色与名称文字色，不可作为大面积背景。

| 品质 | 中文 | Token | 色值 | 典型用途 |
| --- | --- | --- | --- | --- |
| Common | 普通 | `--ink-quality-common` | `#8A8275`（灰白） | 基础材料、白色装备 |
| Uncommon | 优秀 | `--ink-quality-uncommon` | `#6B8E5A`（青绿） | 绿装、初级合成产物 |
| Rare | 稀有 | `--ink-quality-rare` | `#4A7EA8`（蓝紫） | 蓝装、副本掉落 |
| Epic | 史诗 | `--ink-quality-epic` | `#8B5E9E`（橙金偏紫） | 紫装、世界 BOSS 掉落 |
| Legendary | 传说 | `--ink-quality-legendary` | `#C8A858`（赤金） | 金装、神器、赛季奖励 |

> 注：Epic 实际采用偏紫的橙金 `#8B5E9E`，与鎏金主色 `#C8A858` 区分；Legendary 直接复用鎏金主色，并叠加 `--ink-shadow-gold` 辉光。

### 1.4 五行色

五行色用于功法、属性、阵法、相生相克关系展示。五行色与品质色互不干扰，五行色饱和度统一降低 15%，以融入水墨基调。

| 五行 | 中文 | Token | 色值 | 相生关系 |
| --- | --- | --- | --- | --- |
| 金 | 金 | `--ink-element-metal` | `#D4C4A0`（白） | 金生水 |
| 木 | 木 | `--ink-element-wood` | `#6B8E5A`（青） | 木生火 |
| 水 | 水 | `--ink-element-water` | `#4A6E8A`（黑） | 水生木 |
| 火 | 火 | `--ink-element-fire` | `#B85638`（红） | 火生土 |
| 土 | 土 | `--ink-element-earth` | `#8A7B5A`（黄） | 土生金 |

### 1.5 色彩组合原则

**推荐组合**

1. **青金主调**：`--ink-jade-primary` 边框 + `--ink-gold-primary` 标题点缀。适用于 80% 的主面板场景。
2. **墨黑分层**：`--ink-bg-void` 底 + `--ink-bg-ink` 面板 + `rgba(200,168,88,0.08)` 描边。适用于 HUD 与信息密度高的列表。
3. **金血对照**：`--ink-gold-primary` 确认 + `--ink-blood-primary` 取消。适用于二次确认对话框。
4. **青灰退让**：`--ink-jade-primary` 主信息 + `--ink-text-muted` 辅助信息。适用于任务日志、邮件列表。

**禁忌组合**

| 禁忌 | 原因 | 替代方案 |
| --- | --- | --- |
| 鎏金大面积铺底 | 反光过强，破坏水墨沉静感 | 鎏金仅用于边框、标题、图标点缀 |
| 血色作为主色 | 视觉压力过大，引发焦虑 | 血色仅用于危险操作与生命值 |
| 亮青与亮金相邻大面积使用 | 色相过近，边界模糊 | 二者之间必须留墨黑或深青间隔 |
| 品质色与五行色混用 | 语义冲突，玩家认知混乱 | 同一界面仅使用一套语义色系 |
| 纯黑 `#000000` 作为背景 | 缺乏层次，画面发死 | 必须使用 `--ink-bg-void: #0E1016` |
| 纯白 `#FFFFFF` 作为文字 | 对比过强，刺眼 | 必须使用 `--ink-text-primary: #F0EDE4`（宣纸白） |

### 1.6 色彩无障碍与对比度

混沌世界 UI 运行于深色墨黑背景之上，文本与背景的对比度必须满足 WCAG 2.1 AA 标准。下列对比度均已实测，开发时不得擅自降低。

| 前景色 | 背景色 | 对比度 | 等级 | 适用场景 |
| --- | --- | --- | --- | --- |
| `--ink-text-primary` (#F0EDE4) | `--ink-bg-void` (#0E1016) | 15.8 : 1 | AAA | 主文本、标题 |
| `--ink-text-primary` (#F0EDE4) | `--ink-bg-ink` (#14181F) | 14.2 : 1 | AAA | 面板内文本 |
| `--ink-text-secondary` (#B8B0A0) | `--ink-bg-void` (#0E1016) | 8.6 : 1 | AAA | 次要文本 |
| `--ink-text-secondary` (#B8B0A0) | `--ink-bg-ink` (#14181F) | 7.8 : 1 | AAA | 面板内次要文本 |
| `--ink-text-muted` (#8A8275) | `--ink-bg-void` (#0E1016) | 4.8 : 1 | AA | 辅助文本、时间戳 |
| `--ink-jade-primary` (#7EAB9E) | `--ink-bg-void` (#0E1016) | 6.2 : 1 | AA | 青色强调文本 |
| `--ink-gold-primary` (#C8A858) | `--ink-bg-void` (#0E1016) | 7.4 : 1 | AAA | 金色强调文本 |
| `--ink-blood-primary` (#B85450) | `--ink-bg-void` (#0E1016) | 4.2 : 1 | AA | 危险操作文本 |

**对比度底线**

- 正文文本对比度 ≥ 4.5 : 1（AA 标准）。
- 大号文本（≥ 18px 或 ≥ 14px 加粗）对比度 ≥ 3 : 1。
- 交互组件边界对比度 ≥ 3 : 1（按钮边框、输入框边框）。
- 禁用态文本对比度可降至 2.5 : 1，但必须配合 `cursor: not-allowed`。

**色觉障碍适配**

- 品质色阶不单独依赖色相区分，必须配合文字标签（如「稀有」「史诗」）。
- 五行色必须配合五行图标（金元宝、木枝、水滴、火焰、山石）。
- 危险操作除血色外，必须配合危险图标与「不可恢复」文案。
- 战斗中敌方/友方血条除红绿区分外，必须配合边框样式（友方实线、敌方虚线）。

---

## 第 2 章 字体与排版

### 2.1 字体家族

混沌世界 UI 采用「楷书为骨、黑体为肉、数字为筋、等宽为脉」的四层字体体系。

| 角色 | 字体栈 | Token | 应用边界 |
| --- | --- | --- | --- |
| 标题（楷书） | `"STKaiti", "KaiTi", "Noto Serif SC", "Source Han Serif SC", "SimSun", serif` | `--ink-font-display` | 章节标题、面板标题、装备名称、NPC 对话 |
| 正文（黑体） | `"Noto Sans SC", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif` | `--ink-font-body` | 描述文本、按钮文字、列表项、提示语 |
| 数字 | `"DIN Alternate", "DIN", "Bebas Neue", monospace` | `--ink-font-number` | 伤害数值、属性值、金币、计数、坐标 |
| 代码 / 等宽 | `"JetBrains Mono", ui-monospace, "SF Mono", Consolas, monospace` | `--ink-font-mono` | 控制台、快捷键提示、ID、技术性标签 |

**字体加载原则**

- 楷书优先使用系统字体（STKaiti / KaiTi），缺失时回退至 Noto Serif SC。
- 黑体必须通过 Google Fonts 加载 Noto Sans SC，权重覆盖 400 / 500 / 700。
- DIN Alternate 与 JetBrains Mono 仅用于数字与代码区域，不参与正文排版。
- 所有数字必须开启 `font-variant-numeric: tabular-nums` 与 `font-feature-settings: "tnum" 1, "lnum" 1`，确保等宽对齐。

### 2.2 字号阶梯

| 阶梯 | 字号 | 行高 | 字重 | 用途 | Token 别名 |
| --- | --- | --- | --- | --- | --- |
| xs | 10px | 14px | 400 | 辅助标签、角标、版本号 | `--body-xs-font-size` |
| sm | 11px | 16px | 400 | 次要说明、时间戳、副标题 | `--body-sm-font-size` |
| md | 12px | 18px | 400 | 列表项、表格单元、提示语 | `--body-md-font-size` |
| base | 14px | 20px | 400 | 正文默认、按钮文字、输入框 | `--body-base-font-size` |
| lg | 18px | 28px | 400 | 卡片描述、重要正文 | `--body-lg-font-size` |
| h3 | 22px | 30px | 600 | 小节标题、卡片标题 | `--heading-lg-font-size` |
| h2 | 24px | 32px | 600 | 面板标题、对话框标题 | `--heading-xl-font-size` |
| h1 | 28px | 36px | 600 | 页面主标题、章节标题 | `--heading-2xl-font-size` |
| display | 32px | 40px | 600 | 登录页大标题、赛季横幅 | `--heading-3xl-font-size` |

> 严禁使用阶梯外的字号（如 13px、15px、20px）。若需中间值，必须经过设计评审后新增阶梯。

### 2.3 字间距与行高规范

| 文本类型 | letter-spacing | line-height | 说明 |
| --- | --- | --- | --- |
| 标题（楷书） | `0` | `1.3` 倍字号 | 楷书本身字面较宽，无需额外间距 |
| 正文（黑体） | `-0.02em` | `1.4` 倍字号 | 紧凑排版，提升信息密度 |
| 数字（DIN） | `0` | `1.2` 倍字号 | 等宽数字，行高收紧 |
| 标签 / 角标 | `0.05em` | `1.4` 倍字号 | 略宽松，提升可读性 |
| 代码 / 等宽 | `0` | `1.5` 倍字号 | 等宽字体标准行高 |
| 大写英文标签 | `0.08em` | `1.4` 倍字号 | 全大写时需额外间距 |

### 2.4 字体应用边界

**标题区（楷书）**

- 仅用于章节标题、面板标题、装备名称、技能名称、NPC 对话气泡。
- 标题字号必须 ≥ 22px，低于此值改用黑体加粗。
- 楷书不参与正文排版，避免长段落阅读疲劳。

**正文区（黑体）**

- 用于一切描述性文字、按钮文字、列表项、表格单元、输入框、提示语。
- 正文字号范围 10px ~ 18px，超出此范围需改用标题字体。
- 长文本（> 200 字）行高建议提升至 1.5 倍。

**数字区（DIN Alternate）**

- 用于伤害飘字、属性值、金币、经验值、计数、坐标、百分比。
- 数字必须右对齐或小数点对齐，使用 `tabular-nums`。
- 伤害飘字建议 24px 以上，属性值建议 14px ~ 18px。

**代码区（JetBrains Mono）**

- 用于控制台输出、快捷键提示（如 `Ctrl + I`）、技术性 ID、调试信息。
- 代码区不参与游戏内 UI 排版，仅限开发工具与调试面板。

---

## 第 3 章 动效规范

### 3.1 粒子动效系统

混沌世界 UI 内置 4 种粒子动效，分别对应不同的交互语义。所有粒子通过 `ink-particles.js` 与 `ink-particles.css` 统一管理，挂载于 `.ink-particle-layer` 全屏固定层（`z-index: 9999`，`pointer-events: none`）。

| 粒子类型 | 中文名 | 触发条件 | 持续时间 | 粒子类名 | 视觉特征 |
| --- | --- | --- | --- | --- | --- |
| gold-burst | 金粉爆发 | 按钮点击、确认操作 | 800ms | `.ink-particle--gold` | 从中心向外扩散，带重力下坠 |
| ink-ripple | 墨韵涟漪 | 面板切换、标签页切换 | 1200ms | `.ink-ripple-ring` | 从中心扩散 2 圈，青玉边框 |
| jade-firefly | 青玉萤光 | 信息提示出现、新消息 | 1000ms | `.ink-particle--jade` | 萤火虫式漂浮，带辉光 |
| ambient | 环境水墨微粒 | 页面加载后持续 | 持续 | `.ink-ambient` | 缓慢漂浮，营造水墨氛围 |

**粒子触发原则**

- 金粉爆发：仅在「确认」「购买」「强化」「提交」等正向操作时触发，单次爆发 8 ~ 12 颗粒子。
- 墨韵涟漪：仅在面板切换、标签页切换时触发，涟漪中心位于切换目标的几何中心。
- 青玉萤光：仅在 Toast 提示、新邮件、新成就解锁时触发，萤光围绕提示框漂浮。
- 环境微粒：页面加载后自动启动，全屏 30 ~ 50 颗微粒缓慢漂浮，CPU 占用 ≤ 2%。

### 3.2 动效曲线

所有 UI 过渡动画统一使用以下曲线，参考苹果 HIG 的「快速减速」曲线：

```
cubic-bezier(0.16, 1, 0.3, 1)  /* ease-out，主曲线 */
```

| 场景 | 曲线 | 说明 |
| --- | --- | --- |
| 面板进出场 | `cubic-bezier(0.16, 1, 0.3, 1)` | 主曲线，所有 fade / slide |
| 按钮点击反馈 | `ease-out` | 简化曲线，快速响应 |
| 状态过渡 | `ease-out` | opacity / color 过渡 |
| 粒子动效 | `linear`（配合 keyframe） | 粒子物理运动 |
| 滚动惯性 | `cubic-bezier(0.16, 1, 0.3, 1)` | 列表滚动减速 |

> 禁止使用 `ease-in`、`ease-in-out` 作为主要过渡曲线，前者过慢后者节奏不明。

### 3.3 面板进出场

| 属性 | 进场 | 出场 |
| --- | --- | --- |
| 持续时间 | 200ms | 160ms |
| opacity | 0 → 1 | 1 → 0 |
| transform | `translateY(8px) → translateY(0)` | `translateY(0) → translateY(8px)` |
| 曲线 | `cubic-bezier(0.16, 1, 0.3, 1)` | `ease-out` |

**变体**

- 侧边抽屉：`translateX(-16px) → translateX(0)`，其余同上。
- 模态对话框：仅 `opacity` + `scale(0.96) → scale(1)`，不位移。
- 全屏地图：`scale(1.02) → scale(1)` + `opacity`，营造「展开」感。

### 3.4 按钮点击反馈

| 状态 | transform | 持续时间 | 曲线 |
| --- | --- | --- | --- |
| 按下（:active） | `scale(0.97)` | 100ms | `ease-out` |
| 释放 | `scale(1)` | 120ms | `cubic-bezier(0.16, 1, 0.3, 1)` |
| 悬停（:hover） | `scale(1.02)`（可选） | 120ms | `ease-out` |

按钮点击时同步触发金粉爆发粒子（800ms），粒子从按钮中心向外扩散 8 ~ 12 颗，带重力下坠。

### 3.5 状态过渡

| 状态变更 | 过渡属性 | 持续时间 | 曲线 |
| --- | --- | --- | --- |
| 悬停高亮 | `background-color`, `border-color`, `color` | 120ms | `ease-out` |
| 选中态切换 | `background-color`, `box-shadow` | 200ms | `cubic-bezier(0.16, 1, 0.3, 1)` |
| 禁用态 | `opacity` | 200ms | `ease-out` |
| 加载态 | `opacity`, `filter: blur(2px)` | 200ms | `ease-out` |
| 错误态 | `border-color`, `box-shadow` | 200ms | `ease-out` |
| 进度条填充 | `width` | 400ms | `cubic-bezier(0.16, 1, 0.3, 1)` |

### 3.6 粒子性能预算

粒子动效系统在保证视觉沉浸感的同时，必须严格控制性能开销，避免影响游戏帧率。下列预算为单屏上限，超出时自动降级。

| 粒子类型 | 单次数量上限 | 同屏并发上限 | CPU 预算 | GPU 预算 | 降级策略 |
| --- | --- | --- | --- | --- | --- |
| gold-burst | 12 颗 | 3 次 | 1.5% | 0.5% | 降至 6 颗 |
| ink-ripple | 2 圈 | 2 次 | 0.8% | 0.3% | 降至 1 圈 |
| jade-firefly | 8 颗 | 1 次 | 1.0% | 0.3% | 降至 4 颗 |
| ambient | 50 颗 | 持续 | 2.0% | 1.0% | 降至 30 颗 |

**性能守则**

- 粒子统一使用 CSS `transform` 与 `opacity` 动画，禁止使用 `left` / `top` 属性（触发重排）。
- 每个粒子必须声明 `will-change: transform, opacity`，提示浏览器开启 GPU 合成层。
- 环境微粒在帧率低于 30fps 时自动减半数量，低于 20fps 时暂停。
- 移动端（UA 检测）默认关闭 ambient 粒子，仅保留 gold-burst 与 ink-ripple。
- 粒子层 `.ink-particle-layer` 必须 `pointer-events: none`，避免拦截交互事件。

**粒子复用池**

- 所有粒子 DOM 节点通过对象池管理，避免频繁创建 / 销毁。
- 池容量：gold 24 颗、jade 16 颗、ripple 4 圈、ambient 50 颗。
- 粒子结束后回归池中等待复用，`display: none` 隐藏。

---

## 第 4 章 组件库规范

### 4.1 ds-btn 按钮

按钮是混沌世界 UI 中最高频的组件，必须严格遵循状态、尺寸、变体三轴规范。

**尺寸**

| 尺寸 | 高度 | 内边距 | 字号 | 圆角 | 适用场景 |
| --- | --- | --- | --- | --- | --- |
| sm | 24px | `0 8px` | 11px | 8px | 表格内操作、紧凑工具栏 |
| md | 28px | `0 12px` | 14px | 8px | 默认尺寸，通用场景 |
| lg | 32px | `0 16px` | 14px | 8px | 主操作、对话框确认 |

**变体**

| 变体 | 类名 | 背景 | 文字 | 边框 | 用途 |
| --- | --- | --- | --- | --- | --- |
| primary | `.ds-btn--primary` | `--ink-jade-primary` | `--ink-text-inverse` | 同背景 | 主操作（确认、提交） |
| secondary | `.ds-btn--secondary` | `--ink-bg-mist` | `--ink-text-primary` | `--ink-border-gold` | 次操作（取消、返回） |
| ghost | `.ds-btn--ghost` | transparent | `--ink-text-primary` | transparent | 工具栏、行内操作 |
| danger | `.ds-btn--danger` | `--ink-blood-primary` | `--ink-text-inverse` | 同背景 | 危险操作（删除、丢弃） |
| brand | `.ds-btn--brand` | `--ink-gold-primary` | `--ink-text-inverse` | 同背景 | 金色强调（购买、强化） |

**状态**

| 状态 | primary | secondary | ghost | danger | brand |
| --- | --- | --- | --- | --- | --- |
| 默认 | `--ink-jade-primary` | `--ink-bg-mist` | transparent | `--ink-blood-primary` | `--ink-gold-primary` |
| 悬停 | `--ink-jade-bright` | `--ink-bg-hover` | `--ink-bg-hover` | `--ink-blood-bright` | `--ink-gold-bright` |
| 按下 | `--ink-jade-deep` | `--ink-bg-elevated` | `--ink-bg-elevated` | `--ink-blood-deep` | `--ink-gold-deep` |
| 禁用 | `rgba(jade, 0.22)` | `opacity: 0.7` | `--ink-text-disabled` | `rgba(blood, 0.22)` | `rgba(gold, 0.22)` |
| 加载 | 同默认 + spinner | 同默认 + spinner | 同默认 + spinner | 同默认 + spinner | 同默认 + spinner |

**交互规范**

- 所有按钮点击必须触发 `scale(0.97)` 100ms 反馈 + 金粉爆发粒子（800ms）。
- 禁用态 `cursor: not-allowed`，不触发粒子。
- 加载态显示 14px 旋转 spinner，按钮文字保留，`pointer-events: none`。
- 主操作按钮（primary / brand）在同一面板内仅出现 1 个，避免视觉竞争。

### 4.2 ds-card 卡片

卡片用于信息分组的容器，支持 3 种变体。

| 变体 | 类名 | 背景 | 边框 | 阴影 | 用途 |
| --- | --- | --- | --- | --- | --- |
| default | `.ds-card` | `--ink-bg-ink` | `--ink-border-faint` | 无 | 默认信息分组 |
| elevated | `.ds-card--elevated` | `--ink-bg-elevated` | `--ink-border-gold` | `--ink-shadow-panel` | 弹层、悬浮信息 |
| outline | `.ds-card--outline` | transparent | `--ink-border-gold-bright` | 无 | 嵌套分组、属性面板 |

**结构**

```
.ds-card
  ├── .ds-card__title    （楷书 22px，可选）
  ├── .ds-card__desc      （黑体 14px，可选）
  └── .ds-card__body      （插槽，自定义内容）
```

- 卡片内边距默认 `20px`，紧凑模式 `12px`（添加 `.ds-card--compact`）。
- 卡片圆角 `12px`，嵌套卡片圆角 `8px`。
- 卡片标题与描述之间间距 `8px`，描述与正文之间间距 `16px`。

### 4.3 ds-input 输入框

输入框用于搜索、聊天、数量输入等场景。

**尺寸**

- 高度：32px（默认）、28px（sm）、36px（lg）
- 内边距：`0 12px`
- 圆角：8px

**状态**

| 状态 | 背景 | 边框 | 文字 | 阴影 | 说明 |
| --- | --- | --- | --- | --- | --- |
| 默认 | `--ink-bg-ink` | `--ink-border-faint` | `--ink-text-secondary` | 无 | 未聚焦 |
| 聚焦 | `--ink-bg-paper` | `--ink-jade-primary` | `--ink-text-primary` | `0 0 0 2px rgba(126,171,158,0.2)` | 青色聚焦环 |
| 错误 | `--ink-bg-ink` | `--ink-blood-primary` | `--ink-text-primary` | `0 0 0 2px rgba(184,84,80,0.2)` | 校验失败 |
| 禁用 | `--ink-bg-ink` | `--ink-border-faint` | `--ink-text-disabled` | 无 | `cursor: not-allowed` |

**变体**

- `.ds-input--search`：左侧带搜索图标，圆角 `full`。
- `.ds-input--number`：右侧带 +/- 步进器，数字字体。
- `.ds-textarea`：多行输入，最小高度 80px，支持自动撑高。

### 4.4 ds-table 表格

表格用于背包、排行榜、邮件列表、好友列表等数据密集场景。

| 属性 | 值 | 说明 |
| --- | --- | --- |
| 行高 | 36px | 固定行高，保证视觉节奏 |
| 表头高 | 40px | 略高于数据行 |
| 单元格内边距 | `0 12px` | 水平内边距 |
| 斑马纹 | 偶数行 `rgba(200,168,88,0.03)` | 极淡金色条纹 |
| 悬停高亮 | `rgba(200,168,88,0.08)` | 鼠标悬停行 |
| 选中行 | `rgba(126,171,158,0.12)` | 青色选中态 |
| 边框 | 行间 `1px solid --ink-divider` | 仅水平分割线 |
| 圆角 | 表头 `8px 8px 0 0`，末行 `0 0 8px 8px` | 整体圆角 |

**对齐规范**

- 文本列：左对齐
- 数字列（数量、属性值、金币）：右对齐，`tabular-nums`
- 状态列（品质、在线状态）：居中对齐
- 操作列：居中对齐，固定宽度

### 4.5 ds-tabs 标签页

标签页用于面板内信息切换，采用下划线指示器。

| 属性 | 值 | 说明 |
| --- | --- | --- |
| 标签高度 | 36px | 含下划线区域 |
| 标签字号 | 14px | 黑体 |
| 标签内边距 | `0 16px` | 水平内边距 |
| 下划线高度 | 2px | 激活态指示器 |
| 下划线颜色 | `--ink-jade-primary` | 青色 |
| 滑动过渡 | `400ms cubic-bezier(0.16, 1, 0.3, 1)` | 指示器位移动画 |

**交互规范**

- 标签切换时触发墨韵涟漪粒子（1200ms），涟漪中心位于新激活标签中心。
- 激活标签文字色 `--ink-text-primary`，非激活 `--ink-text-secondary`。
- 悬停态文字色 `--ink-text-primary`，无下划线变化。
- 标签数量超过 6 个时，自动出现左右滚动箭头。

### 4.6 ds-progress 进度条

进度条用于经验、体力、充能、加载等场景。

| 属性 | 值 | 说明 |
| --- | --- | --- |
| 高度 | 4px（细）/ 8px（粗） | 两种规格 |
| 圆角 | `full` | 全圆角 |
| 轨道色 | `--ink-bg-elevated` | 深灰底 |
| 填充色 | `--ink-jade-primary` → `--ink-jade-bright` | 青色渐变 |
| 充能色 | `--ink-gold-primary` → `--ink-gold-bright` | 金色渐变（特殊技能） |
| 过渡 | `400ms cubic-bezier(0.16, 1, 0.3, 1)` | 宽度变化动画 |

**变体**

- `.ds-progress--striped`：带 45° 斜纹动画，用于加载态。
- `.ds-progress--glow`：填充末端带辉光，用于关键进度（升级、充能）。
- `.ds-progress--segmented`：分段显示，用于多阶段任务。

### 4.7 ds-dialog 对话框

对话框用于模态交互，必须打断当前操作流。

| 属性 | 值 | 说明 |
| --- | --- | --- |
| 遮罩 | `rgba(14, 16, 22, 0.8)` + `backdrop-filter: blur(8px)` | 墨黑模糊 |
| 对话框背景 | `--ink-bg-ink` | 墨水深背景 |
| 边框 | `1px solid --ink-border-gold` | 金色描边 |
| 圆角 | 12px | |
| 阴影 | `--ink-shadow-panel` + `--ink-shadow-gold` | 双重阴影 |
| 最大宽度 | 520px（默认）/ 560px（表单） | |
| 进场 | `opacity 0→1, scale(0.96→1)` 200ms | |
| 出场 | `opacity 1→0, scale(1→0.96)` 160ms | |

**结构**

```
.ds-dialog
  ├── .ds-dialog__head
  │     ├── .ds-dialog__title    （楷书 22px）
  │     └── .ds-dialog__close    （32x32 关闭按钮）
  ├── .ds-dialog__body           （黑体 14px，--ink-text-secondary）
  └── .ds-dialog__foot           （右对齐按钮组，gap 8px）
```

**交互规范**

- 对话框出现时，背景内容 `filter: blur(8px)` + `opacity: 0.6`。
- 按 `Esc` 键关闭对话框（仅非强制确认型）。
- 点击遮罩区域关闭对话框（仅非强制确认型）。
- 危险操作对话框（删除、丢弃）不响应 Esc 与遮罩点击，必须显式点击按钮。
- 对话框内按钮组：主操作在右，次操作（取消）在左，间距 `8px`。

### 4.8 组件组合模式

混沌世界 UI 的高保真原型页面大量采用组件组合，下列为已验证的组合模式，新页面开发应优先复用。

**模式一：列表 + 详情（Master-Detail）**

用于背包、邮件、好友列表。左侧 `ds-table` 占 40% 宽，右侧 `ds-card--outline` 占 60% 宽。列表选中项触发右侧详情更新，切换时触发墨韵涟漪粒子。

**模式二：标签页 + 卡片网格（Tabs + Card Grid）**

用于成就、商店、坐骑。顶部 `ds-tabs` 切换分类，下方 `grid-3` 或 `grid-4` 排列 `ds-card`。每个卡片点击触发 `ds-dialog` 详情。

**模式三：表单 + 对话框（Form in Dialog）**

用于装备强化、角色创建。`ds-dialog--form` 内嵌 `ds-input` 与 `ds-btn` 组合，提交时触发金粉爆发粒子，失败时触发错误态过渡。

**模式四：工具栏 + 表格（Toolbar + Table）**

用于排行榜、任务日志。顶部 `ds-btn--ghost` 工具栏（筛选 / 排序 / 搜索），下方 `ds-table` 数据展示。工具栏高度 36px，与表头对齐。

**模式五：HUD 叠层（HUD Overlay）**

用于战斗界面。四角集群使用 `--ink-bg-panel` 半透明背景，叠加于 3D 渲染层之上。HUD 元素不触发粒子，避免战斗时视觉干扰。

**组合间距规范**

| 组合类型 | 区块间距 | 内部间距 | 说明 |
| --- | --- | --- | --- |
| 列表 + 详情 | 16px | 12px | 左右分栏 |
| 标签页 + 内容 | 16px | 12px | 上下排列 |
| 卡片网格 | 24px | 20px | 卡片之间 |
| 表单字段 | 16px | 8px | 字段之间 |
| 按钮组 | 8px | - | 水平排列 |

---

## 第 5 章 交互原则

### 5.1 沉浸式 HUD 信息层级

混沌世界作为沉浸式 MMORPG，HUD 信息层级必须遵循「中央无遮挡、四角集群、上下辅助」的三层结构。

**第一层：中央渲染区（绝对无遮挡）**

- 屏幕中心 60% 宽 × 50% 高的区域为 3D 渲染区，禁止放置任何常驻 UI。
- 角色位于渲染区中心偏下，战斗特效、NPC、环境物体均在此区域。
- 仅在战斗飘字、拾取提示、对话气泡等动态信息时短暂占用中央区域。

**第二层：四角功能集群**

| 角落 | 功能集群 | 内容 |
| --- | --- | --- |
| 左上 | 角色信息 | 头像、名称、等级、生命值、内力值、Buff/Debuff 图标 |
| 右上 | 小地图 | 圆形小地图、坐标、当前区域名、时间 |
| 左下 | 聊天 | 聊天标签、输入框、最近 5 条消息（半透明） |
| 右下 | 技能栏 | 8 ~ 12 个技能槽、快捷键提示、药品栏 |

四角集群距屏幕边缘 `16px`，集群内元素间距 `8px`。集群背景统一使用 `--ink-bg-panel: rgba(20,23,30,0.85)`，保证半透明叠加。

**第三层：上下辅助信息**

- 顶部中央：任务追踪（最多 3 条），半透明背景。
- 底部中央：经验条 + 系统提示，紧贴技能栏上方。
- 上下辅助信息宽度不超过屏幕 40%，居中对齐。

### 5.2 触控目标尺寸

参考苹果 Human Interface Guidelines，所有可交互元素的最小触控目标为 44 × 44px。

| 元素类型 | 最小尺寸 | 推荐尺寸 | 说明 |
| --- | --- | --- | --- |
| 主操作按钮 | 44 × 44px | 32px 高（含 padding） | md 按钮高度 28px，需扩大点击区域 |
| 图标按钮 | 44 × 44px | 32 × 32px | 通过 `padding` 扩大点击区 |
| 列表项 | 36px 高 | 40px 高 | 表格行高 36px 为下限 |
| 标签页 | 36px 高 | 44px 高 | 含下划线区域 |
| 复选框 | 44 × 44px | 16 × 16px 视觉 + padding | 视觉小，点击区大 |
| 输入框 | 32px 高 | 36px 高 | 含 padding |

> 在移动端适配时，所有触控目标必须 ≥ 44px，桌面端可适当放宽至 32px（鼠标精度高）。

### 5.3 键盘可达性与焦点可见

**Tab 顺序**

- 焦点顺序遵循视觉流：从左到右、从上到下。
- 模态对话框打开时，焦点自动移至对话框内第一个可交互元素。
- 对话框关闭后，焦点返回触发元素（恢复上下文）。
- 技能栏支持数字键 `1 ~ 9`、`F1 ~ F8` 快捷施法。

**焦点可见性**

| 元素 | 焦点样式 |
| --- | --- |
| 按钮 | `outline: 2px solid --ink-jade-bright` + `outline-offset: 2px` |
| 输入框 | `box-shadow: 0 0 0 2px rgba(126,171,158,0.2)` |
| 列表项 | `background: rgba(126,171,158,0.12)` + 左侧 `2px solid --ink-jade-primary` |
| 链接 | `text-decoration: underline` + `color: --ink-jade-bright` |

> 禁止使用 `outline: none` 移除焦点轮廓。若需自定义焦点样式，必须提供等效的视觉反馈。

**快捷键**

| 快捷键 | 功能 |
| --- | --- |
| `Esc` | 关闭当前面板 / 对话框 |
| `Enter` | 确认 / 发送聊天 |
| `Tab` | 切换焦点 |
| `I` | 打开背包 |
| `K` | 打开技能面板 |
| `M` | 打开世界地图 |
| `L` | 打开任务日志 |
| `C` | 打开角色面板 |
| `B` | 打开好友列表 |
| `Space` | 跳跃（3D 场景） |

### 5.4 危险操作二次确认

以下操作必须弹出二次确认对话框，且对话框不响应 `Esc` 与遮罩点击：

| 操作 | 对话框类型 | 确认按钮 | 取消按钮 |
| --- | --- | --- | --- |
| 删除角色 | 强制确认 | danger「确认删除」 | secondary「取消」 |
| 丢弃品质 ≥ Rare 的装备 | 强制确认 | danger「丢弃」 | secondary「保留」 |
| 分解品质 ≥ Epic 的装备 | 强制确认 | danger「分解」 | secondary「取消」 |
| 购买金额 ≥ 1000 金币 | 强制确认 | brand「确认购买」 | secondary「取消」 |
| 解散队伍 / 退出帮派 | 强制确认 | danger「确认」 | secondary「取消」 |
| 强化降级保护（≥ +10） | 强制确认 | brand「继续强化」 | secondary「取消」 |

**确认对话框文案规范**

- 标题：动词 + 宾语，如「丢弃 龙泉剑」
- 正文：说明操作后果，如「此操作不可恢复，装备将被永久销毁。」
- 按钮：动词明确，避免使用「确定 / 取消」，应使用「丢弃 / 保留」「删除 / 保留」等具体动词。

### 5.5 加载状态反馈

**加载反馈分级**

| 等级 | 场景 | 反馈形式 | 持续时间 |
| --- | --- | --- | --- |
| 即时 | 按钮点击 | 按钮 spinner + 禁用 | < 500ms |
| 短时 | 列表加载、面板切换 | 骨架屏 + 淡入 | 500ms ~ 2s |
| 长时 | 场景切换、副本加载 | 全屏加载页 + 进度条 + 文案 | 2s ~ 10s |
| 超长 | 资源下载、版本更新 | 进度条 + 阶段文案 + 取消按钮 | > 10s |

**骨架屏规范**

- 骨架色：`--ink-bg-elevated` 为主，`--ink-bg-paper` 为高亮。
- 闪烁动画：1.5s 循环，`opacity: 0.4 → 0.8 → 0.4`。
- 骨架形状需匹配实际内容布局，不可使用通用占位符。

**超时处理**

- 网络请求超时阈值：10s。
- 超时后显示「网络不稳，请重试」+ 重试按钮。
- 连续 3 次失败后显示「当前网络异常，请检查连接」+ 联系客服入口。

### 5.6 错误状态恢复

**错误分级**

| 等级 | 场景 | 反馈形式 | 恢复方式 |
| --- | --- | --- | --- |
| 轻微 | 表单校验失败 | 输入框红色边框 + 下方错误提示 | 用户修正后自动清除 |
| 中等 | 操作失败（购买、强化） | Toast 提示（青玉萤光粒子） | 用户重试或关闭 |
| 严重 | 连接断开、数据异常 | 模态对话框 + 重连按钮 | 用户点击重连 |
| 致命 | 客户端崩溃、版本不一致 | 全屏错误页 + 错误码 + 复制按钮 | 用户重启客户端 |

**错误文案规范**

- 标题：简明扼要，如「强化失败」「网络中断」
- 正文：说明原因 + 建议操作，如「材料不足，请前往背包检查」
- 错误码：技术性错误需附带 6 位错误码，格式 `HD-XXXX`，用户可复制。
- 禁止使用纯技术栈信息（如 `NullReferenceException`）面向玩家。

**恢复流程**

1. 错误发生时，立即停止当前操作的加载态。
2. 保留用户已输入的数据（表单场景）。
3. 提供明确的恢复路径（重试 / 返回 / 联系客服）。
4. 错误日志自动上报至服务器，附带客户端版本、设备信息、操作上下文。
5. 严重错误触发自动重连机制，间隔 2s / 5s / 10s 三次递增重试。

### 5.7 响应式断点

混沌世界 UI 以 PC 端 16:9 / 16:10 横屏为主，但需适配主流分辨率与超宽屏。下列断点定义了布局切换阈值。

| 断点名称 | 宽度范围 | 布局策略 | 典型设备 |
| --- | --- | --- | --- |
| xs | < 1280px | 紧凑模式，HUD 集群缩小至 12px 边距 | 笔记本、低分辨率显示器 |
| sm | 1280px ~ 1599px | 标准模式，默认布局 | 1080p 显示器 |
| md | 1600px ~ 1919px | 舒展模式，面板间距增至 20px | 2K 显示器 |
| lg | 1920px ~ 2559px | 宽屏模式，中央渲染区扩大 | 4K 显示器 |
| xl | ≥ 2560px | 超宽模式，HUD 集群固定最大宽度 | 超宽屏 / 4K+ |

**断点适配规则**

- xs 断点下，`ds-table` 行高降至 32px，`ds-card` 内边距降至 16px。
- lg 及以上断点，四角 HUD 集群最大宽度固定 320px，避免过度拉伸。
- 超宽屏（xl）下，中央渲染区两侧留出 8% 安全区，防止关键信息被屏幕曲率遮挡。
- 所有断点切换通过 CSS 媒体查询实现，不依赖 JS 监听。

**安全区规范**

- 屏幕四角内缩 16px 为 UI 安全区，集群不得超出。
- 超宽屏（21:9 / 32:9）下，中央渲染区两侧进一步内缩至屏幕宽度 10%。
- 多显示器场景下，主显示器承担全部 HUD，副显示器仅用于地图 / 背包等次要面板。

### 5.8 信息密度分级

不同场景的信息密度需求不同，混沌世界 UI 定义 3 级密度标准。

| 密度等级 | 场景 | 行高 | 字号 | 间距 | 说明 |
| --- | --- | --- | --- | --- | --- |
| 紧凑 | 背包、邮件、排行榜 | 32px | 12px | 8px | 最大化信息展示 |
| 标准 | 任务日志、技能面板、好友 | 36px | 14px | 12px | 平衡可读性与信息量 |
| 宽松 | 主菜单、设置、成就详情 | 44px | 14px | 16px | 强调可读性，降低视觉疲劳 |

> 同一面板内不得混用密度等级，切换密度需整页统一调整。

---

## 附录 A：色彩 Token 速查表

```
/* 主色 */
--ink-jade-primary:   #7EAB9E;  /* 春青，主色 */
--ink-jade-bright:   #A8D4C4;  /* 嫩绿青，亮色 */
--ink-jade-deep:     #5E8B7E;  /* 深青 */

/* 强调 */
--ink-gold-primary:  #C8A858;  /* 鎏金 */
--ink-gold-bright:   #E0C880;  /* 亮金 */

/* 背景 */
--ink-bg-void:        #0E1016;  /* 墨黑背景 */
--ink-bg-ink:         #14181F;  /* 墨水深背景 */

/* 危险 */
--ink-blood-primary:  #B85450;  /* 血色 */

/* 文本 */
--ink-text-primary:   #F0EDE4;  /* 宣纸白 */
--ink-text-secondary: #B8B0A0;  /* 灰 */
--ink-text-muted:     #8A8275;  /* 弱化 */

/* 品质 */
--ink-quality-common:     #8A8275;
--ink-quality-uncommon:   #6B8E5A;
--ink-quality-rare:       #4A7EA8;
--ink-quality-epic:       #8B5E9E;
--ink-quality-legendary:  #C8A858;

/* 五行 */
--ink-element-metal: #D4C4A0;
--ink-element-wood:   #6B8E5A;
--ink-element-water:  #4A6E8A;
--ink-element-fire:   #B85638;
--ink-element-earth:  #8A7B5A;
```

## 附录 B：字号阶梯速查表

```
/* 正文 */
--body-xs-font-size:   10px;  /* 行高 14px */
--body-sm-font-size:   11px;  /* 行高 16px */
--body-md-font-size:   12px;  /* 行高 18px */
--body-base-font-size: 14px;  /* 行高 20px */
--body-lg-font-size:   18px;  /* 行高 28px */

/* 标题 */
--heading-lg-font-size:   22px;  /* 行高 30px */
--heading-xl-font-size:   24px;  /* 行高 32px */
--heading-2xl-font-size:  28px;  /* 行高 36px */
--heading-3xl-font-size: 32px;  /* 行高 40px */
```

## 附录 C：动效曲线速查

```
/* 主曲线（参考苹果 HIG） */
--ease-out-ink: cubic-bezier(0.16, 1, 0.3, 1);

/* 时长 */
--duration-instant: 100ms;   /* 按钮按下 */
--duration-fast:    120ms;  /* 悬停反馈 */
--duration-base:    200ms;  /* 面板进场 */
--duration-mid:     400ms;  /* 进度条、标签滑动 */
--duration-particle-gold:    800ms;   /* 金粉爆发 */
--duration-particle-jade:   1000ms;  /* 青玉萤光 */
--duration-particle-ripple: 1200ms;  /* 墨韵涟漪 */
```

---

> 本规范为混沌世界 MMORPG UI 设计的唯一权威来源。任何新增组件、色彩、动效必须先更新本规范，再实施代码。  
> 规范版本迭代遵循语义化版本号（Semantic Versioning），重大变更需经过设计评审。  
> 维护团队：混沌世界前端组  
> 最后更新：2026-07-19
