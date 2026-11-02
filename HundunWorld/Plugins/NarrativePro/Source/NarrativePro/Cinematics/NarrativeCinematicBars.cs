using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 电影黑边（letterbox）覆盖层。对应 UE5 <c>NarrativeSequencer::HideHUD + CinematicBars</c>。
    /// <para>
    /// 这是缺陷⑥里"可移植的部分"——UE5 用 Level Sequence 的 CinematicBars 相机节点做黑边，
    /// Flax 无 Level Sequence，故用 Flax GUI 覆盖层实现：上下两条不透明黑条铺满屏幕宽，高度按比例。
    /// 已核对 <c>FlaxEngine.CSharp.xml</c>：<c>Camera</c> 无任何 letterbox/bars API（Camera 成员仅
    /// FieldOfView/NearPlane/FarPlane/Viewport/ProjectPoint 等），屏幕空间叠加只能走 <see cref="UICanvas"/> + <see cref="Image"/>。
    /// </para>
    /// <para>
    /// 遵守本工程 Flax GUI 约束：只在主线程构建（对话 Tick 即主线程）；用 TopLeft 锚 + 显式尺寸并逐帧重排，
    /// 规避非 TopLeft 锚在构造期定位错乱；无 Control.BringToFront，用画布 <see cref="UICanvas.Order"/> 置顶；
    /// Flax 无 Actor.IsValid——用 <c>== null</c> 判定失效（Actor/Control 销毁后托管包装会返回 null）。
    /// </para>
    public class NarrativeCinematicBars : Script
    {
        private static NarrativeCinematicBars _instance;

        /// <summary>黑边高度占屏幕高度的比例（0~0.45）。</summary>
        public float BarHeightPct { get; set; } = 0.12f;

        /// <summary>
        /// 遮幅设置（数据驱动形态）。非 null 时黑边高度由
        /// <see cref="PlateCropSettings.ComputeBarHeightPct"/> 按银幕宽高比/裁切量换算，
        /// 覆盖 <see cref="BarHeightPct"/>。对应 UE5 对话镜头上的 <c>FPlateCropSettings CropSettings</c>。
        /// </summary>
        private PlateCropSettings _cropSettings;

        /// <summary>期望是否可见（由外部 Show/Hide 驱动）。</summary>
        public bool bWantVisible { get; set; }

        private UICanvas _canvas;
        private Image _topBar;
        private Image _bottomBar;

        /// <summary>显示黑边（首次调用自动在主线程创建画布与两条黑条）。对应 WantsCinematicBars 的消费方。</summary>
        public static void Show(float barHeightPct = 0.12f)
        {
            var bars = Ensure();
            if (bars == null) return;
            bars._cropSettings = null; // 显式比例入口优先于遮幅设置
            bars.BarHeightPct = Mathf.Clamp(barHeightPct, 0f, 0.45f);
            bars.bWantVisible = true;
            bars.Apply();
        }

        /// <summary>
        /// 按遮幅设置显示黑边（数据驱动入口）。对应 UE5 把镜头的 <c>CropSettings</c> 赋给
        /// CineCameraComponent（NarrativeDialogueSequence.cpp:209）——Flax 相机无遮幅 API，
        /// 换算成黑条高度占比后复用同一套覆盖层。
        /// </summary>
        public static void Show(PlateCropSettings cropSettings)
        {
            var bars = Ensure();
            if (bars == null) return;

            bars._cropSettings = cropSettings;
            if (cropSettings != null)
            {
                // 首帧即按当前画布尺寸换算一次；尺寸变化时 Relayout 会重算。
                var gui = bars._canvas != null ? bars._canvas.GUI : null;
                if (gui != null && gui.Size.Y > 0f)
                {
                    bars.BarHeightPct = cropSettings.ComputeBarHeightPct(gui.Size.X / gui.Size.Y);
                }
            }
            bars.bWantVisible = true;
            bars.Apply();
        }

        /// <summary>隐藏黑边（保留实例与黑条以便复用）。</summary>
        public static void Hide()
        {
            if (_instance == null) return;
            _instance.bWantVisible = false;
            _instance._cropSettings = null;
            _instance.Apply();
        }

        /// <summary>当前是否正在显示黑边。</summary>
        public static bool IsShowing => _instance != null && _instance.bWantVisible;

        /// <summary>确保实例存在：优先复用场景里已有实例，否则新建一个屏幕空间 UICanvas 宿主。</summary>
        private static NarrativeCinematicBars Ensure()
        {
            if (_instance != null && _instance.Actor != null) return _instance;
            _instance = null;

            try
            {
                var existing = Level.FindScript<NarrativeCinematicBars>();
                if (existing != null)
                {
                    _instance = existing;
                    return _instance;
                }

                var canvas = new UICanvas { Name = "NarrativeCinematicBarsCanvas" };
                // 玩法期覆盖层：显式生成进玩法场景，随场景卸载清理，不落 RootScene
                if (NarrativePro.CommonUI.NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(canvas, "NarrativeCinematicBarsCanvas"))
                {
                    // Flax: SpawnActor 返回 true 表示失败（例如非主线程）
                    FlaxEngine.Object.Destroy(canvas);
                    return null;
                }
                canvas.RenderMode = CanvasRenderMode.ScreenSpace;
                canvas.Order = 10000;         // 覆盖玩法 HUD
                canvas.ReceivesEvents = false; // 黑边不吞输入（跳过键由对话镜头读取）

                _instance = canvas.AddScript<NarrativeCinematicBars>();
                return _instance;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeCinematicBars：创建覆盖层失败：" + ex.Message);
                _instance = null;
                return null;
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;
            BuildBars();
        }

        public override void OnDisable()
        {
            _topBar = null;
            _bottomBar = null;
            _canvas = null;
            if (ReferenceEquals(_instance, this)) _instance = null;
            base.OnDisable();
        }

        private void BuildBars()
        {
            // 本脚本挂在生成的 UICanvas Actor 上，Actor 即画布
            _canvas = Actor as UICanvas;
            var parent = _canvas != null ? _canvas.GUI : null;
            if (parent == null) return;

            var black = new SolidColorBrush(new Color(0f, 0f, 0f, 1f));

            _topBar = new Image { Brush = black };
            _bottomBar = new Image { Brush = black };

            parent.AddChild(_topBar);
            parent.AddChild(_bottomBar);

            Apply();
        }

        /// <summary>按当前屏幕尺寸重排黑边并切换可见性。</summary>
        private void Apply()
        {
            if (_topBar == null || _bottomBar == null) return;
            _topBar.Visible = bWantVisible;
            _bottomBar.Visible = bWantVisible;
            Relayout();
        }

        private void Relayout()
        {
            if (_topBar == null || _bottomBar == null || _canvas == null || _canvas.GUI == null) return;

            Float2 size = _canvas.GUI.Size;
            if (size.X <= 0f || size.Y <= 0f) return;

            // 记录本次布局依据的父尺寸：OnUpdate 只在尺寸变化时重排（缺陷③）。
            _lastLayoutSize = size;

            // 遮幅设置存在时按银幕比/裁切量重算占比（窗口缩放后依然数据驱动）；否则用显式比例。
            float pct = _cropSettings != null
                ? _cropSettings.ComputeBarHeightPct(size.X / size.Y)
                : Mathf.Clamp(BarHeightPct, 0f, 0.45f);
            float barH = size.Y * pct;

            // TopLeft 锚 + 显式位置/尺寸（规避非 TopLeft 锚构造期错位）
            _topBar.AnchorMin = Float2.Zero;
            _topBar.AnchorMax = Float2.Zero;
            _topBar.Location = Float2.Zero;
            _topBar.Size = new Float2(size.X, barH);

            _bottomBar.AnchorMin = Float2.Zero;
            _bottomBar.AnchorMax = Float2.Zero;
            _bottomBar.Location = new Float2(0f, size.Y - barH);
            _bottomBar.Size = new Float2(size.X, barH);
        }

        /// <summary>上次重排所依据的父画布尺寸（哨兵值 -1 保证首次显示必然重排一次）。</summary>
        private Float2 _lastLayoutSize = new Float2(-1f, -1f);

        public override void OnUpdate()
        {
            // 缺陷③：不再「显示期间逐帧 Relayout」。只在显示中且父画布尺寸发生变化
            // （窗口缩放 / 首帧尺寸尚未生效）时重排；Show/Hide 自身已调用 Apply→Relayout。
            if (!bWantVisible) return;

            var gui = _canvas != null ? _canvas.GUI : null;
            if (gui == null) return;

            if (gui.Size != _lastLayoutSize)
            {
                Relayout();
            }
        }
    }
}
