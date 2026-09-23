using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// 水墨面板 Prefab 参数档案（对应 <see cref="InkPanel"/>，燕云融合 .ink-panel 金线描边半透明面板）。
    /// </summary>
    [ExecuteInEditMode]
    public class InkPanelProfile : UIComponentProfile
    {
        /// <summary>圆角半径（yy 规范：面板 4px，卡片 8px）</summary>
        [EditorOrder(100), EditorDisplay("面板"), Tooltip("圆角半径（yy 规范面板 4px / 卡片 8px）")]
        public float Radius = InkWashTheme.RadiusMd;

        /// <summary>面板变体（Default 85% 不透明 / Lightweight 50% 半透）</summary>
        [EditorOrder(101), EditorDisplay("面板")]
        public InkPanelVariant Variant = InkPanelVariant.Default;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public float Width = 320f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public float Height = 200f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkPanel();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkPanel panel)
            {
                panel.Radius = Radius;
                panel.Variant = Variant;
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 纸色卷轴面板 Prefab 参数档案（对应 <see cref="InkPaperPanel"/>，.ink-paper-panel 信笺/卷轴专用）。
    /// </summary>
    [ExecuteInEditMode]
    public class PaperPanelProfile : UIComponentProfile
    {
        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(100), EditorDisplay("纸色面板")]
        public float Width = 360f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(101), EditorDisplay("纸色面板")]
        public float Height = 240f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkPaperPanel();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 面板标题栏 Prefab 参数档案（对应 <see cref="InkPanelTitle"/>，左侧金条 + 品牌色标题）。
    /// </summary>
    [ExecuteInEditMode]
    public class PanelTitleProfile : UIComponentProfile
    {
        /// <summary>标题文字</summary>
        [EditorOrder(100), EditorDisplay("标题栏")]
        public string Title = "标题";

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(101), EditorDisplay("布局")]
        public float Width = 320f;

        /// <summary>高度覆写，&lt;=0 保持控件默认（42px 左右）</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public float Height = -1f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkPanelTitle();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkPanelTitle title)
                title.Title = Title;

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 四角金角装饰 Prefab 参数档案（对应 <see cref="InkCornerDeco"/>，.ink-corner-deco）。
    /// </summary>
    [ExecuteInEditMode]
    public class CornerDecoProfile : UIComponentProfile
    {
        /// <summary>显示左上角</summary>
        [EditorOrder(100), EditorDisplay("金角")]
        public bool ShowTL = true;

        /// <summary>显示右上角</summary>
        [EditorOrder(101), EditorDisplay("金角")]
        public bool ShowTR = true;

        /// <summary>显示左下角</summary>
        [EditorOrder(102), EditorDisplay("金角")]
        public bool ShowBL = true;

        /// <summary>显示右下角</summary>
        [EditorOrder(103), EditorDisplay("金角")]
        public bool ShowBR = true;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkCornerDeco();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkCornerDeco deco)
            {
                deco.ShowTL = ShowTL;
                deco.ShowTR = ShowTR;
                deco.ShowBL = ShowBL;
                deco.ShowBR = ShowBR;
            }
        }
    }

    /// <summary>
    /// 网格格子 Prefab 参数档案（对应 <see cref="InkCell"/>，.ink-cell 背包/技能槽，品质色边框 + 数量徽章）。
    /// </summary>
    [ExecuteInEditMode]
    public class CellProfile : UIComponentProfile
    {
        /// <summary>物品品质（决定边框品质色，传世=朱红）</summary>
        [EditorOrder(100), EditorDisplay("格子"), Tooltip("品质等级：Common/Uncommon/Rare/Epic/Legendary（边框品质色）")]
        public InkWashTheme.InkQuality Quality = InkWashTheme.InkQuality.Common;

        /// <summary>数量徽章文字（如 "99"），空不显示</summary>
        [EditorOrder(101), EditorDisplay("格子")]
        public string Badge = string.Empty;

        /// <summary>是否选中态（金边 + 辉光 + 微金底）</summary>
        [EditorOrder(102), EditorDisplay("格子")]
        public bool Selected = false;

        /// <summary>格子尺寸覆写（yy 规范 56px，控件默认 64px），&lt;=0 保持</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public float Size = -1f;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkCell();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkCell cell)
            {
                cell.Quality = Quality;
                cell.Badge = Badge;
                cell.Selected = Selected;
            }

            UIPrefabUtils.ApplySize(control, Size, Size);
        }
    }

    /// <summary>
    /// 列表项 Prefab 参数档案（对应 <see cref="InkListItem"/>，.ink-list-item，active 左金竖线 + 微金底）。
    /// 列表内容（图标/文字按钮等）作为子控件叠加。
    /// </summary>
    [ExecuteInEditMode]
    public class ListItemProfile : UIComponentProfile
    {
        /// <summary>是否激活态</summary>
        [EditorOrder(100), EditorDisplay("列表项")]
        public bool Active = false;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(101), EditorDisplay("布局")]
        public float Width = 280f;

        /// <summary>高度覆写，&lt;=0 保持控件默认 44px</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public float Height = -1f;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkListItem();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkListItem item)
                item.Active = Active;

            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 头像 Prefab 参数档案（对应 <see cref="InkAvatar"/>，.ink-avatar 36px 金边圆角头像框）。
    /// </summary>
    [ExecuteInEditMode]
    public class AvatarProfile : UIComponentProfile
    {
        /// <summary>头像纹理（可空，空时仅底色）</summary>
        [EditorOrder(100), EditorDisplay("头像")]
        public Texture AvatarTexture;

        /// <summary>头像尺寸覆写（yy 规范 36px），&lt;=0 保持</summary>
        [EditorOrder(101), EditorDisplay("布局")]
        public float Size = -1f;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkAvatar();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkAvatar avatar)
                avatar.AvatarTexture = AvatarTexture;

            UIPrefabUtils.ApplySize(control, Size, Size);
        }
    }

    /// <summary>
    /// 在线状态圆点 Prefab 参数档案（对应 <see cref="InkDot"/>，.ink-dot 8px，在线=绿色发光）。
    /// </summary>
    [ExecuteInEditMode]
    public class StatusDotProfile : UIComponentProfile
    {
        /// <summary>是否在线</summary>
        [EditorOrder(100), EditorDisplay("状态点")]
        public bool Online = false;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkDot();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkDot dot)
                dot.Online = Online;
        }
    }

    /// <summary>
    /// 竖排书法标题 Prefab 参数档案（对应 <see cref="InkVerticalTitle"/>，.ink-vertical-title，加载页/章节过场专用）。
    /// </summary>
    [ExecuteInEditMode]
    public class VerticalTitleProfile : UIComponentProfile
    {
        /// <summary>标题文字（逐字竖排）</summary>
        [EditorOrder(100), EditorDisplay("竖排标题")]
        public string Text = "混沌世界";

        /// <summary>字号</summary>
        [EditorOrder(101), EditorDisplay("竖排标题")]
        public float FontSize = 32f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkVerticalTitle();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkVerticalTitle title)
            {
                title.Text = Text;
                if (FontSize > 0f)
                    title.FontSize = FontSize;
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
        }
    }

    /// <summary>
    /// 水墨渐变进度条 Prefab 参数档案（对应 <see cref="InkBar"/>，.ink-bar 系列：
    /// 金/春青/血/朱红/暖金 五种渐变填充，400ms 平滑过渡，支持竖向）。
    /// </summary>
    [ExecuteInEditMode]
    public class InkBarProfile : UIComponentProfile
    {
        /// <summary>进度值 0~1</summary>
        [EditorOrder(100), EditorDisplay("进度条")]
        public float Value = 0.6f;

        /// <summary>填充渐变变体（Gold 默认/Jade 气血外/Blood 生命/Vermilion 战斗/Alert 体力）</summary>
        [EditorOrder(101), EditorDisplay("进度条"), Tooltip("填充渐变：Gold/Jade/Blood/Vermilion/Alert")]
        public InkBarFillVariant FillVariant = InkBarFillVariant.Gold;

        /// <summary>是否竖向（从下往上填充）</summary>
        [EditorOrder(102), EditorDisplay("进度条")]
        public bool Vertical = false;

        /// <summary>背景槽颜色令牌名，留空保持默认 rgba(0,0,0,0.5)</summary>
        [EditorOrder(103), EditorDisplay("进度条"), Tooltip("背景槽颜色令牌名（如 Abyss），留空保持默认")]
        public string SlotColorToken = string.Empty;

        /// <summary>边框颜色令牌名，留空保持默认弱金边</summary>
        [EditorOrder(104), EditorDisplay("进度条"), Tooltip("边框颜色令牌名（如 BorderGold），留空保持默认")]
        public string BorderColorToken = string.Empty;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(105), EditorDisplay("布局")]
        public float Width = 200f;

        /// <summary>高度覆写，&lt;=0 保持控件默认 8px</summary>
        [EditorOrder(106), EditorDisplay("布局")]
        public float Height = -1f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(107), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new InkBar();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is InkBar bar)
            {
                bar.Value = Value;
                bar.FillVariant = FillVariant;
                bar.Vertical = Vertical;
                if (UIPrefabUtils.TryResolveColorToken(SlotColorToken, out var slot))
                    bar.SlotColor = slot;
                if (UIPrefabUtils.TryResolveColorToken(BorderColorToken, out var border))
                    bar.BorderColor = border;
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }
}
