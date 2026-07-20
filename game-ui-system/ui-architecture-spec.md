# 混沌世界 MMORPG UI 系统架构规范

> 版本：v1.0　|　风格基线：燕云十六声沉浸式水墨古风　|　高保真原型：19 页
> 技术定位：本规范面向 UI 前端实现与后端数据契约对接，定义色彩 Token、页面规格、品质/五行体系与粒子动效接入方式。
> 适用范围：混沌世界客户端 UI 层。后端（.NET 10 / Orleans / SqlServer / Redis / EFCore / TouchSocket / MemoryPack）数据契约以此文档字段名为准。

---

## 目录

- 第 1 章　系统总览
- 第 2 章　设计 Token 体系
- 第 3 章　19 个页面规格
- 第 4 章　品质与五行系统
- 第 5 章　前端集成指引

---

## 第 1 章　系统总览

### 1.1 子系统划分

混沌世界 UI 共 19 个页面，按职能划分为四大子系统。所有页面以「核心战斗 HUD」为枢纽，其余 18 页均为由 HUD 唤起的覆盖式面板，关闭后回到战斗现场。

| 子系统 | 页面 | 职责 |
| --- | --- | --- |
| ① 核心战斗 HUD | combat-hud、combat-hud-traditional | 全屏沉浸式战斗现场，承载血条、技能栏、Buff、小地图、任务追踪、导航栏 |
| ② 角色与背包 | character-panel、skill-panel、inventory、equipment-enhance、crafting、mount-pet | 角色成长与资源配置，围绕「人—物—艺」三条线 |
| ③ 任务与技能 | quest-log、world-map、compass、dungeon-entry | 江湖行进与目标追踪，解决「去哪、做什么、怎么去」 |
| ④ 社交与商城 | social-guild、friends、mail、mentor、leaderboard、achievement、shop | 人际关系、荣誉记录与商业化入口 |

子系统边界即导航边界：HUD 导航栏按上述顺序分组排列，跨子系统跳转一律经 HUD 中转，避免面板叠层过深。每个子系统对应后端一组 Grain 聚合：①战斗 Grain、②角色/背包 Grain、③任务/地图 Grain、④社交/商城 Grain，前端按子系统分通道订阅。

### 1.2 页面流转图（导航拓扑）

以 combat-hud 为根节点，导航拓扑呈星型辐射。每个二级页面顶部均含 `data-dom-id="back-hud"` 返回锚点，返回目标恒为 combat-hud。

```
                         ┌─ character-panel（角色面板）
                         ├─ skill-panel（武学技能）
          ┌─ 角色与背包 ─┼─ inventory（背包行囊）
          │              ├─ equipment-enhance（装备强化）
          │              ├─ crafting（制造技艺）
          │              └─ mount-pet（坐骑灵兽）
          │
          │              ┌─ quest-log（任务日志）
combat-hud├─ 任务与技能 ─┼─ world-map（世界地图）
 (根节点) │              ├─ compass（指南针）
          │              └─ dungeon-entry（江湖秘境）
          │
          │              ┌─ social-guild（社交门派）
          │              ├─ friends（江湖交游）
          ├─ 社交与商城 ─┼─ mail（飞鸽传书）
          │              ├─ mentor（师徒传承）
          │              ├─ leaderboard（江湖风云榜）
          │              ├─ achievement（江湖百艺录）
          │              └─ shop（商城商店）
          │
          └─ 战斗模式切换 ─→ combat-hud-traditional（传统模式 HUD）
                            （toggle-traditional 双向切换）
```

跨页快捷跳转约定（不走 HUD 中转的合法直达路径，其余跨页跳转一律视为违规）：

- friends → mail：好友详情区「飞鸽」按钮直达邮件撰写
- mentor → dungeon-entry：师徒周常任务「前往秘境」按钮
- inventory → equipment-enhance：选中装备后「强化」按钮
- character-panel → skill-panel：经脉页「心法」入口
- combat-hud 小地图 → world-map：点击小地图放大

所有跨页跳转触发 `panel:show` 自定义事件，由粒子系统接墨韵涟漪反馈。返回 HUD 时同样派发 `panel:show`，保证动效一致。

### 1.3 沉浸式 HUD 设计理念

1. **全屏画面优先**：HUD 采用 `fixed inset-0` 全屏铺底，游戏画面穿透可见，UI 元素以浮层卡片贴边排布，中心区域留给战斗现场。任何 UI 元素不得占据屏幕中心 60% 区域。
2. **水墨留白**：背景使用 `--ink-bg-void: #0E1016` 墨黑铺底，叠加 `radial-gradient` 极弱金雾与青雾，营造宣纸浸墨的呼吸感，避免纯黑死沉。明度对比控制在 4:1 以上保证可读。
3. **金为骨、青为魂**：鎏金 `--ink-gold-primary: #C8A858` 用于边框、强调与可交互态；春青 `--ink-jade-primary: #7EAB9E` 用于生命、增益、成功态。两色构成全系统唯一的品牌双色轴，禁止引入第三种品牌色。
4. **粒子即反馈**：点击出金粉、切页出涟漪、提示出萤光，所有交互均有水墨粒子动效兜底，动效曲线统一为 `cubic-bezier(0.16, 1, 0.3, 1)`（参考苹果 HIG ease-out）。
5. **面板即覆盖**：二级页面均为全屏覆盖式，自带半透明遮罩与 `backdrop-filter: blur(8px)`，关闭即回现场，不产生层级栈。同时只允许一个二级面板存在。
6. **古韵字体分层**：标题用 `--ink-font-display`（楷体/STKaiti）显文气，正文用 `--ink-font-body`（Noto Sans SC）保可读，数字用 `--ink-font-number`（DIN）显锐利。三套字体禁止混用场景。
7. **信息密度分级**：HUD 区为高密度（字号 11-13px），面板区为中密度（13-14px），详情弹窗为低密度（14-16px）。同一屏内不混用三档密度。

### 1.4 信息架构与可访问性

