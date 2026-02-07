using FlaxEngine;
using HundunWorld.Game.Rendering;
using HundunWorld.Game.Rendering.Materials;
using HundunWorld.Game.Rendering.Lighting;
using HundunWorld.Game.Rendering.PostProcess;

namespace HundunWorld.Game.Rendering.Scenes
{
    /// <summary>
    /// 角色展示场景控制器
    /// 用于设置和管理高质量角色渲染展示场景
    /// </summary>
    public class CharacterShowcaseController : Script
    {
        #region 场景设置

        /// <summary>
        /// 展示模式
        /// </summary>
        public enum ShowcaseMode
        {
            /// <summary>角色旋转展示</summary>
            Turntable,
            /// <summary>固定摄像机</summary>
            Static,
            /// <summary>面部特写</summary>
            FaceCloseUp,
            /// <summary>全身展示</summary>
            FullBody,
            /// <summary>自由摄像机</summary>
            FreeCamera
        }

        /// <summary>
        /// 当前展示模式
        /// </summary>
        [Header("展示设置")]
        [Tooltip("当前的展示模式")]
        public ShowcaseMode CurrentMode = ShowcaseMode.Turntable;

        /// <summary>
        /// 角色渲染器
        /// </summary>
        [Tooltip("MetaHuman角色渲染器")]
        public MetaHumanCharacterRenderer CharacterRenderer;

        /// <summary>
        /// 展示平台
        /// </summary>
        [Tooltip("角色站立的展示平台")]
        public Actor TurntablePlatform;

        /// <summary>
        /// 旋转速度
        /// </summary>
        [Range(0f, 60f)]
        [Tooltip("转盘旋转速度（度/秒）")]
        public float TurntableSpeed = 10f;

        /// <summary>
        /// 摄像机
        /// </summary>
        [Tooltip("展示用摄像机")]
        public Camera ShowcaseCamera;

        #endregion

        #region 光照配置

        /// <summary>
        /// 光照系统
        /// </summary>
        [Header("光照")]
        [Tooltip("角色光照系统")]
        public CharacterLightingSystem LightingSystem;

        /// <summary>
        /// 背景颜色
        /// </summary>
        [Tooltip("场景背景颜色")]
        public Color BackgroundColor = new Color(0.1f, 0.1f, 0.12f);

        /// <summary>
        /// 使用渐变背景
        /// </summary>
        [Tooltip("是否使用渐变背景")]
        public bool UseGradientBackground = true;

        /// <summary>
        /// 渐变顶部颜色
        /// </summary>
        [Tooltip("渐变背景顶部颜色")]
        public Color GradientTopColor = new Color(0.15f, 0.15f, 0.18f);

        /// <summary>
        /// 渐变底部颜色
        /// </summary>
        [Tooltip("渐变背景底部颜色")]
        public Color GradientBottomColor = new Color(0.05f, 0.05f, 0.06f);

        #endregion

        #region 后期处理

        /// <summary>
        /// 后期处理系统
        /// </summary>
        [Header("后期处理")]
        [Tooltip("后期处理系统")]
        public CinematicPostProcessSystem PostProcessSystem;

        #endregion

        #region 摄像机设置

        /// <summary>
        /// 面部特写距离
        /// </summary>
        [Header("摄像机参数")]
        [Range(0.5f, 3f)]
        [Tooltip("面部特写模式的摄像机距离")]
        public float CloseUpDistance = 1.0f;

        /// <summary>
        /// 全身距离
        /// </summary>
        [Range(2f, 10f)]
        [Tooltip("全身展示模式的摄像机距离")]
        public float FullBodyDistance = 4.0f;

        /// <summary>
        /// 摄像机高度偏移
        /// </summary>
        [Range(-1f, 2f)]
        [Tooltip("摄像机相对角色的高度偏移")]
        public float CameraHeightOffset = 0f;

        /// <summary>
        /// 摄像机FOV
        /// </summary>
        [Range(20f, 90f)]
        [Tooltip("摄像机视场角")]
        public float CameraFOV = 50f;

        #endregion

        #region 交互设置

        /// <summary>
        /// 启用鼠标旋转
        /// </summary>
        [Header("交互")]
        [Tooltip("是否允许鼠标拖拽旋转角色")]
        public bool EnableMouseRotation = true;

        /// <summary>
        /// 启用滚轮缩放
        /// </summary>
        [Tooltip("是否允许滚轮缩放")]
        public bool EnableMouseZoom = true;

        /// <summary>
        /// 旋转灵敏度
        /// </summary>
        [Range(0.1f, 5f)]
        [Tooltip("鼠标旋转灵敏度")]
        public float RotationSensitivity = 1f;

