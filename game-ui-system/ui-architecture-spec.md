# 混沌世界（HundunWorld）UI 架构规范

> 武侠 MMORPG《混沌世界》游戏 UI 系统设计规范文档
> 视觉风格：水墨古风暗色主题，仿燕云十六声极简沉浸式美学
> 画布项目：`game-ui-system.design`

| 项目 | 说明 |
|------|------|
| 客户端引擎 | Flax Engine（C#） |
| 服务端框架 | .NET 10 + Orleans 分布式框架 |
| 网络通信 | TouchSocket + MemoryPack 序列化 |
| 持久化存储 | SqlServer + EFCore + Redis |
| UI 原型技术 | HTML + Tailwind CSS + `--ink-*` Token 系统 |
| 文档版本 | v1.0 |

---

## 目录

1. [设计系统概述](#1-设计系统概述)
2. [Token 体系](#2-token-体系)
3. [九个界面规格](#3-九个界面规格)
4. [导航流转](#4-导航流转)
5. [品质与五行系统](#5-品质与五行系统)
6. [前端集成指引](#6-前端集成指引)

---

## 1. 设计系统概述

### 1.1 视觉语言

混沌世界 UI 系统采用**水墨古风暗色主题**，视觉灵感来源于燕云十六声的极简沉浸式美学。整体设计追求以下核心表达：

- **深邃墨色基底**：以 `#0E1016`（墨色虚空）为最深层背景，通过多层半透明面板叠加营造层次感，模拟水墨在宣纸上的晕染与沉淀。
- **鎏金点缀主调**：以 `#C8A858`（鎏金）作为唯一品牌强调色，用于边框、标题、关键数值与交互高亮，呼应武侠世界中金箔题字、铜环扣饰的物质意象。
- **青玉血色辅色**：青玉 `#5E8B7E` 用于增益、成功、辅助类状态；血色 `#B85450` 用于减益、危险、战斗伤害类状态，形成冷暖对比的情绪语言。
- **楷体标题 + 黑体正文**：标题使用 `STKaiti / KaiTi / Noto Serif SC` 楷体字族，传递古典书卷气；正文使用 `Noto Sans SC / PingFang SC` 黑体字族，保证可读性；数字使用 `DIN Alternate` 等宽数字字体，确保数值对齐。
- **极简沉浸式布局**：去除多余装饰，以金线分割、半透明毛玻璃面板、暗角晕染营造沉浸感，让玩家注意力聚焦于游戏世界本身。

### 1.2 结构保留策略

本 UI 系统基于 **TRAE Work 设计系统**的组件结构构建，采用"结构保留、Token 覆盖"的策略：

- **保留 `.ds-*` 组件结构**：沿用 TRAE Work 设计系统的 `.ds-btn`、`.ds-tooltip`、`.ds-popover`、`.ds-empty`、`.ds-code`、`.ds-kbd` 等原子组件类名与 DOM 结构，确保组件契约的一致性与可维护性。
- **覆盖为 `--ink-*` Token 系统**：在 `colors_and_type.css` 的水墨古风 override 块中，将 TRAE Work 的亮色 Token（`--bg-base-default`、`--text-default`、`--bg-brand` 等）覆盖为暗色墨系色值，并新增 `--ink-*` 命名空间的自定义 Token，用于武侠游戏特有的品质色阶、五行色阶、鎏金辉光等语义。
- **双 Token 共存**：原始 TRAE Work Token（如 `--bg-brand`、`--color-primary`）被重新赋值为墨系色值，保持对原组件样式的兼容；`--ink-*` Token 则提供更细粒度的语义化控制，供游戏 UI 专用组件引用。

### 1.3 画布项目

画布项目文件为 `game-ui-system.design`，包含以下节点：

- **9 个页面节点**（type: `page`）：
  - `page-combat-hud`（核心战斗 HUD）
  - `page-character-panel`（角色属性装备）
  - `page-skill-panel`（武学心法奇术）
  - `page-inventory`（背包行囊）
  - `page-quest-log`（任务日志）
  - `page-world-map`（世界地图）
  - `page-social-guild`（社交门派）
  - `page-shop`（商城商店）
  - `page-compass`（指南针系统）

- **5 个图片节点**（type: `image`）：
  - `image-001`：水墨深色背景纹理（`assets/textures/ink-wash-bg-dark.jpg`）
  - `image-002`：鎏金罗盘圆环（`assets/ui-elements/compass-ring.jpg`）
  - `image-003`：鎏金转角纹饰（`assets/borders/gold-filigree-corner.jpg`）
  - `image-004`：水墨山水地图（`assets/textures/ink-wash-map.jpg`）
  - `image-005`：水墨分割线（`assets/borders/ink-divider.jpg`）

### 1.4 验证状态

- 设计目录扫描脚本 `scan-design-directory.mjs` 执行结果：**通过，0 errors**。
- 9 个 HTML 原型页面均正确引用 `colors_and_type.css`、`components.css`、`scaffold.css`。
- `--ink-*` Token 在 `colors_and_type.css` 水墨 override 块（第 705-920 行）中完整定义。
- 导航交互在 `game-ui-system.design` 的 `devMetadata.interactions` 数组中注册完整（8 条正向导航 + 8 条返回导航）。

### 1.5 文件结构

```
game-ui-system/
├── game-ui-system.design          # 画布项目（页面节点 + 图片节点 + 交互定义）
├── colors_and_type.css            # Token 定义（TRAE Work 基础 Token + 水墨 --ink-* override）
├── components.css                 # 组件样式（.ds-* 原子组件 + 游戏 UI 组件）
├── scaffold.css                   # 布局骨架、重置样式、排版工具类
├── pages/                         # 9 个高保真 HTML 原型页面
│   ├── combat-hud.html
│   ├── character-panel.html
│   ├── skill-panel.html
│   ├── inventory.html
│   ├── quest-log.html
│   ├── world-map.html
│   ├── social-guild.html
│   ├── shop.html
│   └── compass.html
└── assets/                        # 图片与图标资源
    ├── borders/                   # 鎏金转角纹饰、水墨分割线
    ├── textures/                  # 水墨背景纹理、水墨地图
    ├── ui-elements/               # 鎏金罗盘圆环等 UI 元素
    └── icons/                     # SVG 图标库
```

---

## 2. Token 体系

所有 `--ink-*` 变量定义于 `colors_and_type.css` 的"水墨古风 Dark Theme Override"块中（第 705-920 行）。以下按功能分组列出完整 Token 清单。

### 2.1 背景色组（`--ink-bg-*`）

水墨暗色背景的多层堆叠体系，从最深层虚空到半透明面板，模拟墨色在宣纸上的浓淡变化。

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-bg-void` | `#0E1016` | 最深层背景（虚空层，页面底色） |
| `--ink-bg-abyss` | `#0A0B10` | 深渊背景（比虚空更深，用于凹陷区域/滚动条轨道） |
| `--ink-bg-ink` | `#14171E` | 墨色面板（标准面板背景） |
| `--ink-bg-panel` | `rgba(20,23,30,0.85)` | 半透明面板（毛玻璃面板，配合 `--ink-blur-panel` 使用） |
| `--ink-bg-paper` | `#1C1F28` | 宣纸色面板（凸起卡片/输入框背景） |
| `--ink-bg-mist` | `rgba(200,168,88,0.04)` | 雾气叠加（金色微光叠加层） |
| `--ink-bg-elevated` | `#1A1D26` | 凸起面板（悬浮卡片/弹层背景） |
| `--ink-bg-hover` | `rgba(200,168,88,0.08)` | 悬停态（列表项/按钮 hover 背景） |

### 2.2 鎏金组（`--ink-gold-*`）

鎏金色系是整个 UI 的品牌主色调，用于边框、标题、关键数值、交互高亮、品质边框等场景。

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-gold-primary` | `#C8A858` | 主金色（品牌主色，边框/标题/图标） |
| `--ink-gold-bright` | `#E0C880` | 亮金（hover 高亮/激活态） |
| `--ink-gold-deep` | `#8A7438` | 深金（active 按压态/暗部阴影） |
| `--ink-gold-glow` | `rgba(200,168,88,0.4)` | 金辉（发光效果/光晕） |
| `--ink-gold-dim` | `rgba(200,168,88,0.5)` | 暗金（滚动条 thumb/弱化高亮） |
| `--ink-gold-faint` | `rgba(200,168,88,0.15)` | 弱金（背景填充/微弱底色） |
| `--ink-gold-trace` | `rgba(200,168,88,0.08)` | 微金（最弱金色叠加/分割线底色） |

### 2.3 青玉组（`--ink-jade-*`）

青玉色系用于增益状态、成功反馈、辅助系技能、治疗类效果等正向语义场景。

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-jade-primary` | `#5E8B7E` | 主玉色（增益 buff/成功状态） |
| `--ink-jade-bright` | `#7EAB9E` | 亮玉（hover/激活高亮） |
| `--ink-jade-deep` | `#3E6B5E` | 深玉（active 按压/暗部） |
| `--ink-jade-glow` | `rgba(94,139,126,0.4)` | 玉辉（发光效果） |
| `--ink-jade-dim` | `rgba(94,139,126,0.5)` | 暗玉（弱化高亮） |
| `--ink-jade-faint` | `rgba(94,139,126,0.12)` | 弱玉（背景填充） |

### 2.4 血色组（`--ink-blood-*`）

血色色系用于减益状态、危险警告、战斗伤害、敌对目标等负向语义场景。

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-blood-primary` | `#B85450` | 血红（减益 debuff/危险/气血条） |
| `--ink-blood-bright` | `#D87470` | 亮血红（hover/激活高亮） |
| `--ink-blood-deep` | `#8A3E3A` | 暗血红（active 按压/暗部） |
| `--ink-blood-glow` | `rgba(184,84,80,0.4)` | 血辉（发光效果） |
| `--ink-blood-dim` | `rgba(184,84,80,0.5)` | 暗血（弱化高亮） |
| `--ink-blood-faint` | `rgba(184,84,80,0.12)` | 弱血（背景填充） |

### 2.5 文字组（`--ink-text-*`）

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-text-primary` | `#F0EDE4` | 主文字（正文/标题默认色，宣纸白） |
| `--ink-text-secondary` | `#B8B0A0` | 次要文字（说明文字/标签） |
| `--ink-text-muted` | `#8A8275` | 弱化文字（占位符/禁用态文字） |
| `--ink-text-faint` | `rgba(240,237,228,0.4)` | 极弱文字（水印/装饰文字） |
| `--ink-text-gold` | `#C8A858` | 金色文字（关键数值/金色标题） |
| `--ink-text-jade` | `#7EAB9E` | 玉色文字（增益数值/成功提示） |
| `--ink-text-blood` | `#D87470` | 血色文字（伤害数值/危险提示） |
| `--ink-text-inverse` | `#0E1016` | 反色文字（金色背景上的深色文字） |

### 2.6 边框组（`--ink-border-*`）

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-border-gold` | `rgba(200,168,88,0.25)` | 金边（标准面板边框） |
| `--ink-border-gold-bright` | `rgba(200,168,88,0.5)` | 亮金边（聚焦/激活边框） |
| `--ink-border-faint` | `rgba(200,168,88,0.08)` | 弱边框（内部分组边框） |
| `--ink-border-jade` | `rgba(94,139,126,0.3)` | 玉边（青玉系面板边框） |
| `--ink-divider` | `rgba(200,168,88,0.12)` | 分割线（列表分割/区域分隔） |

### 2.7 阴影组（`--ink-shadow-*`）

| Token | 色值 | 语义说明 |
|-------|------|---------|
| `--ink-shadow-deep` | `rgba(0,0,0,0.6)` | 深阴影（最深层投影） |
| `--ink-shadow-mid` | `rgba(0,0,0,0.4)` | 中阴影（标准投影） |
| `--ink-shadow-soft` | `rgba(0,0,0,0.2)` | 柔阴影（微弱投影） |
| `--ink-shadow-panel` | `0 8px 32px rgba(0,0,0,0.6), 0 2px 8px rgba(0,0,0,0.4)` | 面板投影（双层投影组合） |
| `--ink-shadow-gold` | `0 0 24px rgba(200,168,88,0.2)` | 金辉投影（金色发光效果） |
| `--ink-shadow-inset` | `inset 0 1px 0 rgba(200,168,88,0.08)` | 内嵌高光（顶部金色高光线） |

### 2.8 字体组（`--ink-font-*`）

| Token | 字族 | 语义说明 |
|-------|------|---------|
| `--ink-font-display` | `"STKaiti", "KaiTi", "Noto Serif SC", "Source Han Serif SC", "SimSun", serif` | 标题楷体（页面标题/面板标题） |
| `--ink-font-body` | `"Noto Sans SC", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif` | 正文黑体（正文/标签/说明） |
| `--ink-font-mono` | `"JetBrains Mono", ui-monospace, "SF Mono", Consolas, monospace` | 代码等宽（调试/系统信息） |
| `--ink-font-number` | `"DIN Alternate", "DIN", "Bebas Neue", monospace` | 数字字体（等级/气血/货币数值） |

### 2.9 圆角组（`--ink-radius-*`）

水墨古风美学追求棱角分明的硬朗感，圆角值整体偏小。

| Token | 值 | 语义说明 |
|-------|----|---------|
| `--ink-radius-none` | `0px` | 无圆角（分割线/全屏面板） |
| `--ink-radius-sm` | `2px` | 小圆角（按钮/输入框/小卡片） |
| `--ink-radius-md` | `4px` | 中圆角（面板/弹窗） |
| `--ink-radius-lg` | `8px` | 大圆角（大卡片/特殊容器） |

### 2.10 品质组（`--ink-quality-*`）

对应游戏枚举 `EquipmentQuality` / `ItemQuality`，用于装备/物品的边框颜色与品质标识。

| Token | 色值 | 对应枚举值 | 中文名 |
|-------|------|-----------|--------|
| `--ink-quality-common` | `#8A8275` | Common | 普通（灰） |
| `--ink-quality-uncommon` | `#6B8E5A` | Uncommon | 优良（绿） |
| `--ink-quality-rare` | `#4A7EA8` | Rare | 稀有（蓝） |
| `--ink-quality-epic` | `#8B5E9E` | Epic | 史诗（紫） |
| `--ink-quality-legendary` | `#C8A858` | Legendary / Mythic | 传说/神话（金） |

> 注：`EquipmentQuality.Mythic`（神话）复用传说金色 `#C8A858`，通过附加金色辉光效果（`--ink-shadow-gold`）区分。

### 2.11 五行组（`--ink-element-*`）

对应游戏五行属性体系，用于技能图标、装备属性、采集点标记、世界地图标记等场景。

| Token | 色值 | 五行 | 对应场景 |
|-------|------|------|---------|
| `--ink-element-metal` | `#D4C4A0` | 金 | 金属系装备/金系技能/矿石采集 |
| `--ink-element-wood` | `#6B8E5A` | 木 | 草药采集/木系技能/树木采集 |
| `--ink-element-water` | `#4A6E8A` | 水 | 水系技能/水域标记/水兽标记 |
| `--ink-element-fire` | `#B85638` | 火 | 火系技能/火域标记/火兽标记 |
| `--ink-element-earth` | `#8A7B5A` | 土 | 矿石采集/土系技能/山岳标记 |

### 2.12 模糊滤镜组

| Token | 值 | 语义说明 |
|-------|----|---------|
| `--ink-blur-panel` | `blur(8px)` | 面板毛玻璃（半透明面板背景模糊） |
| `--ink-blur-overlay` | `blur(4px)` | 覆盖层模糊（弹层背景模糊） |

---

## 3. 九个界面规格

### 3.1 combat-hud.html — 核心战斗 HUD

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/combat-hud.html` |
| 画布节点 ID | `page-combat-hud` |
| 页面标题 | 核心战斗 HUD |
| 角色定位 | 全局中心枢纽，所有功能面板的入口 |

**布局结构：四角布局**

```
┌─────────────────────────────────────────────┐
│ [角色信息]              [小地图 / 任务追踪]    │
│  · 头像/名称/等级        · 圆形小地图          │
│  · 气血条/内力条         · 当前任务目标        │
│  · 经验进度              · 坐标/区域名         │
│                                              │
│                                              │
│            [中央 3D 游戏视口]                 │
│                                              │
│                                              │
│ [技能快捷栏]            [队伍 / Buff 列表]    │
│  · 8 格技能槽            · 队伍成员头像        │
│  · 药品快捷槽            · Buff/Debuff 图标    │
│  · 8 个导航按钮          · 持续时间倒计时      │
└─────────────────────────────────────────────┘
```

- **左上角**：角色信息面板（头像、名称、等级、气血条、内力条、经验进度条）
- **右上角**：小地图（圆形雷达图）+ 任务追踪条（当前进行中任务的简要目标）
- **左下角**：技能快捷栏（8 格技能槽 + 药品快捷槽）+ 导航按钮组
- **右下角**：队伍成员列表 + Buff/Debuff 图标列表（含持续时间倒计时）

**交互入口：8 个导航按钮**

通过 `data-dom-id` 属性标记，注册在 `devMetadata.interactions` 数组中：

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `nav-character` | `page-character-panel` | 角色属性装备面板 |
| `nav-skill` | `page-skill-panel` | 武学心法奇术面板 |
| `nav-inventory` | `page-inventory` | 背包行囊面板 |
| `nav-quest` | `page-quest-log` | 任务日志面板 |
| `nav-map` | `page-world-map` | 世界地图面板 |
| `nav-social` | `page-social-guild` | 社交门派面板 |
| `nav-shop` | `page-shop` | 商城商店面板 |
| `nav-compass` | `page-compass` | 指南针系统面板 |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `CharacterEntity` | 等级（Level）、经验（Experience）、气血（HP/MaxHP）、内力（MP/MaxMP） | 左上角色信息面板的数值条与等级显示 |
| `CombatStateKind` | 战斗状态枚举（战斗中/脱战/濒死等） | HUD 边框颜色与战斗状态指示器 |
| `SkillBookEntity` | 已学技能列表、快捷栏槽位绑定 | 左下技能快捷栏 8 格技能槽 |
| `BuffType` | 增益/减益类型、剩余持续时间 | 右下 Buff/Debuff 图标与倒计时 |

---

### 3.2 character-panel.html — 角色属性装备

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/character-panel.html` |
| 画布节点 ID | `page-character-panel` |
| 页面标题 | 角色属性装备 |

**布局结构：三栏布局**

```
┌──────────┬──────────────┬──────────────┐
│          │              │              │
│  角色模型 │  五维属性面板  │  装备槽位面板  │
│  预览    │              │              │
│          │ · 力道        │ · 头部        │
│ · 3D 模型│ · 根骨        │ · 胸甲        │
│   旋转   │ · 身法        │ · 腿甲        │
│   预览   │ · 悟性        │ · 手套        │
│          │ · 福缘        │ · 鞋靴        │
│ · 门派   │              │ · 武器        │
│   标识   │ · 攻击力      │ · 饰品        │
│          │ · 防御力      │ · 主手        │
│          │ · 暴击率      │ · 副手        │
│          │ · 命中率      │ · 戒指1/2     │
│          │ · 闪避率      │ · 项链        │
│          │              │ · 耳环1/2     │
└──────────┴──────────────┴──────────────┘
```

- **左栏**：角色 3D 模型预览（可旋转），门派标识，角色基础信息
- **中栏**：五维属性（力道/根骨/身法/悟性/福缘）+ 衍生属性（攻击力/防御力/暴击率/命中率/闪避率等）
- **右栏**：装备槽位网格（13+ 槽位），每个槽位显示装备图标，品质边框颜色由 `EquipmentQuality` 决定

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `AttributeType` | Strength（力道）、Agility（身法）、Intelligence（悟性）、Constitution（根骨）、Luck（福缘） | 中栏五维属性数值 |
| `AttributeType` | Attack（攻击力）、Defense（防御力）、CriticalRate（暴击率）、HitRate（命中率）、EvasionRate（闪避率）等 | 中栏衍生属性数值 |
| `EquipmentSlot` | Head、Chest、Legs、Hands、Feet、Weapon、Accessory、MainHand、OffHand、Ring1、Ring2、Necklace、Earring1、Earring2 | 右栏装备槽位（14 个槽位） |
| `EquipmentQuality` | Common/Uncommon/Rare/Epic/Legendary/Mythic | 装备槽位边框颜色（映射 `--ink-quality-*` Token） |

---

### 3.3 skill-panel.html — 武学心法奇术

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/skill-panel.html` |
| 画布节点 ID | `page-skill-panel` |
| 页面标题 | 武学心法奇术 |

**布局结构：Tab 切换 + 技能列表 + 天赋树**

```
┌─────────────────────────────────────────────┐
│ [武学] [心法] [奇术]          [back-hud]     │
├────────────────┬────────────────────────────┤
│                │                            │
│  技能列表       │     天赋树 / 技能详情       │
│                │                            │
│ · 技能图标      │  · 技能名称/等级            │
│ · 技能名称      │  · 技能描述                 │
│ · 技能等级      │  · 伤害系数/消耗            │
│ · 技能类型标签  │  · 天赋树节点图             │
│                │  · 升级按钮                 │
│ · 拖拽到快捷栏  │                            │
│                │                            │
└────────────────┴────────────────────────────┘
```

- **顶部 Tab**：武学（主动攻击技能）、心法（被动增强技能）、奇术（特殊/控制/位移技能）
- **左侧技能列表**：当前 Tab 分类下的技能列表，含图标、名称、等级、类型标签，支持拖拽到快捷栏
- **右侧天赋树/技能详情**：选中技能的详细信息（描述、伤害系数、内力消耗、冷却时间）+ 天赋树节点图

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `SkillType` | Active（主动）、Passive（被动）、Special（特殊）、Toggle（切换）、ActiveAttack（主动攻击）、PassiveEnhancement（被动增强）、Control（控制）、Dash（位移）、Support（辅助）、Ultimate（终极） | 技能类型标签/Tab 分类/技能图标颜色 |
| `SkillLevel` | Beginner（初学）、Intermediate（入门）、Advanced（精通）、Master（宗师） | 技能列表等级显示/升级按钮状态 |
| `Profession` | 15 门派 + 无门派（共 16 值） | 门派标识/技能归属筛选 |

---

### 3.4 inventory.html — 背包行囊

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/inventory.html` |
| 画布节点 ID | `page-inventory` |
| 页面标题 | 背包行囊 |

**布局结构：8 列网格 + 物品详情 + 货币栏**

```
┌─────────────────────────────────────────────┐
│ [全部] [武器] [防具] [消耗] [材料] [任务]     │
│                                [back-hud]    │
├──────────────────────┬──────────────────────┤
│                      │                      │
│   8 列物品网格        │    物品详情面板       │
│                      │                      │
│  □ □ □ □ □ □ □ □    │  · 物品图标/名称      │
│  □ □ □ □ □ □ □ □    │  · 品质/类型          │
│  □ □ □ □ □ □ □ □    │  · 属性加成           │
│  □ □ □ □ □ □ □ □    │  · 物品描述           │
│  □ □ □ □ □ □ □ □    │  · 使用/装备/丢弃     │
│                      │                      │
├──────────────────────┴──────────────────────┤
│  金币: 99999  银两: 9999  钻石: 99  荣誉: 88 │
└─────────────────────────────────────────────┘
```

- **顶部分类筛选**：全部/武器/防具/消耗品/材料/任务物品
- **左侧物品网格**：8 列网格，每格显示物品图标，品质边框颜色由 `ItemQuality` 决定
- **右侧物品详情**：选中物品的详细信息与操作按钮（使用/装备/丢弃）
- **底部货币栏**：金币/银两/钻石/荣誉四种货币数量

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `ItemCategory` | All（全部）、Weapon（武器）、Armor（防具）、Consumable（消耗品）、Material（材料）、Quest（任务物品） | 顶部分类筛选 Tab |
| `ItemQuality` | Common/Uncommon/Rare/Epic/Legendary | 物品网格边框颜色（映射 `--ink-quality-*` Token） |
| `CurrencyType` | Gold（金币）、Silver（银两）、Diamond（钻石）、Honor（荣誉） | 底部货币栏数值 |
| `EquipmentSlot` | 装备槽位枚举 | 物品详情中的可装备槽位提示 |

---

### 3.5 quest-log.html — 任务日志

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/quest-log.html` |
| 画布节点 ID | `page-quest-log` |
| 页面标题 | 任务日志 |

**布局结构：左列表 + 右详情**

```
┌────────────────┬────────────────────────────┐
│ [主线][支线]    │                            │
│ [日常][周常]    │     任务详情面板            │
│ [活动]          │                            │
│                │  · 任务标题/等级            │
│ 任务列表        │  · 任务描述（剧情文本）     │
│                │  · 任务目标列表             │
│ · 任务名称      │    · [√] 已完成目标        │
│ · 等级/状态     │    · [○] 进行中目标        │
│ · 进度条        │  · 奖励预览                 │
│                │    · 经验/金币/物品         │
│ · 选中高亮      │  · 放弃/追踪按钮            │
│                │                            │
└────────────────┴────────────────────────────┘
```

- **左侧任务分类 + 任务列表**：分类 Tab（主线/支线/日常/周常/活动）+ 当前分类下的任务列表，含任务名称、等级要求、状态标识、进度条
- **右侧任务详情**：选中任务的完整描述、任务目标（含完成状态勾选）、奖励预览、操作按钮（追踪/放弃）

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `QuestCategory` | Main（主线）、Side（支线）、Daily（日常）、Weekly（周常）、Event（活动） | 左侧分类 Tab |
| `QuestStatus` | NotStarted（未开始）、InProgress（进行中）、Completed（已完成）、Failed（已失败） | 任务列表状态标识/目标勾选状态 |

---

### 3.6 world-map.html — 世界地图

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/world-map.html` |
| 画布节点 ID | `page-world-map` |
| 页面标题 | 世界地图 |

**布局结构：三栏（筛选列表 | 地图视口 | 区域信息）**

```
┌──────────┬────────────────────────┬──────────┐
│          │                        │          │
│ 筛选/    │     中央地图视口        │ 区域信息 │
│ 标记列表  │                        │          │
│          │  · 水墨山水风格地图     │ · 区域名 │
│ · 标记   │  · 玩家位置（金色）     │ · 区域   │
│   筛选   │  · NPC 标记            │   描述   │
│ · 标记   │  · 怪物标记            │ · NPC    │
│   列表   │  · 宝箱标记            │   列表   │
│          │  · 任务标记            │ · 采集点 │
│ · 采集点 │  · 路径点标记          │   列表   │
│   类型   │  · 采集点（五行色）    │          │
│   筛选   │                        │          │
└──────────┴────────────────────────┴──────────┘
```

- **左侧栏**：标记类型筛选（玩家/NPC/怪物/宝箱/任务/路径点）+ 采集点类型筛选（草药/树木/野兽/飞禽/矿石）+ 标记列表
- **中央栏**：水墨山水风格地图视口，显示各类标记，采集点使用五行颜色区分
- **右侧栏**：当前选中区域的信息（区域名称、描述、NPC 列表、采集点列表）

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `MarkerType` | Player（玩家）、NPC、Monster（怪物）、Treasure（宝箱）、Quest（任务）、Waypoint（路径点） | 地图标记图标/左侧筛选 |
| 采集点类型 | herb（草药-木/绿色）、tree（树木-木/绿色）、beast（野兽-火/红色）、bird（飞禽-火/红色）、ore（矿石-土/金色） | 地图采集点标记颜色（映射 `--ink-element-*` Token） |

---

### 3.7 social-guild.html — 社交门派

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/social-guild.html` |
| 画布节点 ID | `page-social-guild` |
| 页面标题 | 社交门派 |

**布局结构：Tab 切换 + 聊天窗口 + 成员列表**

```
┌─────────────────────────────────────────────┐
│ [聊天] [好友] [门派]           [back-hud]    │
├──────────────────────────┬──────────────────┤
│                          │                  │
│   聊天窗口                │   成员/好友列表   │
│                          │                  │
│  · 世界频道               │  · 在线成员       │
│  · 队伍频道               │  · 好友列表       │
│  · 门派频道               │  · 门派成员       │
│  · 私聊                   │                  │
│  · 系统/区域              │  · 头像/名称      │
│                          │  · 等级/门派      │
│  · 消息记录               │  · 在线状态       │
│  · 输入框/发送            │                  │
│                          │                  │
└──────────────────────────┴──────────────────┘
```

- **顶部 Tab**：聊天/好友/门派三个功能切换
- **左侧聊天窗口**：频道选择（世界/队伍/门派/私聊/系统/区域）+ 消息记录 + 输入框
- **右侧成员列表**：根据 Tab 切换显示在线成员/好友列表/门派成员

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `ChatKind` | World（世界）、Party（队伍）、Guild（门派）、Private（私聊）、System（系统）、Area（区域） | 聊天频道选择 Tab |
| `Profession` | 15 门派 + 无门派 | 成员列表门派标识/门派频道筛选 |
| `GuildEntity` | 门派名称、门派等级、门派成员列表、门派公告 | 门派 Tab 下的门派信息面板 |

---

### 3.8 shop.html — 商城商店

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/shop.html` |
| 画布节点 ID | `page-shop` |
| 页面标题 | 商城商店 |

**布局结构：左分类 + 中网格 + 右购物车**

```
┌──────────┬────────────────────┬──────────────┐
│          │                    │              │
│ 商品分类  │    商品网格        │ 购物车/详情   │
│          │                    │              │
│ · 武器    │  □ □ □ □          │ · 商品详情    │
│ · 防具    │  □ □ □ □          │   图标/名称   │
│ · 消耗品  │  □ □ □ □          │   品质/属性   │
│ · 材料    │                    │   价格       │
│ · 特殊    │  · 品质边框        │              │
│          │  · 价格标签        │ · 购物车      │
│          │  · 货币图标        │   商品列表    │
│          │                    │   合计金额    │
│          │                    │   购买按钮    │
└──────────┴────────────────────┴──────────────┘
```

- **左侧栏**：商品分类（武器/防具/消耗品/材料/特殊）
- **中央栏**：商品网格，每格显示商品图标（品质边框）、名称、价格（含货币图标）
- **右侧栏**：选中商品详情 + 购物车列表 + 合计金额 + 购买按钮

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `ShopCategory` | Weapon（武器）、Armor（防具）、Consumable（消耗品）、Material（材料）、Special（特殊） | 左侧商品分类 Tab |
| `CurrencyType` | Gold（金币）、Silver（银两）、Diamond（钻石）、Honor（荣誉） | 商品价格货币图标/购买货币选择 |
| `ItemQuality` | Common/Uncommon/Rare/Epic/Legendary | 商品网格边框颜色（映射 `--ink-quality-*` Token） |

---

### 3.9 compass.html — 指南针系统

| 属性 | 说明 |
|------|------|
| 文件路径 | `pages/compass.html` |
| 画布节点 ID | `page-compass` |
| 页面标题 | 指南针系统 |

**布局结构：中央司南罗盘 + 方位标记 + 底部信息**

```
┌─────────────────────────────────────────────┐
│                              [back-hud]      │
│                                              │
│              · 北（坎·水）                   │
│                                              │
│   · 东北      ┌──────────┐      · 西北       │
│  （艮·土）    │  司南罗盘  │    （乾·金）     │
│              │  · 鎏金圆环 │                 │
│              │  · 指针     │                 │
│   · 东       │  · 方位刻度 │      · 西       │
│  （震·木）    └──────────┘    （兑·金）      │
│                                              │
│              · 南（离·火）                   │
│                                              │
│   · 东南      ────────────────      · 西南    │
│  （巽·木）     底部信息栏          （坤·土）  │
│              · 当前坐标/区域/朝向             │
│              · 标记列表                      │
└─────────────────────────────────────────────┘
```

- **中央**：鎏金司南罗盘（旋转指针 + 八卦方位刻度 + 金色圆环装饰）
- **周围**：八方方位标记（东/南/西/北 + 东南/东北/西南/西北），配以八卦五行属性
- **底部**：当前坐标、区域名称、朝向信息、附近标记列表

**交互入口**

| data-dom-id | 目标页面 | 功能说明 |
|-------------|---------|---------|
| `back-hud` | `page-combat-hud` | 返回战斗 HUD（`hideEdge: true`） |

**数据绑定点**

| 数据实体 / 枚举 | 绑定字段 | UI 控件 |
|----------------|---------|---------|
| `MarkerType` | Player/NPC/Monster/Treasure/Quest/Waypoint | 罗盘周围标记点/底部标记列表 |
| 方位信息 | 东/南/西/北 + 东南/东北/西南/西北（八卦方位：坎/离/震/兑/巽/艮/坤/乾） | 罗盘八方方位标签与五行属性配色 |

---

## 4. 导航流转

### 4.1 中心枢纽架构

`combat-hud.html`（核心战斗 HUD）是整个 UI 系统的**中心枢纽**，所有功能面板均从 HUD 进入，并通过返回按钮回到 HUD。这种星型拓扑结构确保玩家始终以战斗 HUD 为核心，快速切换到各功能面板后再返回战斗，不打断游戏沉浸感。

```
                          ┌─── page-character-panel
                          ├─── page-skill-panel
                          ├─── page-inventory
        ┌─────────────┐   ├─── page-quest-log
        │ combat-hud  │───┼─── page-world-map
        │  (中心枢纽)  │   ├─── page-social-guild
        └─────────────┘   ├─── page-shop
              ↑            └─── page-compass
              │                      │
              └──── back-hud ────────┘
                 (hideEdge: true)
```

### 4.2 正向导航（combat-hud → 功能页）

共 8 条正向导航，均由 `combat-hud.html` 中的导航按钮触发，注册在 `page-combat-hud` 节点的 `devMetadata.interactions` 数组中：

| 序号 | 源页面 | data-dom-id | 目标页面 ID | 目标文件 |
|------|--------|-------------|------------|---------|
| 1 | combat-hud | `nav-character` | `page-character-panel` | character-panel.html |
| 2 | combat-hud | `nav-skill` | `page-skill-panel` | skill-panel.html |
| 3 | combat-hud | `nav-inventory` | `page-inventory` | inventory.html |
| 4 | combat-hud | `nav-quest` | `page-quest-log` | quest-log.html |
| 5 | combat-hud | `nav-map` | `page-world-map` | world-map.html |
| 6 | combat-hud | `nav-social` | `page-social-guild` | social-guild.html |
| 7 | combat-hud | `nav-shop` | `page-shop` | shop.html |
| 8 | combat-hud | `nav-compass` | `page-compass` | compass.html |

### 4.3 返回导航（功能页 → combat-hud）

共 8 条返回导航，每个功能页均包含一个 `data-dom-id="back-hud"` 的返回按钮，导航回 `page-combat-hud`，且均设置 `hideEdge: true`（隐藏导航连线，避免画布过于杂乱）：

| 序号 | 源页面 | data-dom-id | 目标页面 ID | hideEdge |
|------|--------|-------------|------------|----------|
| 1 | character-panel | `back-hud` | `page-combat-hud` | `true` |
| 2 | skill-panel | `back-hud` | `page-combat-hud` | `true` |
| 3 | inventory | `back-hud` | `page-combat-hud` | `true` |
| 4 | quest-log | `back-hud` | `page-combat-hud` | `true` |
| 5 | world-map | `back-hud` | `page-combat-hud` | `true` |
| 6 | social-guild | `back-hud` | `page-combat-hud` | `true` |
| 7 | shop | `back-hud` | `page-combat-hud` | `true` |
| 8 | compass | `back-hud` | `page-combat-hud` | `true` |

### 4.4 交互注册机制

- **画布层注册**：所有导航交互注册在 `game-ui-system.design` 文件各页面节点的 `devMetadata.interactions` 数组中，每条交互包含 `domId`（HTML 元素的 `data-dom-id` 属性值）、`targetPageId`（目标页面节点 ID）、可选的 `hideEdge`（是否隐藏导航连线）三个字段。
- **HTML 层标记**：HTML 原型中通过 `data-dom-id` 属性标记可交互元素，与 `.design` 文件中的 `domId` 一一对应。
- **路由规则**：点击带有 `data-dom-id` 的元素时，UI 路由系统查找对应的 `interactions` 记录，切换到 `targetPageId` 指定的页面。

---

## 5. 品质与五行系统

### 5.1 品质色阶映射表

品质色阶对应游戏枚举 `EquipmentQuality`（装备品质）与 `ItemQuality`（物品品质），用于装备槽位边框、物品网格边框、品质标签文字颜色等场景。

| 枚举值 | CSS Token | 色值 | 中文名 | 适用枚举 |
|--------|-----------|------|--------|---------|
| `Common` | `--ink-quality-common` | `#8A8275` | 普通（灰） | EquipmentQuality / ItemQuality |
| `Uncommon` | `--ink-quality-uncommon` | `#6B8E5A` | 优良（绿） | EquipmentQuality / ItemQuality |
| `Rare` | `--ink-quality-rare` | `#4A7EA8` | 稀有（蓝） | EquipmentQuality / ItemQuality |
| `Epic` | `--ink-quality-epic` | `#8B5E9E` | 史诗（紫） | EquipmentQuality / ItemQuality |
| `Legendary` | `--ink-quality-legendary` | `#C8A858` | 传说（金） | EquipmentQuality / ItemQuality |
| `Mythic` | `--ink-quality-legendary` | `#C8A858` | 神话（金·复用传说色） | EquipmentQuality only |

**设计说明**：

- `Mythic`（神话）品质仅存在于 `EquipmentQuality` 枚举中，`ItemQuality` 枚举无此值。
- 神话品质复用传说金色 `#C8A858`，通过附加 `--ink-shadow-gold`（`0 0 24px rgba(200,168,88,0.2)`）金色辉光效果与传说品质做视觉区分。
- 品质色从灰→绿→蓝→紫→金，形成从低到高的价值递进，金色与品牌主色 `--ink-gold-primary` 一致，强化传说/神话品质的尊贵感。

### 5.2 五行色阶映射表

五行色阶对应游戏五行属性体系（金木水火土），用于技能图标、装备五行属性、世界地图采集点标记、指南针八卦方位等场景。

| 五行 | CSS Token | 色值 | 对应场景 | 采集点/标记类型 |
|------|-----------|------|---------|----------------|
| 金 | `--ink-element-metal` | `#D4C4A0` | 金属系装备/金系技能/矿石采集 | ore（矿石） |
| 木 | `--ink-element-wood` | `#6B8E5A` | 草药采集/木系技能/树木采集 | herb（草药）、tree（树木） |
| 水 | `--ink-element-water` | `#4A6E8A` | 水系技能/水域标记/水兽标记 | — |
| 火 | `--ink-element-fire` | `#B85638` | 火系技能/火域标记/火兽标记 | beast（野兽）、bird（飞禽） |
| 土 | `--ink-element-earth` | `#8A7B5A` | 矿石采集/土系技能/山岳标记 | — |

**设计说明**：

- 五行色采用低饱和度的自然色系，与水墨古风整体色调和谐统一。
- 金（古铜 `#D4C4A0`）与品牌金色系相近但更偏暖灰，避免与品质金色混淆。
- 木（青绿 `#6B8E5A`）与青玉色系呼应，代表生机与生长。
- 水（深青 `#4A6E8A`）与土（赭石 `#8A7B5A`）均为冷色调，营造沉稳厚重感。
- 火（赤红 `#B85638`）与血色系呼应，代表危险与力量。

### 5.3 五行与八卦方位对应

指南针系统（compass.html）中，八卦方位与五行属性的对应关系：

| 方位 | 八卦 | 五行 | 配色 Token |
|------|------|------|-----------|
| 北 | 坎 | 水 | `--ink-element-water` |
| 南 | 离 | 火 | `--ink-element-fire` |
| 东 | 震 | 木 | `--ink-element-wood` |
| 西 | 兑 | 金 | `--ink-element-metal` |
| 东南 | 巽 | 木 | `--ink-element-wood` |
| 东北 | 艮 | 土 | `--ink-element-earth` |
| 西南 | 坤 | 土 | `--ink-element-earth` |
| 西北 | 乾 | 金 | `--ink-element-metal` |

---

## 6. 前端集成指引

本章说明如何在 Flax Engine（C#）客户端中将 HTML 原型映射为实际游戏 UI 组件，并与服务端 Orleans Grain 完成数据绑定。

### 6.1 UI 框架映射

HTML 原型作为**视觉参考与设计契约**，实际游戏 UI 使用 Flax Engine 的 **UI Control 系统**实现：

| HTML 原型层 | Flax Engine 实现层 | 说明 |
|------------|-------------------|------|
| `<div class="ds-btn">` | `Button` UI Control | 按钮控件，映射样式为墨系金边按钮 |
| `<div class="ds-tooltip">` | `Tooltip` UI Control | 工具提示控件 |
| `<div class="ds-popover">` | `Panel` + `Border` UI Control | 弹层面板 |
| `<div class="ds-empty">` | `Label` + `Image` UI Control | 空状态占位 |
| `<div class="ds-code">` | `Label`（等宽字体） UI Control | 代码/调试信息 |
| CSS Grid / Flexbox | `UniformGrid` / `FlowPanel` UI Control | 网格/流式布局 |
| Tailwind 响应式 | `UI Anchor` + `Stretch` 模式 | 分辨率适配 |
| `--ink-*` CSS 变量 | `Color` 对象（C#） | 颜色 Token 转换 |

HTML 原型中每个带有 `data-dom-id` 的元素，在 Flax Engine 中对应一个具名的 UI Control，通过 UI 路由系统管理显示/隐藏切换。

### 6.2 数据绑定模式

数据流从服务端 Grain 到客户端 UI 控件的完整链路：

```
服务端 Grain ──→ MemoryPack 序列化 ──→ TouchSocket 传输 ──→ 客户端反序列化 ──→ UI 控件更新
```

**详细流程**：

1. **服务端 Grain 处理**：Orleans Grain（如 `CharacterGrain`）处理业务逻辑，生成数据状态。
2. **MemoryPack 序列化**：将 Grain 的状态对象序列化为紧凑的二进制格式（MemoryPack），相比 JSON 减少约 50% 体积与序列化耗时。
3. **TouchSocket 传输**：通过 TouchSocket TCP 连接将序列化字节流传输到客户端。
4. **客户端反序列化**：客户端接收字节流，使用 MemoryPack 反序列化为对应的 DTO 对象。
5. **UI 控件更新**：将 DTO 字段绑定到 Flax Engine UI Control 的属性（Text、Color、Visible 等）。

**关键 Grain 与对应界面**：

| Grain 接口 | 职责 | 绑定界面 |
|-----------|------|---------|
| `CharacterGrain` | 角色基础数据（等级、经验、属性、装备） | combat-hud、character-panel |
| `CombatGrain` | 战斗状态、伤害计算、Buff/Debuff 管理 | combat-hud |
| `SkillGrain` | 技能书、技能等级、天赋树、快捷栏绑定 | skill-panel、combat-hud |
| `InventoryGrain` | 背包物品、货币、物品操作 | inventory、shop |
| `QuestGrain` | 任务进度、任务目标、任务奖励 | quest-log、combat-hud |
| `SocialGrain` | 聊天消息、好友列表、门派信息 | social-guild |

### 6.3 场景映射

Flax Engine 中的场景（Scene）与 UI 界面的对应关系：

| SceneType 枚举 | 对应界面 | UI 层级 | 说明 |
|----------------|---------|--------|------|
| `SceneType.GameWorld` | combat-hud | Base HUD Layer | 游戏世界主场景，HUD 常驻显示 |
| `SceneType.CharacterPanel` | character-panel | Overlay UI | 叠加在 GameWorld 之上，半透明遮罩 |
| `SceneType.SkillPanel` | skill-panel | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.Inventory` | inventory | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.QuestLog` | quest-log | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.WorldMap` | world-map | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.SocialGuild` | social-guild | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.Shop` | shop | Overlay UI | 叠加在 GameWorld 之上 |
| `SceneType.Compass` | compass | Overlay UI | 叠加在 GameWorld 之上 |

功能面板作为 Overlay UI 叠加在 `SceneType.GameWorld` 之上，底层 3D 游戏世界持续渲染，面板关闭后回到战斗 HUD 状态。

### 6.4 Token 转换

`--ink-*` CSS 变量在 Flax Engine 中转换为 `Color` 对象（RGBA 浮点值，0.0-1.0 范围）。以下是核心 Token 的转换对照：

| CSS Token | 十六进制 | Flax Engine Color（C#） |
|-----------|---------|------------------------|
| `--ink-bg-void` | `#0E1016` | `new Color(0.055f, 0.063f, 0.086f, 1.0f)` |
| `--ink-bg-ink` | `#14171E` | `new Color(0.078f, 0.090f, 0.118f, 1.0f)` |
| `--ink-bg-paper` | `#1C1F28` | `new Color(0.110f, 0.122f, 0.157f, 1.0f)` |
| `--ink-gold-primary` | `#C8A858` | `new Color(0.784f, 0.659f, 0.345f, 1.0f)` |
| `--ink-gold-bright` | `#E0C880` | `new Color(0.878f, 0.784f, 0.502f, 1.0f)` |
| `--ink-gold-deep` | `#8A7438` | `new Color(0.541f, 0.455f, 0.220f, 1.0f)` |
| `--ink-jade-primary` | `#5E8B7E` | `new Color(0.369f, 0.545f, 0.494f, 1.0f)` |
| `--ink-blood-primary` | `#B85450` | `new Color(0.722f, 0.329f, 0.314f, 1.0f)` |
| `--ink-text-primary` | `#F0EDE4` | `new Color(0.941f, 0.929f, 0.894f, 1.0f)` |
| `--ink-text-secondary` | `#B8B0A0` | `new Color(0.722f, 0.690f, 0.627f, 1.0f)` |

**转换规则**：将十六进制色值每两位转换为 0-255 的整数值，再除以 255 得到 0.0-1.0 的浮点值。半透明色值（`rgba(...)`）的 alpha 通道直接取 0.0-1.0 范围的值。

### 6.5 品质色系统

`EquipmentQuality` / `ItemQuality` 枚举值映射为 UI 边框颜色的 C# 实现：

```csharp
/// <summary>
/// 将装备/物品品质枚举转换为 UI 边框颜色
/// 对应 CSS Token: --ink-quality-*
/// </summary>
/// <param name="quality">品质枚举值</param>
/// <returns>Flax Engine Color 对象</returns>
public static Color GetQualityColor(EquipmentQuality quality)
{
    return quality switch
    {
        EquipmentQuality.Common    => new Color(0.541f, 0.510f, 0.459f),    // #8A8275 普通
        EquipmentQuality.Uncommon  => new Color(0.420f, 0.557f, 0.353f),    // #6B8E5A 优良
        EquipmentQuality.Rare      => new Color(0.290f, 0.494f, 0.659f),    // #4A7EA8 稀有
        EquipmentQuality.Epic      => new Color(0.545f, 0.369f, 0.620f),    // #8B5E9E 史诗
        EquipmentQuality.Legendary => new Color(0.784f, 0.659f, 0.345f),    // #C8A858 传说
        EquipmentQuality.Mythic    => new Color(0.784f, 0.659f, 0.345f),    // #C8A858 神话（复用传说色，附加金色辉光）
        _ => Color.White
    };
}
```

**神话品质辉光效果**：`Mythic` 品质在复用传说金色的基础上，通过附加 UI 边框辉光效果区分：

```csharp
// 神话品质附加金色辉光（对应 --ink-shadow-gold: 0 0 24px rgba(200,168,88,0.2)）
if (quality == EquipmentQuality.Mythic)
{
    borderControl.SetGlowEffect(
        color: new Color(0.784f, 0.659f, 0.345f, 0.2f),  // rgba(200,168,88,0.2)
        blurRadius: 24f
    );
}
```

**五行色系统**同理：

```csharp
/// <summary>
/// 将五行属性枚举转换为 UI 颜色
/// 对应 CSS Token: --ink-element-*
/// </summary>
public static Color GetElementColor(ElementType element)
{
    return element switch
    {
        ElementType.Metal => new Color(0.831f, 0.769f, 0.627f),  // #D4C4A0 金
        ElementType.Wood  => new Color(0.420f, 0.557f, 0.353f),  // #6B8E5A 木
        ElementType.Water => new Color(0.290f, 0.431f, 0.541f),  // #4A6E8A 水
        ElementType.Fire  => new Color(0.722f, 0.337f, 0.220f),  // #B85638 火
        ElementType.Earth => new Color(0.541f, 0.482f, 0.353f),  // #8A7B5A 土
        _ => Color.White
    };
}
```

### 6.6 导航路由

`data-dom-id` 属性对应 `SceneType` 枚举，通过 UI 路由系统切换面板：

```csharp
/// <summary>
/// UI 路由系统：根据 data-dom-id 切换到对应的功能面板
/// </summary>
public static class UIRouter
{
    /// <summary>
    /// data-dom-id 到 SceneType 的映射表
    /// </summary>
    private static readonly Dictionary<string, SceneType> RouteMap = new()
    {
        // 正向导航：combat-hud → 功能页
        { "nav-character", SceneType.CharacterPanel },
        { "nav-skill",     SceneType.SkillPanel },
        { "nav-inventory", SceneType.Inventory },
        { "nav-quest",     SceneType.QuestLog },
        { "nav-map",       SceneType.WorldMap },
        { "nav-social",    SceneType.SocialGuild },
        { "nav-shop",      SceneType.Shop },
        { "nav-compass",   SceneType.Compass },

        // 返回导航：功能页 → combat-hud
        { "back-hud",      SceneType.GameWorld }
    };

    /// <summary>
    /// 处理导航按钮点击
    /// </summary>
    /// <param name="domId">HTML 原型中的 data-dom-id 属性值</param>
    public static void Navigate(string domId)
    {
        if (RouteMap.TryGetValue(domId, out var targetType))
        {
            // 隐藏当前面板，显示目标面板
            UIManager.SwitchScene(targetType);
        }
    }
}
```

**导航逻辑说明**：

- 点击 `nav-*` 按钮时，从 `SceneType.GameWorld` 切换到对应功能面板的 `SceneType`，功能面板作为 Overlay UI 叠加。
- 点击 `back-hud` 按钮时，隐藏当前功能面板，回到 `SceneType.GameWorld`，战斗 HUD 恢复交互。
- `hideEdge: true` 在 Flax Engine 中对应关闭面板切换动画的连线指示，仅做淡入淡出过渡。

### 6.7 响应式布局

HTML 原型使用 Tailwind CSS 的响应式断点适配不同屏幕尺寸，Flax Engine 中使用 UI Anchor + Stretch 模式实现等效的多分辨率适配：

| HTML 响应式策略 | Flax Engine 适配策略 | 说明 |
|----------------|---------------------|------|
| Tailwind `sm/md/lg/xl` 断点 | `ResolutionType` 枚举 | 定义目标分辨率档位 |
| `max-width: 720px` 媒体查询 | UI Anchor Pivot 调整 | 窄屏时面板收缩 |
| Flexbox `flex-wrap` | `FlowPanel` 自动换行 | 网格自动换行适配 |
| CSS Grid `grid-template-columns` | `UniformGrid` 列数调整 | 网格列数随分辨率调整 |
| `viewport` meta 标签 | `Viewport` + `Canvas` 缩放 | 全屏自适应缩放 |

**UI Anchor 适配模式**：

```csharp
// 四角布局适配（combat-hud 的四角面板）
// 左上角：角色信息
characterInfoControl.AnchorPreset = AnchorPresets.TopLeft;
characterInfoControl.Anchor = new Vector2(0, 1);    // 锚点在左上角
characterInfoControl.Offset = new Vector2(20, -20);  // 距离边缘 20px

// 右下角：队伍/Buff
partyBuffControl.AnchorPreset = AnchorPresets.BottomRight;
partyBuffControl.Anchor = new Vector2(1, 0);        // 锚点在右下角
partyBuffControl.Offset = new Vector2(-20, 20);      // 距离边缘 20px

// 中央面板：Stretch 模式自适应
overlayPanel.AnchorPreset = AnchorPresets.StretchAll;
overlayPanel.Offset = new Vector4(80, 80, 80, 80);   // 四边留 80px 边距
```

**ResolutionType 分辨率档位**：

- `ResolutionType.FullHD`（1920x1080）：基准分辨率，HTML 原型设计基准。
- `ResolutionType.QHD`（2560x1440）：2K 分辨率，UI 元素等比放大。
- `ResolutionType.UHD`（3840x2160）：4K 分辨率，UI 元素等比放大 + 字体微调。
- `ResolutionType.HD`（1280x720）：720P 分辨率，面板边距缩小，字号微缩。

---

> 本文档基于 `game-ui-system.design` 画布项目与 9 个高保真 HTML 原型编写，作为混沌世界游戏 UI 系统的架构规范说明，指导 Flax Engine 客户端的 UI 实现与服务端数据绑定集成。