- 所有可交互元素必须含 `aria-label` 或可见文本，供屏幕阅读器识别。
- 键盘焦点环统一使用 `--ink-border-gold-bright`，禁用浏览器默认 outline。
- 焦点切换顺序遵循视觉流：从左上至右下，导航栏优先于内容区。
- `prefers-reduced-motion: reduce` 下，粒子动画降至 0.01ms，面板过渡改为瞬切。
- 色盲安全：品质/五行信息除颜色外必须辅以图标或文字标签，不可仅靠颜色区分。

---

## 第 2 章　设计 Token 体系

所有 Token 定义于 `colors_and_type.css`，采用「Light 基线 + 水墨 Dark 覆盖」双层结构。`:root` 先承载 Light 令牌，随后由同文件后半段 `:root` 覆盖为水墨暗色值。页面通过内联 `<style id="theme-vars">` 引入本文件内容。

### 2.1 命名规范

- 全局品牌 Token 前缀 `--ink-`，子域以 `-` 分隔：`--ink-{domain}-{variant}`。
- 品质 Token：`--ink-quality-{rarity}`，如 `--ink-quality-legendary`。
- 五行 Token：`--ink-element-{wuxing}`，如 `--ink-element-fire`。
- 透明度派生后缀：`-glow`(0.4) / `-dim`(0.5) / `-faint`(0.15) / `-trace`(0.08)，语义固定，禁止混用。
- 组件库原 Token（`--bg-*`/`--text-*`/`--icon-*`/`--border-*`/`--status-*`）保留，由水墨覆盖层重定向到 `--ink-*` 同义值，保证 ds-* 通用组件零改造复用。

### 2.2 背景层 Token

背景层由深到浅分四级，构成「墨黑—墨—纸—雾」的纵深。

| Token | 值 | 用途 |
| --- | --- | --- |
| `--ink-bg-void` | `#0E1016` | 全屏底色，最深的墨黑背景 |
| `--ink-bg-abyss` | `#0A0B10` | 比底色更深的深渊层，滚动条轨道 |
| `--ink-bg-ink` | `#14171E` | 面板基色，卡片底 |
| `--ink-bg-paper` | `#1C1F28` | 宣纸层，内容区抬高一档 |
| `--ink-bg-elevated` | `#1A1D26` | 抬升层，悬浮卡片 |
| `--ink-bg-panel` | `rgba(20,23,30,0.85)` | 半透明面板底，配 backdrop-blur |
| `--ink-bg-mist` | `rgba(200,168,88,0.04)` | 金雾叠加层 |
| `--ink-bg-hover` | `rgba(200,168,88,0.08)` | 悬停高亮底 |

同时维护与 Light 体系的映射：`--bg-base-default`→`#0E1016`、`--bg-base-secondary`→`#14171E`、`--bg-base-tertiary`→`#1C1F28`，保证组件库原 Token 在暗色下自动生效。选用原则：越靠近视点越亮，`void → ink → paper → elevated` 逐级抬升。

### 2.3 品牌色 Token

品牌双色轴：鎏金（交互/强调）与春青（生命/增益）。每色含 primary/bright/deep 三档，外加 glow/dim/faint 三档透明度派生。

**鎏金系**

| Token | 值 | 场景 |
| --- | --- | --- |
| `--ink-gold-primary` | `#C8A858` | 主品牌色，按钮/边框/强调文本 |
| `--ink-gold-bright` | `#E0C880` | 高亮态，选中/悬浮 |
| `--ink-gold-deep` | `#8A7438` | 按压态/渐变起点 |
| `--ink-gold-glow` | `rgba(200,168,88,0.4)` | 发光阴影 |
| `--ink-gold-dim` | `rgba(200,168,88,0.5)` | 弱化金，刻度/连线 |
| `--ink-gold-faint` | `rgba(200,168,88,0.15)` | 极弱金，底纹 |
| `--ink-gold-trace` | `rgba(200,168,88,0.08)` | 痕迹级，描边底线 |

**春青系**

| Token | 值 | 场景 |
| --- | --- | --- |
| `--ink-jade-primary` | `#7EAB9E` | 生命/内力/增益主色 |
| `--ink-jade-bright` | `#A8D4C4` | 高亮态，队友/成功 |
| `--ink-jade-deep` | `#5E8B7E` | 按压态/深底描边 |
| `--ink-jade-glow` | `rgba(126,171,158,0.45)` | 玩家点发光 |
| `--ink-jade-dim` | `rgba(126,171,158,0.55)` | 弱化青 |
| `--ink-jade-faint` | `rgba(126,171,158,0.15)` | 卡片底纹 |

**朱砂系（生命/危险补充）**

| Token | 值 | 场景 |
| --- | --- | --- |
| `--ink-blood-primary` | `#B85450` | 敌对/扣血/危险 |
| `--ink-blood-bright` | `#D87470` | 高亮态，删除/扣减 |
| `--ink-blood-deep` | `#8A3E3A` | 铜牌/深底 |

组件库品牌映射：`--bg-brand`/`--color-primary`/`--text-brand`/`--icon-brand`/`--border-brand` 全部指向 `#C8A858`，确保 ds-btn 等通用组件无需改色即融入水墨主题。

### 2.4 文本 Token

| Token | 值 | 用途 |
| --- | --- | --- |
| `--ink-text-primary` | `#F0EDE4` | 主文本，宣纸白 |
| `--ink-text-secondary` | `#B8B0A0` | 次级文本，浅褐 |
| `--ink-text-muted` | `#8A8275` | 弱化文本，辅助说明 |
| `--ink-text-faint` | `rgba(240,237,228,0.4)` | 极弱文本，占位/禁用 |
| `--ink-text-gold` | `#C8A858` | 金色强调文本 |
| `--ink-text-jade` | `#A8D4C4` | 青色强调文本 |
| `--ink-text-blood` | `#D87470` | 危险/扣减文本 |
| `--ink-text-inverse` | `#0E1016` | 金底/青底上的反色文本 |

