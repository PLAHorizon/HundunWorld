using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Layout;
using System;

namespace HundunWorld.Game.UI.StyleSystem
{
    /// <summary>
    /// 中国古典美学风格主题系统
    /// 基于《浮云十八声》的设计理念
    /// </summary>
    public static class ChineseClassicalTheme
    {
        #region 色彩方案
        
        /// <summary>
        /// 主色调 - 黛青色（深邃如翡翠空的青色）
        /// </summary>
        public static readonly Color PrimaryColor = new Color(45f/255f, 85f/255f, 105f/255f, 1f);
        
        /// <summary>
        /// 辅助色 - 古典金（温润如玉的金色）
        /// </summary>
        public static readonly Color SecondaryColor = new Color(205f/255f, 165f/255f, 85f/255f, 1f);
        
        /// <summary>
        /// 背景色 - 雅致灰白（典雅如水墨的灰白色）
        /// </summary>
        public static readonly Color BackgroundColor = new Color(35f/255f, 40f/255f, 45f/255f, 0.95f);
        
        /// <summary>
        /// 面板色 - 青石色（温润的石材色）
        /// </summary>
        public static readonly Color PanelColor = new Color(55f/255f, 65f/255f, 75f/255f, 0.9f);
        
        /// <summary>
        /// 文字色 - 清雅白（柔和的白颜色）
        /// </summary>
        public static readonly Color TextColor = new Color(245f/255f, 245f/255f, 245f/255f, 1f);
        
        /// <summary>
        /// 强调色 - 朱砂红（传统的朱砂红）
        /// </summary>
        public static readonly Color AccentColor = new Color(185f/255f, 65f/255f, 65f/255f, 1f);
        
        /// <summary>
        /// 成功色 - 竹叶青（清新的绿色）
        /// </summary>
        public static readonly Color SuccessColor = new Color(85f/255f, 165f/255f, 85f/255f, 1f);
        
        /// <summary>
        /// 输入框色 - 深青色
        /// </summary>
        public static readonly Color InputColor = new Color(65f/255f, 75f/255f, 85f/255f, 1f);
        
        /// <summary>
        /// 边框色 - 古典金边框
        /// </summary>
        public static readonly Color BorderColor = new Color(205f/255f, 165f/255f, 85f/255f, 0.6f);
        
        /// <summary>
        /// 输入框背景色 - 更深的青色
        /// </summary>
        public static readonly Color InputBackgroundColor = new Color(45f/255f, 55f/255f, 65f/255f, 0.9f);
        
        #endregion
        
        #region 黄金比例设计系统
        
        /// <summary>
        /// 黄金比例布局计算器
        /// </summary>
        public static class GoldenRatioLayout
        {
            /// <summary>
            /// 计算登录面板的黄金比例尺寸
            /// </summary>
            /// <param name="baseWidth">基础宽度</param>
            /// <returns>符合黄金比例的尺寸</returns>
            public static Float2 CalculateLoginPanelSize(float baseWidth = 480)
            {
                var height = baseWidth / ResponsiveLayoutCalculator.GoldenRatio;
                return ResponsiveLayoutCalculator.EnsureSafeSize(new Float2(baseWidth, height));
            }
            
            /// <summary>
            /// 计算注册面板的优化尺寸（紧凑网格布局）
            /// </summary>
            /// <param name="baseWidth">基础宽度</param>
            /// <returns>优化的尺寸</returns>
            public static Float2 CalculateRegisterPanelSize(float baseWidth = 680)
            {
                var height = baseWidth * 0.52f; // 调整高度比例以适应紧凑布局
                return ResponsiveLayoutCalculator.EnsureSafeSize(new Float2(baseWidth, height));
            }
            
            /// <summary>
            /// 计算输入框的统一尺寸
            /// </summary>
            /// <param name="containerWidth">容器宽度</param>
            /// <returns>输入框尺寸</returns>
            public static Float2 CalculateInputSize(float containerWidth)
            {
                var width = containerWidth * 0.65f; // 占容器65%宽度
                var height = 36f; // 标准高度
                return new Float2(width, height);
            }
            
            /// <summary>
            /// 计算按钮的标准尺寸
            /// </summary>
            /// <param name="buttonType">按钮类型</param>
            /// <returns>按钮尺寸</returns>
            public static Float2 CalculateButtonSize(ButtonType buttonType)
            {
                return buttonType switch
                {
                    ButtonType.Primary => new Float2(120, 40),      // 主要按钮
                    ButtonType.Secondary => new Float2(100, 36),    // 次要按钮
                    ButtonType.Small => new Float2(80, 32),         // 小按钮
                    ButtonType.Large => new Float2(160, 48),        // 大按钮
                    _ => new Float2(120, 40)
                };
            }
            
