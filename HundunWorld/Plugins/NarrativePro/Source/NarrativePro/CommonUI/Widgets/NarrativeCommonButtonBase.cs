using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// CommonButton 的视觉状态机。对应 UE5 <c>UCommonButtonBase::ECommonButtonStyle</c> 的状态相位
    /// （Normal / Hovered / Pressed / Selected / SelectedHovered / Disabled）。
    /// </summary>
    public enum NarrativeCommonButtonState
    {
        Normal = 0,
        Hovered = 1,
        Pressed = 2,
        Selected = 3,
        SelectedHovered = 4,
        Disabled = 5,
    }

    /// <summary>
    /// Narrative CommonUI 按钮。对应 UE5 <c>UNarrativeCommonButtonBase</c>（继承 UCommonButtonBase）。
    /// <para>
    /// Flax 没有 CommonUI 的样式表/状态机，这里以真正的 <see cref="Button"/> 控件实现同一行为：
    /// <list type="bullet">
    /// <item><description>状态：在 <see cref="Update"/> 里轮询 Flax 的真实输入相
    /// （<see cref="Control.IsMouseOver"/> / <see cref="Button.IsPressed"/> / <see cref="Control.Enabled"/>）
    /// 加上 <see cref="bIsSelected"/>，得到与 UE5 一致的六相状态，并把主题色落到
    /// <see cref="Control.BackgroundColor"/> 与 Button 的高亮/选中背景与边框上；</description></item>
    /// <item><description>文本绑定：<see cref="ButtonText"/> 直接驱动 Button 自身文本；
    /// 绑定了 <see cref="ButtonTextBlock"/>（UE5 的 BindWidgetOptional）时把文本/对齐/样式镜像到该
    /// <see cref="NarrativeCommonTextBlock"/> 并让位（自身 Text 清空，避免双份绘制）；</description></item>
    /// <item><description>样式：颜色一律从 <see cref="NarrativeUIDeveloperSettings"/> 取
    /// （对应 UE5 从 CommonButtonStyle 取画刷），<see cref="NativeOnCurrentTextStyleChanged"/>
    /// 复用 <see cref="NarrativeCommonTextBlock.SetStyle"/> 的字号/粗体映射；</description></item>
    /// <item><description>选中：点击切换选中态（对应 UE5 的 ButtonStyle=ToggleButton），
    /// <see cref="ForceSetIsSelected"/> 保留 UE5 的"绕过 SetSelectedInternal 保护"语义，
    /// <paramref name="bAllowSound"/> 控制点击音（<see cref="ClickSound"/>），
    /// <paramref name="bBroadcast"/> 控制 <see cref="SelectionChanged"/> 是否广播。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class NarrativeCommonButtonBase : Button
    {
        /// <summary>可选的按钮文本块。对应 UE5 ButtonTextBlock（BindWidgetOptional）。</summary>
        public NarrativeCommonTextBlock ButtonTextBlock;

        /// <summary>按钮上显示的文本。对应 UE5 ButtonText（FText）。</summary>
        public string ButtonText = "Button Text";

        /// <summary>文本对齐方式。对应 UE5 TextJustification（ETextJustify::Type）。</summary>
        public ETextJustify TextJustification = ETextJustify.Left;

        /// <summary>当前是否被选中。对应 UE5 Internal::IsSelected。</summary>
        public bool bIsSelected = false;

        /// <summary>当前文本样式名。对应 UE5 TextStyleClass（FName）。</summary>
        public string TextStyleClass = "Button";

        /// <summary>点击是否翻转选中态。对应 UE5 的 ToggleButton 行为。</summary>
        public bool bToggleSelectionOnClick = true;

        /// <summary>是否跟随全局主题色（关闭后保留调用方自己设置的颜色）。</summary>
        public bool bFollowTheme = true;

        /// <summary>点击/选中音（对应 UE5 CommonButtonStyle 的 ClickSound；为空则静音）。</summary>
        public AudioClip ClickSound;

        /// <summary>点击音量。</summary>
        public float ClickSoundVolume = 0.6f;

        /// <summary>当前状态相位（只读，供样式与调试用）。</summary>
        public NarrativeCommonButtonState State { get; private set; } = NarrativeCommonButtonState.Normal;

        /// <summary>选中状态变更事件（对应 UE5 FOnButtonSelectionEvent）。</summary>
        public event Action<NarrativeCommonButtonBase, bool> SelectionChanged;

        /// <summary>被点击事件（在 Flax 的 Clicked 之外补一份带自身的语义，便于容器统一接线）。</summary>
        public event Action<NarrativeCommonButtonBase> ButtonClickedByUser;

        // 主题色缓存（ApplyTheme 写入，ApplyState 按相位取用）
        private Color _normalColor = new Color(0.02f, 0.02f, 0.02f, 1f);
        private Color _hoveredColor = new Color(0.06f, 0.10f, 0.16f, 1f);
        private Color _pressedColor = new Color(0.10f, 0.20f, 0.30f, 1f);
        private Color _selectedColor = new Color(0.0f, 0.67f, 0.17f, 1f);
        private Color _textColor = Color.White;
        private Color _textColorSelected = Color.Black;
        private NarrativeCommonButtonState _lastApplied = (NarrativeCommonButtonState)(-1);

        /// <summary>构造函数。对应 UE5 UNarrativeCommonButtonBase 构造函数的默认值 + 模板尺寸。</summary>
        public NarrativeCommonButtonBase()
        {
            TextJustification = ETextJustify.Left;
            ButtonText = "Button Text";

            // 构造期只用 TopLeft 锚 + 显式尺寸（非 TopLeft 锚会在父尺寸为 0 时算错位置）
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(180f, 34f);

            HasBorder = true;
            BorderThickness = 1f;

            Clicked += HandleClicked;

            NativePreConstruct();
            ApplyTheme();
            ApplyState(true);
        }

        private void HandleClicked()
        {
            ButtonClickedByUser?.Invoke(this);

            if (bToggleSelectionOnClick)
            {
                SetSelectedInternal(!bIsSelected, true, true);
                return;
            }

            if (ClickSound != null) PlayClickSound();
        }

        /// <summary>
        /// 强制设置选中状态。对应 UE5 ForceSetIsSelected。
        /// UE5 中 SetSelectedInternal 是 BP Protected 的，需要包装一下才能在蓝图中调用。
        /// </summary>
        /// <param name="bInSelected">是否选中。</param>
        /// <param name="bAllowSound">是否允许播放声音。</param>
        /// <param name="bBroadcast">是否广播事件。</param>
        public virtual void ForceSetIsSelected(bool bInSelected, bool bAllowSound, bool bBroadcast)
        {
            SetSelectedInternal(bInSelected, bAllowSound, bBroadcast);
        }

        /// <summary>读取当前选中状态。对应 UE5 IsSelected。</summary>
        public bool IsSelected() => bIsSelected;

        /// <summary>
        /// 设置按钮文本。对应 UE5 SetButtonText。
        /// </summary>
        public virtual void SetButtonText(string inText)
        {
            ButtonText = inText ?? string.Empty;

            if (ButtonTextBlock != null)
            {
                // 绑定了外部文本块时由它负责绘制，Button 自身文本清空避免双份
                Text = string.Empty;
                ButtonTextBlock.SetText(ButtonText);
            }
            else
            {
                Text = ButtonText;
            }
        }

        /// <summary>取当前按钮文本。对应 UE5 GetButtonText。</summary>
        public string GetButtonText() => ButtonText;

        /// <summary>设置文本对齐。对应 UE5 SetJustification。</summary>
        public void SetJustification(ETextJustify justification)
        {
            TextJustification = justification;
            HorizontalAlignment = justification switch
            {
                ETextJustify.Center => TextAlignment.Center,
                ETextJustify.Right => TextAlignment.Far,
                _ => TextAlignment.Near
            };

            ButtonTextBlock?.SetJustification(justification);
        }

        /// <summary>
        /// 预构造回调。对应 UE5 NativePreConstruct：
        /// 调 Super 后设置按钮文本与文本对齐。
        /// </summary>
        public virtual void NativePreConstruct()
        {
            SetButtonText(ButtonText);
            SetJustification(TextJustification);
        }

        /// <summary>
        /// 当前文本样式变更回调。对应 UE5 NativeOnCurrentTextStyleChanged：
        /// Super 之后把样式应用到 ButtonTextBlock。
        /// </summary>
        public virtual void NativeOnCurrentTextStyleChanged()
        {
            TextStyleClass = GetCurrentTextStyleClass();
            ButtonTextBlock?.SetStyle(TextStyleClass);
        }

        /// <summary>
        /// 获取当前文本样式。对应 UE5 GetCurrentTextStyleClass。
        /// Flax 无 CommonUI 样式表，样式名由本控件持有（默认 "Button"，与
        /// <see cref="NarrativeCommonTextBlock.ApplyStyle"/> 的字号/粗体表一致）。
        /// </summary>
        protected virtual string GetCurrentTextStyleClass()
        {
            return string.IsNullOrEmpty(TextStyleClass) ? "Button" : TextStyleClass;
        }

        /// <summary>
        /// 内部选中状态设置。对应 UE5 SetSelectedInternal。
        /// </summary>
        protected virtual void SetSelectedInternal(bool bInSelected, bool bAllowSound, bool bBroadcast)
        {
            bool changed = bIsSelected != bInSelected;
            bIsSelected = bInSelected;

            if (changed)
            {
                ApplyState(true);
            }

            if (bAllowSound)
            {
                PlayClickSound();
            }

            if (bBroadcast && changed)
            {
                SelectionChanged?.Invoke(this, bInSelected);
            }
        }

        /// <summary>从 <see cref="NarrativeUIDeveloperSettings"/> 取主题色。对应 UE5 CommonButtonStyle 取画刷。</summary>
        public void ApplyTheme()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            if (!bFollowTheme)
            {
                ApplyState(true);
                return;
            }

            // 常态用反色底、悬停/按下用反色强调、选中用主色（对应 UE5 样式表的四个画刷槽）
            _normalColor = settings.UIInvertColor;
            _hoveredColor = settings.UIInvertAccentColor;
            _pressedColor = Blend(settings.UIInvertAccentColor, settings.UIPrimaryColor, 0.55f);
            _selectedColor = settings.UIPrimaryColor;

            // 主色偏亮时选中文字要压暗，偏暗时白字更清晰（与文本控件的取舍一致）
            _textColor = settings.bUseLightTextOnPrimary ? Color.White : Color.White;
            _textColorSelected = settings.bUseLightTextOnPrimary
                ? settings.UIInvertColor
                : Color.White;

            BorderColor = settings.UIInvertAccentColor;
            ApplyState(true);
        }

        /// <summary>按当前相位落地颜色/文本色。UE5 由样式表切换画刷，这里显式赋值。</summary>
        protected virtual void ApplyState(bool bForce)
        {
            NarrativeCommonButtonState state = ComputeState();
            if (!bForce && state == _lastApplied) return;
            _lastApplied = state;
            State = state;

            Color background = state switch
            {
                NarrativeCommonButtonState.Disabled => WithAlpha(_normalColor, 0.35f),
                NarrativeCommonButtonState.Pressed => _pressedColor,
                NarrativeCommonButtonState.Selected => _selectedColor,
                NarrativeCommonButtonState.SelectedHovered => Blend(_selectedColor, _hoveredColor, 0.35f),
                NarrativeCommonButtonState.Hovered => Blend(_normalColor, _hoveredColor, 0.7f),
                _ => _normalColor,
            };

            BackgroundColor = background;
            BackgroundColorHighlighted = state == NarrativeCommonButtonState.Selected ||
                                         state == NarrativeCommonButtonState.SelectedHovered
                ? _selectedColor
                : Blend(_normalColor, _hoveredColor, 0.6f);
            BackgroundColorSelected = _selectedColor;

            bool bSelectedLook = state == NarrativeCommonButtonState.Selected ||
                                 state == NarrativeCommonButtonState.SelectedHovered;

            BorderColorSelected = bSelectedLook ? _textColorSelected : BorderColor;
            TextColor = state == NarrativeCommonButtonState.Disabled
                ? WithAlpha(_textColor, 0.4f)
                : (bSelectedLook ? _textColorSelected : _textColor);

            // 镜像到绑定的文本块（UE5 的样式表会同时作用两者）
            if (ButtonTextBlock != null)
            {
                ButtonTextBlock.TextColor = TextColor;
            }
        }

        /// <summary>加入父控件后重新取主题（对应 UE5 NativeConstruct 时机）。</summary>
        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            ApplyTheme();
        }

        /// <summary>尺寸变化后保持文本块铺满（文本块由调用方父化到本控件内时生效）。</summary>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();

            if (ButtonTextBlock != null && ButtonTextBlock.Parent == this)
            {
                ButtonTextBlock.Location = Float2.Zero;
                ButtonTextBlock.Size = Size;
            }
        }

        /// <summary>轮询真实输入相推导状态位（Flax 无 Slate 状态机，这里自己算）。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            ApplyState(false);
        }

        private NarrativeCommonButtonState ComputeState()
        {
            if (!Enabled || !EnabledInHierarchy) return NarrativeCommonButtonState.Disabled;
            if (IsPressed) return NarrativeCommonButtonState.Pressed;
            if (bIsSelected) return IsMouseOver ? NarrativeCommonButtonState.SelectedHovered : NarrativeCommonButtonState.Selected;
            return IsMouseOver ? NarrativeCommonButtonState.Hovered : NarrativeCommonButtonState.Normal;
        }

        /// <summary>播放点击音。对应 UE5 CommonButtonStyle 的点击声音槽（一次性音源，按片段长度回收）。</summary>
        protected virtual void PlayClickSound()
        {
            if (ClickSound == null) return;

            try
            {
                var source = new AudioSource
                {
                    Name = "NarrativeButtonClick",
                    Clip = ClickSound,
                    Volume = ClickSoundVolume,
                    IsLooping = false,
                    PlayOnStart = true,
                    AllowSpatialization = false,
                };

                // 点击音显式生成进玩法场景；Flax 约定 SpawnActor 返回 true 才是失败
                if (NarrativeRuntimeBootstrap.TrySpawnToGameplayScene(source, source.Name)) return;

                source.Play();
                FlaxEngine.Object.Destroy(source, System.Math.Max(ClickSound.Length, 0.1f));
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeCommonButtonBase 播放点击音失败：" + ex.Message);
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return new Color(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.A + (b.A - a.A) * t);
        }

        private static Color WithAlpha(Color c, float alpha)
        {
            c.A = alpha;
            return c;
        }
    }
}