层级用法：标题 `--ink-text-primary`、正文 `--ink-text-secondary`、说明 `--ink-text-muted`、禁用 `--ink-text-faint`。强调优先用语义色（gold/jade/blood），仅在缺少语义时回退到 primary/secondary。

### 2.5 图标 Token

沿用组件库 `--icon-*` 系列，暗色覆盖后：默认 `#D8D4C8`、次级 `#B8B0A0`、三级 `#8A8275`、品牌 `#C8A858`、禁用 `rgba(240,237,228,0.3)`。页面内 lucide 图标统一以 `currentColor` 承色，通过父级文本色或 `style="color: var(--ink-gold-primary)"` 局部着色。图标尺寸统一取 `--icon-size-16`（16px，HUD）/ `--icon-size-20`（20px，面板）。

### 2.6 边框 Token

| Token | 值 | 用途 |
| --- | --- | --- |
| `--ink-border-gold` | `rgba(200,168,88,0.25)` | 标准金边 |
| `--ink-border-gold-bright` | `rgba(200,168,88,0.5)` | 高亮金边（选中态） |
| `--ink-border-gold-subtle` | `rgba(200,168,88,0.15)` | 弱金边（卡片描边） |
| `--ink-border-faint` | `rgba(200,168,88,0.08)` | 极弱描边 |
| `--ink-border-jade` | `rgba(94,139,126,0.3)` | 青色边（增益/成功） |
| `--ink-divider` | `rgba(200,168,88,0.12)` | 分割线 |

选用原则：卡片默认 `gold-subtle`，选中升级 `gold-bright`，分割线用 `divider`。禁止用 `border-gold` 作为大面积底色。

### 2.7 状态 Token

| Token | 值 | 语义 |
| --- | --- | --- |
| `--status-success-default` | `#5E8B5E` | 成功（青绿系，与品牌青区分） |
| `--status-warning-default` | `#C47B3E` | 警告（暖橙） |
| `--status-error-default` | `#B85450` | 危险（朱砂） |
| `--status-primary-default` | `#4A7BA8` | 信息（黛蓝） |
| `--status-alert-default` | `#C49B5E` | 提醒（暖金） |

每档状态色均配 `surface-l1/l2/l3`（0.12/0.18/0.36 透明度）三档半透明底，用于徽章底色与浅提示条。状态色不可用作品牌色替代，仅用于瞬态反馈。

### 2.8 水墨自定义 Token（glow / dim / faint）

为粒子动效与发光态预留的语义化透明度派生，已随品牌色一并定义（见 2.3）。补充阴影与模糊：

| Token | 值 |
| --- | --- |
| `--ink-shadow-panel` | `0 8px 32px rgba(0,0,0,0.6), 0 2px 8px rgba(0,0,0,0.4)` |
| `--ink-shadow-gold` | `0 0 24px rgba(200,168,88,0.2)` |
| `--ink-shadow-inset` | `inset 0 1px 0 rgba(200,168,88,0.08)` |
| `--ink-blur-panel` | `blur(8px)` |
| `--ink-blur-overlay` | `blur(4px)` |
| `--ink-radius-sm/md/lg` | `2px / 4px / 8px` |

阴影语义：卡片用 `panel`（深重）、选中态叠加 `gold`（发光）、内嵌高光用 `inset`。圆角统一 `radius-lg`(8px) 为卡片、`radius-md`(4px) 为按钮、`radius-sm`(2px) 为标签。

---

## 第 3 章　19 个页面规格

> 统一模板：**布局结构 / 关键组件 / 数据绑定 / Token 使用 / 交互锚点（data-dom-id）**
> 所有页面根节点均为 `<main class="fixed inset-0">`，内联 `<style id="theme-vars">` 承载全局 Token，`<style id="component-vars">` 承载组件与页面样式。

### 3.1 combat-hud（核心战斗 HUD）

**布局结构**
- 全屏 `hud-root` 浮层，`fixed inset-0`。
- 左上：玩家信息卡（八角头像 + 血条 + 内力条 + 经验条）。
- 顶部中央：`hud-quest-tracker` 任务追踪器（`<details>` 可折叠）。
- 右上：`hud-minimap` 小地图 160×160，含罗盘指针与三类地图标记。
- 右侧：Buff 列表（增益/减益分组）。
- 底部中央：`hud-skill-bar` 技能栏（武器槽 + 6 技能槽 Q/W/E/R/F + 辅助）。
- 底部左侧：`hud-nav-btn` 导航栏群组（18 个入口）。

**关键组件**
- `hud-avatar-octagon`：八角形头像框。
- `hud-bar`：三色条（hp/mp/exp），用 `--ink-blood/jade/gold-primary`。
- `hud-skill-slot` + `hud-cooldown`：技能槽，冷却用 `conic-gradient` 遮罩。
- `hud-buff-item`：Buff 项，左边框色区分增益（青）/减益（朱砂）。
- `hud-map-marker`：地图标记，金=NPC、朱砂=敌对、青=队友。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| player.hp/mp/exp | number/number/number | 当前/最大值成对 |
| target.info | object | 目标名称、等级、血量 |
| skill[] | array | 含 cooldownLeft、key 绑定 |
| buff[] | array | type(gain/debuff)、duration、stack |
| minimap.markers[] | array | 坐标、类型、label |
| quest.active[] | array | 追踪中任务摘要 |

**Token 使用**：血条 `--ink-blood-primary`、内力条 `--ink-jade-primary`、经验条 `--ink-gold-primary`；技能栏底 `--ink-bg-ink` + `backdrop-filter: blur(8px)`；面板阴影 `--ink-shadow-panel`；导航按钮 hover `--ink-bg-hover`。

**交互锚点（data-dom-id）**：`nav-character`、`nav-skill`、`nav-inventory`、`nav-quest`、`nav-map`、`nav-social`、`nav-shop`、`nav-compass`、`nav-enhance`、`nav-crafting`、`nav-mount`、`nav-friends`、`nav-mail`、`nav-leaderboard`、`nav-mentor`、`nav-achievement`、`nav-dungeon`、`toggle-traditional`。

