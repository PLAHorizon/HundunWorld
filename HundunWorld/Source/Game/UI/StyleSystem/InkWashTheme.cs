using FlaxEngine;
using FlaxEngine.GUI;
using System;

namespace HundunWorld.Game.UI.StyleSystem
{
    /// <summary>
    /// 燕云十六声风格水墨武侠 UI 主题 Token 系统。
    /// 对应 HTML 设计方案 <c>colors_and_type.css</c> 中的 <c>:root</c> 变量，
    /// 作为所有 Ink 组件与页面的设计 Token 来源。
    /// 与 <see cref="ChineseClassicalTheme"/> 并存，互不引用，不替换旧 UI。
    /// </summary>
    public static class InkWashTheme
    {
        #region 背景层 — 深墨黑

        /// <summary>默认背景色 #0E1016</summary>
        public static readonly Color BaseDefault = new Color(14f / 255f, 16f / 255f, 22f / 255f, 1f);

        /// <summary>次级背景色 #14171E</summary>
        public static readonly Color BaseSecondary = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f);

        /// <summary>三级背景色 #1C1F28</summary>
        public static readonly Color BaseTertiary = new Color(28f / 255f, 31f / 255f, 40f / 255f, 1f);

        /// <summary>抬升背景色 #1A1D26（按新设计方案 game-ui-system 修正）</summary>
        public static readonly Color BaseElevated = new Color(26f / 255f, 29f / 255f, 38f / 255f, 1f);

