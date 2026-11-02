using System;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 页面呈现类型。决定外壳为该页铺什么、给不给返回入口——取代原先按 dom-id 前缀
    /// （<c>popup-</c>）猜、以及两张手写白名单（ShellBackSuppressedPages / SelfDressedPages）的做法。
    /// </summary>
    public enum InkPageKind
    {
        /// <summary>战斗 HUD：叠加在游戏场景之上，不铺墨底、不叠暗角、不显示外壳返回。</summary>
        Hud,

        /// <summary>沉浸式全屏页：外壳铺墨底 + 柔暗角 + 左上角统一返回入口。</summary>
        Immersive,

        /// <summary>模态弹层：由外壳叠加层承载，不铺全屏压淡/暗角，面板自带投影。</summary>
        Modal,
    }

    /// <summary>
    /// 页面框架描述符：在 <see cref="InkPageRouter.RegisterPage"/> 处随工厂一起登记，
    /// 是"哪些层给哪些页"的唯一来源。
    /// </summary>
    public readonly struct InkPageFrame
    {
        /// <summary>呈现类型。</summary>
        public readonly InkPageKind Kind;

        /// <summary>页面自带返回入口（左上角 logo / 侧栏 / 阶段流返回），不显示外壳返回按钮以免重叠。</summary>
        public readonly bool OwnBack;

        /// <summary>
        /// 自铺舞台页：页面自带背景/暗角（如过场加载图、取景器式相机页、创角浅色舞台），
        /// 外壳只保留墨底（遮住非 16:9 窗口的信箱黑边）、不叠暗角——免得与页面自己的暗角叠成两层。
        /// </summary>
        public readonly bool SelfLitStage;

        /// <summary>
        /// 透明舞台页：外壳不铺墨底也不铺暗角，页面直接叠在 3D 场景上（拍照模式的取景器）。
        /// 与 <see cref="SelfLitStage"/>（保留墨底遮信箱边）不同，这里连墨底也要让开。
        /// </summary>
        public readonly bool NoBackdrop;

        public InkPageFrame(InkPageKind kind, bool ownBack = false, bool selfLitStage = false, bool noBackdrop = false)
        {
            Kind = kind;
            OwnBack = ownBack;
            SelfLitStage = selfLitStage;
            NoBackdrop = noBackdrop;
        }

        /// <summary>战斗 HUD。</summary>
        public static InkPageFrame Hud => new InkPageFrame(InkPageKind.Hud);

        /// <summary>标准沉浸式全屏页：墨底 + 柔暗角 + 外壳返回按钮。</summary>
        public static InkPageFrame Immersive => new InkPageFrame(InkPageKind.Immersive);

        /// <summary>沉浸式全屏页，但页面自带返回入口。</summary>
        public static InkPageFrame ImmersiveOwnBack => new InkPageFrame(InkPageKind.Immersive, ownBack: true);

        /// <summary>沉浸式全屏页，自铺浅色舞台（保留墨底遮信箱边、不叠暗角）。</summary>
        public static InkPageFrame SelfLitImmersive => new InkPageFrame(InkPageKind.Immersive, selfLitStage: true);

        /// <summary>沉浸式全屏页，自铺浅色舞台且自带返回入口（创角/选角流程）。</summary>
        public static InkPageFrame SelfLitImmersiveOwnBack => new InkPageFrame(InkPageKind.Immersive, ownBack: true, selfLitStage: true);

        /// <summary>透明舞台页：不铺墨底/暗角，直接叠在 3D 场景上（拍照模式取景器）。</summary>
        public static InkPageFrame ImmersiveTransparent => new InkPageFrame(InkPageKind.Immersive, ownBack: true, selfLitStage: true, noBackdrop: true);

        /// <summary>模态弹层。</summary>
        public static InkPageFrame Modal => new InkPageFrame(InkPageKind.Modal);

        /// <summary>是否不铺墨底/暗角（HUD 与模态弹层都不铺）。</summary>
        public bool IsBare => Kind == InkPageKind.Hud || Kind == InkPageKind.Modal;

        public override string ToString() => $"{Kind}{(OwnBack ? "+OwnBack" : "")}{(SelfLitStage ? "+SelfLit" : "")}{(NoBackdrop ? "+NoBackdrop" : "")}";
    }
}