### 3.2 combat-hud-traditional（传统模式战斗 HUD）

**布局结构**
- 与 combat-hud 同构的全屏浮层，区别在于四象限固定式排布：左上角色头像+血条、右上目标信息、左下技能轮盘、右下 Buff/快捷栏，居中留战斗现场。提供经典 MMO 玩家的熟悉布局。

**关键组件**
- `trad-avatar`：方形头像（区别于沉浸式的八角）。
- `trad-bar`：水平血条/内力条。
- `trad-target-frame`：目标框，含施法进度条。
- `trad-skill-wheel`：轮盘式 8 槽技能盘。
- `trad-buff-row`：横向 Buff 行。
- `trad-action-bar`：快捷物品栏。

**数据绑定**：与 3.1 同源 player/target/skill/buff，额外 `target.castProgress`（目标施法进度 0-1）、`actionBar.items[8]`（快捷物品）。

**Token 使用**：边框统一 `--ink-border-gold`；血条 `--ink-blood-primary`；轮盘中心 `--ink-gold-glow`；底色 `--ink-bg-panel`（半透明 + blur）。

**交互锚点**：`toggle-traditional`（切回沉浸式）、其余导航锚点与 3.1 一致。

### 3.3 character-panel（角色面板）

**布局结构**
- 左中右三栏全屏覆盖。
- 左栏 `cp-portrait`：立绘 + 捏脸 Tab（发型/脸型/肤色）。
- 中栏 `cp-attr`：六维竖条 `cp-vbar`（体魄/根骨/身法/悟性/气海/福缘，部分用 `--jade` 变体）。
- 右栏：`cp-equip-grid` 4×4 装备槽 + 下方 `cp-meridian` 经脉层级点 + `cp-gem-slot` 宝石孔。

**关键组件**
- `cp-tab`：捏脸切换。
- `cp-vbar-bg`/`cp-vbar-fill`：竖向属性条。
- `cp-slot`：装备槽，四态 `--equipped/--empty/--selected/--placeholder`，通过 `--quality-color` CSS 变量注入品质色。
- `cp-layer-dot`：经脉层级点亮态。
- `cp-gem-slot`：宝石孔，`--gem-color` 注入宝石色。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| character.baseAttr[6] | array | 六维数值 |
| character.equipment[16] | array | 槽位装备 |
| meridian.layers[] | array | 经脉层级激活状态 |
| gems[] | array | 宝石镶嵌 |
| portrait.config | object | 捏脸参数 |

**Token 使用**：竖条填充默认 `--ink-gold-primary`、青系属性用 `--ink-jade-primary`；槽位边框用 `--quality-color`（品质 token）；面板底 `--ink-bg-paper`；选中槽 `--ink-border-gold-bright` + `--ink-shadow-gold`。

**交互锚点**：`back-hud`、各 `cp-slot`、`cp-tab`、经脉点。

### 3.4 skill-panel（武学技能）

**布局结构**
- 左侧 `skill-tree` 技能树（节点连线图）。
- 中部 `skill-detail` 技能详情（图标 + 名称 + 描述 + 等级 + 消耗）。
- 右侧 `skill-tabs` 心法/奇术 Tab 切换。
- 底部技能槽配置预览。

**关键组件**
- `skill-node`：已学/可学/锁定三态。
- `skill-connector`：连线，已激活高亮金。
- `skill-card`：技能卡片。
- `skill-slot-preview`：槽位预览。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| skillTree.nodes[] | array | 节点状态、前置依赖 |
| skill.detail | object | 当前选中技能详情 |
| skill.learned[] | array | 已学技能 ID |
| skill.xinfa[] | array | 心法列表 |
| skill.qishu[] | array | 奇术列表 |

**Token 使用**：已学节点 `--ink-gold-primary`、可学 `--ink-jade-bright`、锁定 `--ink-text-muted`；连线激活态 `--ink-gold-dim`；卡片底 `--ink-bg-ink`。

**交互锚点**：`back-hud`、`skill-node-*`、`tab-xinfa`/`tab-qishu`、`btn-learn`。

### 3.5 inventory（背包行囊）

**布局结构**
- `inv-panel` 顶栏（标题 + 搜索 + 分类筛选 + 金币）。
- `inv-content`：左侧 `inv-grid` 网格（8 列×N 行）+ 右侧 `inv-detail` 选中物品详情。

**关键组件**
- `inv-cell`：六态 `--legendary/--epic/--rare/--uncommon/--common/--empty`。
- `inv-item-icon`：lucide 图标，色随品质。
- `inv-enhance`：+N 强化角标。
- `inv-qty`：数量角标。
- `inv-filter-chip`：分类筛选片。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| inventory.items[64] | array | 格位物品 |
| item.quality | enum | common~legendary |
| item.enhanceLevel | number | 强化等级 |
| item.qty | number | 堆叠数量 |
| selectedItemId | string | 选中物品 |
| gold | number | 持有银两 |

**Token 使用**：格子边框/图标色由 `--ink-quality-*` 注入；选中态边框 `--ink-border-gold-bright` + `--ink-shadow-gold`；金币数字 `--ink-text-gold` + `--ink-font-number`。

**交互锚点**：`back-hud`、`inv-cell-*`、`btn-sort`、`btn-enhance`（跳 equipment-enhance）。

### 3.6 equipment-enhance（装备强化）

**布局结构**
- 左中右三栏。
- 左 `enh-slot-list`：装备槽位选择。
- 中 `enh-stage`：强化主舞台（装备图标 + 当前/预览属性 + N→N+1 箭头）。
- 右 `enh-material` + `enh-actions`：材料清单 + 强化按钮区。

**关键组件**
- `enh-slot`：装备槽。
- `enh-stat-row`：属性对比条。
- `enh-material-card`：材料卡。
- `enh-progress`：成功率条。
- `enh-btn-primary`：含 `data-particle="gold-burst"`。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| equipment.selected | object | 选中装备 |
| enhance.currentStats | object | 当前属性 |
| enhance.previewStats | object | 预览属性 |
| materials[] | array | 所需材料及持有量 |
| successRate | number | 0-1 |
| cost | number | 银两消耗 |

