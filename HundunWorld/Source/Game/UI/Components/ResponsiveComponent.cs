using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Layout;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 响应式UI组件基类
    /// 实现自动布局、样式应用和屏幕适配功能
    /// </summary>
    public abstract class ResponsiveComponent : ContainerControl
    {
        #region 属性

        /// <summary>
        /// 设计时尺寸（参考尺寸）
        /// </summary>
        protected Float2 DesignSize { get; set; } = new Float2(1920, 1080);

        /// <summary>
        /// 最小尺寸
        /// </summary>
        protected Float2 MinSize { get; set; } = new Float2(100, 50);

        /// <summary>
        /// 最大尺寸
        /// </summary>
        protected Float2 MaxSize { get; set; } = new Float2(2560, 1440);

        /// <summary>
        /// 是否启用响应式布局
        /// </summary>
        protected bool EnableResponsiveLayout { get; set; } = true;

        /// <summary>
        /// 视觉层次等级
        /// </summary>
        protected VisualHierarchy HierarchyLevel { get; set; } = VisualHierarchy.Secondary;

        /// <summary>
        /// 中式边框样式
        /// </summary>
        protected ChineseBorderStyle BorderStyle { get; set; } = ChineseBorderStyle.Elegant;

        /// <summary>
        /// 是否自动居中
        /// </summary>
        protected bool AutoCenter { get; set; } = false;

        /// <summary>
        /// 上次布局检查时间
        /// </summary>
        private float _lastLayoutCheckTime = 0.0f;

        #endregion

        #region 构造函数

        protected ResponsiveComponent()
        {
            // 监听屏幕尺寸变化
            Engine.RequestingExit += OnEngineExit;
        }

        #endregion

        #region 抽象方法

        /// <summary>
        /// 初始化组件内容（由子类实现）
        /// </summary>
        protected abstract void InitializeContent();

        /// <summary>
        /// 应用响应式布局（由子类实现具体逻辑）
        /// </summary>
        protected abstract void ApplyResponsiveLayout();

        #endregion

        #region 生命周期方法

        /// <summary>
        /// 组件初始化
        /// </summary>
        public override void DrawSelf()
        {
            base.DrawSelf();


            InitializeResponsiveComponent();
            InitializeContent();
            ApplyInitialLayout();
            ApplyTheme();
        }
       
        /// <summary>
        /// 更新方法 - 检查屏幕尺寸变化
        /// </summary>
        public override void OnParentResized()
        {
            base.OnParentResized();

            if (EnableResponsiveLayout)
            {
                CheckAndUpdateLayout();
            }
        }
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (EnableResponsiveLayout)
            {
                CheckAndUpdateLayout();
            }
        }
        #endregion

        #region 初始化方法

        /// <summary>
        /// 初始化响应式组件
        /// </summary>
        private void InitializeResponsiveComponent()
        {
            // 设置基础样式
            BackgroundColor = ChineseClassicalTheme.PanelColor;

            // 应用中式边框
            ChineseClassicalTheme.ApplyChineseBorder(this, BorderStyle);

            FlaxEngine.Debug.Log($"响应式组件初始化: {GetType().Name}");
        }

        /// <summary>
        /// 应用初始布局
        /// </summary>
        private void ApplyInitialLayout()
        {
            if (EnableResponsiveLayout)
            {
                CalculateAndApplySize();
                CalculateAndApplyPosition();
                ApplyResponsiveLayout();
            }
        }

        /// <summary>
        /// 应用主题样式
        /// </summary>
        private void ApplyTheme()
        {
            // 应用视觉层次
            ChineseClassicalTheme.ApplyVisualHierarchy(this, HierarchyLevel);

            // 递归应用主题到子控件
            ApplyThemeToChildren();
        }

        /// <summary>
        /// 递归应用主题到子控件
        /// </summary>
        private void ApplyThemeToChildren()
        {
            for (int i = 0; i < ChildrenCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                {
                    ApplyThemeToControl(child);
                }
            }
        }

        /// <summary>
        /// 为单个控件应用主题
        /// </summary>
        private void ApplyThemeToControl(Control control)
        {
            switch (control)
            {
                case Button button:
                    if (button.BackgroundColor == Color.Transparent || button.BackgroundColor == Color.Gray)
                    {
                        ChineseClassicalTheme.ApplyVisualHierarchy(button, VisualHierarchy.Secondary);
                    }
                    break;

                case TextBox textBox:
                    textBox.BackgroundColor = ChineseClassicalTheme.InputColor;
                    textBox.TextColor = ChineseClassicalTheme.TextColor;
                    ChineseClassicalTheme.ApplyVisualHierarchy(textBox, VisualHierarchy.Tertiary);
                    break;

                case Label label:
                    if (label.TextColor == Color.White || label.TextColor == Color.Gray)
                    {
                        label.TextColor = ChineseClassicalTheme.TextColor;
                        ChineseClassicalTheme.ApplyVisualHierarchy(label, VisualHierarchy.Auxiliary);
                    }
                    break;

                case Panel panel:
                    if (panel.BackgroundColor == Color.Transparent)
                    {
                        // 保持透明背景
                    }
                    else
                    {
                        panel.BackgroundColor = ChineseClassicalTheme.PanelColor;
                        ChineseClassicalTheme.ApplyChineseBorder(panel, ChineseBorderStyle.Elegant);
                    }
                    break;
            }
        }

        #endregion

        #region 布局计算方法

        /// <summary>
        /// 计算并应用尺寸
        /// </summary>
        protected virtual void CalculateAndApplySize()
        {
            var scaleFactor = ResponsiveLayoutCalculator.CalculateScaleFactor(DesignSize);
            var newSize = new Float2(Size.X * scaleFactor, Size.Y * scaleFactor);

            // 应用尺寸限制
            newSize = ResponsiveLayoutCalculator.EnsureSafeSize(newSize, MaxSize);
            newSize = new Float2(
                Math.Max(newSize.X, MinSize.X),
                Math.Max(newSize.Y, MinSize.Y)
            );

            Size = newSize;
        }

        /// <summary>
        /// 计算并应用位置
        /// </summary>
        protected virtual void CalculateAndApplyPosition()
        {
            if (AutoCenter)
            {
                Location = ResponsiveLayoutCalculator.IsUltraWideScreen()
                    ? ResponsiveLayoutCalculator.CalculateUltraWideCenterPosition(Size)
                    : ResponsiveLayoutCalculator.CalculateCenterPosition(Size);
            }
        }

        /// <summary>
        /// 检查并更新布局
        /// </summary>
        private void CheckAndUpdateLayout()
        {
            // 每秒检查一次屏幕尺寸变化
            if (Time.TimeSinceStartup - _lastLayoutCheckTime > 1.0f)
            {
                _lastLayoutCheckTime = Time.TimeSinceStartup;
                ApplyInitialLayout();
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置响应式属性
        /// </summary>
        /// <param name="designSize">设计时尺寸</param>
        /// <param name="minSize">最小尺寸</param>
        /// <param name="maxSize">最大尺寸</param>
        /// <param name="autoCenter">是否自动居中</param>
        public void SetResponsiveProperties(Float2? designSize = null, Float2? minSize = null, Float2? maxSize = null, bool? autoCenter = null)
        {
            if (designSize.HasValue) DesignSize = designSize.Value;
            if (minSize.HasValue) MinSize = minSize.Value;
            if (maxSize.HasValue) MaxSize = maxSize.Value;
            if (autoCenter.HasValue) AutoCenter = autoCenter.Value;

            ApplyInitialLayout();
        }

        /// <summary>
        /// 设置视觉层次
        /// </summary>
        /// <param name="hierarchy">层次等级</param>
        public void SetVisualHierarchy(VisualHierarchy hierarchy)
        {
            HierarchyLevel = hierarchy;
            ChineseClassicalTheme.ApplyVisualHierarchy(this, hierarchy);
        }

        /// <summary>
        /// 设置边框样式
        /// </summary>
        /// <param name="borderStyle">边框样式</param>
        public void SetBorderStyle(ChineseBorderStyle borderStyle)
        {
            BorderStyle = borderStyle;
            ChineseClassicalTheme.ApplyChineseBorder(this, borderStyle);
        }

        /// <summary>
        /// 强制刷新布局
        /// </summary>
        public void RefreshLayout()
        {
            ApplyInitialLayout();
        }

        /// <summary>
        /// 获取当前缩放因子
        /// </summary>
        /// <returns>缩放因子</returns>
        public float GetScaleFactor()
        {
            return ResponsiveLayoutCalculator.CalculateScaleFactor(DesignSize);
        }

        /// <summary>
        /// 获取适配后的尺寸
        /// </summary>
        /// <param name="originalSize">原始尺寸</param>
        /// <returns>适配后的尺寸</returns>
        public Float2 GetAdaptedSize(Float2 originalSize)
        {
            var scaleFactor = GetScaleFactor();
            return new Float2(originalSize.X * scaleFactor, originalSize.Y * scaleFactor);
        }

        #endregion

        #region 清理方法

        /// <summary>
        /// 引擎退出事件处理
        /// </summary>
        private void OnEngineExit()
        {
            Engine.RequestingExit -= OnEngineExit;
        }

        /// <summary>
        /// 组件销毁
        /// </summary>
        public override void OnDestroy()
        {
            Engine.RequestingExit -= OnEngineExit;
            base.OnDestroy();
        }

        #endregion
    }

    /// <summary>
    /// 响应式面板组件
    /// 继承自ResponsiveComponent的具体实现
    /// </summary>
    public class ResponsivePanel : ResponsiveComponent
    {
        protected override void InitializeContent()
        {
            // 面板默认不需要额外内容初始化
        }

        protected override void ApplyResponsiveLayout()
        {
            // 面板的响应式布局主要是尺寸和位置的调整
            // 这些已在基类中处理
        }

        /// <summary>
        /// 创建响应式面板
        /// </summary>
        /// <param name="size">尺寸</param>
        /// <param name="autoCenter">是否自动居中</param>
        /// <param name="hierarchy">视觉层次</param>
        /// <returns>响应式面板实例</returns>
        public static ResponsivePanel Create(Float2 size, bool autoCenter = false, VisualHierarchy hierarchy = VisualHierarchy.Secondary)
        {
            var panel = new ResponsivePanel
            {
                Size = size,
                AutoCenter = autoCenter,
                HierarchyLevel = hierarchy
            };

            return panel;
        }
    }
}