            /// <summary>
            /// 计算元素间距
            /// </summary>
            /// <param name="elementType">元素类型</param>
            /// <returns>间距值</returns>
            public static float CalculateSpacing(SpacingType elementType)
            {
                return elementType switch
                {
                    SpacingType.Small => 8f,       // 小间距
                    SpacingType.Medium => 16f,     // 中等间距
                    SpacingType.Large => 24f,      // 大间距
                    SpacingType.ExtraLarge => 32f, // 超大间距
                    SpacingType.Big => 64f, // 超大间距
                    _ => 16f
                };
            }
        }
        
        #endregion
        
        #region 视觉层次设计
        
        /// <summary>
        /// 应用视觉层次样式
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="hierarchy">层次等级</param>
        public static void ApplyVisualHierarchy(Control control, VisualHierarchy hierarchy)
        {
            switch (hierarchy)
            {
                case VisualHierarchy.Primary:
                    ApplyPrimaryStyle(control);
                    break;
                case VisualHierarchy.Secondary:
                    ApplySecondaryStyle(control);
                    break;
                case VisualHierarchy.Tertiary:
                    ApplyTertiaryStyle(control);
                    break;
                case VisualHierarchy.Auxiliary:
                    ApplyAuxiliaryStyle(control);
                    break;
            }
        }
        
        /// <summary>
        /// 应用主要样式（最高优先级）
        /// </summary>
        private static void ApplyPrimaryStyle(Control control)
        {
            if (control is Button button)
            {
                button.BackgroundColor = SecondaryColor; // 使用古典金
                button.TextColor = Color.Black;
            }
            else if (control is Label label)
            {
                label.TextColor = SecondaryColor;
            }
        }
        
        /// <summary>
        /// 应用次要样式
        /// </summary>
        private static void ApplySecondaryStyle(Control control)
        {
            if (control is Button button)
            {
                button.BackgroundColor = PrimaryColor; // 使用黛青色
                button.TextColor = TextColor;
            }
            else if (control is Label label)
            {
                label.TextColor = PrimaryColor;
            }
        }
        
        /// <summary>
        /// 应用第三级样式
        /// </summary>
        private static void ApplyTertiaryStyle(Control control)
        {
            if (control is TextBox textBox)
            {
                textBox.BackgroundColor = InputColor;
                textBox.TextColor = TextColor;
            }
            else if (control is Panel panel)
            {
                panel.BackgroundColor = PanelColor;
            }
        }
        
        /// <summary>
        /// 应用辅助样式（最低优先级）
        /// </summary>
        private static void ApplyAuxiliaryStyle(Control control)
        {
            if (control is Label label)
            {
                label.TextColor = new Color(TextColor.R, TextColor.G, TextColor.B, 0.7f); // 透明度70%
            }
        }
        
        #endregion
        
        #region 中式装饰元素
        
        /// <summary>
        /// 应用中式边框装饰
        /// </summary>
        /// <param name="panel">面板</param>
        /// <param name="borderStyle">边框样式</param>
        public static void ApplyChineseBorder(Panel panel, ChineseBorderStyle borderStyle)
        {
            // 这里可以根据Flax Engine的具体API来实现边框装饰
            // 目前先实现基础的色彩和圆角
            switch (borderStyle)
            {
                case ChineseBorderStyle.Elegant:
                    // 优雅边框 - 细线条
                    panel.BackgroundColor = PanelColor;
                    break;
                case ChineseBorderStyle.Traditional:
                    // 传统边框 - 厚实边框
                    panel.BackgroundColor = new Color(PanelColor.R * 0.9f, PanelColor.G * 0.9f, PanelColor.B * 0.9f, PanelColor.A);
                    break;
                case ChineseBorderStyle.Ornate:
                    // 华丽边框 - 装饰性强
                    panel.BackgroundColor = new Color(PanelColor.R * 1.1f, PanelColor.G * 1.1f, PanelColor.B * 1.1f, PanelColor.A);
                    break;
            }
        }

        /// <summary>
        /// 应用中式边框装饰
        /// </summary>
        /// <param name="panel">面板</param>
        /// <param name="borderStyle">边框样式</param>
        public static void ApplyChineseBorder(ContainerControl panel, ChineseBorderStyle borderStyle)
        {
            // 这里可以根据Flax Engine的具体API来实现边框装饰
            // 目前先实现基础的色彩和圆角
            switch (borderStyle)
            {
                case ChineseBorderStyle.Elegant:
                    // 优雅边框 - 细线条
                    panel.BackgroundColor = PanelColor;
                    break;
                case ChineseBorderStyle.Traditional:
                    // 传统边框 - 厚实边框
                    panel.BackgroundColor = new Color(PanelColor.R * 0.9f, PanelColor.G * 0.9f, PanelColor.B * 0.9f, PanelColor.A);
                    break;
                case ChineseBorderStyle.Ornate:
                    // 华丽边框 - 装饰更强
                    panel.BackgroundColor = new Color(PanelColor.R * 1.1f, PanelColor.G * 1.1f, PanelColor.B * 1.1f, PanelColor.A);
                    break;
            }
        }

        #endregion
    }
    
    #endregion
}