**Token 使用**：成功率条 `<50%` 用 `--status-error-default`、`50-80%` 用 `--status-warning-default`、`>80%` 用 `--ink-jade-primary`；按钮渐变 `linear-gradient(135deg, var(--ink-gold-deep), var(--ink-gold-primary))`；属性提升用 `--ink-jade-bright`、下降用 `--ink-blood-bright`。

**交互锚点**：`back-hud`、`enh-slot-*`、`btn-enhance`、`btn-add-material`、`btn-auto-fill`。

### 3.7 crafting（制造技艺）

**布局结构**
- 左 `craft-tree` 配方分类树（锻造/炼药/烹饪/缝纫…）。
- 中 `craft-recipe-list` 配方列表。
- 右 `craft-detail` 详情（产物预览 + 材料清单 + 制造按钮）。

**关键组件**
- `craft-category`：分类项。
- `craft-recipe-card`：含熟练度进度条。
- `craft-material-row`：足/缺两态。
- `craft-btn`：制造按钮。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| craft.categories[] | array | 技艺分类 |
| recipes[] | array | 配方列表 |
| recipe.materials[] | array | 所需材料 |
| proficiency.current/max | number | 熟练度 |
| craftable | boolean | 是否可制造 |

**Token 使用**：材料充足 `--ink-jade-primary`、缺失 `--ink-blood-bright`；熟练度条 `--ink-gold-primary`；卡片底 `--ink-bg-paper`；可制造按钮高亮 `--ink-gold-bright`。

**交互锚点**：`back-hud`、`craft-category-*`、`craft-recipe-*`、`btn-craft`、`btn-craft-batch`。

### 3.8 quest-log（任务日志）

**布局结构**
- 左侧 `quest-tabs` 任务分类（主线/支线/日常/周常/师门）+ `quest-list` 任务列表。
- 右侧 `quest-detail` 任务详情（描述 + 目标 + 奖励 + 追踪按钮）。

**关键组件**
- `quest-tab`：分类切换。
- `quest-item`：进行中/可完成/锁定三态。
- `quest-objective`：目标勾选/未完成。
- `quest-reward-row`：奖励行。
- `btn-track`：追踪按钮。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| quest.category | enum | 当前分类 |
| quests[] | array | 任务列表 |
| quest.objectives[] | array | 目标及完成度 |
| quest.rewards[] | array | 奖励物品 |
| trackedQuestId | string | 追踪中任务 |

**Token 使用**：可完成态 `--ink-gold-bright` 描边；进行中 `--ink-jade-primary`；锁定 `--ink-text-muted`；目标勾选 `--ink-jade-bright`；奖励品质色 `--ink-quality-*`。

**交互锚点**：`back-hud`、`quest-tab-*`、`quest-item-*`、`btn-track`、`btn-abandon`。

### 3.9 world-map（世界地图）

**布局结构**
- 全屏 `map-canvas` 地图画布（SVG/位图底图）。
- 叠加 `map-region` 区域块、`map-marker` 兴趣点、`map-player` 当前位置。
- 右侧 `map-legend` 图例 + 区域信息卡。

**关键组件**
- `map-region`：hover 高亮。
- `map-marker`：城池/据点/秘境/资源四类图标。
- `map-player-pin`：玩家定位针。
- `map-zoom-control`：缩放控件。
- `map-legend-item`：图例项。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| world.regions[] | array | 区域多边形 |
| markers[] | array | 兴趣点 |
| player.position | object | 玩家坐标 |
| currentRegion | string | 当前所在区域 |

**Token 使用**：区域描边 `--ink-border-gold`；玩家点 `--ink-jade-bright` + `--ink-jade-glow`；秘境标记 `--ink-gold-primary`；敌对 `--ink-blood-primary`；资源点 `--ink-jade-deep`。

**交互锚点**：`back-hud`、`map-region-*`、`map-marker-*`、`btn-zoom-in`/`btn-zoom-out`、`btn-teleport`。

### 3.10 compass（指南针）

**布局结构**
- 全屏暗背景居中 `compass-dial` 旋转刻度盘。
- 中心 `compass-needle` 金针。
- 底部 `compass-targets` 目标列表。

**关键组件**
- `compass-dial`：N/E/S/W 刻度。
- `compass-needle`：金针。
- `compass-target-row`：目标名 + 距离 + 方位角。
- `compass-bearing`：方位读数。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| player.heading | number | 朝向 0-360 |
| targets[] | array | 目标列表 |
| selectedTarget | string | 锁定目标 |

**Token 使用**：刻度 `--ink-gold-dim`；指针 `--ink-gold-bright` + `--ink-shadow-gold`；选中目标 `--ink-jade-bright`；背景径向渐变叠加 `--ink-bg-abyss`。

**交互锚点**：`back-hud`、`compass-target-*`、`btn-lock-target`。

### 3.11 social-guild（社交门派）

**布局结构**
- 左 `guild-info` 门派信息（徽记 + 名称 + 等级 + 宣言）。
- 中 `guild-members` 成员列表（职务分组）。
- 右 `guild-events` 门派事件/公告。
- 底部申请/管理操作区。

**关键组件**
- `guild-emblem`：门派徽记。
- `guild-member-row`：在线/离线态 + 职务徽章。
- `guild-event-item`：事件项。
- `guild-rank-badge`：职务徽章。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| guild.info | object | 门派基础信息 |
| members[] | array | role/online/level |
| events[] | array | 事件流 |
| applications[] | array | 入派申请 |

**Token 使用**：在线点 `--ink-jade-primary`、离线 `--ink-text-muted`；职务徽章金底 `--ink-gold-primary`；成员行 hover `--ink-bg-hover`；徽记描边 `--ink-border-gold`。

**交互锚点**：`back-hud`、`guild-member-*`、`tab-members`/`tab-events`/`tab-applications`、`btn-apply`、`btn-leave`。