        /// <summary>
        /// 缩放灵敏度
        /// </summary>
        [Range(0.1f, 5f)]
        [Tooltip("滚轮缩放灵敏度")]
        public float ZoomSensitivity = 1f;

        /// <summary>
        /// 最小缩放距离
        /// </summary>
        [Range(0.5f, 5f)]
        [Tooltip("摄像机最近距离")]
        public float MinZoomDistance = 0.8f;

        /// <summary>
        /// 最大缩放距离
        /// </summary>
        [Range(5f, 20f)]
        [Tooltip("摄像机最远距离")]
        public float MaxZoomDistance = 10f;

        #endregion

        private float _currentRotation = 0f;
        private float _currentDistance = 3f;
        private float _currentHeight = 0f;
        private bool _isDragging = false;
        private Vector2 _lastMousePosition;

        public override void OnStart()
        {
            InitializeShowcase();
            ApplyShowcaseMode();
        }

        public override void OnUpdate()
        {
            switch (CurrentMode)
            {
                case ShowcaseMode.Turntable:
                    UpdateTurntable();
                    break;
                case ShowcaseMode.FreeCamera:
                    UpdateFreeCamera();
                    break;
            }

            HandleInput();
            UpdateCamera();
        }

        /// <summary>
        /// 初始化展示场景
        /// </summary>
        private void InitializeShowcase()
        {
            // 设置角色渲染器为展示模式
            if (CharacterRenderer != null)
            {
                CharacterRenderer.RenderMode = MetaHumanCharacterRenderer.CharacterRenderMode.Showcase;
                CharacterRenderer.ApplyRenderMode();
            }

            // 设置光照
            if (LightingSystem != null)
            {
                LightingSystem.CurrentScheme = CharacterLightingSystem.LightingScheme.ThreePoint;
                LightingSystem.CurrentMood = CharacterLightingSystem.LightingMood.Neutral;
                LightingSystem.ApplyLightingScheme();
            }

            // 设置后期处理
            if (PostProcessSystem != null)
            {
                PostProcessSystem.CurrentStyle = CinematicPostProcessSystem.VisualStyle.Cinematic;
                PostProcessSystem.ApplyVisualStyle();
            }

            // 初始化摄像机参数
            _currentDistance = FullBodyDistance;
            _currentHeight = CameraHeightOffset;

            if (ShowcaseCamera != null)
            {
                ShowcaseCamera.FieldOfView = CameraFOV;
            }
        }

        /// <summary>
        /// 应用展示模式
        /// </summary>
        public void ApplyShowcaseMode()
        {
            switch (CurrentMode)
            {
                case ShowcaseMode.Turntable:
                    _currentDistance = FullBodyDistance;
                    break;
                    
                case ShowcaseMode.Static:
                    _currentDistance = FullBodyDistance;
                    break;
                    
                case ShowcaseMode.FaceCloseUp:
                    _currentDistance = CloseUpDistance;
                    _currentHeight = 1.6f; // 面部高度
                    if (PostProcessSystem != null)
                    {
                        PostProcessSystem.SetCloseUpMode();
                    }
                    break;
                    
                case ShowcaseMode.FullBody:
                    _currentDistance = FullBodyDistance;
                    _currentHeight = 0.9f;
                    if (PostProcessSystem != null)
                    {
                        PostProcessSystem.SetFullBodyMode();
                    }
                    break;
                    
                case ShowcaseMode.FreeCamera:
                    // 保持当前设置
                    break;
            }
        }

        /// <summary>
        /// 更新转盘旋转
        /// </summary>
        private void UpdateTurntable()
        {
            if (TurntablePlatform != null && !_isDragging)
            {
                _currentRotation += TurntableSpeed * Time.DeltaTime;
                if (_currentRotation > 360f) _currentRotation -= 360f;
                
                TurntablePlatform.LocalOrientation = Quaternion.Euler(0, _currentRotation, 0);
            }
        }

        /// <summary>
        /// 更新自由摄像机
        /// </summary>
        private void UpdateFreeCamera()
        {
            // 自由摄像机模式下的额外逻辑
        }

        /// <summary>
        /// 处理输入
        /// </summary>
        private void HandleInput()
        {
            // 鼠标旋转
            if (EnableMouseRotation)
            {
                if (Input.GetMouseButtonDown(MouseButton.Left))
                {
                    _isDragging = true;
                    _lastMousePosition = Input.MousePosition;
                }
                else if (Input.GetMouseButtonUp(MouseButton.Left))
                {
                    _isDragging = false;
                }

                if (_isDragging)
                {
                    Vector2 delta = Input.MousePosition - _lastMousePosition;
                    _currentRotation += delta.X * RotationSensitivity * 0.5f;
                    _currentHeight -= delta.Y * RotationSensitivity * 0.01f;
                    _currentHeight = Mathf.Clamp(_currentHeight, -0.5f, 2.5f);
                    
                    _lastMousePosition = Input.MousePosition;
                    
                    if (TurntablePlatform != null)
                    {
                        TurntablePlatform.LocalOrientation = Quaternion.Euler(0, _currentRotation, 0);
                    }
                }
            }

            // 滚轮缩放
            if (EnableMouseZoom)
            {
                float scroll = Input.MouseScrollDelta;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _currentDistance -= scroll * ZoomSensitivity;
                    _currentDistance = Mathf.Clamp(_currentDistance, MinZoomDistance, MaxZoomDistance);
                }
            }

