using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// 输入绑定类型。对应 UE5 <c>EInputKeySelectorType</c>（键 / 鼠标 / 手柄）。
    /// </summary>
    public enum NarrativeInputBindingType : byte
    {
        /// <summary>未绑定。</summary>
        None = 0,
        /// <summary>键盘按键。</summary>
        Keyboard = 1,
        /// <summary>鼠标按键。</summary>
        Mouse = 2,
        /// <summary>手柄按钮。</summary>
        Gamepad = 3
    }

    /// <summary>
    /// 一条输入绑定。对应 UE5 的 <c>FInputKey</c>。
    /// </summary>
    [Serializable]
    public struct NarrativeInputBinding
    {
        /// <summary>绑定类型。</summary>
        public NarrativeInputBindingType Type;

        /// <summary>键盘键（Type==Keyboard 时有效）。</summary>
        public KeyboardKeys Key;

        /// <summary>鼠标键（Type==Mouse 时有效）。</summary>
        public MouseButton MouseKey;

        /// <summary>手柄按钮（Type==Gamepad 时有效）。</summary>
        public GamepadButton GamepadKey;

        /// <summary>构造一个键盘绑定。</summary>
        public static NarrativeInputBinding FromKey(KeyboardKeys key)
        {
            return new NarrativeInputBinding { Type = NarrativeInputBindingType.Keyboard, Key = key };
        }

        /// <summary>构造一个鼠标绑定。</summary>
        public static NarrativeInputBinding FromMouseButton(MouseButton button)
        {
            return new NarrativeInputBinding { Type = NarrativeInputBindingType.Mouse, MouseKey = button };
        }

        /// <summary>构造一个手柄绑定。</summary>
        public static NarrativeInputBinding FromGamepad(GamepadButton button)
        {
            return new NarrativeInputBinding { Type = NarrativeInputBindingType.Gamepad, GamepadKey = button };
        }

        /// <summary>是否为空绑定。</summary>
        public bool IsNone => Type == NarrativeInputBindingType.None;

        /// <summary>显示名（键帽文案）。</summary>
        public string ToDisplayString()
        {
            switch (Type)
            {
                case NarrativeInputBindingType.Keyboard: return NarrativeInputKeyFormatter.Describe(Key);
                case NarrativeInputBindingType.Mouse: return NarrativeInputKeyFormatter.Describe(MouseKey);
                case NarrativeInputBindingType.Gamepad: return NarrativeInputKeyFormatter.Describe(GamepadKey);
                default: return string.Empty;
            }
        }

        public override string ToString() => ToDisplayString();
    }

    /// <summary>
    /// 键位显示名格式化。UE5 由 Slate 的样式表提供图标，Flax 侧用可读文案表达同一信息。
    /// </summary>
    public static class NarrativeInputKeyFormatter
    {
        /// <summary>键盘键 → 文案。</summary>
        public static string Describe(KeyboardKeys key)
        {
            switch (key)
            {
                case KeyboardKeys.None: return string.Empty;
                case KeyboardKeys.Spacebar: return "Space";
                case KeyboardKeys.Return: return "Enter";
                case KeyboardKeys.Escape: return "Esc";
                case KeyboardKeys.Backspace: return "Backspace";
                case KeyboardKeys.Delete: return "Del";
                case KeyboardKeys.Tab: return "Tab";
                case KeyboardKeys.Shift: return "Shift";
                case KeyboardKeys.Control: return "Ctrl";
                case KeyboardKeys.Alt: return "Alt";
                case KeyboardKeys.LeftMenu: return "LAlt";
                case KeyboardKeys.RightMenu: return "RAlt";
                case KeyboardKeys.LeftBracket: return "[";
                case KeyboardKeys.RightBracket: return "]";
                case KeyboardKeys.Backslash: return "\\";
                case KeyboardKeys.Colon: return ":";
                case KeyboardKeys.Comma: return ",";
                case KeyboardKeys.Period: return ".";
                case KeyboardKeys.Slash: return "/";
                case KeyboardKeys.Minus: return "-";
                case KeyboardKeys.Plus: return "+";
                case KeyboardKeys.Quote: return "'";
                case KeyboardKeys.BackQuote: return "`";
                case KeyboardKeys.ArrowUp: return "Up";
                case KeyboardKeys.ArrowDown: return "Down";
                case KeyboardKeys.ArrowLeft: return "Left";
                case KeyboardKeys.ArrowRight: return "Right";
                case KeyboardKeys.PageUp: return "PgUp";
                case KeyboardKeys.PageDown: return "PgDn";
                case KeyboardKeys.Home: return "Home";
                case KeyboardKeys.End: return "End";
                case KeyboardKeys.Insert: return "Ins";
            }

            string name = key.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6)
            {
                return name.Substring("Alpha".Length);
            }
            return name;
        }

        /// <summary>鼠标键 → 文案。</summary>
        public static string Describe(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left: return "LMB";
                case MouseButton.Right: return "RMB";
                case MouseButton.Middle: return "MMB";
                default: return button.ToString();
            }
        }

        /// <summary>手柄按钮 → 文案。</summary>
        public static string Describe(GamepadButton button)
        {
            string name = button.ToString();
            if (name.StartsWith("Button", StringComparison.Ordinal))
            {
                return name.Substring("Button".Length);
            }
            return name;
        }
    }

    /// <summary>
    /// Narrative 键位选择器。对应 UE5 <c>UNarrativeInputKeySelector</c>（继承 UInputKeySelector）。
    /// <para>
    /// UE5 子类唯一的行为是 <c>RebuildWidget</c>：从 <c>NarrativeUIDeveloperSettings</c>
    /// 取主色/反色覆盖按钮样式（Normal/Hovered/Pressed 的 TintColor 与前景色）。
    /// 本端口把它落成真正的 Flax 控件：<see cref="Button"/> 派生 + 监听态，
    /// 点击后进入"请按键"状态，捕获下一次键盘/鼠标/手柄输入，
    /// 并通过 <see cref="Input.SetActionConfigByName(string,KeyboardKeys,bool)"/> 等真实 API
    /// 把绑定写进 Flax 的输入动作表（对应 UE5 的 Input Mapping Context 改写）。
    /// </para>
    /// </summary>
    public class NarrativeInputKeySelector : Button
    {
        /// <summary>要重新绑定的 Flax 输入动作名（对应 UE5 的 InputAction）。</summary>
        public string ActionName = string.Empty;

        /// <summary>当前选中键（对应 UE5 SelectedKey）。</summary>
        public NarrativeInputBinding SelectedBinding;

        /// <summary>空态文案。对应 UE5 NoneText。</summary>
        public string NoneText = "<Null>";

        /// <summary>监听态文案。对应 UE5 PressKeyText。</summary>
        public string PressKeyText = "[Press a key]";

        /// <summary>允许绑定"无"（右键清除）。对应 UE5 bAllowNone。</summary>
        public bool bAllowNone = true;

        /// <summary>按 Esc 取消选择而不是绑成 Esc。对应 UE5 bUseEscToCancel。</summary>
        public bool bUseEscToCancel = true;

        /// <summary>是否接受鼠标键绑定。</summary>
        public bool bAllowMouse = true;

        /// <summary>是否接受手柄按钮绑定（0 号手柄）。</summary>
        public bool bAllowGamepad = true;

        /// <summary>主题色是否覆盖样式（UE5 子类行为，默认 true）。</summary>
        public bool bFollowTheme = true;

        /// <summary>是否处于监听态。对应 UE5 bIsSelecting。</summary>
        public bool IsListening { get; private set; }

        /// <summary>选中键确定后触发（含清除）。对应 UE5 OnKeySelected。</summary>
        public event Action<NarrativeInputKeySelector, NarrativeInputBinding> KeySelected;

        /// <summary>用户取消选择时触发。对应 UE5 OnKeySelectionCanceled。</summary>
        public event Action<NarrativeInputKeySelector> KeySelectionCanceled;

        /// <summary>监听候选键位表（构造一次即可）。</summary>
        private static readonly KeyboardKeys[] KeyCandidates = BuildKeyCandidates();

        private string _textBeforeListen;

        public NarrativeInputKeySelector()
        {
            AnchorPreset = AnchorPresets.TopLeft;

            // UE5 的 UInputKeySelector 内部就是一个按钮 + 文案，Flax 侧 Button 已具备该形态
            HasBorder = true;
            BorderThickness = 1f;

            Clicked += OnButtonClicked;
            ApplyButtonStyle();
            RefreshText();
        }

        /// <summary>
        /// 重建控件：应用主题样式。对应 UE5 <c>RebuildWidget</c>。
        /// </summary>
        public virtual void RebuildWidget()
        {
            ApplyButtonStyle();
            RefreshText();
        }

        /// <summary>
        /// 从开发者设置取主题覆盖按钮样式。
        /// 对应 UE5：Normal.TintColor=Primary，Hovered.TintColor=Invert，Pressed.TintColor=Primary，
        /// 三态前景色都用 Primary。
        /// </summary>
        public void ApplyButtonStyle()
        {
            if (!bFollowTheme) return;

            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            BackgroundColor = settings.UIPrimaryColor;
            BackgroundColorHighlighted = settings.UIInvertColor;
            BackgroundColorSelected = settings.UIPrimaryColor;

            BorderColor = settings.UIInvertAccentColor;
            BorderColorHighlighted = settings.UIInvertColor;
            BorderColorSelected = settings.UIPrimaryColor;

            TextColor = settings.bUseLightTextOnPrimary ? Color.White : settings.UIInvertColor;
            TextColorHighlighted = settings.UIPrimaryColor;
        }

        /// <summary>取当前键位（对应 UE5 GetSelectedKey）。</summary>
        public NarrativeInputBinding GetSelectedBinding() => SelectedBinding;

        /// <summary>设置当前键位并刷新显示（对应 UE5 SetSelectedKey）。</summary>
        public void SetSelectedBinding(NarrativeInputBinding binding, bool bApplyToInput = true, bool bBroadcast = true)
        {
            if (IsListening) EndListen(false);

            SelectedBinding = binding;
            RefreshText();

            if (bApplyToInput) ApplyBindingToInput(binding);
            if (bBroadcast) KeySelected?.Invoke(this, binding);
        }

        /// <summary>清除绑定（对应 UE5 的 None 选项）。</summary>
        public void ClearSelection()
        {
            SetSelectedBinding(new NarrativeInputBinding { Type = NarrativeInputBindingType.None });
        }

        /// <summary>进入监听态（对应 UE5 ListenForInput）。</summary>
        public void ListenForInput()
        {
            if (IsListening) return;

            IsListening = true;
            _textBeforeListen = Text;
            Text = PressKeyText;
        }

        /// <summary>退出监听态；bCanceled 为 true 时不改动已选键。</summary>
        public void EndListen(bool bCanceled)
        {
            if (!IsListening) return;

            IsListening = false;
            Text = bCanceled ? (_textBeforeListen ?? PressKeyText) : Text;
            RefreshText();

            if (bCanceled) KeySelectionCanceled?.Invoke(this);
        }

        /// <summary>把绑定写进 Flax 输入动作表（对应 UE5 修改 Input Mapping Context 的按键）。</summary>
        public void ApplyBindingToInput(NarrativeInputBinding binding)
        {
            if (string.IsNullOrEmpty(ActionName)) return;

            // ActionConfig 是结构体（无 null），与仓库既有做法一致：用 Name 字段判断动作是否存在
            var existingConfig = Input.GetActionConfigByName(ActionName);
            if (string.IsNullOrEmpty(existingConfig.Name)) return;

            switch (binding.Type)
            {
                case NarrativeInputBindingType.Keyboard:
                    Input.SetActionConfigByName(ActionName, binding.Key, true);
                    break;
                case NarrativeInputBindingType.Mouse:
                    Input.SetActionConfigByName(ActionName, binding.MouseKey, true);
                    break;
                case NarrativeInputBindingType.Gamepad:
                    Input.SetActionConfigByName(ActionName, binding.GamepadKey, InputGamepadIndex.Gamepad0, true);
                    break;
                default:
                    // None：把动作绑到 KeyboardKeys.None（等价 UE5 的 Unbind）
                    Input.SetActionConfigByName(ActionName, KeyboardKeys.None, true);
                    break;
            }
        }

        /// <summary>控件销毁：确保退出监听态。</summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            EndListen(true);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (IsListening)
            {
                // 监听期间的右键点击 = 取消（UE5 的 bUseMouseRightClickToClear）
                if (button == MouseButton.Right && bAllowNone)
                {
                    ClearSelection();
                    EndListen(false);
                    return true;
                }
                return true;
            }

            return base.OnMouseUp(location, button);
        }

        /// <summary>每帧轮询候选输入，捕获监听态下的第一个按键。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!IsListening) return;

            for (int i = 0; i < KeyCandidates.Length; i++)
            {
                KeyboardKeys key = KeyCandidates[i];
                if (!Input.GetKeyDown(key)) continue;

                if (bUseEscToCancel && key == KeyboardKeys.Escape)
                {
                    EndListen(true);
                    return;
                }

                SetSelectedBinding(NarrativeInputBinding.FromKey(key));
                EndListen(false);
                return;
            }

            if (bAllowMouse)
            {
                // 预先初始化为默认值：|| 短路时后两个 out 变量不会被赋值
                NarrativeInputBinding leftBinding = default;
                NarrativeInputBinding rightBinding = default;
                NarrativeInputBinding middleBinding = default;
                if (TryPollMouseButton(MouseButton.Left, out leftBinding) ||
                    TryPollMouseButton(MouseButton.Right, out rightBinding) ||
                    TryPollMouseButton(MouseButton.Middle, out middleBinding))
                {
                    NarrativeInputBinding picked = leftBinding.Type != NarrativeInputBindingType.None
                        ? leftBinding
                        : (rightBinding.Type != NarrativeInputBindingType.None ? rightBinding : middleBinding);
                    SetSelectedBinding(picked);
                    EndListen(false);
                    return;
                }
            }

            if (bAllowGamepad && TryPollGamepadButton(out var padBinding))
            {
                SetSelectedBinding(padBinding);
                EndListen(false);
            }
        }

        private void OnButtonClicked()
        {
            if (!IsListening) ListenForInput();
        }

        private bool TryPollMouseButton(MouseButton button, out NarrativeInputBinding binding)
        {
            binding = default;
            if (Input.GetMouseButtonDown(button))
            {
                binding = NarrativeInputBinding.FromMouseButton(button);
                return true;
            }
            return false;
        }

        private bool TryPollGamepadButton(out NarrativeInputBinding binding)
        {
            binding = default;

            var buttons = Enum.GetValues(typeof(GamepadButton));
            foreach (GamepadButton value in buttons)
            {
                if (value == GamepadButton.None) continue;
                if (Input.GetGamepadButtonDown(0, value, 0.2f))
                {
                    binding = NarrativeInputBinding.FromGamepad(value);
                    return true;
                }
            }
            return false;
        }

        private void RefreshText()
        {
            if (IsListening)
            {
                Text = PressKeyText;
                return;
            }

            string display = SelectedBinding.ToDisplayString();
            Text = string.IsNullOrEmpty(display) ? NoneText : display;
        }

        private static readonly System.Collections.Generic.HashSet<string> IgnoredKeyNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            // IME / 媒体 / 系统保留键：在游戏绑定里没有实际意义
            "Packet", "Processkey", "Modechange", "Convert", "Nonconvert", "Ereof", "Attn", "Exsel",
            "Pa1", "Clear", "Execute", "Select", "Sleep", "Hangul", "Hanja", "Junja", "Kana", "Kanji",
            "Final", "Help", "Print", "PrintScreen", "Play", "Pause", "Scroll", "Numlock", "Capital",
            "Oem8", "Oem102", "OemClear", "Crsel"
        };

        private static KeyboardKeys[] BuildKeyCandidates()
        {
            var keys = new System.Collections.Generic.List<KeyboardKeys>();

            foreach (KeyboardKeys value in Enum.GetValues(typeof(KeyboardKeys)))
            {
                if (value == KeyboardKeys.None || value == KeyboardKeys.MAX) continue;

                string name = value.ToString();
                if (IgnoredKeyNames.Contains(name)) continue;
                if (name.StartsWith("Browser", StringComparison.Ordinal)) continue;
                if (name.StartsWith("Launch", StringComparison.Ordinal)) continue;
                if (name.StartsWith("Volume", StringComparison.Ordinal)) continue;
                if (name.StartsWith("Media", StringComparison.Ordinal)) continue;

                keys.Add(value);
            }

            return keys.ToArray();
        }
    }
}
