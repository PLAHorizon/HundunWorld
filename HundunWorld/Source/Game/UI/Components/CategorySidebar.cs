using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 分类侧边栏组件 - 左侧图标分类导航
    /// 用于角色创建界面的主要分类切换（捏脸/妆容/发型等）
    /// </summary>
    public class CategorySidebar : ContainerControl
    {
        public event Action<int, string> OnCategoryChanged; // (index, name)

        // 样式统一引用设计 Token（出处：game-ui-system/colors_and_type.css --ink-* 系列）
        private static readonly Color NormalColor = UIStyleTokens.InkPanel(0.7f);
        private static readonly Color SelectedColor = UIStyleTokens.GoldFaint; // 选中底色 --ink-gold-faint
        private static readonly Color HoverColor = UIStyleTokens.BgHover; // 悬停叠层 --ink-bg-hover
        private static readonly Color SelectedBorderColor = UIStyleTokens.GoldPrimary; // 选中描边 --ink-gold-primary
        private static readonly Color NormalTextColor = UIStyleTokens.TextSecondary; // --ink-text-secondary
        private static readonly Color SelectedTextColor = UIStyleTokens.GoldPrimary; // 选中文字 --ink-text-gold

        private List<Button> _categoryButtons = new List<Button>();
        private int _selectedIndex = 0;
        private float _buttonHeight = 50f;
        private float _padding = 4f;

        public int SelectedIndex => _selectedIndex;

        public CategorySidebar()
        {
            Width = 120;
            // 侧边栏背景：墨黑 0.85 透明度（出处：--ink-bg-panel）
            BackgroundColor = UIStyleTokens.BgPanel;
        }

        /// <summary>
        /// 设置分类列表
        /// </summary>
        public void SetCategories(string[] categories)
        {
            RemoveChildren();
            _categoryButtons.Clear();

            float y = _padding;
            for (int i = 0; i < categories.Length; i++)
            {
                int index = i;
                bool isSelected = i == _selectedIndex;
                var btn = new Button
                {
                    Parent = this,
                    Y = y,
                    Width = Width - _padding * 2,
                    X = _padding,
                    Height = _buttonHeight,
                    Text = categories[i],
                    TextColor = isSelected ? SelectedTextColor : NormalTextColor,
                    BackgroundColor = isSelected ? SelectedColor : NormalColor,
                    BorderColor = isSelected ? SelectedBorderColor : Color.Transparent,
                    BorderThickness = isSelected ? 3f : 0f,
                    Font = UIHelper.SetFont(size: 12)
                };
                btn.Clicked += () => SelectCategory(index);
                _categoryButtons.Add(btn);
                y += _buttonHeight + _padding;
            }

            LayoutButtons();
        }

        /// <summary>
        /// 重新布局所有按钮的宽度和位置
        /// </summary>
        private void LayoutButtons()
        {
            float y = _padding;
            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                _categoryButtons[i].X = _padding;
                _categoryButtons[i].Y = y;
                _categoryButtons[i].Width = Width - _padding * 2;
                y += _buttonHeight + _padding;
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutButtons();
        }

        /// <summary>
        /// 选择分类
        /// </summary>
        public void SelectCategory(int index)
        {
            if (index < 0 || index >= _categoryButtons.Count) return;

            _selectedIndex = index;

            for (int i = 0; i < _categoryButtons.Count; i++)
            {
                bool isSelected = i == _selectedIndex;
                _categoryButtons[i].TextColor = isSelected ? SelectedTextColor : NormalTextColor;
                _categoryButtons[i].BackgroundColor = isSelected ? SelectedColor : NormalColor;
                _categoryButtons[i].BorderColor = isSelected ? SelectedBorderColor : Color.Transparent;
                _categoryButtons[i].BorderThickness = isSelected ? 3f : 0f;
            }

            var btn = _categoryButtons[_selectedIndex];
            OnCategoryChanged?.Invoke(_selectedIndex, btn.Text);
        }
    }
}
