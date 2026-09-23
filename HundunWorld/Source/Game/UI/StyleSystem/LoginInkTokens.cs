using FlaxEngine;

namespace HundunWorld.Game.UI.StyleSystem
{
    /// <summary>
    /// 混沌世界登录界面 — 浅色「水墨青绿」设计 Token。
    ///
    /// 出处对照（设计稿，只读）：hundun-login/pages/login.html 的 :root theme-vars
    /// 与 .hd-shell-* / .hd-login-* 组件样式。登录页为独立的浅色写意风格，
    /// 与游戏内暗金水墨主题（UIStyleTokens / InkWashTheme）刻意区分，自成一套令牌，
    /// 满足「UI 视觉样式禁止硬编码色值、必须引用主题令牌」规范。
    ///
    /// 命名沿用设计稿语义：Primary=水墨青绿，Ink=青灰中性，Paper=宣纸底。
    /// </summary>
    public static class LoginInkTokens
    {
        // 便捷构造：从 0-255 通道与 alpha 生成颜色
        private static Color C(int r, int g, int b, float a = 1f)
            => new Color(r / 255f, g / 255f, b / 255f, a);

        /// <summary>按给定透明度返回某颜色的变体（保留 RGB）。</summary>
        public static Color WithAlpha(Color color, float alpha)
            => new Color(color.R, color.G, color.B, alpha);

        #region 品牌主色相 — 水墨青绿（login.html --hd-primary-*）

        public static readonly Color Primary50 = C(0xEE, 0xF4, 0xF4);   // #EEF4F4
        public static readonly Color Primary100 = C(0xD6, 0xE5, 0xE5);  // #D6E5E5
        public static readonly Color Primary200 = C(0xAD, 0xCC, 0xCC);  // #ADCCCC
        public static readonly Color Primary300 = C(0x84, 0xB3, 0xB4);  // #84B3B4
        public static readonly Color Primary400 = C(0x5F, 0x94, 0x96);  // #5F9496
        public static readonly Color Primary500 = C(0x4A, 0x7C, 0x7E);  // #4A7C7E 主色
        public static readonly Color Primary600 = C(0x3A, 0x65, 0x67);  // #3A6567 hover
        public static readonly Color Primary700 = C(0x2D, 0x4E, 0x50);  // #2D4E50 active / 印章底
        public static readonly Color Primary800 = C(0x1F, 0x38, 0x39);  // #1F3839
        public static readonly Color Primary900 = C(0x12, 0x23, 0x24);  // #122324

        /// <summary>语义别名：品牌主色（--hd-primary）</summary>
        public static readonly Color Primary = Primary500;

        #endregion

        #region 中性系统 — 青灰（login.html --hd-ink-* / --hd-paper）

        public static readonly Color Ink900 = C(0x1C, 0x21, 0x28);      // #1C2128 标题
        public static readonly Color Ink800 = C(0x2A, 0x32, 0x3D);      // #2A323D 欢迎标题/输入文字
        public static readonly Color Ink700 = C(0x3A, 0x46, 0x54);      // #3A4654 前景
        public static readonly Color Ink600 = C(0x4A, 0x56, 0x64);      // #4A5664 诗句/复选文字
        public static readonly Color Ink500 = C(0x5C, 0x68, 0x78);      // #5C6878 副文案/标签/状态文字
        public static readonly Color Ink400 = C(0x7A, 0x85, 0x93);      // #7A8593 版本/版权
        public static readonly Color Ink300 = C(0xA8, 0xB0, 0xBC);      // #A8B0BC 占位符/复选边框
        public static readonly Color Ink200 = C(0xCD, 0xD3, 0xDB);      // #CDD3DB 输入底线/边框
        public static readonly Color Ink100 = C(0xE5, 0xE9, 0xED);      // #E5E9ED
        public static readonly Color Ink50 = C(0xF2, 0xF4, 0xF6);       // #F2F4F6 弱化底

        /// <summary>宣纸底色（--hd-paper / 页面背景）</summary>
        public static readonly Color Paper = C(0xF5, 0xF6, 0xF4);       // #F5F6F4