        /// <summary>面板背景色 rgba(20,23,30,0.85)</summary>
        public static readonly Color Panel = new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f);

        /// <summary>纯色面板背景 #14171E</summary>
        public static readonly Color PanelSolid = new Color(20f / 255f, 23f / 255f, 30f / 255f, 1f);

        /// <summary>虚空背景 #0E1016</summary>
        public static readonly Color Void = new Color(14f / 255f, 16f / 255f, 22f / 255f, 1f);

        /// <summary>深渊背景 #0A0B10</summary>
        public static readonly Color Abyss = new Color(10f / 255f, 11f / 255f, 16f / 255f, 1f);

        /// <summary>遮罩色 rgba(8,9,14,0.72)</summary>
        public static readonly Color Scrim = new Color(8f / 255f, 9f / 255f, 14f / 255f, 0.72f);

        /// <summary>金雾叠加层 rgba(200,168,88,0.04) — 对应 --ink-bg-mist</summary>
        public static readonly Color BgMist = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.04f);

        /// <summary>金色悬停高亮底 rgba(200,168,88,0.08) — 对应 --ink-bg-hover</summary>
        public static readonly Color BgHover = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        #endregion

        #region 鎏金系 — 品牌主色

        /// <summary>鎏金主色 #C8A858</summary>
        public static readonly Color GoldPrimary = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);

        /// <summary>鎏金亮色 #E0C880</summary>
        public static readonly Color GoldBright = new Color(224f / 255f, 200f / 255f, 128f / 255f, 1f);

        /// <summary>鎏金深色 #8A7438</summary>
        public static readonly Color GoldDeep = new Color(138f / 255f, 116f / 255f, 56f / 255f, 1f);

        /// <summary>鎏金辉光 rgba(200,168,88,0.4)</summary>
        public static readonly Color GoldGlow = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.4f);

        /// <summary>品牌悬停色 #E0C880</summary>
        public static readonly Color BrandHover = new Color(224f / 255f, 200f / 255f, 128f / 255f, 1f);

        /// <summary>品牌激活色 #8A7438</summary>
        public static readonly Color BrandActive = new Color(138f / 255f, 116f / 255f, 56f / 255f, 1f);

        /// <summary>品牌禁用色 rgba(200,168,88,0.28)</summary>
        public static readonly Color BrandDisabled = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.28f);

        /// <summary>弱化金 rgba(200,168,88,0.5) — 对应 --ink-gold-dim</summary>
        public static readonly Color GoldDim = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f);

        /// <summary>淡金 rgba(200,168,88,0.15) — 对应 --ink-gold-faint，标签底色/弱填充</summary>
        public static readonly Color GoldFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);

        /// <summary>极弱金 rgba(200,168,88,0.08) — 对应 --ink-gold-trace，悬停底色/微填充</summary>
        public static readonly Color GoldTrace = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        #endregion

        #region 古铜系 — 辅助主色

        /// <summary>古铜主色 #B87333</summary>
        public static readonly Color BronzePrimary = new Color(184f / 255f, 115f / 255f, 51f / 255f, 1f);

        /// <summary>古铜亮色 #D4944E</summary>
        public static readonly Color BronzeBright = new Color(212f / 255f, 148f / 255f, 78f / 255f, 1f);

        /// <summary>古铜深色 #7A4A20</summary>
        public static readonly Color BronzeDeep = new Color(122f / 255f, 74f / 255f, 32f / 255f, 1f);

        /// <summary>古铜辉光 rgba(184,115,51,0.4)</summary>
        public static readonly Color BronzeGlow = new Color(184f / 255f, 115f / 255f, 51f / 255f, 0.4f);

        #endregion

        #region 朱红系 — 战斗/危险/传世品质强调

        /// <summary>朱红主色 #c0392b</summary>
        public static readonly Color VermilionPrimary = new Color(192f / 255f, 57f / 255f, 43f / 255f, 1f);

        /// <summary>朱红亮色 #d9504a</summary>
        public static readonly Color VermilionBright = new Color(217f / 255f, 80f / 255f, 74f / 255f, 1f);

        /// <summary>朱红深色 #a93226</summary>
        public static readonly Color VermilionDeep = new Color(169f / 255f, 50f / 255f, 38f / 255f, 1f);

        /// <summary>朱红褪色 #8b2a20</summary>
        public static readonly Color VermilionFaded = new Color(139f / 255f, 42f / 255f, 32f / 255f, 1f);

        /// <summary>朱红辉光 rgba(192,57,43,0.4)</summary>
        public static readonly Color VermilionGlow = new Color(192f / 255f, 57f / 255f, 43f / 255f, 0.4f);

        #endregion

        #region 纸色系 — 浅色卷轴/对话框/信笺面板

        /// <summary>纸色亮色 #f5f0e8</summary>
        public static readonly Color PaperBright = new Color(245f / 255f, 240f / 255f, 232f / 255f, 1f);

        /// <summary>纸色 #ebe5d8</summary>
        public static readonly Color Paper = new Color(235f / 255f, 229f / 255f, 216f / 255f, 1f);

        /// <summary>陈旧纸色 #d4c9b8</summary>
        public static readonly Color PaperAged = new Color(212f / 255f, 201f / 255f, 184f / 255f, 1f);

        /// <summary>褪色纸色 #c8bba8</summary>
        public static readonly Color PaperFaded = new Color(200f / 255f, 187f / 255f, 168f / 255f, 1f);

        /// <summary>暗纸色 #a89e8a</summary>
        public static readonly Color PaperDark = new Color(168f / 255f, 158f / 255f, 138f / 255f, 1f);

        /// <summary>纸色面板背景 rgba(245,240,232,0.92)</summary>
        public static readonly Color PaperPanelBg = new Color(245f / 255f, 240f / 255f, 232f / 255f, 0.92f);

        /// <summary>纸色面板边框 rgba(168,158,138,0.4)</summary>
        public static readonly Color PaperPanelBorder = new Color(168f / 255f, 158f / 255f, 138f / 255f, 0.4f);

        #endregion

        #region 春青系 — 主色品牌色（新设计方案 game-ui-system 核心）

        /// <summary>春青主色 #7EAB9E — 对应 --ink-jade-primary，生命/内力/增益主色</summary>
        public static readonly Color JadePrimary = new Color(126f / 255f, 171f / 255f, 158f / 255f, 1f);

        /// <summary>嫩绿青亮色 #A8D4C4 — 对应 --ink-jade-bright，悬停态/高亮/青玉萤光粒子</summary>
        public static readonly Color JadeBright = new Color(168f / 255f, 212f / 255f, 196f / 255f, 1f);

        /// <summary>深青色 #5E8B7E — 对应 --ink-jade-deep，按下态/激活态/描边强调</summary>
        public static readonly Color JadeDeep = new Color(94f / 255f, 139f / 255f, 126f / 255f, 1f);

        /// <summary>春青辉光 rgba(126,171,158,0.45) — 对应 --ink-jade-glow</summary>
        public static readonly Color JadeGlow = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.45f);

        /// <summary>弱化青 rgba(126,171,158,0.55) — 对应 --ink-jade-dim</summary>
        public static readonly Color JadeDim = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.55f);

        /// <summary>极弱青 rgba(126,171,158,0.15) — 对应 --ink-jade-faint</summary>
        public static readonly Color JadeFaint = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.15f);

        #endregion

        #region 朱砂系 — 生命/危险补充色

        /// <summary>血色主色 #B85450 — 对应 --ink-blood-primary，敌对/扣血/危险</summary>
        public static readonly Color BloodPrimary = new Color(184f / 255f, 84f / 255f, 80f / 255f, 1f);

        /// <summary>血色亮色 #D87470 — 对应 --ink-blood-bright，高亮态/删除/扣减</summary>
        public static readonly Color BloodBright = new Color(216f / 255f, 116f / 255f, 112f / 255f, 1f);

        /// <summary>血色深色 #8A3E3A — 对应 --ink-blood-deep，铜牌/深底</summary>
        public static readonly Color BloodDeep = new Color(138f / 255f, 62f / 255f, 58f / 255f, 1f);

        /// <summary>血色辉光 rgba(184,84,80,0.4) — 对应 --ink-blood-glow</summary>
        public static readonly Color BloodGlow = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.4f);

        /// <summary>弱化血色 rgba(184,84,80,0.5) — 对应 --ink-blood-dim</summary>
        public static readonly Color BloodDim = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.5f);

        /// <summary>淡血色 rgba(184,84,80,0.12) — 对应 --ink-blood-faint，仇人/危险底色</summary>
        public static readonly Color BloodFaint = new Color(184f / 255f, 84f / 255f, 80f / 255f, 0.12f);

        #endregion

        #region 春色 / 墨青系 — 自然生机与深潭墨韵

        /// <summary>墨青底色 #0E1318</summary>
        public static readonly Color InkCyanBase = new Color(14f / 255f, 19f / 255f, 24f / 255f, 1f);

        /// <summary>墨青次级 #121A20</summary>
        public static readonly Color InkCyanSecondary = new Color(18f / 255f, 26f / 255f, 32f / 255f, 1f);

        /// <summary>春色芽绿 #8FAE6B</summary>
        public static readonly Color SpringGreenPrimary = new Color(143f / 255f, 174f / 255f, 107f / 255f, 1f);

        /// <summary>春色亮绿 #A8C77D</summary>
        public static readonly Color SpringGreenBright = new Color(168f / 255f, 199f / 255f, 125f / 255f, 1f);

        /// <summary>春色辉光 rgba(143,174,107,0.35)</summary>
        public static readonly Color SpringGreenGlow = new Color(143f / 255f, 174f / 255f, 107f / 255f, 0.35f);

        /// <summary>墨青半透明面板 rgba(18,26,32,0.85)</summary>
        public static readonly Color PanelInkCyan = new Color(18f / 255f, 26f / 255f, 32f / 255f, 0.85f);

        #endregion

        #region 品质色 — 游戏语义（传世=朱红）

        /// <summary>普通品质 #8A8275</summary>
        public static readonly Color QualityCommon = new Color(138f / 255f, 130f / 255f, 117f / 255f, 1f);

        /// <summary>优秀品质 #6B8E5A</summary>
        public static readonly Color QualityUncommon = new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f);

        /// <summary>精良品质 #4A7EA8</summary>
        public static readonly Color QualityRare = new Color(74f / 255f, 126f / 255f, 168f / 255f, 1f);

        /// <summary>史诗品质 #8B5E9E</summary>
        public static readonly Color QualityEpic = new Color(139f / 255f, 94f / 255f, 158f / 255f, 1f);

        /// <summary>传说品质（赤金）#C8A858 — 按 ui-design-guidelines.md §1.3 品质色阶，复用鎏金主色并叠加金色辉光</summary>
        public static readonly Color QualityLegendary = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);

        #endregion

        #region 五行色 — ui-design-guidelines.md §1.4（饱和度降低 15% 融入水墨基调）

        /// <summary>金行 #D4C4A0（白） — 对应 --ink-element-metal，金生水</summary>
        public static readonly Color ElementMetal = new Color(212f / 255f, 196f / 255f, 160f / 255f, 1f);

        /// <summary>木行 #6B8E5A（青） — 对应 --ink-element-wood，木生火</summary>
        public static readonly Color ElementWood = new Color(107f / 255f, 142f / 255f, 90f / 255f, 1f);

        /// <summary>水行 #4A6E8A（黑） — 对应 --ink-element-water，水生木</summary>
        public static readonly Color ElementWater = new Color(74f / 255f, 110f / 255f, 138f / 255f, 1f);

        /// <summary>火行 #B85638（红） — 对应 --ink-element-fire，火生土</summary>
        public static readonly Color ElementFire = new Color(184f / 255f, 86f / 255f, 56f / 255f, 1f);

        /// <summary>土行 #8A7B5A（黄） — 对应 --ink-element-earth，土生金</summary>
        public static readonly Color ElementEarth = new Color(138f / 255f, 123f / 255f, 90f / 255f, 1f);

        /// <summary>
        /// 五行枚举，对应金/木/水/火/土。
        /// 用于功法、属性、阵法、相生相克关系展示。
        /// </summary>
        public enum InkElement
        {
            /// <summary>金行 — 金生水</summary>
            Metal,

            /// <summary>木行 — 木生火</summary>
            Wood,

            /// <summary>水行 — 水生木</summary>
            Water,

            /// <summary>火行 — 火生土</summary>
            Fire,

            /// <summary>土行 — 土生金</summary>
            Earth
        }

        /// <summary>
        /// 根据五行返回对应色。
        /// </summary>
        /// <param name="element">五行</param>
        /// <returns>五行色</returns>
        public static Color ElementColor(InkElement element)
        {
            return element switch
            {
                InkElement.Metal => ElementMetal,
                InkElement.Wood => ElementWood,
                InkElement.Water => ElementWater,
                InkElement.Fire => ElementFire,
                InkElement.Earth => ElementEarth,
                _ => ElementMetal
            };
        }

        /// <summary>
        /// 判断 src 是否生克 dst（相生关系）。
        /// 相生序：金→水→木→火→土→金
        /// </summary>
        /// <param name="src">源五行</param>
        /// <param name="dst">目标五行</param>
        /// <returns>true=相生；false=不相生</returns>
        public static bool IsElementGenerating(InkElement src, InkElement dst)
        {
            return src switch
            {
                InkElement.Metal => dst == InkElement.Water,
                InkElement.Wood => dst == InkElement.Fire,
                InkElement.Water => dst == InkElement.Wood,
                InkElement.Fire => dst == InkElement.Earth,
                InkElement.Earth => dst == InkElement.Metal,
                _ => false
            };
        }

        #endregion

        #region 粒子动效色 — ink-particles.css（4 种粒子类型）

        /// <summary>金粉粒子核心 #F0D898（径向渐变中心色）</summary>
        public static readonly Color ParticleGoldCore = new Color(240f / 255f, 216f / 255f, 152f / 255f, 1f);

        /// <summary>金粉粒子中段 #C8A858（径向渐变 50%）</summary>
        public static readonly Color ParticleGoldMid = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);

        /// <summary>金粉粒子辉光 rgba(200,168,88,0.8)</summary>
        public static readonly Color ParticleGoldGlow = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.8f);

        /// <summary>金粉粒子外层辉光 rgba(200,168,88,0.4)</summary>
        public static readonly Color ParticleGoldGlowSoft = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.4f);

        /// <summary>青玉萤光核心 #C8E6D8（径向渐变中心色）</summary>
        public static readonly Color ParticleJadeCore = new Color(200f / 255f, 230f / 255f, 216f / 255f, 1f);

        /// <summary>青玉萤光中段 #A8D4C4（径向渐变 50%）</summary>
        public static readonly Color ParticleJadeMid = new Color(168f / 255f, 212f / 255f, 196f / 255f, 1f);

        /// <summary>青玉萤光辉光 rgba(168,212,196,0.7)</summary>
        public static readonly Color ParticleJadeGlow = new Color(168f / 255f, 212f / 255f, 196f / 255f, 0.7f);

        /// <summary>青玉萤光外层辉光 rgba(126,171,158,0.4)</summary>
        public static readonly Color ParticleJadeGlowSoft = new Color(126f / 255f, 171f / 255f, 158f / 255f, 0.4f);

        /// <summary>环境微粒 rgba(168,212,196,0.5)</summary>
        public static readonly Color ParticleAmbientJade = new Color(168f / 255f, 212f / 255f, 196f / 255f, 0.5f);

        /// <summary>环境微粒金 rgba(224,200,128,0.4)</summary>
        public static readonly Color ParticleAmbientGold = new Color(224f / 255f, 200f / 255f, 128f / 255f, 0.4f);

        /// <summary>墨韵涟漪边 rgba(168,212,196,0.3)</summary>
        public static readonly Color RippleJade = new Color(168f / 255f, 212f / 255f, 196f / 255f, 0.3f);

        /// <summary>墨韵涟漪金边 rgba(224,200,128,0.4)</summary>
        public static readonly Color RippleGold = new Color(224f / 255f, 200f / 255f, 128f / 255f, 0.4f);

        #endregion

        #region 动效曲线与持续时间 — ink-particles.css + ui-design-guidelines.md §3

        /// <summary>
        /// 统一缓动曲线 — cubic-bezier(0.16, 1, 0.3, 1)，参考苹果 HIG ease-out。
        /// 用于所有粒子动效、面板过渡、按钮反馈。
        /// </summary>
        public static readonly Float2 EasingEaseOutC1 = new Float2(0.16f, 1.0f);

        /// <summary>动效曲线第三参数</summary>
        public const float EasingEaseOutC2 = 0.3f;

        /// <summary>动效曲线第四参数</summary>
        public const float EasingEaseOutC3 = 1.0f;

        /// <summary>金粉飘落持续时间 800ms — 对应 gold-burst 动画</summary>
        public const float DurationGoldBurst = 0.8f;

        /// <summary>墨韵涟漪持续时间 1200ms — 对应 ink-ripple 动画</summary>
        public const float DurationInkRipple = 1.2f;

        /// <summary>青玉萤光持续时间 1000ms — 对应 jade-firefly 动画</summary>
        public const float DurationJadeFirefly = 1.0f;

        /// <summary>环境微粒漂移周期 4000ms — 对应 ambient-drift 动画</summary>
        public const float DurationAmbientDrift = 4.0f;

        /// <summary>面板进入过渡 200ms — 对应 panel-entering 动画</summary>
        public const float DurationPanelEnter = 0.2f;

        /// <summary>面板进入 Y 位移 8px — 对应 translateY(8px)</summary>
        public const float PanelEnterOffsetY = 8f;

        /// <summary>Toast 入场持续时间 300ms — 对应 toast-slide-in 动画</summary>
        public const float DurationToastIn = 0.3f;

        /// <summary>Toast 入场 Y 位移 12px — 对应 translateY(-12px)</summary>
        public const float ToastInOffsetY = 12f;

        /// <summary>Toast 入场缩放 0.95 — 对应 scale(0.95)</summary>
        public const float ToastInScale = 0.95f;

        /// <summary>按钮按下缩放 0.97 — 对应 ds-btn:active scale(0.97)</summary>
        public const float ButtonPressScale = 0.97f;

        /// <summary>按钮按下过渡 100ms</summary>
        public const float DurationButtonPress = 0.1f;

        /// <summary>prefers-reduced-motion 模式下动画持续时间 0.01ms</summary>
        public const float DurationReducedMotion = 0.00001f;

        /// <summary>金粉粒子大小（小）4px</summary>
        public const float ParticleGoldSizeSm = 4f;

        /// <summary>金粉粒子大小（大）6px</summary>
        public const float ParticleGoldSizeLg = 6f;

        /// <summary>青玉萤光粒子大小 5px</summary>
        public const float ParticleJadeSize = 5f;

        /// <summary>环境微粒大小 3px</summary>
        public const float ParticleAmbientSize = 3f;

        /// <summary>墨韵涟漪初始描边 3px</summary>
        public const float RippleStrokeInitial = 3f;

        /// <summary>墨韵涟漪结束描边 1px</summary>
        public const float RippleStrokeFinal = 1f;

        /// <summary>墨韵涟漪第二环延迟 30%（动画 0-30% 不可见）</summary>
        public const float RippleSecondDelay = 0.3f;

        /// <summary>墨韵涟漪第二环最终缩放 1.2</summary>
        public const float RippleSecondScale = 1.2f;

        /// <summary>
        /// 判断是否启用 reduced motion（动效降至 0.01ms）。
        /// 默认返回 false，可由 InkParticleSystem 在初始化时根据系统偏好或玩家设置覆盖。
        /// </summary>
        public static bool ReducedMotion { get; set; } = false;

        /// <summary>
        /// 根据当前动效偏好返回实际持续时间。
        /// </summary>
        /// <param name="duration">正常模式下的持续时间（秒）</param>
        /// <returns>实际持续时间</returns>
        public static float GetDuration(float duration)
        {
            return ReducedMotion ? DurationReducedMotion : duration;
        }

        #endregion

        #region 状态色 — colors_and_type.css 暗色覆盖值

        /// <summary>成功状态 #5E8B5E — 对应 --status-success-default(dark)</summary>
        public static readonly Color Success = new Color(94f / 255f, 139f / 255f, 94f / 255f, 1f);

        /// <summary>警告状态 #C47B3E — 对应 --status-warning-default(dark)</summary>
        public static readonly Color Warning = new Color(196f / 255f, 123f / 255f, 62f / 255f, 1f);

        /// <summary>错误状态 #B85450（--status-error-default / --ink-blood-primary）</summary>
        public static readonly Color Error = new Color(184f / 255f, 84f / 255f, 80f / 255f, 1f);

        /// <summary>信息状态 #4A7BA8 — 对应 --status-info-default(dark)</summary>
        public static readonly Color Info = new Color(74f / 255f, 123f / 255f, 168f / 255f, 1f);

        /// <summary>提醒状态 #C49B5E — 对应 --status-alert-default(dark)</summary>
        public static readonly Color Alert = new Color(196f / 255f, 155f / 255f, 94f / 255f, 1f);

        /// <summary>提醒悬停 #D4BB7E — 对应 --status-alert-hover(dark)</summary>
        public static readonly Color AlertHover = new Color(212f / 255f, 187f / 255f, 126f / 255f, 1f);

        /// <summary>成功悬停 #7EAB7E — 对应 --status-success-hover(dark)</summary>
        public static readonly Color SuccessHover = new Color(126f / 255f, 171f / 255f, 126f / 255f, 1f);

        /// <summary>错误悬停 #D87470 — 对应 --status-error-hover(dark)，同 BloodBright</summary>
        public static readonly Color ErrorHover = BloodBright;

        #endregion

        #region 文字色

        /// <summary>默认文字 #F0EDE4 — 对应 --ink-text-primary，宣纸白</summary>
        public static readonly Color TextDefault = new Color(240f / 255f, 237f / 255f, 228f / 255f, 1f);

        /// <summary>次级文字 #B8B0A0 — 对应 --ink-text-secondary，浅褐</summary>
        public static readonly Color TextSecondary = new Color(184f / 255f, 176f / 255f, 160f / 255f, 1f);

        /// <summary>三级文字 #8A8275 — 对应 --ink-text-muted，按新设计方案修正</summary>
        public static readonly Color TextTertiary = new Color(138f / 255f, 130f / 255f, 117f / 255f, 1f);

        /// <summary>弱化文字 rgba(240,237,228,0.4) — 对应 --ink-text-faint</summary>
        public static readonly Color TextFaint = new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.4f);

        /// <summary>禁用文字 rgba(240,237,228,0.3) — 对应 --ink-text-disabled</summary>
        public static readonly Color TextDisabled = new Color(240f / 255f, 237f / 255f, 228f / 255f, 0.3f);

        /// <summary>品牌文字 #E0C880</summary>
        public static readonly Color TextBrand = new Color(224f / 255f, 200f / 255f, 128f / 255f, 1f);

        /// <summary>品牌上文字 #1A1408</summary>
        public static readonly Color TextOnBrand = new Color(26f / 255f, 20f / 255f, 8f / 255f, 1f);

        /// <summary>纸色上文字 #2a2520</summary>
        public static readonly Color TextOnPaper = new Color(42f / 255f, 37f / 255f, 32f / 255f, 1f);

        /// <summary>金色强调文字 #C8A858 — 对应 --ink-text-gold</summary>
        public static readonly Color TextGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 1f);

        /// <summary>青色强调文字 #A8D4C4 — 对应 --ink-text-jade</summary>
        public static readonly Color TextJade = new Color(168f / 255f, 212f / 255f, 196f / 255f, 1f);

        /// <summary>危险/扣减文字 #D87470 — 对应 --ink-text-blood</summary>
        public static readonly Color TextBlood = new Color(216f / 255f, 116f / 255f, 112f / 255f, 1f);

        /// <summary>反色文本 #0E1016 — 对应 --ink-text-inverse，金底/青底上的反色文本</summary>
        public static readonly Color TextInverse = new Color(14f / 255f, 16f / 255f, 22f / 255f, 1f);

        /// <summary>朱红文字 #d9504a</summary>
        public static readonly Color TextVermilion = new Color(217f / 255f, 80f / 255f, 74f / 255f, 1f);

        #endregion

        #region 边框色

        /// <summary>中性边框 L1 rgba(200,168,88,0.08) — 对应 --ink-border-faint / --border-neutral-l1</summary>
        public static readonly Color BorderNeutralL1 = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        /// <summary>中性边框 L2 rgba(200,168,88,0.15) — 对应 --ink-border-gold-subtle / --border-neutral-l2</summary>
        public static readonly Color BorderNeutralL2 = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);

        /// <summary>中性边框 L3 rgba(200,168,88,0.25) — 对应 --ink-border-gold / --border-neutral-l3</summary>
        public static readonly Color BorderNeutralL3 = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.25f);

        /// <summary>金色标准边框 rgba(200,168,88,0.25) — 对应 --ink-border-gold，按新设计方案修正</summary>
        public static readonly Color BorderGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.25f);

        /// <summary>金色高亮边框（选中态）rgba(200,168,88,0.5) — 对应 --ink-border-gold-bright</summary>
        public static readonly Color BorderGoldBright = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.5f);

        /// <summary>金色强边框 rgba(200,168,88,0.6) — 保留旧 Token 向后兼容</summary>
        public static readonly Color BorderGoldStrong = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.6f);

        /// <summary>弱金边 rgba(200,168,88,0.15) — 对应 --ink-border-gold-subtle，卡片描边</summary>
        public static readonly Color BorderGoldSubtle = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.15f);

        /// <summary>极弱描边 rgba(200,168,88,0.08) — 对应 --ink-border-faint</summary>
        public static readonly Color BorderFaint = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        /// <summary>青色边（增益/成功）rgba(94,139,126,0.3) — 对应 --ink-border-jade</summary>
        public static readonly Color BorderJade = new Color(94f / 255f, 139f / 255f, 126f / 255f, 0.3f);

        /// <summary>古铜边框 rgba(184,115,51,0.3)</summary>
        public static readonly Color BorderBronze = new Color(184f / 255f, 115f / 255f, 51f / 255f, 0.3f);

        /// <summary>朱红边框 rgba(192,57,43,0.35)</summary>
        public static readonly Color BorderVermilion = new Color(192f / 255f, 57f / 255f, 43f / 255f, 0.35f);

        /// <summary>分割线 rgba(200,168,88,0.12) — 对应 --ink-divider</summary>
        public static readonly Color Divider = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.12f);

        #endregion

        #region 阴影 — 水墨纵深（新设计方案 game-ui-system）

        /// <summary>深阴影 rgba(0,0,0,0.6) — 对应 --ink-shadow-deep</summary>
        public static readonly Color ShadowDeep = new Color(0f, 0f, 0f, 0.6f);

        /// <summary>中阴影 rgba(0,0,0,0.4) — 对应 --ink-shadow-mid</summary>
        public static readonly Color ShadowMid = new Color(0f, 0f, 0f, 0.4f);

        /// <summary>软阴影 rgba(0,0,0,0.2) — 对应 --ink-shadow-soft</summary>
        public static readonly Color ShadowSoft = new Color(0f, 0f, 0f, 0.2f);

        /// <summary>金色辉光 rgba(200,168,88,0.2) — 对应 --ink-shadow-gold，传说品质发光</summary>
        public static readonly Color ShadowGold = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.2f);

        /// <summary>春青辉光 rgba(126,171,158,0.45) — 玩家点发光，复用 JadeGlow</summary>
        public static readonly Color ShadowJade = JadeGlow;

        /// <summary>内阴影色 rgba(200,168,88,0.08) — 对应 --ink-shadow-inset (inset 0 1px 0)</summary>
        public static readonly Color ShadowInset = new Color(200f / 255f, 168f / 255f, 88f / 255f, 0.08f);

        #endregion

        #region 圆角 — 古风克制（新设计方案 2/4/8/12/16px 阶梯）

        /// <summary>无圆角 0px</summary>
        public const float RadiusNone = 0f;

        /// <summary>小圆角 2px — 对应 --ink-radius-sm / --radius-2</summary>
        public const float RadiusSm = 2f;

        /// <summary>中圆角 4px — 对应 --ink-radius-md / --radius-4</summary>
        public const float RadiusMd = 4f;

        /// <summary>大圆角 8px — 对应 --ink-radius-lg / --radius-8</summary>
        public const float RadiusLg = 8f;

        /// <summary>超大圆角 12px — 对应 --radius-12，弹窗/对话框</summary>
        public const float RadiusXl = 12f;

        /// <summary>特大圆角 16px — 对应 --radius-16，大卡片/英雄横幅</summary>
        public const float Radius2xl = 16f;

        /// <summary>全圆角 999px</summary>
        public const float RadiusFull = 999f;

        #endregion

        #region 间距

        /// <summary>间距 1 — 4px</summary>
        public const float Space1 = 4f;

        /// <summary>间距 2 — 8px</summary>
        public const float Space2 = 8f;

        /// <summary>间距 3 — 12px</summary>
        public const float Space3 = 12f;

        /// <summary>间距 4 — 16px</summary>
        public const float Space4 = 16f;

        /// <summary>间距 5 — 24px</summary>
        public const float Space5 = 24f;

        /// <summary>间距 6 — 32px</summary>
        public const float Space6 = 32f;

        /// <summary>间距 8 — 48px</summary>
        public const float Space8 = 48f;

        #endregion

        #region 控件高度 — ds-btn §4.1 三尺寸

        /// <summary>小控件高度 24px（ds-btn sm）</summary>
        public const float ControlHSm = 24f;

        /// <summary>中控件高度 28px（ds-btn md）</summary>
        public const float ControlHMd = 28f;

        /// <summary>大控件高度 32px（ds-btn lg）</summary>
        public const float ControlHLg = 32f;

        #endregion

        #region 纹理资产路径 — SubTask 2.3

        /// <summary>横屏加载背景纹理资产路径</summary>
        public const string TexAssetPathLoadingLandscape = "Content/InkWash/Textures/bg-loading-landscape";

        /// <summary>山峦加载背景纹理资产路径</summary>
        public const string TexAssetPathLoadingMountain = "Content/InkWash/Textures/bg-loading-mountain";

        /// <summary>章节水墨背景纹理资产路径</summary>
        public const string TexAssetPathChapterInk = "Content/InkWash/Textures/bg-chapter-ink";

        /// <summary>水墨场景背景纹理资产路径</summary>
        public const string TexAssetPathInkWashScene = "Content/InkWash/Textures/bg-ink-wash-scene";

        /// <summary>角色命名背景纹理资产路径</summary>
        public const string TexAssetPathCcNaming = "Content/InkWash/Textures/bg-cc-naming";

        /// <summary>角色预览背景纹理资产路径（带 .flax 后缀）</summary>
        public const string TexAssetPathCharPreviewV2 = "Content/InkWash/Textures/bg-char-preview-v2.flax";

        #endregion

        #region 字体系统

        /// <summary>
        /// 字体角色枚举，对应水墨主题的四类排版用途。
        /// </summary>
        public enum FontRole
        {
            /// <summary>展示字体 — 毛笔书法（马善政体），用于标题/加载页</summary>
            Display,

            /// <summary>标题字体 — 思源宋体，用于面板标题/章节</summary>
            Heading,

            /// <summary>正文字体 — 思源黑体，用于正文/按钮/列表</summary>
            Body,

            /// <summary>数字字体 — DIN，用于数值/计数</summary>
            Number
        }

        // 字体路径说明：
        // 原计划使用 Content/InkWash/Fonts/ 下的马善政体/思源宋体/思源黑体/DIN，
        // 但该目录与字体资产尚未导入。为避免 "Missing file" 错误导致 UI 无法渲染文字，
        // 当前临时复用 Content/Fonts/ 下已有的字体（必须带 .flax 后缀）：
        //   - Source_Han_Serif_SC_Light_Light.flax：思源宋体（覆盖 Display/Heading/Body）
        //   - Inconsolata-Regular.flax：等宽字体（覆盖 Number）
        // 待水墨字体资产导入后，恢复原路径即可。

        /// <summary>展示字体资产路径（临时使用思源宋体）</summary>
        public const string FontAssetPathDisplay = "Content/Fonts/Source_Han_Serif_SC_Light_Light.flax";

        /// <summary>标题字体资产路径（思源宋体）</summary>
        public const string FontAssetPathHeading = "Content/Fonts/Source_Han_Serif_SC_Light_Light.flax";

        /// <summary>正文字体资产路径（临时使用思源宋体）</summary>
        public const string FontAssetPathBody = "Content/Fonts/Source_Han_Serif_SC_Light_Light.flax";

        /// <summary>数字字体资产路径（Inconsolata 等宽字体）</summary>
        public const string FontAssetPathNumber = "Content/Fonts/Inconsolata-Regular.flax";

        /// <summary>FlaxEngine 内置默认字体资产路径（降级兜底用）</summary>
        private const string EngineDefaultFontPath = "Content/Fonts/Source_Han_Serif_SC_Light_Light.flax";

        /// <summary>展示字体资产（运行时由 <see cref="InitializeFonts"/> 赋值，初始为 null）</summary>
        public static FontAsset FontDisplay;

        /// <summary>标题字体资产（运行时由 <see cref="InitializeFonts"/> 赋值，初始为 null）</summary>
        public static FontAsset FontHeading;

        /// <summary>正文字体资产（运行时由 <see cref="InitializeFonts"/> 赋值，初始为 null）</summary>
        public static FontAsset FontBody;

        /// <summary>数字字体资产（运行时由 <see cref="InitializeFonts"/> 赋值，初始为 null）</summary>
        public static FontAsset FontNumber;

        /// <summary>引擎默认字体缓存（降级兜底，避免反复加载）</summary>
        private static FontAsset _engineDefaultFont;

        /// <summary>
        /// 判断字体资产是否已就绪（非空且已加载完成）。
        /// </summary>
        /// <param name="font">待检测的字体资产</param>
        /// <returns>已就绪返回 true，否则 false</returns>
        private static bool IsFontReady(FontAsset font)
        {
            return font != null && font.IsLoaded;
        }

        /// <summary>
        /// 获取（必要时加载并缓存）兜底字体资产，作为最终降级。
        /// 使用 Source_Han_Serif_SC_Light_Light 作为兜底（已确认存在于 Content/Fonts/）。
        /// 加载失败时返回 null，不抛异常。
        /// </summary>
        /// <returns>兜底字体资产，或 null</returns>
        private static FontAsset GetEngineDefaultFont()
        {
            if (IsFontReady(_engineDefaultFont))
                return _engineDefaultFont;

            try
            {
                // 优先使用思源宋体作为兜底（确定存在且为中文字体）
                var font = Content.LoadAsync<FontAsset>(FontAssetPathHeading);
                if (font != null)
                {
                    font.WaitForLoaded(5000.0);
                    if (font.IsLoaded)
                    {
                        _engineDefaultFont = font;
                        return font;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InkWashTheme] 加载引擎默认字体失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 从 <c>Content/InkWash/Fonts/</c> 路径加载全部水墨字体资产。
        /// 任意字体加载失败时记录日志但不抛异常，缺失字体会通过 <see cref="GetFont"/> 降级处理。
        /// </summary>
        public static void InitializeFonts()
        {
            FontDisplay = LoadFontAsset(FontAssetPathDisplay, nameof(FontDisplay));
            FontHeading = LoadFontAsset(FontAssetPathHeading, nameof(FontHeading));
            FontBody = LoadFontAsset(FontAssetPathBody, nameof(FontBody));
            FontNumber = LoadFontAsset(FontAssetPathNumber, nameof(FontNumber));
        }

        /// <summary>
        /// 加载单个字体资产，失败时记录日志并返回 null，不抛异常。
        /// </summary>
        /// <param name="path">字体资产路径</param>
        /// <param name="roleName">角色名（用于日志标识）</param>
        /// <returns>已加载的字体资产，或 null</returns>
        private static FontAsset LoadFontAsset(string path, string roleName)
        {
            try
            {
                var font = Content.LoadAsync<FontAsset>(path);
                if (font == null)
                {
                    Debug.LogWarning($"[InkWashTheme] 字体资产为空: {roleName} ({path})，将使用降级字体");
                    return null;
                }

                font.WaitForLoaded(10000.0);
                if (font.IsLoaded)
                {
                    Debug.Log($"[InkWashTheme] 字体加载成功: {roleName} ({path})");
                    return font;
                }

                Debug.LogWarning($"[InkWashTheme] 字体加载超时或未完成: {roleName} ({path})，将使用降级字体");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InkWashTheme] 字体加载异常: {roleName} ({path}) — {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根据字体角色获取字体资产，永不抛异常。
        /// 降级策略：
        /// 1) 优先返回对应角色的字体资产（若已加载）；
        /// 2) 若缺失，依次尝试其他已加载的水墨字体（正文→标题→展示→数字）；
        /// 3) 最终降级到 FlaxEngine 内置默认字体（<c>/Game/Fonts/DefaultFont</c>）；
        /// 4) 若内置字体也不可用，返回 null，调用方将其用于 <see cref="FontReference"/> 时
        ///    引擎会自动渲染为内置字体。
        /// </summary>
        /// <param name="role">字体角色</param>
        /// <returns>已就绪的字体资产，或 null（由引擎默认字体兜底）</returns>
        public static FontAsset GetFont(FontRole role)
        {
            // 1. 优先返回对应角色字体
            FontAsset result = role switch
            {
                FontRole.Display => FontDisplay,
                FontRole.Heading => FontHeading,
                FontRole.Body => FontBody,
                FontRole.Number => FontNumber,
                _ => FontBody
            };
            if (IsFontReady(result))
                return result;

            // 2. 跨角色降级：依次尝试其他已加载的水墨字体
            if (IsFontReady(FontBody)) return FontBody;
            if (IsFontReady(FontHeading)) return FontHeading;
            if (IsFontReady(FontDisplay)) return FontDisplay;
            if (IsFontReady(FontNumber)) return FontNumber;

            // 3. 最终降级：FlaxEngine 内置默认字体
            return GetEngineDefaultFont();
        }

        #endregion

        #region 品质辅助方法

        /// <summary>
        /// 水墨主题品质等级枚举。
        /// </summary>
        public enum InkQuality
        {
            /// <summary>普通</summary>
            Common,

            /// <summary>优秀</summary>
            Uncommon,

            /// <summary>精良</summary>
            Rare,

            /// <summary>史诗</summary>
            Epic,

            /// <summary>传说（传世）</summary>
            Legendary
        }

        /// <summary>
        /// 根据品质等级返回对应品质色。
        /// </summary>
        /// <param name="quality">品质等级</param>
        /// <returns>品质色</returns>
        public static Color QualityColor(InkQuality quality)
        {
            return quality switch
            {
                InkQuality.Common => QualityCommon,
                InkQuality.Uncommon => QualityUncommon,
                InkQuality.Rare => QualityRare,
                InkQuality.Epic => QualityEpic,
                InkQuality.Legendary => QualityLegendary,
                _ => QualityCommon
            };
        }

        /// <summary>
        /// 根据品质等级返回带 0.35 alpha 的发光色，用于品质外发光效果。
        /// </summary>
        /// <param name="quality">品质等级</param>
        /// <returns>带透明度的品质发光色</returns>
        public static Color QualityGlowColor(InkQuality quality)
        {
            var baseColor = QualityColor(quality);
            return new Color(baseColor.R, baseColor.G, baseColor.B, 0.35f);
        }

        /// <summary>
        /// 根据品质等级返回略加亮的品质文字色，保证可读性。
        /// </summary>
        /// <param name="quality">品质等级</param>
        /// <returns>略加亮的品质文字色</returns>
        public static Color QualityTextColor(InkQuality quality)
        {
            var c = QualityColor(quality);
            return new Color(c.R * 0.92f + 0.08f, c.G * 0.92f + 0.08f, c.B * 0.92f + 0.08f, 1f);
        }

        #endregion

        #region 描边辅助方法

        /// <summary>
        /// 为按钮设置金线飞白描边效果。
        /// 通过 <see cref="Button.BorderColor"/> 与 <see cref="Button.BorderThickness"/> 属性实现，
        /// 描边色取 <see cref="BorderGoldStrong"/>（强金线），模拟水墨毛笔飞白描边。
        /// </summary>
        /// <param name="button">目标按钮，为 null 时直接返回</param>
        /// <param name="thickness">描边厚度（像素），小于等于 0 时取默认 1px</param>
        public static void SetBrushBorder(Button button, float thickness)
        {
            if (button == null)
                return;

            button.BorderColor = BorderGoldStrong;
            button.BorderThickness = thickness > 0f ? thickness : 1f;
        }

        #endregion
    }
}
