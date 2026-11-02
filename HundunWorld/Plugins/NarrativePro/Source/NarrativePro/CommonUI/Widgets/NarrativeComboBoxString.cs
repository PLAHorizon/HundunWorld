using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// 选项被改变的方式。对应 UE5 <c>ESelectInfo::Type</c>（ComboBox 的 SelectInfo 参数）。
    /// </summary>
    public enum NarrativeSelectInfo
    {
        /// <summary>代码直接设置。</summary>
        Direct = 0,
        /// <summary>鼠标点击选项。</summary>
        MouseClick = 1,
        /// <summary>键盘/手柄导航选择。</summary>
        Navigation = 2,
    }

    /// <summary>
    /// Narrative 字符串下拉框。对应 UE5 <c>UNarrativeComboBoxString</c>（继承 UComboBoxString）。
    /// <para>
    /// 用真正的 Flax 控件组合实现：一个 <see cref="Button"/> 头部显示当前选项，
    /// 点开一个 <see cref="ContainerControl"/> 弹层，弹层里每个选项一个 <see cref="Button"/>
    /// （Flax 1.12 的 <c>Dropdown</c> 项集合是引擎内部类型，无法按 UE5 的字符串语义稳定构造，
    /// 因此这里用组合式实现同一行为，代价是多几个控件，换来的是可控的主题色与事件语义）。
    /// </para>
    /// <para>
    /// 行为对应 UE5：<see cref="DefaultOptions"/> 是选项表（<c>AddItem/RemoveItem/ClearOptions</c>），
    /// <see cref="SelectedOption"/> / <see cref="SelectedIndex"/> 是选择态，
    /// <see cref="OnSelectionChanged"/> 对应 <c>OnSelectionChanged(FString, ESelectInfo::Type)</c>，
    /// <see cref="OnOptionsChanged"/> 对应选项表变更；
    /// <see cref="RebuildWidget"/> 从 <see cref="NarrativeUIDeveloperSettings"/> 拉前景色并按选项表
    /// 重建弹层按钮（UE5 的 RebuildWidget → SComboBox 重建）。
    /// </para>
    /// </summary>
    public class NarrativeComboBoxString : ContainerControl
    {
        /// <summary>下拉框可选项列表。对应 UE5 ComboBoxString 的 DefaultOptions。</summary>
        public List<string> DefaultOptions = new List<string>();

        /// <summary>前景色（从 NarrativeUIDeveloperSettings.UIPrimaryColor 拉取）。对应 UE5 ForegroundColor。</summary>
        public Color ForegroundColor = Color.White;

        /// <summary>头部/弹层背景色。对应 UE5 ComboBox 的 WidgetStyle 背景画刷色。</summary>
        public Color PopupColor = new Color(0.0f, 0.009721f, 0.059511f, 1f);

        /// <summary>弹层项高度（像素）。</summary>
        public float ItemHeight = 24f;

        /// <summary>头部高度（像素）。</summary>
        public float HeaderHeight = 26f;

        /// <summary>选项文本超过该长度时截断显示。</summary>
        public int MaxDisplayLength = 24;

        /// <summary>当前选中项下标（无选中为 -1）。</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetSelectedIndex(value, NarrativeSelectInfo.Direct);
        }

        /// <summary>当前选中项。对应 UE5 SelectedOption。</summary>
        public string SelectedOption
        {
            get => _selectedOption;
            set => SelectItem(value, NarrativeSelectInfo.Direct);
        }

        /// <summary>弹层是否打开。</summary>
        public bool bPopupIsOpen { get; private set; }

        /// <summary>头部按钮（真实 Flax 控件，公开以便宿主接线/布局）。</summary>
        public Button HeaderButton { get; private set; }

        /// <summary>弹层根（真实 Flax 控件）。</summary>
        public ContainerControl PopupRoot { get; private set; }

        /// <summary>选择变更事件（选项文本 + 选择方式）。对应 UE5 OnSelectionChanged。</summary>
        public event Action<string, NarrativeSelectInfo> OnSelectionChanged;

        /// <summary>选项表变更事件（增删清后触发，弹层会自动重建）。</summary>
        public event Action OnOptionsChanged;

        /// <summary>弹层打开/关闭事件。</summary>
        public event Action<bool> OnPopupChanged;

        private readonly List<Button> _itemButtons = new List<Button>();
        private string _selectedOption = string.Empty;
        private int _selectedIndex = -1;
        private bool _bItemsDirty;

        /// <summary>构造函数。对应 UE5 UNarrativeComboBoxString 构造函数（默认尺寸 + 头部/弹层子控件）。</summary>
        public NarrativeComboBoxString()
        {
            // 构造期只用 TopLeft 锚 + 显式尺寸（非 TopLeft 锚会在父尺寸为 0 时算错位置）
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(220f, HeaderHeight);
            BackgroundColor = PopupColor;

            HeaderButton = new Button
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Size.X, HeaderHeight),
                Text = "请选择",
                TextColor = ForegroundColor,
                HasBorder = true,
                BorderThickness = 1f,
            };
            HeaderButton.Clicked += TogglePopup;
            AddChild(HeaderButton);

            PopupRoot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, HeaderHeight),
                Size = new Float2(Size.X, 0f),
                BackgroundColor = PopupColor,
                Visible = false,
            };
            AddChild(PopupRoot);
        }

        /// <summary>
        /// 重建控件。对应 UE5 RebuildWidget（UWidget 接口）：
        /// 从 NarrativeUIDeveloperSettings 拉前景色后重建弹层项。
        /// </summary>
        public virtual void RebuildWidget()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings != null)
            {
                InitForegroundColor(settings.UIPrimaryColor);
            }

            RebuildPopup();
            RefreshHeader();
            SyncSelectedIndex();
        }

        /// <summary>初始化前景色。对应 UE5 InitForegroundColor。</summary>
        public void InitForegroundColor(Color color)
        {
            ForegroundColor = color;

            if (HeaderButton != null)
            {
                HeaderButton.TextColor = color;
            }

            for (int i = 0; i < _itemButtons.Count; i++)
            {
                if (_itemButtons[i] != null) _itemButtons[i].TextColor = color;
            }
        }

        // ===== 选项表（对应 UE5 AddItem / RemoveItem / ClearOptions / GetOptionCount / GetOptionAsString） =====

        /// <summary>追加一个选项。</summary>
        public void AddItem(string option)
        {
            if (option == null) return;
            DefaultOptions.Add(option);
            NotifyOptionsChanged();
        }

        /// <summary>在指定位置插入选项。</summary>
        public void AddItemAt(int index, string option)
        {
            if (option == null) return;
            if (index < 0) index = 0;
            if (index > DefaultOptions.Count) index = DefaultOptions.Count;
            DefaultOptions.Insert(index, option);
            NotifyOptionsChanged();
        }

        /// <summary>移除一个选项（按文本）。返回是否移除成功。</summary>
        public bool RemoveItem(string option)
        {
            bool removed = DefaultOptions.Remove(option);
            if (removed) NotifyOptionsChanged();
            return removed;
        }

        /// <summary>清空所有选项。对应 UE5 ClearOptions。</summary>
        public void ClearOptions()
        {
            DefaultOptions.Clear();
            _selectedOption = string.Empty;
            _selectedIndex = -1;
            NotifyOptionsChanged();
        }

        /// <summary>选项数量。对应 UE5 GetOptionCount。</summary>
        public int GetOptionCount() => DefaultOptions.Count;

        /// <summary>按下标取选项文本。对应 UE5 GetOptionAsString。</summary>
        public string GetOptionAsString(int index)
        {
            if (index < 0 || index >= DefaultOptions.Count) return string.Empty;
            return DefaultOptions[index];
        }

        /// <summary>按下标选择。对应 UE5 SetSelectedIndex。</summary>
        public void SetSelectedIndex(int index, NarrativeSelectInfo selectInfo = NarrativeSelectInfo.Direct)
        {
            if (index < 0 || index >= DefaultOptions.Count)
            {
                _selectedIndex = -1;
                return;
            }

            ApplySelection(DefaultOptions[index], index, selectInfo);
        }

        /// <summary>按文本选择。对应 UE5 SelectItemOption。</summary>
        public bool SelectItem(string option, NarrativeSelectInfo selectInfo = NarrativeSelectInfo.Direct)
        {
            int index = option == null ? -1 : DefaultOptions.IndexOf(option);
            if (index < 0)
            {
                // UE5 允许选中不在表里的字符串（SelectedOption 直接赋值），这里保留同一语义
                ApplySelection(option ?? string.Empty, -1, selectInfo);
                return false;
            }

            ApplySelection(option, index, selectInfo);
            return true;
        }

        // ===== 弹层 =====

        /// <summary>打开弹层。</summary>
        public void ShowPopup()
        {
            if (bPopupIsOpen) return;
            bPopupIsOpen = true;

            RebuildPopup();
            if (PopupRoot != null)
            {
                PopupRoot.Visible = true;
                PopupRoot.BringToFront();
            }

            OnPopupChanged?.Invoke(true);
        }

        /// <summary>关闭弹层。</summary>
        public void HidePopup()
        {
            if (!bPopupIsOpen) return;
            bPopupIsOpen = false;

            if (PopupRoot != null) PopupRoot.Visible = false;
            OnPopupChanged?.Invoke(false);
        }

        private void TogglePopup()
        {
            if (bPopupIsOpen) HidePopup();
            else ShowPopup();
        }

        /// <summary>头部点击（供外部接线同一行为）。</summary>
        public void OnHeaderClicked() => TogglePopup();

        private void ApplySelection(string option, int index, NarrativeSelectInfo selectInfo)
        {
            bool changed = _selectedOption != option || _selectedIndex != index;
            _selectedOption = option ?? string.Empty;
            _selectedIndex = index;

            RefreshHeader();
            HighlightItem(index);

            if (changed)
            {
                OnSelectionChanged?.Invoke(_selectedOption, selectInfo);
            }
        }

        private void NotifyOptionsChanged()
        {
            _bItemsDirty = true;
            OnOptionsChanged?.Invoke();
        }

        private void RebuildPopup()
        {
            if (PopupRoot == null) return;

            if (_bItemsDirty)
            {
                for (int i = _itemButtons.Count - 1; i >= 0; i--)
                {
                    _itemButtons[i]?.Dispose();
                }
                _itemButtons.Clear();
                _bItemsDirty = false;
            }
            else if (_itemButtons.Count == DefaultOptions.Count)
            {
                // 项数一致时只同步文本，避免反复重建控件
                for (int i = 0; i < _itemButtons.Count; i++)
                {
                    _itemButtons[i].Text = Shorten(DefaultOptions[i]);
                }
                LayoutPopup();
                return;
            }
            else
            {
                for (int i = _itemButtons.Count - 1; i >= 0; i--)
                {
                    _itemButtons[i]?.Dispose();
                }
                _itemButtons.Clear();
                _bItemsDirty = false;
            }

            float y = 0f;
            for (int i = 0; i < DefaultOptions.Count; i++)
            {
                string optionText = DefaultOptions[i];
                int index = i;

                var item = new Button
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, y),
                    Size = new Float2(PopupRoot.Width, ItemHeight),
                    Text = Shorten(optionText),
                    TextColor = ForegroundColor,
                    TooltipText = optionText,
                };
                item.Clicked += () => OnItemClicked(index, optionText);
                PopupRoot.AddChild(item);
                _itemButtons.Add(item);

                y += ItemHeight;
            }

            LayoutPopup();
        }

        private void OnItemClicked(int index, string optionText)
        {
            ApplySelection(optionText, index, NarrativeSelectInfo.MouseClick);
            HidePopup();
        }

        private void HighlightItem(int activeIndex)
        {
            for (int i = 0; i < _itemButtons.Count; i++)
            {
                var button = _itemButtons[i];
                if (button == null) continue;
                button.BackgroundColorSelected = i == activeIndex ? Blend(PopupColor, ForegroundColor, 0.35f) : PopupColor;
            }
        }

        private void RefreshHeader()
        {
            if (HeaderButton == null) return;
            HeaderButton.Text = string.IsNullOrEmpty(_selectedOption) ? "（空）" : Shorten(_selectedOption);
            HeaderButton.TextColor = ForegroundColor;
        }

        private void SyncSelectedIndex()
        {
            if (_selectedIndex >= 0 && _selectedIndex < DefaultOptions.Count &&
                DefaultOptions[_selectedIndex] == _selectedOption)
            {
                HighlightItem(_selectedIndex);
                return;
            }

            int index = string.IsNullOrEmpty(_selectedOption) ? -1 : DefaultOptions.IndexOf(_selectedOption);
            _selectedIndex = index;
            HighlightItem(index);
        }

        private void LayoutPopup()
        {
            if (PopupRoot == null) return;

            PopupRoot.Location = new Float2(0f, HeaderHeight);
            PopupRoot.Size = new Float2(Width, DefaultOptions.Count * ItemHeight);

            float y = 0f;
            for (int i = 0; i < _itemButtons.Count; i++)
            {
                var button = _itemButtons[i];
                if (button == null) continue;
                button.Location = new Float2(0f, y);
                button.Size = new Float2(PopupRoot.Width, ItemHeight);
                y += ItemHeight;
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();

            if (HeaderButton != null)
            {
                HeaderButton.Location = Float2.Zero;
                HeaderButton.Size = new Float2(Width, HeaderHeight);
            }

            LayoutPopup();
        }

        /// <summary>键盘导航：上下键移动选择、回车确认（对应 UE5 的 Navigation 选择方式）。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            if (DefaultOptions.Count == 0) return false;

            switch (key)
            {
                case KeyboardKeys.ArrowDown:
                    SetSelectedIndex((_selectedIndex < 0 ? -1 : _selectedIndex) + 1, NarrativeSelectInfo.Navigation);
                    return true;
                case KeyboardKeys.ArrowUp:
                    SetSelectedIndex((_selectedIndex < 0 ? DefaultOptions.Count : _selectedIndex) - 1, NarrativeSelectInfo.Navigation);
                    return true;
                case KeyboardKeys.Return:
                    if (!bPopupIsOpen) ShowPopup();
                    else HidePopup();
                    return true;
                case KeyboardKeys.Escape:
                    if (bPopupIsOpen)
                    {
                        HidePopup();
                        return true;
                    }
                    break;
            }

            return false;
        }

        private string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length <= MaxDisplayLength) return text;
            return text.Substring(0, MaxDisplayLength) + "…";
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
    }
}
