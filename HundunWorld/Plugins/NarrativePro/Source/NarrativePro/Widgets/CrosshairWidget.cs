using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 准星控件基类。移植自 UE5 NarrativeArsenal: <c>Widgets/CrosshairWidget.h</c>
    /// （<c>UCrosshairWidget : UUserWidget</c>，Abstract + Blueprintable —— 即"装备武器时自定义准星"的基类，
    /// 具体外观在蓝图子类里画）。
    /// <para>
    /// Flax 没有 UMG，因此这里用真正的 Flax GUI 控件实现：派生自 <see cref="Control"/>，
    /// 在 <see cref="Draw"/> 里用 <see cref="Render2D"/> 直接绘制四向准星 + 中心点，
    /// 子类只需要调参数（<see cref="SetSpread"/> 用于 recoil/腰射扩散，<see cref="ApplyTheme"/> 用于换色）。
    /// 准星本身不拦截输入（<see cref="Control.Enabled"/> 保持 false 时仍会绘制，但不再参与命中测试），
    /// 由 HUD 层把它居中摆放。
    /// </para>
    /// </summary>
    public abstract class CrosshairWidget : Control
    {
        /// <summary>准星臂长（像素）。</summary>
        public float LineLength = 10f;

        /// <summary>准星臂宽（像素）。</summary>
        public float LineThickness = 2f;

        /// <summary>准星与中心点的间隙（像素）。</summary>
        public float CenterGap = 4f;

        /// <summary>是否绘制中心点。</summary>
        public bool bShowCenterDot = true;

        /// <summary>中心点半径（像素）。</summary>
        public float CenterDotRadius = 1.5f;

        /// <summary>准星颜色；主题跟随开启时会被覆盖。</summary>
        public Color CrosshairColor = Color.White;

        /// <summary>是否从 <see cref="NarrativeUIDeveloperSettings"/> 取色（对应 UE5 控件从样式表取色）。</summary>
        public bool bFollowTheme = true;

        /// <summary>额外扩散量（像素），随开火/移动动态变化（对应 UE5 蓝图里按 spread 改间隙）。</summary>
        public float Spread { get; private set; }

        /// <summary>整体透明度（瞄准镜切换、HUD 隐藏动画用）。</summary>
        public float Opacity = 1f;

        protected CrosshairWidget()
        {
            // 构造期只用 TopLeft 锚，位置由宿主 HUD 显式居中（非 TopLeft 锚会在父尺寸为 0 时算错位置）
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(64f, 64f);

            // 准星是纯显示层，不吃鼠标事件
            AutoFocus = false;

            ApplyTheme();
        }

        /// <summary>设置扩散量（0 表示归位）。</summary>
        public virtual void SetSpread(float spread)
        {
            Spread = Math.Max(0f, spread);
        }

        /// <summary>整体显隐（对应 UE5 的 SetVisibility(Collapsed)）。</summary>
        public virtual void SetCrosshairVisible(bool bVisible)
        {
            Visible = bVisible;
        }

        /// <summary>从全局 UI 主题取色。对应 UE5 控件从 CommonUI 样式表拉前景色。</summary>
        public void ApplyTheme()
        {
            if (!bFollowTheme) return;

            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            // 主色偏暗时准星用白色更清晰，偏亮时用反色强调（与文本控件的取舍一致）
            CrosshairColor = settings.bUseLightTextOnPrimary ? Color.White : settings.UIPrimaryColor;
        }

        public override void Draw()
        {
            base.Draw();

            if (!Visible || Opacity <= 0.001f) return;

            Color color = CrosshairColor;
            color.A *= Opacity;

            Float2 center = new Float2(Size.X * 0.5f, Size.Y * 0.5f);
            float start = CenterGap + Spread;
            float end = start + LineLength;

            // 上
            Render2D.DrawLine(
                new Float2(center.X, center.Y - end),
                new Float2(center.X, center.Y - start),
                color,
                LineThickness);
            // 下
            Render2D.DrawLine(
                new Float2(center.X, center.Y + start),
                new Float2(center.X, center.Y + end),
                color,
                LineThickness);
            // 左
            Render2D.DrawLine(
                new Float2(center.X - end, center.Y),
                new Float2(center.X - start, center.Y),
                color,
                LineThickness);
            // 右
            Render2D.DrawLine(
                new Float2(center.X + start, center.Y),
                new Float2(center.X + end, center.Y),
                color,
                LineThickness);

            if (bShowCenterDot)
            {
                // Render2D 没有填充圆/椭圆原语（已核对 API），用中心小方块表达同一个点
                float r = CenterDotRadius;
                Render2D.FillRectangle(
                    new Rectangle(center.X - r, center.Y - r, r * 2f, r * 2f),
                    color);
            }
        }

        /// <summary>把准星居中到父控件（供 HUD 布局调用，显式重排而不是靠锚点）。</summary>
        public void CenterInParent()
        {
            var parent = Parent;
            if (parent == null) return;

            AnchorMin = Float2.Zero;
            AnchorMax = Float2.Zero;
            Location = new Float2(
                Math.Max(0f, (parent.Size.X - Size.X) * 0.5f),
                Math.Max(0f, (parent.Size.Y - Size.Y) * 0.5f));
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            CenterInParent();
        }
    }
}
