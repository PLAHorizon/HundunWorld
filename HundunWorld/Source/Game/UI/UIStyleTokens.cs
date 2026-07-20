using FlaxEngine;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 混沌世界 UI 集中式设计 Token（第一阶段）。
    ///
    /// 出处对照（设计方案目录 game-ui-system，只读）：
    /// - 色彩/圆角/间距主来源：<c>colors_and_type.css</c> 末尾「水墨古风 Dark Theme Override」
    ///   的 <c>--ink-*</c> 变量（燕云十六声式水墨古风，青为骨、金为魂、墨黑为底）。
    /// - 语义与使用比例：<c>ui-design-guidelines.md</c> 第 1 章（色彩）、第 2 章（字体与排版）、
    ///   第 4 章（组件库规范 ds-btn / ds-card / ds-input / ds-table / ds-progress / ds-dialog）。
    ///
    /// 规范要点（ui-design-guidelines.md §1.5 禁忌组合）：
    /// - 禁止纯黑 #000000 作背景，必须使用 BgVoid #0E1016。
    /// - 禁止纯白 #FFFFFF 作文字，必须使用 TextPrimary #F0EDE4（宣纸白）。
    /// - 鎏金仅用于边框/标题/图标点缀，不可大面积铺底。
    /// - 血色仅用于危险操作与生命值。
    ///
    /// 所有 UI 代码新增/修改视觉样式时必须引用本类，禁止再写硬编码色值。
    /// </summary>
    public static class UIStyleTokens
    {
        #region 背景层 — 墨黑（colors_and_type.css: --ink-bg-*）

        /// <summary>墨黑全局背景 #0E1016（--ink-bg-void / --bg-base-default）</summary>
        public static readonly Color BgVoid = new Color(14f / 255f, 16f / 255f, 22f / 255f, 1f);

        /// <summary>深渊背景 #0A0B10（--ink-bg-abyss）</summary>
        public static readonly Color BgAbyss = new Color(10f / 255f, 11f / 255f, 16f / 255f, 1f);

        /// <summary>墨水深背景 #14171E，面板/卡片底色（--ink-bg-ink / --bg-base-secondary）</summary>
        public static readonly Color BgInk = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f);

        /// <summary>半透明面板背景 rgba(20,23,30,0.85)（--ink-bg-panel，HUD 四角集群用）</summary>
        public static readonly Color BgPanel = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f);

        /// <summary>纸面/输入聚焦背景 #1C1F28（--ink-bg-paper / --bg-base-tertiary）</summary>
        public static readonly Color BgPaper = new Color(28f / 255f, 31f / 255f, 40f / 255f, 1f);

        /// <summary>抬升背景 #1A1D26，进度条轨道、按下态（--ink-bg-elevated）</summary>
        public static readonly Color BgElevated = new Color(26f / 255f, 29f / 255f, 38f / 255f, 1f);

        /// <summary>金色薄雾叠层 L1 rgba(200,168,88,0.04)（--ink-bg-mist / --bg-overlay-l1，次按钮背景）</summary>
        public static readonly Color BgMist = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f);

        /// <summary>悬停叠层 rgba(200,168,88,0.08)（--ink-bg-hover / --bg-overlay-l2）</summary>
        public static readonly Color BgHover = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        /// <summary>叠层 L3 rgba(200,168,88,0.12)（--bg-overlay-l3）</summary>
        public static readonly Color BgOverlay3 = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f);

        /// <summary>对话框遮罩 rgba(14,16,22,0.8)（ds-dialog §4.7：墨黑模糊遮罩）</summary>
        public static readonly Color Scrim = new Color(14f / 255f, 16f / 255f, 22f / 255f, 0.8f);

        #endregion

        #region 鎏金系 — 次主色 / 品牌（colors_and_type.css: --ink-gold-*）

        /// <summary>鎏金主色 #C8A858（--ink-gold-primary / --bg-brand）</summary>
        public static readonly Color GoldPrimary = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);

        /// <summary>亮金 #E0C880，悬停高亮（--ink-gold-bright / --bg-brand-hover）</summary>
        public static readonly Color GoldBright = new Color(224f / 255f, 200f / 255f, 128f / 255f, 1f);

        /// <summary>深金 #8A7438，按下/激活态（--ink-gold-deep / --bg-brand-active）</summary>
        public static readonly Color GoldDeep = new Color(138f / 255f, 116f / 255f, 56f / 255f, 1f);

        /// <summary>金色辉光 rgba(200,168,88,0.4)（--ink-gold-glow）</summary>
        public static readonly Color GoldGlow = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.4f);

        /// <summary>金色柔光 rgba(200,168,88,0.5)（--ink-gold-dim）</summary>
        public static readonly Color GoldDim = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f);

        /// <summary>金色淡痕 rgba(200,168,88,0.15)（--ink-gold-faint，选中底色）</summary>
        public static readonly Color GoldFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);

        /// <summary>金色微痕 rgba(200,168,88,0.08)（--ink-gold-trace，内描边高光）</summary>
        public static readonly Color GoldTrace = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        /// <summary>品牌禁用 rgba(200,168,88,0.22)（--bg-brand-disabled）</summary>
        public static readonly Color GoldDisabled = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.22f);

        #endregion

        #region 水墨青系 — 主色（colors_and_type.css: --ink-jade-*）

        /// <summary>水墨青主色 #7EAB9E，主操作/进度条填充（--ink-jade-primary）</summary>
        public static readonly Color JadePrimary = new Color(126f / 255f, 171f / 255f, 158f / 255f, 1f);

        /// <summary>嫩绿青 #A8D4C4，悬停/高亮（--ink-jade-bright）</summary>
        public static readonly Color JadeBright = new Color(168f / 255f, 212f / 255f, 196f / 255f, 1f);

        /// <summary>深青 #5E8B7E，按下/描边强调（--ink-jade-deep）</summary>
        public static readonly Color JadeDeep = new Color(94f / 255f, 139f / 255f, 126f / 255f, 1f);

        /// <summary>青玉辉光 rgba(126,171,158,0.45)（--ink-jade-glow）</summary>
        public static readonly Color JadeGlow = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.45f);

        /// <summary>青玉柔光 rgba(126,171,158,0.55)（--ink-jade-dim）</summary>
        public static readonly Color JadeDim = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.55f);

        /// <summary>青玉淡痕 rgba(126,171,158,0.15)（--ink-jade-faint，表格选中行近似值）</summary>
        public static readonly Color JadeFaint = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.15f);

        /// <summary>青色禁用 rgba(126,171,158,0.22)（ds-btn §4.1 primary 禁用态）</summary>
        public static readonly Color JadeDisabled = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.22f);

        #endregion

        #region 血色系 — 危险 / 生命（colors_and_type.css: --ink-blood-*）

        /// <summary>血色主色 #B85450，危险操作/生命值（--ink-blood-primary）</summary>
        public static readonly Color BloodPrimary = new Color(184f / 255f, 84f / 255f, 80f / 255f, 1f);

        /// <summary>血色亮色 #D87470，悬停（--ink-blood-bright）</summary>
        public static readonly Color BloodBright = new Color(216f / 255f, 116f / 255f, 112f / 255f, 1f);

        /// <summary>血色深色 #8A3E3A，按下（--ink-blood-deep）</summary>
        public static readonly Color BloodDeep = new Color(138f / 255f, 62f / 255f, 58f / 255f, 1f);

        /// <summary>血色辉光 rgba(184,84,80,0.4)（--ink-blood-glow）</summary>
        public static readonly Color BloodGlow = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.4f);

        /// <summary>血色淡痕 rgba(184,84,80,0.12)（--ink-blood-faint）</summary>
        public static readonly Color BloodFaint = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.12f);

        /// <summary>血色禁用 rgba(184,84,80,0.22)（ds-btn §4.1 danger 禁用态）</summary>
        public static readonly Color BloodDisabled = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.22f);

        #endregion

        #region 文本色（colors_and_type.css: --ink-text-*；规范 §1.6 对比度）

        /// <summary>主文本 宣纸白 #F0EDE4（--ink-text-primary，禁纯白）</summary>
        public static readonly Color TextPrimary = new Color(240f / 255f, 237f / 255f, 228f / 255f, 1f);

        /// <summary>次文本 #B8B0A0（--ink-text-secondary）</summary>
        public static readonly Color TextSecondary = new Color(184f / 255f, 176f / 255f, 160f / 255f, 1f);

        /// <summary>弱化文本 #8A8275（--ink-text-muted）</summary>
        public static readonly Color TextMuted = new Color(138f / 255f, 130f / 255f, 117f / 255f, 1f);

        /// <summary>禁用文本 rgba(240,237,228,0.3)（--text-disabled）</summary>
        public static readonly Color TextDisabled = new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.3f);

        /// <summary>金色强调文本 #C8A858（--ink-text-gold）</summary>
        public static readonly Color TextGold = GoldPrimary;

        /// <summary>青玉强调文本 #A8D4C4（--ink-text-jade）</summary>
        public static readonly Color TextJade = JadeBright;

        /// <summary>血色强调文本 #D87470（--ink-text-blood）</summary>
        public static readonly Color TextBlood = BloodBright;

        /// <summary>品牌底色上的反白文本 墨黑 #0E1016（--ink-text-inverse / --text-onbrand）</summary>
        public static readonly Color TextInverse = BgVoid;

        #endregion

        #region 边框 / 分割线（colors_and_type.css: --ink-border-*）

        /// <summary>金色描边 rgba(200,168,88,0.25)（--ink-border-gold / --border-neutral-l3）</summary>
        public static readonly Color BorderGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.25f);

        /// <summary>亮金描边 rgba(200,168,88,0.5)（--ink-border-gold-bright，outline 卡片/选中项）</summary>
        public static readonly Color BorderGoldBright = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f);

        /// <summary>淡金描边 rgba(200,168,88,0.08)（--ink-border-faint / --border-neutral-l1）</summary>
        public static readonly Color BorderFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        /// <summary>青玉描边 rgba(94,139,126,0.3)（--ink-border-jade）</summary>
        public static readonly Color BorderJade = new Color(94f / 255f, 139f / 255f, 126f / 255f, 0.3f);

        /// <summary>分割线 rgba(200,168,88,0.12)（--ink-divider，表格行间线）</summary>
        public static readonly Color Divider = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f);

        #endregion

        #region 状态色（colors_and_type.css: --status-*，暗色适配版）

        /// <summary>信息 #4A7BA8（--status-primary-default）</summary>
        public static readonly Color StatusInfo = new Color(74f / 255f, 123f / 255f, 168f / 255f, 1f);

        /// <summary>成功 #5E8B5E（--status-success-default）</summary>
        public static readonly Color StatusSuccess = new Color(94f / 255f, 139f / 255f, 94f / 255f, 1f);

        /// <summary>提醒 #C49B5E（--status-alert-default，进行中等温和提示）</summary>
        public static readonly Color StatusAlert = new Color(196f / 255f, 155f / 255f, 94f / 255f, 1f);

        /// <summary>警告 #C47B3E（--status-warning-default）</summary>
        public static readonly Color StatusWarning = new Color(196f / 255f, 123f / 255f, 62f / 255f, 1f);

        /// <summary>错误 #B85450（--status-error-default，同血色主色）</summary>
        public static readonly Color StatusError = BloodPrimary;

        #endregion

        #region 品质色阶（colors_and_type.css: --ink-quality-*；规范 §1.3）

        /// <summary>普通 灰白 #8A8275</summary>
        public static readonly Color QualityCommon = new Color(138f / 255f, 130f / 255f, 117f / 255f, 1f);

        /// <summary>优秀 青绿 #6B8E5A</summary>
        public static readonly Color QualityUncommon = new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f);

        /// <summary>稀有 蓝紫 #4A7EA8</summary>
        public static readonly Color QualityRare = new Color(74f / 255f, 126f / 255f, 168f / 255f, 1f);

        /// <summary>史诗 紫 #8B5E9E</summary>
        public static readonly Color QualityEpic = new Color(139f / 255f, 94f / 255f, 158f / 255f, 1f);

        /// <summary>传说 赤金 #C8A858（复用鎏金主色，叠加金色辉光）</summary>
        public static readonly Color QualityLegendary = GoldPrimary;

        #endregion

        #region 五行色（colors_and_type.css: --ink-element-*；规范 §1.4，饱和度降 15%）

        /// <summary>金（白）#D4C4A0</summary>
        public static readonly Color ElementMetal = new Color(212f / 255f, 196f / 255f, 160f / 255f, 1f);

        /// <summary>木（青）#6B8E5A</summary>
        public static readonly Color ElementWood = new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f);

        /// <summary>水（黑）#4A6E8A</summary>
        public static readonly Color ElementWater = new Color(74f / 255f, 110f / 255f, 138f / 255f, 1f);

        /// <summary>火（红）#B85638</summary>
        public static readonly Color ElementFire = new Color(184f / 255f, 86f / 255f, 56f / 255f, 1f);

        /// <summary>土（黄）#8A7B5A</summary>
        public static readonly Color ElementEarth = new Color(138f / 255f, 123f / 255f, 90f / 255f, 1f);

        #endregion

        #region 阴影色（colors_and_type.css: --ink-shadow-*；Flax 无 CSS 阴影，供自定义绘制使用）

        /// <summary>深阴影 rgba(0,0,0,0.6)（--ink-shadow-deep / --ink-shadow-panel 第一层）</summary>
        public static readonly Color ShadowDeep = new Color(0f, 0f, 0f, 0.6f);

        /// <summary>中阴影 rgba(0,0,0,0.4)（--ink-shadow-mid）</summary>
        public static readonly Color ShadowMid = new Color(0f, 0f, 0f, 0.4f);

        /// <summary>浅阴影 rgba(0,0,0,0.2)（--ink-shadow-soft）</summary>
        public static readonly Color ShadowSoft = new Color(0f, 0f, 0f, 0.2f);

        /// <summary>金色辉光阴影 rgba(200,168,88,0.2)（--ink-shadow-gold 0 0 24px）</summary>
        public static readonly Color ShadowGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.2f);

        /// <summary>春青辉光阴影 rgba(126,171,158,0.45)（--ink-shadow-jade，玩家点发光/青玉光晕）</summary>
        public static readonly Color ShadowJade = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.45f);

        #endregion

        #region 圆角（colors_and_type.css: --ink-radius-*；ds-card/ds-dialog 12px 见 RadiusCard）

        /// <summary>无圆角 0（--ink-radius-none）</summary>
        public const float RadiusNone = 0f;

        /// <summary>小圆角 2px（--ink-radius-sm）</summary>
        public const float RadiusSm = 2f;

        /// <summary>中圆角 4px（--ink-radius-md）</summary>
        public const float RadiusMd = 4f;

        /// <summary>大圆角 8px（--ink-radius-lg，按钮/输入框/嵌套卡片）</summary>
        public const float RadiusLg = 8f;

        /// <summary>卡片/对话框圆角 12px（ds-card §4.2 / ds-dialog §4.7 / --radius-12）</summary>
        public const float RadiusCard = 12f;

        /// <summary>特大圆角 16px（--radius-16，大卡片/英雄横幅/弹窗主体）</summary>
        public const float Radius2xl = 16f;

        /// <summary>全圆角 999px（--radius-full，进度条/搜索框/滚动条）</summary>
        public const float RadiusFull = 999f;

        #endregion

        #region 间距（colors_and_type.css: --spacer-*；组件组合间距规范 §4.8）

        /// <summary>间距 2px</summary>
        public const float Space2 = 2f;
        /// <summary>间距 4px（--spacer-4）</summary>
        public const float Space4 = 4f;
        /// <summary>间距 6px（--spacer-6）</summary>
        public const float Space6 = 6f;
        /// <summary>间距 8px（--spacer-8，按钮组间距）</summary>
        public const float Space8 = 8f;
        /// <summary>间距 10px（--spacer-10）</summary>
        public const float Space10 = 10f;
        /// <summary>间距 12px（--spacer-12，控件内边距/组合内部间距）</summary>
        public const float Space12 = 12f;
        /// <summary>间距 16px（--spacer-16，区块间距/表单字段间距）</summary>
        public const float Space16 = 16f;
        /// <summary>间距 20px（--spacer-20，卡片默认内边距）</summary>
        public const float Space20 = 20f;
        /// <summary>间距 24px（--spacer-24，卡片网格间距）</summary>
        public const float Space24 = 24f;
        /// <summary>间距 32px（--spacer-32）</summary>
        public const float Space32 = 32f;
        /// <summary>间距 40px（--spacer-40）</summary>
        public const float Space40 = 40f;
        /// <summary>间距 48px（--spacer-48）</summary>
        public const float Space48 = 48f;
        /// <summary>间距 64px（--spacer-64）</summary>
        public const float Space64 = 64f;

        #endregion

        #region 字号阶梯（ui-design-guidelines.md §2.2，严禁使用阶梯外字号）

        /// <summary>xs 10px — 辅助标签、角标、版本号</summary>
        public const float FontSizeXs = 10f;
        /// <summary>sm 11px — 次要说明、时间戳</summary>
        public const float FontSizeSm = 11f;
        /// <summary>md 12px — 列表项、提示语</summary>
        public const float FontSizeMd = 12f;
        /// <summary>base 14px — 正文默认、按钮文字</summary>
        public const float FontSizeBase = 14f;
        /// <summary>lg 18px — 卡片描述、重要正文</summary>
        public const float FontSizeLg = 18f;
        /// <summary>h3 22px — 小节/卡片标题（楷书下限）</summary>
        public const float FontSizeH3 = 22f;
        /// <summary>h2 24px — 面板/对话框标题</summary>
        public const float FontSizeH2 = 24f;
        /// <summary>h1 28px — 页面主标题</summary>
        public const float FontSizeH1 = 28f;
        /// <summary>display 32px — 登录页大标题、赛季横幅</summary>
        public const float FontSizeDisplay = 32f;

        #endregion

        #region 控件尺寸 / 描边（ui-design-guidelines.md 第 4 章）

        /// <summary>按钮高 sm 24px（ds-btn §4.1）</summary>
        public const float ButtonHeightSm = 24f;
        /// <summary>按钮高 md 28px（ds-btn §4.1）</summary>
        public const float ButtonHeightMd = 28f;
        /// <summary>按钮高 lg 32px（ds-btn §4.1，主操作）</summary>
        public const float ButtonHeightLg = 32f;

        /// <summary>输入框高 32px（ds-input §4.3 默认）</summary>
        public const float InputHeight = 32f;

        /// <summary>表格行高 36px / 表头高 40px（ds-table §4.4）</summary>
        public const float TableRowHeight = 36f;
        /// <summary>表头高度 40px</summary>
        public const float TableHeaderHeight = 40f;

        /// <summary>标签页高 36px，下划线 2px（ds-tabs §4.5）</summary>
        public const float TabHeight = 36f;
        /// <summary>标签页激活下划线高度 2px</summary>
        public const float TabIndicatorHeight = 2f;

        /// <summary>进度条细 4px / 粗 8px（ds-progress §4.6）</summary>
        public const float ProgressHeightThin = 4f;
        /// <summary>进度条粗 8px</summary>
        public const float ProgressHeightThick = 8f;

        /// <summary>默认描边宽度 1px（--border-width-default）</summary>
        public const float BorderWidthDefault = 1f;
        /// <summary>强调描边宽度 2px（聚焦环等）</summary>
        public const float BorderWidthStrong = 2f;

        /// <summary>对话框最大宽度 520px（ds-dialog §4.7 默认）</summary>
        public const float DialogMaxWidth = 520f;

        #endregion

        #region 便捷构造（常用 alpha 变体）

        /// <summary>按给定透明度返回某颜色的变体（保留 RGB）。</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        /// <summary>墨黑背景 + 指定透明度（面板底）。</summary>
        public static Color InkPanel(float alpha)
        {
            return new Color(BgInk.R, BgInk.G, BgInk.B, alpha);
        }

        /// <summary>鎏金 + 指定透明度（选中底/辉光）。</summary>
        public static Color Gold(float alpha)
        {
            return new Color(GoldPrimary.R, GoldPrimary.G, GoldPrimary.B, alpha);
        }

        /// <summary>水墨青 + 指定透明度（选中行/辉光）。</summary>
        public static Color Jade(float alpha)
        {
            return new Color(JadePrimary.R, JadePrimary.G, JadePrimary.B, alpha);
        }

        #endregion
    }
}
