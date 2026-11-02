using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// 字幕处理委托。对应 UE5 FNarrativeHandleSubtitle（动态多播委托，单参数 FText）。
    /// </summary>
    /// <param name="subtitleText">字幕文本。</param>
    public delegate void FNarrativeHandleSubtitle(string subtitleText);

    /// <summary>
    /// Narrative CommonUI 子系统。对应 UE5 UNarrativeCommonUISubsystem（继承 UGameInstanceSubsystem）。
    /// 提供对 Narrative HUD 的高效访问，并接管字幕分发。
    /// <para>
    /// UE5 中 Initialize 会挂接引擎的 FSubtitleManager；Flax 没有 SubtitleManager，
    /// 所以这里把 <see cref="OnSetSubtitle"/> 作为对等的入口（由对话/演出系统调用），
    /// 并由本子系统自己维护一个真正渲染出来的字幕控件（对应 UE5 蓝图侧订阅 OnHandleSubtitle 后显示的文本块）。
    /// </para>
    /// </summary>
    public class NarrativeCommonUISubsystem : Script
    {
        private static NarrativeCommonUISubsystem _instance;

        /// <summary>当前实例。Flax 中以单例模式等价 GameInstanceSubsystem 的全局访问。</summary>
        public static NarrativeCommonUISubsystem Instance => _instance;

        /// <summary>
        /// 缓存的通用 HUD 实例。对应 UE5 CommonHUD（UPROPERTY BlueprintReadOnly）。
        /// </summary>
        public NarrativeCommonHUD CommonHUD;

        /// <summary>
        /// 字幕处理事件。对应 UE5 OnHandleSubtitle（BlueprintAssignable）。
        /// 订阅后可自行接管字幕显示；若不订阅，子系统会用内置的 <see cref="SubtitleText"/> 兜底渲染。
        /// </summary>
        public event FNarrativeHandleSubtitle OnHandleSubtitle;

        /// <summary>字幕控件的锚定位置（屏幕底部居中的偏移比例，0~1）。</summary>
        public Vector2 SubtitleAlignmentRatio = new Vector2(0.5f, 0.88f);

        /// <summary>字幕控件宽度（像素）。</summary>
        public float SubtitleWidth = 900f;

        /// <summary>字幕文本字号。</summary>
        public int SubtitleFontSize = 20;

        /// <summary>无外部订阅者时是否由子系统自行渲染字幕。</summary>
        public bool bRenderBuiltInSubtitle = true;

        /// <summary>内置字幕控件（首次显示时惰性创建）。</summary>
        private NarrativeCommonTextBlock _subtitleText;

        /// <summary>最近一次收到的字幕文本。</summary>
        public string CurrentSubtitleText { get; private set; } = string.Empty;

        /// <summary>子系统初始化。对应 UE5 Initialize。</summary>
        public override void OnEnable()
        {
            base.OnEnable();
            _instance = this;

            // UE5: FSubtitleManager::GetSubtitleManager()->OnSetSubtitleText().AddUObject(this, &OnSetSubtitle);
            // Flax 无 SubtitleManager，默认由本子系统接管字幕通路（外部桥接器直接调 OnSetSubtitle）。
            NarrativeCommonUI.StartupModule();
            NarrativeLog.Log("NarrativeCommonUISubsystem 已初始化。");
        }

        /// <summary>子系统反初始化。对应 UE5 Deinitialize。</summary>
        public override void OnDisable()
        {
            DestroyBuiltInSubtitle();

            CommonHUD = null;
            OnHandleSubtitle = null;

            if (_instance == this) _instance = null;
            base.OnDisable();
        }

        /// <summary>
        /// 字幕设置回调。对应 UE5 OnSetSubtitle。
        /// UE5 中由 SubtitleManager 调用；Flax 中由对话/演出系统调用。
        /// </summary>
        /// <param name="subtitleText">字幕文本。</param>
        public void OnSetSubtitle(string subtitleText)
        {
            CurrentSubtitleText = subtitleText ?? string.Empty;

            OnHandleSubtitle?.Invoke(CurrentSubtitleText);

            if (bRenderBuiltInSubtitle)
            {
                ShowBuiltInSubtitle(CurrentSubtitleText);
            }
        }

        /// <summary>
        /// 打开一个菜单。对应 UE5 中被注释掉的 OpenMenu —— 端口在 NarrativeGameplayHUD 落地后接通。
        /// </summary>
        /// <param name="menuWidget">要打开的菜单控件实例</param>
        /// <returns>是否成功打开</returns>
        public bool OpenMenu(NarrativeActivatableWidget menuWidget)
        {
            if (CommonHUD == null)
            {
                NarrativeLog.LogWarning("OpenMenu 失败：CommonHUD 尚未就绪。");
                return false;
            }

            return CommonHUD.PushWidget(menuWidget) != null;
        }

        /// <summary>关闭当前栈顶菜单。对应 UE5 的返回行为。</summary>
        public bool CloseTopMenu()
        {
            return CommonHUD != null && CommonHUD.PopTopWidget();
        }

        // =================================================================
        // 内置字幕渲染
        // =================================================================

        private void ShowBuiltInSubtitle(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_subtitleText != null) _subtitleText.Visible = false;
                return;
            }

            if (!EnsureBuiltInSubtitle()) return;

            _subtitleText.SetText(text);

            // 依据文本长度重排尺寸与位置（TopLeft 锚 + 显式重排）
            var root = CommonHUD != null ? CommonHUD.RootControl : null;
            if (root != null)
            {
                float width = Math.Min(SubtitleWidth, root.Width);
                float height = _subtitleText.Height;
                _subtitleText.Size = new Float2(width, height);
                _subtitleText.Location = new Float2(
                    root.Width * SubtitleAlignmentRatio.X - width * 0.5f,
                    root.Height * SubtitleAlignmentRatio.Y - height * 0.5f);
            }

            _subtitleText.BringToFront();
            _subtitleText.Visible = true;
        }

        private bool EnsureBuiltInSubtitle()
        {
            if (_subtitleText != null && _subtitleText.Parent != null) return true;

            if (CommonHUD == null)
            {
                CommonHUD = NarrativeCommonHUD.Instance;
            }
            if (CommonHUD == null) return false;

            CommonHUD.Initialize(null);
            var root = CommonHUD.RootControl;
            if (root == null) return false;

            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();

            _subtitleText = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Font = new FontReference(null, SubtitleFontSize),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Wrapping = TextWrapping.WrapWords,
                AutoHeight = true,
                BackgroundColor = settings.UIInvertColor,
                Visible = false
            };

            root.AddChild(_subtitleText);
            return true;
        }

        private void DestroyBuiltInSubtitle()
        {
            if (_subtitleText == null) return;

            var parent = _subtitleText.Parent;
            if (parent != null) parent.RemoveChild(_subtitleText);
            _subtitleText.Dispose();
            _subtitleText = null;
        }
    }
}