        #endregion

        #region 语义状态色（login.html --state-*）

        public static readonly Color StateError = C(0xB8, 0x54, 0x50);   // #B85450
        public static readonly Color StateSuccess = C(0x5E, 0x8B, 0x5E); // #5E8B5E 状态点/成功
        public static readonly Color StateWarning = C(0xC4, 0x9B, 0x5E); // #C49B5E
        public static readonly Color StateInfo = C(0x4A, 0x7C, 0x7E);    // #4A7C7E

        #endregion

        #region 毛玻璃卡片（login.html .hd-login-card）

        /// <summary>卡片底：rgba(245,246,244,0.60) — 提高毛玻璃透出度</summary>
        public static readonly Color CardBg = C(0xF5, 0xF6, 0xF4, 0.60f);

        /// <summary>卡片描边：rgba(255,255,255,0.34) — 细淡描边，避免边框过粗</summary>
        public static readonly Color CardBorder = C(0xFF, 0xFF, 0xFF, 0.34f);

        /// <summary>卡片投影：rgba(28,33,40,0.20) — 加深以强化悬浮层次</summary>
        public static readonly Color CardShadow = C(0x1C, 0x21, 0x28, 0.20f);

        /// <summary>品牌区遮罩渐变顶：rgba(245,246,244,0.30)</summary>
        public static readonly Color OverlayTop = C(0xF5, 0xF6, 0xF4, 0.30f);

        /// <summary>品牌区遮罩渐变底：rgba(28,33,40,0.25)</summary>
        public static readonly Color OverlayBottom = C(0x1C, 0x21, 0x28, 0.25f);

        /// <summary>灵光粒子/青雾：rgba(173,204,204,0.10)</summary>
        public static readonly Color LightGlow = C(0xAD, 0xCC, 0xCC, 0.10f);

        #endregion

        #region 圆角（login.html --hd-radius-*）

        public const float RadiusSm = 4f;    // 印章
        public const float RadiusMd = 8f;    // 按钮
        public const float RadiusLg = 12f;   // 卡片
        public const float RadiusFull = 9999f;

        #endregion

        #region 间距（login.html --hd-space-*）

        public const float SpaceXs = 4f;
        public const float SpaceSm = 8f;
        public const float SpaceMd = 16f;
        public const float SpaceLg = 24f;
        public const float SpaceXl = 32f;
        public const float Space2xl = 48f;
        public const float Space3xl = 64f;

        #endregion

        #region 字号阶梯（login.html 排版工具类，精确还原，允许登录页专属大字号）

        public const float FontDisplay = 48f;   // hd-display 游戏名
        public const float FontHeadingLg = 28f; // hd-heading-lg 欢迎归来
        public const float FontHeadingMd = 20f; // hd-heading-md
        public const float FontBodyLg = 16f;    // hd-body-lg 诗句/输入文字
        public const float FontBody = 14f;      // hd-body 复选/按钮文字
        public const float FontBodySm = 12f;    // hd-body-sm 标签/副文案/链接/页脚

        #endregion

        #region 卡片/控件尺寸（login.html 具体像素）

        public const float CardMaxWidth = 400f;     // hd-login-card max-width
        public const float CardPadX = 36f;          // padding:32px 36px
        public const float CardPadY = 32f;
        public const float InputHeight = 48f;       // 底线输入：padding 12 + 行高
        public const float InputGap = 24f;          // 字段间距 margin-top:24
        public const float OptionsGap = 24f;        // 复选项间距
        public const float CheckboxSize = 18f;
        public const float ButtonHeight = 50f;      // padding:14 上 22 行高
        public const float ButtonMarginTop = 28f;
        public const float SealSize = 48f;          // hd-seal 48x48
        public const float InkStrokeWidth = 120f;   // hd-ink-stroke
        public const float InkStrokeHeight = 3f;
        public const float BrandRatio = 0.60f;      // 品牌区宽度占比 60%

        #endregion
    }
}
