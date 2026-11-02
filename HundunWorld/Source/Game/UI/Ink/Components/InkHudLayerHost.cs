using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>HUD 三层：常驻 / 上下文 / 瞬时。</summary>
    public enum InkHudLayer
    {
        Persistent = 0,
        Context = 1,
        Transient = 2,
    }

    /// <summary>
    /// HUD 分层宿主：三层全屏透明容器，按添加顺序决定绘制/命中优先级。
    /// <para>
    /// 三层都是**原生 ContainerControl** 且背景全透明，Flax 的精确命中会穿透，
    /// `OnMouseDown` 未被子控件消费时返回 false，因此不会挡住下层 HUD。
    /// </para>
    /// </summary>
    public class InkHudLayerHost : ContainerControl
    {
        public ContainerControl Persistent { get; }
        public ContainerControl Context { get; }
        public ContainerControl Transient { get; }

        public InkHudLayerHost()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            Persistent = CreateLayer();
            Context = CreateLayer();
            Transient = CreateLayer();
            AddChild(Persistent);
            AddChild(Context);
            AddChild(Transient);
        }

        public void AddToLayer(Control control, InkHudLayer layer)
        {
            if (control == null) return;
            var parent = layer switch
            {
                InkHudLayer.Context => Context,
                InkHudLayer.Transient => Transient,
                _ => Persistent,
            };
            parent.AddChild(control);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();

            // 构造期设置 AnchorPreset/Offsets 会先触发一次 UpdateBounds → OnSizeChanged，
            // 此时三层字段还没赋值；没有这道守卫会在页面构造里抛 NRE，整页变成空壳。
            if (Persistent == null || Context == null || Transient == null) return;

            foreach (var layer in new[] { Persistent, Context, Transient })
            {
                layer.Location = Float2.Zero;
                layer.Size = Size;
            }
        }

        private static ContainerControl CreateLayer()
        {
            return new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                AutoFocus = false,
            };
        }
    }
}
