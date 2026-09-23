using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Ink;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Prefabs
{
    /// <summary>
    /// 圆角面板 Prefab 参数档案（对应 <see cref="RoundedPanel"/>，背景色走主题令牌名）。
    /// </summary>
    [ExecuteInEditMode]
    public class RoundedPanelProfile : UIComponentProfile
    {
        /// <summary>圆角半径</summary>
        [EditorOrder(100), EditorDisplay("面板")]
        public float CornerRadius = 8f;

        /// <summary>背景颜色令牌名（InkWashTheme 字段名，如 Panel/BgMist/Void）</summary>
        [EditorOrder(101), EditorDisplay("面板"), Tooltip("背景颜色令牌名（InkWashTheme 字段名，如 Panel/BgMist/Void），留空保持默认")]
        public string BackgroundToken = "Panel";

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
            return new RoundedPanel();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is RoundedPanel panel)
                panel.CornerRadius = CornerRadius;

            if (UIPrefabUtils.TryResolveColorToken(BackgroundToken, out var color))
                control.BackgroundColor = color;

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// 圆角进度条 Prefab 参数档案（对应 <see cref="RoundedProgressBar"/>）。
    /// </summary>
    [ExecuteInEditMode]
    public class ProgressBarProfile : UIComponentProfile
    {
        /// <summary>进度值 0~1</summary>
        [EditorOrder(100), EditorDisplay("进度条"), Tooltip("进度值 0~1")]
        public float Value = 0.5f;

        /// <summary>进度条填充色令牌名</summary>
        [EditorOrder(101), EditorDisplay("进度条"), Tooltip("填充色令牌名（如 GoldPrimary/JadePrimary），留空保持默认")]
        public string BarColorToken = "GoldPrimary";

        /// <summary>背景色令牌名</summary>
        [EditorOrder(102), EditorDisplay("进度条"), Tooltip("背景色令牌名（如 Void/BaseTertiary），留空保持默认")]
        public string BackgroundToken = string.Empty;

        /// <summary>圆角半径</summary>
        [EditorOrder(103), EditorDisplay("进度条")]
        public float CornerRadius = 10f;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public float Width = 200f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(105), EditorDisplay("布局")]
        public float Height = 16f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(106), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new RoundedProgressBar();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is ProgressBar bar)
            {
                bar.Value = Mathf.Clamp(Value, 0f, 1f);
                if (UIPrefabUtils.TryResolveColorToken(BarColorToken, out var barColor))
                    bar.BarColor = barColor;
            }
            if (control is RoundedProgressBar rounded)
                rounded.CornerRadius = CornerRadius;

            if (UIPrefabUtils.TryResolveColorToken(BackgroundToken, out var bg))
                control.BackgroundColor = bg;

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }

    /// <summary>
    /// Toast 通知 Prefab 参数档案（对应 <see cref="ToastNotification"/>）。
    /// </summary>
    [ExecuteInEditMode]
    public class ToastProfile : UIComponentProfile
    {
        /// <summary>是否在 OnStart 时自动弹出一条（调试/示例用）</summary>
        [EditorOrder(100), EditorDisplay("Toast"), Tooltip("是否在 OnStart 时自动弹出配置内容（调试用）")]
        public bool AutoShow = false;

        /// <summary>默认消息文本</summary>
        [EditorOrder(101), EditorDisplay("Toast")]
        public string Message = "操作成功";

        /// <summary>通知类型</summary>
        [EditorOrder(102), EditorDisplay("Toast")]
        public ToastType Type = ToastType.Info;

        /// <summary>显示时长（秒）</summary>
        [EditorOrder(103), EditorDisplay("Toast")]
        public float Duration = 3f;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new ToastNotification();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            // ToastNotification 内容由 Show() 驱动，参数在触发时生效
        }

        /// <inheritdoc />
        public override void OnStart()
        {
            base.OnStart();
            if (AutoShow && BoundControl is ToastNotification toast)
                toast.Show(Message, Type, Duration);
        }

        /// <summary>以当前参数弹出一条通知（供脚本 / 事件绑定调用）。</summary>
        public void Show()
        {
            if (BoundControl is ToastNotification toast)
                toast.Show(Message, Type, Duration);
        }
    }

    /// <summary>
    /// 确认对话框 Prefab 参数档案（对应 <see cref="ConfirmDialog"/>，事件 Confirmed / Cancelled）。
    /// </summary>
    [ExecuteInEditMode]
    public class ConfirmDialogProfile : UIComponentProfile
    {
        /// <summary>标题</summary>
        [EditorOrder(100), EditorDisplay("对话框")]
        public string Title = "确认";

        /// <summary>正文</summary>
        [EditorOrder(101), EditorDisplay("对话框")]
        public string Message = "确定执行该操作吗？";

        /// <summary>确认按钮文字</summary>
        [EditorOrder(102), EditorDisplay("对话框")]
        public string ConfirmText = "确定";

        /// <summary>取消按钮文字</summary>
        [EditorOrder(103), EditorDisplay("对话框")]
        public string CancelText = "取消";

        /// <summary>编辑器/启动时是否可见（运行后 Show/Hide 由外部驱动）</summary>
        [EditorOrder(104), EditorDisplay("对话框")]
        public bool VisibleOnInit = true;

        private string _lastShownSignature;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new ConfirmDialog();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is ConfirmDialog dialog)
            {
                dialog.SetConfirmText(ConfirmText);
                dialog.SetCancelText(CancelText);

                // Show 会重置对话框内容/动画，仅在内容变化时调用，避免编辑器轮询反复重放
                var signature = Title + "\n" + Message;
                if (signature != _lastShownSignature)
                {
                    _lastShownSignature = signature;
                    dialog.Show(Title, Message);
                }
                dialog.Visible = VisibleOnInit;
            }
        }
    }

    /// <summary>
    /// 加载指示器 Prefab 参数档案（对应 <see cref="LoadingIndicator"/>）。
    /// </summary>
    [ExecuteInEditMode]
    public class LoadingIndicatorProfile : UIComponentProfile
    {
        /// <summary>提示文字</summary>
        [EditorOrder(100), EditorDisplay("加载")]
        public string Message = "加载中...";

        /// <summary>OnStart 时自动开始转圈</summary>
        [EditorOrder(101), EditorDisplay("加载")]
        public bool AutoStart = true;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(102), EditorDisplay("布局")]
        public float Width = -1f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public float Height = -1f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public string AnchorPreset = "Center";

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new LoadingIndicator();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }

        /// <inheritdoc />
        public override void OnStart()
        {
            base.OnStart();
            if (AutoStart && BoundControl is LoadingIndicator indicator)
            {
                indicator.Show(Message);
                indicator.Start();
            }
        }

        /// <summary>停止并隐藏。</summary>
        public void Stop()
        {
            if (BoundControl is LoadingIndicator indicator)
                indicator.Hide();
        }
    }

    /// <summary>
    /// 带验证输入框 Prefab 参数档案（对应 <see cref="ValidatedTextBox"/>，事件 TextChanged）。
    /// </summary>
    [ExecuteInEditMode]
    public class TextBoxProfile : UIComponentProfile
    {
        /// <summary>初始文本</summary>
        [EditorOrder(100), EditorDisplay("输入框")]
        public string Text = string.Empty;

        /// <summary>占位提示文字</summary>
        [EditorOrder(101), EditorDisplay("输入框")]
        public string Watermark = "请输入";

        /// <summary>是否密码模式</summary>
        [EditorOrder(102), EditorDisplay("输入框")]
        public bool IsPassword = false;

        /// <summary>宽度覆写，&lt;=0 保持原宽</summary>
        [EditorOrder(103), EditorDisplay("布局")]
        public float Width = 220f;

        /// <summary>高度覆写，&lt;=0 保持原高</summary>
        [EditorOrder(104), EditorDisplay("布局")]
        public float Height = -1f;

        /// <summary>锚点预设名，空表示不修改</summary>
        [EditorOrder(105), EditorDisplay("布局")]
        public string AnchorPreset = string.Empty;

        /// <inheritdoc />
        protected override Control CreateDefaultControl()
        {
            return new ValidatedTextBox();
        }

        /// <inheritdoc />
        protected override void ApplyParameters(Control control)
        {
            if (control is ValidatedTextBox box)
            {
                box.WatermarkText = Watermark;
                box.IsPassword = IsPassword;
                if (box.Text != Text)
                    box.Text = Text;
            }

            UIPrefabUtils.TryApplyAnchor(control, AnchorPreset);
            UIPrefabUtils.ApplySize(control, Width, Height);
        }
    }
}
