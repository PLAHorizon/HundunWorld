using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Core;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 通用动作控件（输入操作栏）。移植自 UE5 NarrativeArsenal: <c>Widgets/NarrativeCommonActionWidget.h</c>
    /// （<c>UNarrativeCommonActionWidget : UCommonActionWidget</c>）。
    /// <para>
    /// UE5 里 CommonActionWidget 就是"一个输入动作的键位图标 + 文案"，
    /// 菜单把它们排成一行放在屏幕底部；本插件的子类只覆写 <c>UpdateActionWidget</c>
    /// —— 只在 <c>CommonInputSubsystem::ShouldShowInputKeys()</c> 为真（或设计期）时才刷新，
    /// 以绕开 UE5 的一个断言。
    /// </para>
    /// <para>
    /// Flax 侧实现为一个真正的 GUI 行容器：绑定一个
    /// <see cref="NarrativeActivatableWidget"/> 后，把它 <see cref="NarrativeActivatableWidget.GetActionBarEntries"/>
    /// 里的每个动作做成一枚键位芯片（键位从 Flax 的 <see cref="Input"/> 动作表实时解析，
    /// 因此 <see cref="NarrativeInputKeySelector"/> 改绑之后这里会跟着变），
    /// 并按 <see cref="NarrativeInputRouter.ShouldShowInputKeys"/> 决定显隐 —— 与 UE5 的门禁一致。
    /// </para>
    /// </summary>
    public class NarrativeCommonActionWidget : ContainerControl
    {
        /// <summary>芯片之间的间距（像素）。</summary>
        public float Spacing = 10f;

        /// <summary>芯片内左右留白（像素）。</summary>
        public float ChipPadding = 8f;

        /// <summary>行高（像素）。</summary>
        public float RowHeight = 26f;

        /// <summary>芯片文案字号。</summary>
        public float FontSize = 13f;

        /// <summary>是否显示键位提示（对应 UE5 ShouldShowInputKeys 的按键图标部分）。</summary>
        public bool bShowKeyGlyphs = true;

        /// <summary>无键位绑定时是否仍显示动作文案。</summary>
        public bool bShowTextWithoutKey = true;

        /// <summary>文案在前（true）还是键位在前（false）。</summary>
        public bool bTextBeforeKey = true;

        /// <summary>当前绑定的可激活控件。</summary>
        public NarrativeActivatableWidget BoundWidget { get; private set; }

        /// <summary>是否正在手柄输入模式（决定用键帽还是手柄按钮名）。</summary>
        public bool bUsingGamepad { get; private set; }

        private readonly List<Label> _chips = new List<Label>();
        private bool _bPendingRebuild;

        public NarrativeCommonActionWidget()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            ClipChildren = false;
            Visible = false;
        }

        /// <summary>
        /// 绑定一个可激活控件并立即重建行。对应 UE5 蓝图把 action widget 挂到某个 widget 的输入绑定上。
        /// </summary>
        public void Bind(NarrativeActivatableWidget widget)
        {
            if (BoundWidget == widget)
            {
                UpdateActionWidget();
                return;
            }

            Unbind();
            BoundWidget = widget;
            if (widget != null)
            {
                widget.OnActionBindingsChanged += OnBoundWidgetBindingsChanged;
            }
            UpdateActionWidget();
        }

        /// <summary>解绑并清空行。</summary>
        public void Unbind()
        {
            if (BoundWidget != null)
            {
                BoundWidget.OnActionBindingsChanged -= OnBoundWidgetBindingsChanged;
                BoundWidget = null;
            }
            ClearChips();
        }

        /// <summary>销毁时解绑，避免静态栈上残留回调。</summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            Unbind();
        }

        /// <summary>
        /// 刷新这一行。对应 UE5 <c>UpdateActionWidget</c>：
        /// 只有 <see cref="NarrativeInputRouter.ShouldShowInputKeys"/> 为真时才重建（UE5 子类的断言规避逻辑）。
        /// </summary>
        public virtual void UpdateActionWidget()
        {
            if (!NarrativeInputRouter.ShouldShowInputKeys())
            {
                // 触屏/不显示键位的输入模式下整行隐藏（等价 UE5 不调用 Super::UpdateActionWidget）
                Visible = false;
                _bPendingRebuild = true;
                return;
            }

            _bPendingRebuild = true;
            RebuildActionWidgets();
        }

        /// <summary>按当前绑定重建芯片行。对应 UE5 CommonActionWidget 的逐个刷新。</summary>
        public void RebuildActionWidgets()
        {
            ClearChips();

            var entries = BoundWidget != null ? BoundWidget.GetActionBarEntries() : null;
            bool bGamepadNow = NarrativeInputRouter.IsUsingGamepad();
            bUsingGamepad = bGamepadNow;

            if (entries == null || entries.Count == 0)
            {
                Visible = false;
                _bPendingRebuild = false;
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                string keyText = bShowKeyGlyphs ? ResolveKeyText(entry.ActionName, bGamepadNow) : string.Empty;

                if (string.IsNullOrEmpty(keyText) && !bShowTextWithoutKey) continue;

                string text;
                if (string.IsNullOrEmpty(keyText))
                {
                    text = entry.DisplayName;
                }
                else if (bTextBeforeKey)
                {
                    text = string.IsNullOrEmpty(entry.DisplayName) ? keyText : entry.DisplayName + "  " + keyText;
                }
                else
                {
                    text = string.IsNullOrEmpty(entry.DisplayName) ? keyText : keyText + "  " + entry.DisplayName;
                }

                var chip = new Label
                {
                    Text = text,
                    AutoWidth = true,
                    AutoHeight = false,
                    AnchorPreset = AnchorPresets.TopLeft,
                    VerticalAlignment = TextAlignment.Center,
                    Wrapping = TextWrapping.NoWrap,
                };

                // 沿用 GUI 默认字体，只统一字号（Flax 的 FontReference 是引用类型，可直接改 Size）
                if (chip.Font != null)
                {
                    chip.Font.Size = FontSize;
                }

                ApplyChipTheme(chip);
                chip.Size = new Float2(chip.Width, RowHeight);

                AddChild(chip);
                _chips.Add(chip);
            }

            Visible = _chips.Count > 0;
            _bPendingRebuild = false;
            LayoutChips();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 输入模式切换（插拔手柄）时刷新一次键位文案
            bool bGamepadNow = NarrativeInputRouter.IsUsingGamepad();
            if (bGamepadNow != bUsingGamepad)
            {
                RebuildActionWidgets();
                return;
            }

            if (_bPendingRebuild && NarrativeInputRouter.ShouldShowInputKeys())
            {
                RebuildActionWidgets();
                return;
            }

            LayoutChips();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutChips();
        }

        private void LayoutChips()
        {
            if (_chips.Count == 0) return;

            // 一律 TopLeft 锚 + 显式重排（非 TopLeft 锚在构造期按父尺寸 0 计算会错位）
            float x = 0f;
            for (int i = 0; i < _chips.Count; i++)
            {
                var chip = _chips[i];
                if (chip == null) continue;

                if (chip.Height <= 1f || Math.Abs(chip.Height - RowHeight) > 0.01f)
                {
                    chip.Size = new Float2(chip.Width, RowHeight);
                }

                chip.AnchorMin = Float2.Zero;
                chip.AnchorMax = Float2.Zero;
                chip.Location = new Float2(x, 0f);
                x += chip.Width + Spacing + ChipPadding;
            }
        }

        private void ClearChips()
        {
            for (int i = _chips.Count - 1; i >= 0; i--)
            {
                var chip = _chips[i];
                if (chip == null) continue;
                RemoveChild(chip);
                chip.Dispose();
            }
            _chips.Clear();
        }

        private void OnBoundWidgetBindingsChanged(NarrativeActivatableWidget widget)
        {
            if (widget != BoundWidget) return;
            UpdateActionWidget();
        }

        /// <summary>
        /// 解析某个输入动作当前绑定的键位文案。
        /// 对应 UE5 CommonActionWidget 从 Input Modifier 里取按键图标的做法，
        /// 数据源换成 Flax 的 <see cref="Input.GetActionConfigByName"/>。
        /// </summary>
        private static string ResolveKeyText(string actionName, bool bGamepad)
        {
            if (string.IsNullOrEmpty(actionName)) return string.Empty;

            try
            {
                var configs = Input.GetAllActionConfigsByName(actionName);
                if (configs == null || configs.Length == 0) return string.Empty;

                for (int i = 0; i < configs.Length; i++)
                {
                    var config = configs[i];

                    if (bGamepad)
                    {
                        if (config.GamepadButton != GamepadButton.None)
                        {
                            return NarrativeInputKeyFormatter.Describe(config.GamepadButton);
                        }
                        continue;
                    }

                    if (config.Key != KeyboardKeys.None)
                    {
                        return NarrativeInputKeyFormatter.Describe(config.Key);
                    }
                    if (config.MouseButton != MouseButton.None)
                    {
                        return NarrativeInputKeyFormatter.Describe(config.MouseButton);
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeCommonActionWidget 解析键位失败：" + ex.Message);
            }

            return string.Empty;
        }

        private void ApplyChipTheme(Label chip)
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null) return;

            chip.BackgroundColor = Color.Lerp(settings.UIInvertColor, Color.Black, 0.2f);
            chip.TextColor = settings.bUseLightTextOnPrimary ? Color.White : settings.UIPrimaryColor;
        }

        private static Font GetOrDefaultFont()
        {
            // 主题没有单独配置字体时使用 GUI 默认字体，避免芯片因缺字体而不可见。
            // Flax 1.12 托管层没有静态 GUI 入口（GUI.IsGUIInitialized 不存在于本版本），
            // 返回 null 即让控件回退到自身默认字体（与原实现各分支的返回值一致）。
            return null;
        }
    }
}