### 3.12 friends（江湖交游）

**布局结构**
- 左侧 `cat-tab` 分类（好友/仇人/黑名单）+ `friend-list` 好友列表。
- 右侧 `friend-detail` 好友详情（头像 + 签名 + 亲密度 + 操作按钮组）。

**关键组件**
- `friend-item`：is-selected 态。
- `friend-avatar`：好友头像。
- `intimacy-bar`：亲密度条。
- `action-btn`：私聊/组队/传送/赠礼/邮件/删除六操作。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| friends.category | enum | 好友/仇人/黑名单 |
| friends[] | array | online/intimacy/signature |
| selectedFriendId | string | 选中好友 |

**Token 使用**：在线点 `--ink-jade-primary`；亲密度条 `--ink-jade-bright`；主操作按钮 `--ink-gold-primary` 边框；删除按钮 `--ink-blood-bright`；仇人项左边框 `--ink-blood-faint`。

**交互锚点**：`back-hud`、`tab-friends`/`tab-enemies`/`tab-blacklist`、`friend-*`、`btn-chat`、`btn-team-invite`、`btn-teleport`、`btn-gift`、`btn-mail`、`btn-remove-friend`、`btn-add-friend`。

### 3.13 mail（飞鸽传书）

**布局结构**
- 左侧 `mail-folder` 邮件夹（系统/玩家/战报 Tab + 列表）。
- 右侧 `mail-body` 邮件正文（发件人 + 主题 + 正文 + 附件区 + 操作按钮）。

**关键组件**
- `mail-item`：未读/已读/带附件三态。
- `mail-attachment-grid`：附件网格。
- `btn-claim-all`：一键领取。
- `btn-reply`：回复。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| mail.folder | enum | 邮件夹分类 |
| mails[] | array | read/hasAttachment/sender |
| selectedMailId | string | 选中邮件 |
| attachments[] | array | 附件物品 |

**Token 使用**：未读标记 `--ink-gold-bright`；带附件 `--ink-jade-primary` 角标；附件格 `--ink-quality-*` 边框；已读 `--ink-text-muted`；系统邮件标题 `--ink-text-gold`。

**交互锚点**：`back-hud`、`mail-item-*`、`btn-claim-all`、`btn-reply`、`btn-delete`。

### 3.14 mentor（师徒传承）

**布局结构**
- 顶部 `role-tab` 角色切换（师父/徒弟）+ 徒弟/师父列表。
- 中部 `task-tab` 任务区（日常/周常/出师）+ 任务卡片列表。
- 底部 `graduation-progress` 出师进度 + `shop-item` 师徒商店。

**关键组件**
- `disciple-item`：徒弟项。
- `task-card`：可领/进行中/已完成三态。
- `claim-btn`：领取奖励。
- `graduation-bar`：出师进度条。
- `shop-item`：含 `data-quality`。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| mentor.role | enum | 师父/徒弟视角 |
| disciples[] | array | 徒弟列表 |
| tasks[] | array | type/status/reward |
| graduation.percent | number | 出师进度 0-1 |
| mentorShop[] | array | 兑换商品 |

**Token 使用**：可领取 `--ink-jade-primary` 边框；已完成 `--ink-text-muted` + `opacity:0.65`；出师进度 `linear-gradient(135deg, var(--ink-gold-deep), var(--ink-gold-primary))`；周常卡片 `--ink-jade-faint` 底。

**交互锚点**：`back-hud`、`tab-role-master`/`tab-role-disciple`、`tab-daily`/`tab-weekly`/`tab-graduation`、`disciple-*`、`task-*`、`claim-task-*`、`btn-recruit`、`btn-seek-master`、`btn-dismiss-master`、`btn-go-dungeon`、`btn-go-teach`、`btn-go-spar`、`btn-view-all-records`、`shop-item-*`。

### 3.15 leaderboard（江湖风云榜）

**布局结构**
- 顶部 `lb-tabs` 榜单分类（战力/等级/财富/门派…）。
- 中部 `lb-podium` 前三甲展示。
- 下方 `lb-list` 排名列表。
- 右侧 `lb-mine` 我的排名卡片。

**关键组件**
- `lb-tab`：分类切换。
- `lb-podium-step`：金/银/铜台。
- `lb-row`：排名 + 头像 + 名 + 数值。
- `lb-mine-card`：我的排名卡。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| leaderboard.category | enum | 榜单分类 |
| ranks[] | array | rank/name/value/guild |
| myRank | number | 我的排名 |

**Token 使用**：榜首 `--ink-gold-bright` + `--ink-shadow-gold`；银 `--ink-text-secondary`；铜 `--ink-blood-deep`；自己行 `--ink-jade-faint` 底；数字 `--ink-font-number`。

**交互锚点**：`back-hud`、`lb-tab-*`、`lb-row-*`、`btn-view-profile`。

### 3.16 achievement（江湖百艺录）

**布局结构**
- 左侧 `ach-tree` 成就分类树（江湖历练/武学/收集/社交…）。
- 中部 `ach-grid` 成就网格（已解锁/未解锁/进行中）。
- 右侧 `ach-detail` 详情 + 总进度环。

**关键组件**
- `ach-card`：已解锁高亮/未解锁灰/进行中带进度。
- `ach-progress-ring`：进度环。
- `ach-badge`：成就徽章。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| achievement.categories[] | array | 成就分类 |
| achievements[] | array | unlocked/progress/total |
| totalUnlocked/total | number | 总进度 |

**Token 使用**：已解锁 `--ink-gold-primary` 描边 + `--ink-shadow-gold`；进行中 `--ink-jade-primary`；未解锁 `--ink-text-muted` + `opacity:0.5`；进度环 `--ink-jade-bright`。

**交互锚点**：`back-hud`、`ach-category-*`、`ach-card-*`、`btn-claim-reward`。

### 3.17 shop（商城商店）

