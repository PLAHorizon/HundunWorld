using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 横向筛选条：Ghost 胶囊 + 选中金底。用于成就/收集/商店/任务的分类筛选，
    /// 避免每页各写一套 chip 状态机。
    /// </summary>
    public class InkFilterBar : ContainerControl
    {
        private const float ChipGap = 6f;
        private const float ChipHeight = 28f;
        private const float MinChipWidth = 64f;

        private readonly InkButton[] _chips;
        private int _selectedIndex;

        public event Action<int> SelectedChanged;

        public InkFilterBar(string[] labels)
        {
            AutoFocus = false;
            ClipChildren = false;
            BackgroundColor = Color.Transparent;

            labels ??= Array.Empty<string>();
            _chips = new InkButton[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var chip = new InkButton
                {
                    Variant = InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Sm,
                    Text = labels[i] ?? "",
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                chip.Clicked += () => Select(index, notify: true);
                _chips[i] = chip;
                AddChild(chip);
            }

            Select(0, notify: false);
        }

        public int SelectedIndex => _selectedIndex;

        public void Select(int index, bool notify = true)
        {
            if (_chips.Length == 0) return;
            _selectedIndex = Mathf.Clamp(index, 0, _chips.Length - 1);
            for (var i = 0; i < _chips.Length; i++)
            {
                var active = i == _selectedIndex;
                _chips[i].BackgroundColor = active
                    ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.16f)
                    : Color.Transparent;
                _chips[i].TextColor = active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary;
            }

            if (notify) SelectedChanged?.Invoke(_selectedIndex);
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutChips();
        }

        private void LayoutChips()
        {
            if (_chips.Length == 0) return;
            float available = Mathf.Max(0f, Width);
            float chipW = Mathf.Max(MinChipWidth, (available - (_chips.Length - 1) * ChipGap) / _chips.Length);
            for (var i = 0; i < _chips.Length; i++)
            {
                _chips[i].Location = new Float2(i * (chipW + ChipGap), 0f);
                _chips[i].Size = new Float2(chipW, ChipHeight);
            }
        }
    }
}