            // 快捷键切换模式
            if (Input.GetKeyDown(KeyboardKeys.Alpha1))
            {
                CurrentMode = ShowcaseMode.FullBody;
                ApplyShowcaseMode();
            }
            else if (Input.GetKeyDown(KeyboardKeys.Alpha2))
            {
                CurrentMode = ShowcaseMode.FaceCloseUp;
                ApplyShowcaseMode();
            }
            else if (Input.GetKeyDown(KeyboardKeys.Alpha3))
            {
                CurrentMode = ShowcaseMode.Turntable;
                ApplyShowcaseMode();
            }
        }

        /// <summary>
        /// 更新摄像机位置
        /// </summary>
        private void UpdateCamera()
        {
            if (ShowcaseCamera == null || CharacterRenderer?.CharacterActor == null) return;

            var characterPos = CharacterRenderer.CharacterActor.Position;
            var targetPos = characterPos + new Vector3(0, _currentHeight, 0);
            
            // 计算摄像机位置
            var cameraOffset = new Vector3(
                Mathf.Sin(Mathf.DegreesToRadians * _currentRotation) * _currentDistance,
                _currentHeight,
                Mathf.Cos(Mathf.DegreesToRadians * _currentRotation) * _currentDistance
            );

            // 如果是转盘模式，摄像机固定，角色旋转
            if (CurrentMode == ShowcaseMode.Turntable || CurrentMode == ShowcaseMode.Static)
            {
                cameraOffset = new Vector3(0, _currentHeight, _currentDistance);
            }

            var cameraPos = characterPos + cameraOffset;
            
            // 平滑过渡
            ShowcaseCamera.Position = Vector3.Lerp(
                ShowcaseCamera.Position, 
                cameraPos, 
                Time.DeltaTime * 5f);
            
            // 看向角色
            var lookDir = (targetPos - ShowcaseCamera.Position).Normalized;
            var targetRotation = Quaternion.LookRotation(lookDir, Vector3.Up);
            ShowcaseCamera.Orientation = Quaternion.Lerp(
                ShowcaseCamera.Orientation, 
                targetRotation, 
                Time.DeltaTime * 5f);
        }

        /// <summary>
        /// 切换到下一个光照方案
        /// </summary>
        public void CycleLightingScheme()
        {
            if (LightingSystem == null) return;

            var schemes = System.Enum.GetValues(typeof(CharacterLightingSystem.LightingScheme));
            int currentIndex = (int)LightingSystem.CurrentScheme;
            currentIndex = (currentIndex + 1) % (schemes.Length - 1); // 排除Custom
            
            LightingSystem.CurrentScheme = (CharacterLightingSystem.LightingScheme)currentIndex;
            LightingSystem.ApplyLightingScheme();
        }

        /// <summary>
        /// 切换到下一个视觉风格
        /// </summary>
        public void CycleVisualStyle()
        {
            if (PostProcessSystem == null) return;

            var styles = System.Enum.GetValues(typeof(CinematicPostProcessSystem.VisualStyle));
            int currentIndex = (int)PostProcessSystem.CurrentStyle;
            currentIndex = (currentIndex + 1) % (styles.Length - 1); // 排除Custom
            
            PostProcessSystem.CurrentStyle = (CinematicPostProcessSystem.VisualStyle)currentIndex;
            PostProcessSystem.ApplyVisualStyle();
        }

        /// <summary>
        /// 重置展示
        /// </summary>
        public void ResetShowcase()
        {
            _currentRotation = 0f;
            _currentDistance = FullBodyDistance;
            _currentHeight = CameraHeightOffset;
            CurrentMode = ShowcaseMode.Turntable;
            ApplyShowcaseMode();
        }

        /// <summary>
        /// 截图
        /// </summary>
        public void TakeScreenshot()
        {
            string filename = $"Character_Showcase_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = System.IO.Path.Combine(Globals.ProjectFolder, "Screenshots", filename);
            
            // 确保目录存在
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            
            // FlaxEngine截图
            Screenshot.Capture(path);
            Debug.Log($"Screenshot saved: {path}");
        }
    }
}