**布局结构**
- 顶部 `shop-tabs` 分类（推荐/限免/时装/坐骑/材料/礼包）+ 货币栏（元宝/银两）。
- 中部 `shop-grid` 商品网格。
- 右侧 `shop-cart` 购物车/详情。

**关键组件**
- `shop-card`：含品质边框、限时角标、原价划线。
- `shop-price`：元宝/银两图标。
- `shop-tab`：分类切换。
- `btn-buy`、`btn-add-cart`：购买操作。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| shop.category | enum | 商城分类 |
| goods[] | array | price/currency/discount/quality/limitTime |
| cart[] | array | 购物车 |
| wallet | object | 元宝/银两余额 |

**Token 使用**：商品品质边框 `--ink-quality-*`；限时角标 `--status-error-default`；元宝价 `--ink-gold-primary`；银两价 `--ink-text-secondary`；折扣价 `--ink-blood-bright`；原价划线 `--ink-text-muted`。

**交互锚点**：`back-hud`、`shop-tab-*`、`shop-card-*`、`btn-buy`、`btn-add-cart`、`btn-checkout`。

### 3.18 mount-pet（坐骑灵兽）

**布局结构**
- 左侧 `mp-tab`（坐骑/宠物）+ `mp-list` 列表。
- 中部 `mp-stage` 展示舞台（模型/立绘 + 属性）。
- 右侧 `mp-detail` 详情（技能/饱食度/亲密度/操作）。

**关键组件**
- `mp-card`：出战/休养/锁定态。
- `mp-stage-view`：舞台视图。
- `mp-skill-row`：技能行。
- `mp-vital-bar`：饱食/亲密双条。
- `btn-summon`：召唤按钮。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| mount.type | enum | 坐骑/宠物 |
| mounts[] | array | status/skills/vitals |
| selectedId | string | 选中 |

**Token 使用**：出战态 `--ink-jade-primary` 描边；饱食度 `--ink-gold-primary`；亲密度 `--ink-blood-bright`；锁定 `--ink-text-muted`；舞台底 `--ink-bg-abyss` 径向渐变。

**交互锚点**：`back-hud`、`tab-mount`/`tab-pet`、`mp-card-*`、`btn-summon`、`btn-feed`、`btn-train`。

### 3.19 dungeon-entry（江湖秘境）

**布局结构**
- 全屏 `dungeon-list` 秘境选择（卡片网格，含难度/推荐战力/掉落品质）。
- 选中后展开 `dungeon-detail`（Boss 列表 + 掉落预览 + 进入按钮）。
- 顶部难度切换与组队状态。

**关键组件**
- `dungeon-card`：普通/困难/噩梦色阶。
- `dungeon-boss-row`：Boss 行。
- `dungeon-drop-grid`：掉落网格（品质边框）。
- `dungeon-enter-btn`：含 `data-particle="gold-burst"`。

**数据绑定**

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| dungeons[] | array | difficulty/recommendedPower/drops/bosses |
| party.members[] | array | 队伍成员 |
| selectedDungeon | string | 选中秘境 |

**Token 使用**：难度色阶 普通=`--ink-jade-primary`、困难=`--status-warning-default`、噩梦=`--ink-blood-primary`；进入按钮渐变 `var(--ink-gold-deep) → var(--ink-gold-primary)`；掉落格 `--ink-quality-*`；推荐战力不足时数值 `--ink-blood-bright`。

**交互锚点**：`back-hud`、`dungeon-card-*`、`enter-dungeon`、`btn-party`、`difficulty-toggle-*`。

---

## 第 4 章　品质与五行系统

### 4.1 装备品质色阶

品质色阶用于装备/掉落/商品等所有物品的边框、图标与名称着色。定义语义如下（同时给出实现 Token 与实际 CSS 值）：

| 品质 | 语义色 | 实现 Token | CSS 实际值 |
| --- | --- | --- | --- |
| common（普通） | 灰白 `#B0B5BD` | `--ink-quality-common` | `#8A8275` |
| uncommon（良好） | 青绿 `#7EAB9E` | `--ink-quality-uncommon` | `#6B8E5A` |
| rare（稀有） | 蓝紫 `#8B7EC8` | `--ink-quality-rare` | `#4A7EA8` |
| epic（史诗） | 橙金 `#E0A050` | `--ink-quality-epic` | `#8B5E9E` |
| legendary（传说） | 赤金 `#E8C050` | `--ink-quality-legendary` | `#C8A858` |

> 说明：语义色为设计规范基色（亮色域），实现 Token 为适配 `--ink-bg-void` 暗底而做去饱和处理。原型页面以实现 Token 为准；如需切换至语义亮色域，可在 `--ink-bg-void` 提亮后同步替换。

**注入方式**：物品 DOM 通过 `style="--quality-color: var(--ink-quality-epic)"` 或 `data-quality="epic"` 注入，子元素边框/图标引用 `var(--quality-color)`。示例：

```html
<div class="inv-cell inv-cell--epic" style="--quality-color: var(--ink-quality-epic);">
  <i data-lucide="sword" style="color: var(--quality-color);"></i>
  <span class="inv-enhance">+8</span>
</div>
```

**应用场景**：背包格位、装备槽、掉落预览、商城商品卡、邮件附件、制造产物。品质色同时决定名称文本色与边框色，但图标可用更高一档的 bright 变体提升可见度。

### 4.2 五行相生相克

五行用于属性亲和、技能属性、装备词条与克制计算。相生：金生水、水生木、木生火、火生土、土生金。相克：金克木、木克土、土克水、水克火、火克金。

| 五行 | 正色 | 实现 Token | CSS 实际值 |
| --- | --- | --- | --- |
| 金 | 白 | `--ink-element-metal` | `#D4C4A0` |
| 木 | 青 | `--ink-element-wood` | `#6B8E5A` |
| 水 | 黑 | `--ink-element-water` | `#4A6E8A` |
| 火 | 红 | `--ink-element-fire` | `#B85638` |
| 土 | 黄 | `--ink-element-earth` | `#8A7B5A` |

