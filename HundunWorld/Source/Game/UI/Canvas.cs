using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// UI画布管理器 - 使用组合模式包装UICanvas
    /// </summary>
    public class Canvas : Script
    {
        private UICanvas _uiCanvas;
        
        /// <summary>
        /// 画布渲染模式
        /// </summary>
        public enum RenderMode
        {
            ScreenSpaceOverlay,
            ScreenSpaceCamera,
            WorldSpace
        }

        private RenderMode _renderMode = RenderMode.ScreenSpaceOverlay;
        private Camera _renderCamera;

        public RenderMode Mode
        {
            get => _renderMode;
            set
            {
                _renderMode = value;
                if (_uiCanvas != null)
                {
                    UpdateRenderMode();
                }
            }
        }

        public Camera RenderCamera
        {
            get => _renderCamera;
            set
            {
                _renderCamera = value;
                if (_renderMode == RenderMode.ScreenSpaceCamera)
                {
                    UpdateRenderMode();
                }
            }
        }

        public override void OnStart()
        {
            base.OnStart();
            
            // 创建UICanvas组件
            _uiCanvas = Actor.As<UICanvas>();
            if (_uiCanvas == null)
            {
                _uiCanvas = new UICanvas();
                _uiCanvas = Actor.AddChild<UICanvas>();
            }
            UpdateRenderMode();
        }

        private void UpdateRenderMode()
        {
            if (_uiCanvas == null) return;
            
            switch (_renderMode)
            {
                case RenderMode.ScreenSpaceOverlay:
                    _uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
                    break;
                case RenderMode.ScreenSpaceCamera:
                    _uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace; // 使用ScreenSpace作为替代
                    if (_renderCamera != null)
                    {
                        // 关联到指定相机
                    }
                    break;
                case RenderMode.WorldSpace:
                    _uiCanvas.RenderMode = CanvasRenderMode.WorldSpace;
                    break;
            }
        }

        /// <summary>
        /// 添加UI元素到画布
        /// </summary>
        public void AddUIElement(UIElement element)
        {
            if (element != null && _uiCanvas != null)
            {
                _uiCanvas.GUI.AddChild(element);
                element.ParentCanvas = this;
            }
        }

        /// <summary>
        /// 从画布移除UI元素
        /// </summary>
        public void RemoveUIElement(UIElement element)
        {
            if (element != null && _uiCanvas != null)
            {
                _uiCanvas.GUI.RemoveChild(element);
                element.ParentCanvas = null;
            }
        }
    }

    /// <summary>
    /// UI元素基类 - 继承自Flax引擎的ContainerControl
    /// </summary>
    public class UIElement : ContainerControl
    {
        public Canvas ParentCanvas { get; internal set; }
        public Vector2 AnchorsMin { get; set; } = Vector2.Zero;
        public Vector2 AnchorsMax { get; set; } = Vector2.One;
        public Vector2 OffsetMin { get; set; }
        public Vector2 OffsetMax { get; set; }

        public UIElement(float x, float y, float width, float height) : base(x, y, width, height)
        {
            // 构造函数
        }
    }
}