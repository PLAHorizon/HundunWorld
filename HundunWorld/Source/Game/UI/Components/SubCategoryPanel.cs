using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 二级子分类面板 - 显示当前主分类下的子分类列表
    /// 点击子分类后触发参数面板更新
    /// </summary>
    public class SubCategoryPanel : ContainerControl
    {
        public event Action<int, string> OnSubCategoryChanged; // (index, name)

        private static readonly Color NormalColor = new Color(0.10f, 0.10f, 0.12f, 0.6f);
        private static readonly Color SelectedColor = new Color(0.18f, 0.15f, 0.06f, 0.9f);
        private static readonly Color NormalTextColor = new Color(0.65f, 0.65f, 0.7f);
        private static readonly Color SelectedTextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1.0f);

        private List<Button> _subCategoryButtons = new List<Button>();
        private int _selectedIndex = 0;
        private float _buttonHeight = 36f;
        private float _padding = 4f;

        public int SelectedIndex => _selectedIndex;

        public SubCategoryPanel()
        {
            Width = 140;
            BackgroundColor = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        }

        /// <summary>
        /// 设置子分类列表
        /// </summary>
        public void SetSubCategories(string[] subCategories)
        {
            RemoveChildren();
            _subCategoryButtons.Clear();
            _selectedIndex = 0;

            float y = _padding;
            for (int i = 0; i < subCategories.Length; i++)
            {
                int index = i;
                var btn = new Button
                {
                    Parent = this,
                    Y = y,
                    Width = Width - _padding * 2,
                    X = _padding,
                    Height = _buttonHeight,
                    Text = subCategories[i],
                    TextColor = i == _selectedIndex ? SelectedTextColor : NormalTextColor,
                    BackgroundColor = i == _selectedIndex ? SelectedColor : NormalColor,
                    Font = UIHelper.SetFont(size: 12)
                };
                btn.Clicked += () => SelectSubCategory(index);
                _subCategoryButtons.Add(btn);
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
            for (int i = 0; i < _subCategoryButtons.Count; i++)
            {
                _subCategoryButtons[i].X = _padding;
                _subCategoryButtons[i].Y = y;
                _subCategoryButtons[i].Width = Width - _padding * 2;
                y += _buttonHeight + _padding;
            }
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutButtons();
        }

        /// <summary>
        /// 选择子分类
        /// </summary>
        public void SelectSubCategory(int index)
        {
            if (index < 0 || index >= _subCategoryButtons.Count) return;

            _selectedIndex = index;

            for (int i = 0; i < _subCategoryButtons.Count; i++)
            {
                _subCategoryButtons[i].TextColor = i == _selectedIndex ? SelectedTextColor : NormalTextColor;
                _subCategoryButtons[i].BackgroundColor = i == _selectedIndex ? SelectedColor : NormalColor;
            }

            var btn = _subCategoryButtons[_selectedIndex];
            OnSubCategoryChanged?.Invoke(_selectedIndex, btn.Text);
        }
    }
}