> 说明：正色为传统五行配色规范；实现 Token 为适配暗底调整（水用黛蓝替代纯黑以保证可见度，金用暖白替代纯白以贴合水墨调性）。技能/Boss 属性图标以 `var(--ink-element-*)` 着色。

**克制反馈**：攻击触发克制时，伤害飘字使用攻击方五行色 + 被克方五行色描边，并在 tooltip 中以「金→木」箭头标注。被克时飘字降为 `--ink-text-muted`。装备词条的五行亲和以 `--ink-element-*` 小圆点置于词条前缀。

---

## 第 5 章　前端集成指引

### 5.1 HTML 结构约定

每个页面 `<head>` 内联两个 `<style>` 块，按职责分层：

```html
<head>
  <style id="theme-vars">
    /* colors_and_type.css 全文：Light 基线 + 水墨 Dark 覆盖 */
    /* 包含 --ink-* 全部 Token */
  </style>
  <style id="component-vars">
    /* components.css + scaffold.css 全文 */
    /* 含 ds-btn / ds-tooltip / ds-empty 等通用组件 */
    /* 本页面专属组件样式（如 hud-* / cp-* / inv-*） */
  </style>
  <link rel="stylesheet" href="../assets/css/ink-particles.css">
</head>
```

- `id="theme-vars"`：仅承载设计 Token，不含组件规则，便于主题切换时整体替换。
- `id="component-vars"`：承载组件库与页面专属样式，可按页面裁剪。
- `ink-particles.css` 外链，全站共享，禁止内联（保证粒子动效版本统一）。
- 页面 `<html>` 标签统一 `class="dark"`，`lang="zh"`。

### 5.2 CSS 加载顺序

页面样式解析顺序必须如下，后者覆盖前者：

1. **colors_and_type.css**（内联于 `#theme-vars`）——Token 定义层，最底。
2. **components.css + scaffold.css**（内联于 `#component-vars`）——通用组件与布局原子。
3. **页面专属样式**（内联于 `#component-vars` 末尾）——`hud-*`/`cp-*` 等页面 BEM 块。
4. **ink-particles.css**（外链）——粒子动效层，z-index 9999，pointer-events: none。

Tailwind Browser CDN（`@tailwindcss/browser@4`）与 lucide 图标库在 `#component-vars` 之后、body 之前加载，用于原子类与图标替换。页面内联样式禁止覆盖 Token 定义，仅允许通过 `var(--ink-*)` 引用。

### 5.3 JS 事件协议

粒子运行时 `assets/js/ink-particles.js` 暴露 `window.InkParticles`，监听三类事件：

| 事件 | 触发方式 | 粒子反馈 | 时长 |
| --- | --- | --- | --- |
| `click`（委托） | 点击 `[data-particle]` 或 `.ds-btn` | 金粉爆发 `burst(x,y,type)` | 800ms |
| `panel:show` | `el.dispatchEvent(new CustomEvent('panel:show'))` | 墨韵涟漪 `ripple(el)`（双环） | 1200ms |
| `toast:show` | `el.dispatchEvent(new CustomEvent('toast:show'))` | 青玉萤光 `firefly(el)`（7 颗） | 1000ms |

约定：

- 金粉为默认反馈，按钮点击自动触发，无需手写。
- 青玉粒子反馈需在元素上显式标注 `data-particle="jade-burst"`，否则出金粉。
- 面板切换（如 Tab 切换、弹层打开）由业务代码派发 `panel:show`。
- 信息提示（Toast/飘字）由业务代码派发 `toast:show`。
- `DOMContentLoaded` 时自动 `startAmbient()`，全屏持续飘散环境水墨微粒（≤20 颗，12s 生命周期）。

### 5.4 粒子动效系统接入（InkParticles API）

```js
// 主动调用
window.InkParticles.burst(x, y, 'gold' | 'jade');   // 指定坐标爆发
window.InkParticles.ripple(el);                       // 在元素中心出涟漪
window.InkParticles.firefly(el);                      // 在元素边缘出萤光
window.InkParticles.startAmbient();                   // 启动环境微粒
window.InkParticles.stopAmbient();                    // 停止环境微粒
window.InkParticles.config;                           // 读取配置（粒子数/时长）
```

接入要点：

1. **统一曲线**：所有动效使用 `cubic-bezier(0.16, 1, 0.3, 1)`，禁止页面内自定义其他曲线，保证手感一致。
2. **降级**：`prefers-reduced-motion: reduce` 时所有粒子动画时长降为 `0.01ms`，环境微粒不启动。
3. **层级**：粒子层 `.ink-particle-layer` 固定全屏、`z-index: 9999`、`pointer-events: none`，不拦截交互。
4. **性能**：粒子节点动画结束后 100ms 内移除 DOM；环境微粒通过 `setInterval(800ms)` 补充，上限 20 颗；窗口 resize 防抖 300ms 后重建环境层。
5. **与业务解耦**：业务代码只派发事件或调用 API，不直接操作粒子 DOM；粒子系统升级不影响业务逻辑。

### 5.5 命名与版本约束

- **BEM 前缀**：每个页面拥有独立 BEM 前缀（hud-/cp-/inv-/enh-/craft-/quest-/map-/compass-/guild-/friend-/mail-/mentor-/lb-/ach-/shop-/mp-/dungeon-），禁止跨页面复用前缀。
- **交互锚点**：`data-dom-id` 命名采用 `{域}-{动作}-{标识}` 三段式，如 `nav-character`、`btn-enhance`、`quest-item-1`。返回锚点全站统一 `back-hud`。
- **品质/五行注入**：统一通过 `--quality-color` / `--gem-color` CSS 变量或 `data-quality` 属性，禁止硬编码色值。
- **版本管理**：Token 变更须同步更新本规范第 2 章；新增页面须补第 3 章对应小节；ink-particles 升级须同步第 5 章 API 表。

---

> 本规范为混沌世界 UI 系统的唯一架构基线。新增页面须遵循第 3 章模板，新增 Token 须归入第 2 章对应分组并经设计评审，禁止在页面内硬编码色值。
