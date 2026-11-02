using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;
using NarrativePro.CommonUI.Widgets;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 暂停菜单（<see cref="NarrativeMenu"/> 的具体 Flax 实现）。
    /// <para>
    /// UE5 的 <c>UNarrativeMenu</c> 只有构造函数，内容由蓝图子类提供，并由
    /// <c>UNarrativeGameplayHUD::OpenMenu</c> 压到某个层上；本端口需要一个可实例化的具体类，
    /// <c>OpenMenu</c> 的重复菜单守卫（<c>CurrentWidget.GetType() == menuType</c>）与
    /// <c>Activator.CreateInstance(menuType)</c> 才有真正的落点。
    /// </para>
    /// <para>
    /// 内容全部是真实 Flax 控件：标题 + 每个菜单项一个
    /// <see cref="NarrativeCommonButtonBase"/>（选中相由导航高亮驱动）+ 操作提示。
    /// 默认项接的是这套 UI 已经存在的真实行为：关闭自己、经
    /// <see cref="NarrativeRuntimeBootstrap.ToggleQuestLog"/> 打开任务日志。
    /// 键盘：↑↓ 导航、Enter 确认、Esc 关闭（基类 <c>bDeactivateOnBack</c> 负责）。
    /// </para>
    /// </summary>
    public class NarrativePauseMenu : NarrativeMenu
    {
        /// <summary>菜单标题文本。</summary>
        public string Title = "暂停";

        /// <summary>菜单项高度（像素）。</summary>
        public float ItemHeight = 34f;

        /// <summary>菜单项宽度（像素）。</summary>
        public float ItemWidth = 260f;

        /// <summary>菜单项间距（像素）。</summary>
        public float ItemSpacing = 8f;

        /// <summary>标题控件（真实 Flax 文本控件）。</summary>
        public NarrativeCommonTextBlock TitleLabel { get; private set; }

        /// <summary>操作提示控件。</summary>
        public NarrativeCommonTextBlock HintLabel { get; private set; }

        /// <summary>背景板（对应 UE5 菜单蓝图里的半透明底）。用 <see cref="Image"/> + SolidColorBrush，
        /// 因为 Flax 的 Panel 画布刷可能为空时不落色，Image 的画刷是确定绘制的。</summary>
        public Image Backdrop { get; private set; }

        private readonly List<NarrativeCommonButtonBase> _items = new List<NarrativeCommonButtonBase>();
        private readonly List<Action> _callbacks = new List<Action>();
        private int _selectedIndex = -1;

        /// <summary>当前高亮项下标（-1 表示未选）。</summary>
        public int SelectedIndex => _selectedIndex;

        /// <summary>菜单项数量。</summary>
        public int ItemCount => _items.Count;

        public NarrativePauseMenu()
        {
            OptionalDisplayName = "暂停菜单";

            Backdrop = new Image
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(320f, 220f),
                Brush = new SolidColorBrush(new Color(0f, 0f, 0f, 0.62f)),
            };
            AddChild(Backdrop);

            TitleLabel = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 16f),
                Size = new Float2(320f, 34f),
                Style = "H2",
            };
            TitleLabel.SetJustification(ETextJustify.Center);
            TitleLabel.SetText(Title);
            AddChild(TitleLabel);

            HintLabel = new NarrativeCommonTextBlock
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 190f),
                Size = new Float2(320f, 20f),
                Style = "Small",
            };
            HintLabel.SetJustification(ETextJustify.Center);
            HintLabel.SetText("↑↓ 选择 · Enter 确认 · Esc 关闭");
            AddChild(HintLabel);

            // 默认菜单项：全部接到本端口已有的真实入口
            AddMenuItem("继续游戏", Close);
            AddMenuItem("任务日志", OpenQuestLog);

            BuildItems();
            HighlightIndex(0);
        }

        /// <summary>追加一个菜单项（文本 + 点击回调）。对应 UE5 菜单蓝图里的 AddAction。</summary>
        public NarrativeCommonButtonBase AddMenuItem(string text, Action callback)
        {
            _itemTexts.Add(text ?? string.Empty);
            _callbacks.Add(callback);
            _bItemsDirty = true;
            return null;
        }

        private readonly List<string> _itemTexts = new List<string>();
        private bool _bItemsDirty = true;

        /// <summary>清空菜单项并回收按钮。</summary>
        public void ClearMenuItems()
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i]?.Dispose();
            }
            _items.Clear();
            _itemTexts.Clear();
            _callbacks.Clear();
            _selectedIndex = -1;
            _bItemsDirty = true;
        }

        /// <summary>按下标触发菜单项。</summary>
        public bool InvokeMenuItem(int index)
        {
            if (index < 0 || index >= _callbacks.Count) return false;

            var callback = _callbacks[index];
            callback?.Invoke();
            return true;
        }

        /// <summary>按标题触发菜单项（找不到返回 false）。</summary>
        public bool InvokeMenuItem(string text)
        {
            int index = _itemTexts.FindIndex(t => t == text);
            return InvokeMenuItem(index);
        }

        /// <summary>高亮某一项（只影响视觉选中相，不触发回调）。对应 UE5 的 SetCurrentTabActiveWidget。</summary>
        public void HighlightIndex(int index)
        {
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }

            if (index < 0) index = _items.Count - 1;
            if (index >= _items.Count) index = 0;

            _selectedIndex = index;

            for (int i = 0; i < _items.Count; i++)
            {
                var button = _items[i];
                if (button == null) continue;
                button.ForceSetIsSelected(i == index, false, false);
            }
        }

        /// <summary>把内容铺到当前父容器尺寸上（TopLeft 锚 + 显式 Location，随父尺寸变化重排）。</summary>
        public void RelayoutContent()
        {
            Float2 parentSize = Parent != null ? Parent.Size : Float2.Zero;
            if (parentSize.X <= 0f || parentSize.Y <= 0f) return;

            AnchorMin = Float2.Zero;
            AnchorMax = Float2.Zero;
            Location = Float2.Zero;
            Size = parentSize;

            float panelWidth = System.Math.Max(ItemWidth + 40f, 320f);
            float panelHeight = 62f + _items.Count * (ItemHeight + ItemSpacing) + 26f;

            if (Backdrop != null)
            {
                Backdrop.Size = new Float2(panelWidth, panelHeight);
                Backdrop.Location = new Float2(
                    System.Math.Max(0f, (parentSize.X - panelWidth) * 0.5f),
                    System.Math.Max(0f, (parentSize.Y - panelHeight) * 0.5f));
            }

            float contentX = Backdrop != null ? Backdrop.Location.X : 20f;
            float contentY = Backdrop != null ? Backdrop.Location.Y : 20f;

            if (TitleLabel != null)
            {
                TitleLabel.Size = new Float2(panelWidth, 34f);
                TitleLabel.Location = new Float2(contentX, contentY + 16f);
            }

            float y = contentY + 58f;
            for (int i = 0; i < _items.Count; i++)
            {
                var button = _items[i];
                if (button == null) continue;

                button.Size = new Float2(ItemWidth, ItemHeight);
                button.Location = new Float2(contentX + (panelWidth - ItemWidth) * 0.5f, y);
                y += ItemHeight + ItemSpacing;
            }

            if (HintLabel != null)
            {
                HintLabel.Size = new Float2(panelWidth, 20f);
                HintLabel.Location = new Float2(contentX, contentY + panelHeight - 24f);
            }
        }

        /// <summary>菜单被压入激活栈时重建内容并重排。对应 UE5 蓝图菜单的 NativeConstruct。</summary>
        public override void NativeOnActivated()
        {
            BuildItems();
            HighlightIndex(_selectedIndex < 0 ? 0 : _selectedIndex);
            base.NativeOnActivated();
            RelayoutContent();
        }

        /// <summary>父容器尺寸变化时重排（激活栈 RelayoutAll 会改动本控件尺寸）。</summary>
        public override void OnParentResized()
        {
            base.OnParentResized();
            RelayoutContent();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            RelayoutContent();
        }

        /// <summary>注册输入动作（对应 UE5 RegisterActions）。基类负责动作绑定表。</summary>
        public override void RegisterActions()
        {
            BuildItems();
            base.RegisterActions();
        }

        /// <summary>焦点目标：首个菜单项按钮。对应 UE5 GetDesiredFocusTarget。</summary>
        protected override Control NativeGetDesiredFocusTarget()
        {
            BuildItems();
            return _items.Count > 0 ? _items[0] : null;
        }

        private void BuildItems()
        {
            if (!_bItemsDirty) return;
            _bItemsDirty = false;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i]?.Dispose();
            }
            _items.Clear();

            for (int i = 0; i < _itemTexts.Count; i++)
            {
                string text = _itemTexts[i];
                int index = i;

                var button = new NarrativeCommonButtonBase
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    ButtonText = text,
                    bToggleSelectionOnClick = false,
                    Size = new Float2(ItemWidth, ItemHeight),
                };
                button.SetButtonText(text);
                button.ButtonClickedByUser += _ => InvokeMenuItem(index);
                AddChild(button);
                _items.Add(button);
            }

            _selectedIndex = System.Math.Min(_selectedIndex, _items.Count - 1);
        }

        /// <summary>键盘导航。Esc/Backspace 由基类（bDeactivateOnBack）处理，因此先问基类。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            if (base.OnKeyDown(key)) return true;

            switch (key)
            {
                case KeyboardKeys.ArrowDown:
                    HighlightIndex(_selectedIndex + 1);
                    return true;
                case KeyboardKeys.ArrowUp:
                    HighlightIndex(_selectedIndex - 1);
                    return true;
                case KeyboardKeys.Return:
                    InvokeMenuItem(_selectedIndex);
                    return true;
            }

            return false;
        }

        private void OpenQuestLog()
        {
            NarrativeRuntimeBootstrap.ToggleQuestLog(true);
            Close();
        }
    }
}
