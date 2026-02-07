using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 场景切换过渡效果组件
    /// 提供淡入淡出效果，减少场景切换的跳跃感
    /// </summary>
    public class SceneTransitionEffect : Script
    {
        #region 单例

        private static SceneTransitionEffect _instance;
        public static SceneTransitionEffect Instance => _instance;

        #endregion

        #region 配置

        /// <summary>淡出持续时间（秒）</summary>
        [Serialize]
        public float FadeOutDuration = 0.3f;

        /// <summary>淡入持续时间（秒）</summary>
        [Serialize]
        public float FadeInDuration = 0.3f;

        /// <summary>遮罩颜色</summary>
        [Serialize]
        public Color MaskColor = Color.Black;

        /// <summary>是否在加载时显示提示</summary>
        [Serialize]
        public bool ShowLoadingText = true;

        /// <summary>加载提示文本</summary>
        [Serialize]
        public string LoadingText = "加载中...";

        #endregion

        #region 状态

        private Panel _overlayPanel;
        private Label _loadingLabel;
        private UICanvas _canvas;
        private float _currentAlpha;
        private float _targetAlpha;
        private float _fadeSpeed;
        private bool _isFading;
        private Action _onFadeComplete;
        private TransitionPhase _phase = TransitionPhase.None;

        public enum TransitionPhase
        {
            None,
            FadingOut,
            Loading,
            FadingIn
        }

        public bool IsBusy => _phase != TransitionPhase.None;

        #endregion

        #region 事件

        /// <summary>淡出完成</summary>
        public event Action FadeOutCompleted;

        /// <summary>淡入完成</summary>
        public event Action FadeInCompleted;

        #endregion

        #region 生命周期

        public override void OnAwake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            Actor.SetStaticFlag(StaticFlags.FullyStatic, true);

            InitializeUI();
            Debug.Log("[SceneTransitionEffect] 初始化完成");
        }

        public override void OnDestroy()
        {
            CleanupUI();

            if (_instance == this)
                _instance = null;
        }

        public override void OnUpdate()
        {
            if (!_isFading)
                return;

            // 计算当前帧的alpha变化
            float delta = _fadeSpeed * Time.DeltaTime;

            if (_targetAlpha > _currentAlpha)
            {
                _currentAlpha = Math.Min(_currentAlpha + delta, _targetAlpha);
            }
            else
            {
                _currentAlpha = Math.Max(_currentAlpha - delta, _targetAlpha);
            }

            // 更新遮罩透明度
            UpdateOverlayAlpha(_currentAlpha);

            // 检查是否完成
            if (Math.Abs(_currentAlpha - _targetAlpha) < 0.01f)
            {
                _currentAlpha = _targetAlpha;
                UpdateOverlayAlpha(_currentAlpha);
                _isFading = false;

                var callback = _onFadeComplete;
                _onFadeComplete = null;
                callback?.Invoke();
            }
        }

        #endregion

        #region 初始化

        private void InitializeUI()
        {
            // 查找或创建 UICanvas
            _canvas = FindOrCreateCanvas();
            if (_canvas == null)
            {
                Debug.LogError("[SceneTransitionEffect] 无法创建UICanvas");
                return;
            }

            // 创建遮罩面板
            _overlayPanel = new Panel
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = new Color(MaskColor.R, MaskColor.G, MaskColor.B, 0),
                Visible = false
            };

            // 创建加载文本
            if (ShowLoadingText)
            {
                _loadingLabel = new Label
                {
                    Text = LoadingText,
                    TextColor = Color.White,
                    TextColorHighlighted = Color.White,
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    AnchorPreset = AnchorPresets.MiddleCenter,
                    Size = new Float2(300, 50),
                    Location = new Float2(-150, -25),
                    Visible = false
                };
                _overlayPanel.AddChild(_loadingLabel);
            }

            _canvas.GUI.AddChild(_overlayPanel);

            // 确保遮罩在最上层
            _overlayPanel.IndexInParent = _canvas.GUI.ChildrenCount - 1;
        }

        private UICanvas FindOrCreateCanvas()
        {
            // 查找现有的 UICanvas（按名称）
            var existingActor = Level.FindActor("TransitionCanvas");
            if (existingActor != null && existingActor is UICanvas existingCanvas)
            {
                return existingCanvas;
            }

            // 从场景中查找任意 UICanvas
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null)
                {
                    var canvases = scene.GetChildren<UICanvas>();
                    if (canvases != null && canvases.Length > 0)
                    {
                        return canvases[0];
                    }
                }
            }

            // 创建新的 UICanvas（使用 UIHelper 的方式）
            var actor = new EmptyActor { Name = "TransitionCanvasParent" };
            actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            Level.SpawnActor(actor);

            var newCanvas = actor.AddChild<UICanvas>();
            newCanvas.Name = "TransitionCanvas";
            newCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
            newCanvas.Order = 1000; // 确保在最顶层
            newCanvas.IgnoreDepth = true;
            newCanvas.ReceivesEvents = false;

            return newCanvas;
        }

        private void CleanupUI()
        {
            if (_overlayPanel != null)
            {
                _overlayPanel.Parent?.RemoveChild(_overlayPanel);
                _overlayPanel.Dispose();
                _overlayPanel = null;
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 开始淡出（屏幕变黑）
        /// </summary>
        public void StartFadeOut(Action onComplete = null)
        {
            if (_overlayPanel == null)
            {
                Debug.LogWarning("[SceneTransitionEffect] 遮罩面板未初始化");
                onComplete?.Invoke();
                return;
            }

            Debug.Log("[SceneTransitionEffect] 开始淡出");
            _phase = TransitionPhase.FadingOut;
            _overlayPanel.Visible = true;
            _currentAlpha = 0f;
            _targetAlpha = 1f;
            _fadeSpeed = 1f / FadeOutDuration;
            _isFading = true;
            _onFadeComplete = () =>
            {
                _phase = TransitionPhase.Loading;
                if (_loadingLabel != null)
                    _loadingLabel.Visible = ShowLoadingText;
                FadeOutCompleted?.Invoke();
                onComplete?.Invoke();
            };
        }

        /// <summary>
        /// 开始淡入（屏幕恢复）
        /// </summary>
        public void StartFadeIn(Action onComplete = null)
        {
            if (_overlayPanel == null)
            {
                Debug.LogWarning("[SceneTransitionEffect] 遮罩面板未初始化");
                onComplete?.Invoke();
                return;
            }

            Debug.Log("[SceneTransitionEffect] 开始淡入");
            _phase = TransitionPhase.FadingIn;
            if (_loadingLabel != null)
                _loadingLabel.Visible = false;

            _currentAlpha = 1f;
            _targetAlpha = 0f;
            _fadeSpeed = 1f / FadeInDuration;
            _isFading = true;
            _onFadeComplete = () =>
            {
                _phase = TransitionPhase.None;
                _overlayPanel.Visible = false;
                FadeInCompleted?.Invoke();
                onComplete?.Invoke();
            };
        }

        /// <summary>
        /// 执行完整的过渡效果（淡出 -> 执行操作 -> 淡入）
        /// </summary>
        public void DoTransition(Action duringTransition, Action onComplete = null)
        {
            StartFadeOut(() =>
            {
                duringTransition?.Invoke();

                // 延迟一帧后开始淡入，确保场景加载完成
                Scripting.InvokeOnUpdate(() =>
                {
                    StartFadeIn(onComplete);
                });
            });
        }

        /// <summary>
        /// 立即隐藏遮罩
        /// </summary>
        public void HideImmediate()
        {
            _isFading = false;
            _phase = TransitionPhase.None;
            if (_overlayPanel != null)
            {
                _overlayPanel.Visible = false;
                UpdateOverlayAlpha(0);
            }
            if (_loadingLabel != null)
                _loadingLabel.Visible = false;
        }

        /// <summary>
        /// 设置加载提示文本
        /// </summary>
        public void SetLoadingText(string text)
        {
            LoadingText = text;
            if (_loadingLabel != null)
                _loadingLabel.Text = text;
        }

        #endregion

        #region 私有方法

        private void UpdateOverlayAlpha(float alpha)
        {
            if (_overlayPanel != null)
            {
                _overlayPanel.BackgroundColor = new Color(MaskColor.R, MaskColor.G, MaskColor.B, alpha);
            }
        }

        #endregion

        #region 静态辅助

        /// <summary>
        /// 获取或创建实例
        /// </summary>
        public static SceneTransitionEffect GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            // 从场景中查找
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null)
                {
                    var scripts = scene.GetScripts<SceneTransitionEffect>();
                    if (scripts != null && scripts.Length > 0)
                    {
                        _instance = scripts[0];
                        return _instance;
                    }
                }
            }

            // 创建新实例
            var actor = new EmptyActor { Name = "SceneTransitionEffect" };
            actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            Level.SpawnActor(actor);
            _instance = actor.AddScript<SceneTransitionEffect>();

            return _instance;
        }

        #endregion
    }
}
