using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 品质色格子。
    /// 对应 CSS <c>.ink-cell</c> + <c>.ink-cell-quality</c> + <c>.ink-cell-badge</c>，
    /// 56x56 默认尺寸，通过 <see cref="Draw"/> 绘制品质色边框、图标纹理与数量徽章。
    /// </summary>
    public class InkCell : ContainerControl
    {
        /// <summary>格子背景色（rgba(0,0,0,0.35)）</summary>
        private static readonly Color CellBackground = new Color(0f, 0f, 0f, 0.35f);

        /// <summary>格子默认尺寸</summary>
        private const float DefaultSize = 56f;

        /// <summary>品质等级</summary>
        private InkWashTheme.InkQuality _quality = InkWashTheme.InkQuality.Common;

        /// <summary>图标纹理</summary>
        private Texture _icon;

        /// <summary>数量徽章文字</summary>
        private string _badge;

        /// <summary>边框颜色（ContainerControl 不支持 BorderColor，手动绘制）</summary>
        private Color _borderColor = InkWashTheme.QualityCommon;

        /// <summary>边框厚度</summary>
        private float _borderThickness = 1f;

        /// <summary>
        /// 品质等级。设置时更新边框色。
        /// </summary>
        public InkWashTheme.InkQuality Quality
        {
            get => _quality;
            set
            {
                _quality = value;
                _borderColor = InkWashTheme.QualityColor(_quality);
            }
        }

        /// <summary>
        /// 图标纹理。为 null 时不绘制图标。
        /// </summary>
        public Texture Icon
        {
            get => _icon;
            set => _icon = value;
        }

        /// <summary>
        /// 数量徽章文字（如 "99"）。为空时不绘制徽章。
        /// </summary>
        public string Badge
        {
            get => _badge;
            set => _badge = value;
        }

        /// <summary>
        /// 构造函数：56x56 默认尺寸，普通品质。
        /// </summary>
        public InkCell()
        {
            Size = new Float2(DefaultSize, DefaultSize);
            BackgroundColor = Color.Transparent;
            _borderColor = InkWashTheme.QualityCommon;
            _borderThickness = 1f;
            ClipChildren = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 背景
            Render2D.FillRectangle(bounds, CellBackground);

            // 2. 图标（居中，占 70%）
            if (_icon != null && _icon.IsLoaded)
            {
                float iconSize = Mathf.Min(Width, Height) * 0.7f;
                float iconX = (Width - iconSize) * 0.5f;
                float iconY = (Height - iconSize) * 0.5f;
                Render2D.DrawTexture(
                    _icon,
                    new Rectangle(iconX, iconY, iconSize, iconSize),
                    Color.White);
            }

            // 3. 品质色边框
            Render2D.DrawRectangle(bounds, _borderColor, _borderThickness);

            // 4. 数量徽章（右下角）
            if (!string.IsNullOrEmpty(_badge))
            {
                var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f);
                var font = fontRef.GetFont();
                if (font != null)
                {
                    var badgeRect = new Rectangle(
                        Width * 0.3f, Height * 0.55f,
                        Width * 0.68f, Height * 0.42f);
                    Render2D.DrawText(
                        font,
                        _badge,
                        badgeRect,
                        InkWashTheme.TextDefault,
                        TextAlignment.Far,
                        TextAlignment.Near,
                        TextWrapping.NoWrap);
                }
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 列表项。
    /// 对应 CSS <c>.ink-list-item</c> + <c>.ink-list-item.active</c>，
    /// active 状态绘制左侧 2px 金竖线与微金背景。底部 1px 分割线始终绘制。
    /// </summary>
    public class InkListItem : ContainerControl
    {
        /// <summary>active 态背景色（rgba(200,168,88,0.1)）</summary>
        private static readonly Color ActiveBackground = new Color(
            InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
            InkWashTheme.GoldPrimary.B, 0.1f);

        /// <summary>active 态左竖线宽度</summary>
        private const float ActiveBarWidth = 2f;

        /// <summary>是否激活</summary>
        private bool _active;

        /// <summary>
        /// 是否激活。true 时绘制金色左竖线与微金背景。
        /// </summary>
        public bool Active
        {
            get => _active;
            set => _active = value;
        }

        /// <summary>
        /// 构造函数：默认高度 44px，透明背景。
        /// </summary>
        public InkListItem()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Height = 44f;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. active 背景先绘制
            if (_active)
            {
                Render2D.FillRectangle(
                    new Rectangle(0, 0, Width, Height),
                    ActiveBackground);
            }

            // 2. 基类绘制子控件
            base.Draw();

            // 3. 底部分割线
            Render2D.FillRectangle(
                new Rectangle(0, Height - 1f, Width, 1f),
                InkWashTheme.Divider);

            // 4. active 左竖线
            if (_active)
            {
                Render2D.FillRectangle(
                    new Rectangle(0, 0, ActiveBarWidth, Height),
                    InkWashTheme.GoldPrimary);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 头像。
    /// 对应 CSS <c>.ink-avatar</c>，36x36 圆角矩形 + 金边，
    /// 通过 <see cref="Draw"/> 绘制背景 + 可选头像纹理 + 金色描边。
    /// </summary>
    public class InkAvatar : ContainerControl
    {
        /// <summary>头像默认尺寸</summary>
        private const float DefaultSize = 36f;

        /// <summary>头像纹理</summary>
        private Texture _avatarTexture;

        /// <summary>
        /// 头像纹理。为 null 时仅绘制底色。
        /// </summary>
        public Texture AvatarTexture
        {
            get => _avatarTexture;
            set => _avatarTexture = value;
        }

        /// <summary>
        /// 构造函数：36x36 默认尺寸。
        /// </summary>
        public InkAvatar()
        {
            Size = new Float2(DefaultSize, DefaultSize);
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 底色
            Render2D.FillRectangle(bounds, InkWashTheme.BaseTertiary);

            // 2. 头像纹理
            if (_avatarTexture != null && _avatarTexture.IsLoaded)
            {
                Render2D.DrawTexture(_avatarTexture, bounds, Color.White);
            }

            // 3. 金色边框
            Render2D.DrawRectangle(bounds, InkWashTheme.BorderGold, 1f);
        }
    }

    // =======================================================================

    /// <summary>
    /// 圆点。
    /// 对应 CSS <c>.ink-dot</c> / <c>.ink-dot-online</c>，
    /// 8x8 圆形，<see cref="Online"/> 为 true 时切换为翡翠色并绘制外发光。
    /// 继承 <see cref="Control"/>（非容器控件）。
    /// </summary>
    public class InkDot : Control
    {
        /// <summary>圆点默认尺寸</summary>
        private const float DefaultSize = 8f;

        /// <summary>是否在线</summary>
        private bool _online;

        /// <summary>
        /// 是否在线。true 时圆点为翡翠色并带发光效果。
        /// </summary>
        public bool Online
        {
            get => _online;
            set => _online = value;
        }

        /// <summary>
        /// 构造函数：8x8 默认尺寸，离线态（普通品质灰色）。
        /// </summary>
        public InkDot()
        {
            Size = new Float2(DefaultSize, DefaultSize);
            BackgroundColor = Color.Transparent;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;

            Color dotColor = _online
                ? InkWashTheme.QualityUncommon
                : InkWashTheme.QualityCommon;

            // 在线时绘制外发光（多层同心圆递减 alpha）
            if (_online)
            {
                Color jadeGlow = new Color(
                    InkWashTheme.QualityUncommon.R,
                    InkWashTheme.QualityUncommon.G,
                    InkWashTheme.QualityUncommon.B, 0.2f);
                Color jadeMid = new Color(
                    InkWashTheme.QualityUncommon.R,
                    InkWashTheme.QualityUncommon.G,
                    InkWashTheme.QualityUncommon.B, 0.4f);

                InkRenderHelper.FillCircle(center, radius + 4f, jadeGlow);
                InkRenderHelper.FillCircle(center, radius + 2f, jadeMid);
            }

            // 主体圆点
            InkRenderHelper.FillCircle(center, radius, dotColor);
        }
    }
}
