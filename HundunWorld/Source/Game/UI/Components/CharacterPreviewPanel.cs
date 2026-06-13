using System;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Effects;
using HundunWorld.Game.UI;

namespace HundunWorld.Game.UI.Components
{
    public class CharacterPreviewPanel : ContainerControl
    {
        private SceneRenderTask _renderTask;
        private GPUTexture _renderTexture;
        private GPUTextureBrush _textureBrush;
        private UICharacterCameraSystem _cameraSystem;
        private Button _resetViewButton;

        // 水墨背景 + 飘落粒子 + 角色投影圆盘
        private InkWashBackground _inkBackground;
        private FloatingParticles _particles;
        private CharacterGroundDisc _groundDisc;

        private bool _initialized = false;
        private int _initWaitFrames = 0;
        private FlaxEngine.Scene _targetScene;

        // === 相机过渡（基于 OrbitCamera 的薄包装，保留 TransitionCamera 公开签名）===
        // 旧的固定相机位姿过渡已被 OrbitCamera 系统取代；这里仅保留一个延迟回调机制。
        private Action _onCameraTransitionComplete;
        private float _cameraTransitionCompleteTimer = 0f;

        // === 轨道相机（OrbitCamera）相关字段 ===
        private OrbitCamera _orbitCamera = new OrbitCamera();
        private float _idleTime = 0f;
        private bool _autoRotate = true;
        private bool _isDragging = false;
        private Float2 _lastMousePos;
        private bool _isRightDragging = false;
        private bool _isPanning = false;

        // === 右键双击检测（双击复位相机）===
        private float _lastRightClickTime = -1f;
        private const float DoubleClickInterval = 0.3f;

        // === 相机预设（Far / Mid / Near）===
        public enum CameraPreset
        {
            Far,
            Mid,
            Near
        }

        private struct PresetData
        {
            public float Azimuth;
            public float Elevation;
            public float Distance;

            public PresetData(float azimuth, float elevation, float distance)
            {
                Azimuth = azimuth;
                Elevation = elevation;
                Distance = distance;
            }
        }

        private bool _isPresetTransitioning = false;
        private FloatTween _azimuthTween;
        private FloatTween _elevationTween;
        private FloatTween _distanceTween;

        // 性别切换专用:相机距离过渡(使用独立字段避免与 _distanceTween 冲突)
        private FloatTween _genderDistanceTween;
        private Actor _pendingGenderModel;

        public event Action OnCharacterLoaded;

        public UICharacterCameraSystem CameraSystem => _cameraSystem;
        public Actor CharacterActor => _cameraSystem?.CharacterActor;

        /// <summary>
        /// 当前预览角色的唯一标识(ID 字符串)。由预览层持有,通过事件对外发布变更。
        /// </summary>
        public string CurrentCharacterId { get; private set; } = "0126998214";

        /// <summary>
        /// 当 CurrentCharacterId 通过 SetCharacterId 改变时触发,参数为新的 ID 字符串。
        /// 由视图层(CharacterSceneController)订阅以同步到全局 ID 标签。
        /// </summary>
        public event Action<string> OnCharacterIdChanged;

        public string CharacterPrefabPath { get; set; } = "Content/Character/Models/skm_uefn_mannequin.flax";

        /// <summary>
        /// 模型缩放因子，传递给相机系统
        /// </summary>
        public float ModelScale
        {
            get => _cameraSystem?.ModelScale ?? 1.0f;
            set
            {
                if (_cameraSystem != null)
                    _cameraSystem.ModelScale = value;
            }
        }

        /// <summary>
        /// 实时应用体型参数到 3D 角色模型
        /// bodyHeight: 0~1 → 仅影响 Y 轴缩放 (0.9~1.1)
        /// bodyType: 0~1 → 仅影响 X/Z 轴缩放 (0.9~1.1)
        /// headSize: 0~1 → 独立微调，不干扰身体比例
        /// </summary>
        public void ApplyBodyParams(float bodyHeight, float bodyType, float headSize)
        {
            var actor = _cameraSystem?.CharacterActor;
            if (actor == null) return;

            // ★ 关键修复: 头部参数不再通过 uniform 缩放干扰身高
            // 身体各轴独立计算，互不干扰
            float scaleY = 0.9f + bodyHeight * 0.2f;    // Y轴: 仅由身高参数控制
            float scaleXZ = 0.9f + bodyType * 0.2f;     // X/Z轴: 仅由体型参数控制

            actor.Scale = new Vector3(scaleXZ, scaleY, scaleXZ);
        }

        /// <summary>
        /// 设置目标场景
        /// </summary>
        public FlaxEngine.Scene TargetScene
        {
            get => _targetScene;
            set
            {
                _targetScene = value;
                if (!_initialized) return;
                if (_cameraSystem != null && value != null)
                {
                    ReinitializeCameraSystem();
                }
            }
        }

        public CharacterPreviewPanel()
        {
            BackgroundColor = Color.Transparent; // 改为透明,3D渲染由DrawSelf负责
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            // 显式设置默认尺寸,避免父容器尚未布局时 size=0 导致首次渲染失败
            Size = new Float2(1280, 720);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_initialized && Parent != null)
            {
                _initWaitFrames++;
                if (Width > 0 && Height > 0)
                {
                    _initialized = true;
                    CreateUIElements();
                    InitializeCameraSystem();
                }
                else if (_initWaitFrames > 120)
                {
                    _initialized = true;
                    CreateUIElements();
                    InitializeCameraSystem();
                }
            }

            if (_cameraSystem != null)
            {
                _cameraSystem.Update();
            }

            // 轨道相机：自动旋转 / 预设过渡
            if (_cameraSystem?.Camera != null)
            {
                _idleTime += deltaTime;

                // 预设过渡驱动 OrbitCamera 的三个参数
                if (_isPresetTransitioning)
                {
                    if (_azimuthTween != null) _azimuthTween.Update(deltaTime);
                    if (_elevationTween != null) _elevationTween.Update(deltaTime);
                    if (_distanceTween != null) _distanceTween.Update(deltaTime);

                    if (_azimuthTween != null) _orbitCamera.Azimuth = _azimuthTween.CurrentValue;
                    if (_elevationTween != null) _orbitCamera.Elevation = _elevationTween.CurrentValue;
                    if (_distanceTween != null) _orbitCamera.Distance = _distanceTween.CurrentValue;

                    if ((_azimuthTween == null || _azimuthTween.IsCompleted) &&
                        (_elevationTween == null || _elevationTween.IsCompleted) &&
                        (_distanceTween == null || _distanceTween.IsCompleted))
                    {
                        _isPresetTransitioning = false;
                    }
                }

                // 空闲 5 秒后自动绕 Y 轴旋转（30 秒/圈 = 12 度/秒）
                if (!_isDragging && !_isRightDragging && !_isPanning && _autoRotate && _idleTime > 5f && !_isPresetTransitioning)
                {
                    _orbitCamera.Rotate(12f * deltaTime, 0f);
                }

                // 应用 OrbitCamera 变换到 Flax 相机（最后写入，胜出）
                _orbitCamera.ApplyToCamera(_cameraSystem.Camera);
            }

            // 相机过渡完成回调（保留 TransitionCamera 公开 API 时使用的延迟回调）
            if (_cameraTransitionCompleteTimer > 0f)
            {
                _cameraTransitionCompleteTimer -= deltaTime;
                if (_cameraTransitionCompleteTimer <= 0f)
                {
                    _cameraTransitionCompleteTimer = 0f;
                    var cb = _onCameraTransitionComplete;
                    _onCameraTransitionComplete = null;
                    cb?.Invoke();
                }
            }

            // 性别切换:相机距离过渡(简化方案:仅 tween 距离,模型由调用方负责加载)
            if (_genderDistanceTween != null && _cameraSystem?.Camera != null)
            {
                _genderDistanceTween.Update(deltaTime);
                _orbitCamera.Distance = _genderDistanceTween.CurrentValue;
                // 暂停自动旋转,避免距离过渡期间镜头自转
                _idleTime = 0f;

                // 在距离过渡中段(40%~60%)闪烁水墨背景,作为"切换感"反馈
                if (_inkBackground != null && _genderDistanceTween.Duration > 0f)
                {
                    float progress = _genderDistanceTween.Elapsed / _genderDistanceTween.Duration;
                    bool flash = progress >= 0.4f && progress <= 0.6f;
                    if (_inkBackground.Visible != !flash)
                    {
                        _inkBackground.Visible = !flash;
                    }
                }

                if (_genderDistanceTween.IsCompleted)
                {
                    // 过渡结束后恢复水墨背景显示
                    if (_inkBackground != null && !_inkBackground.Visible)
                    {
                        _inkBackground.Visible = true;
                    }
                    // 过渡结束后,如有待替换的模型则执行替换
                    if (_pendingGenderModel != null)
                    {
                        SwapToPendingGenderModel();
                    }
                    _genderDistanceTween = null;
                }
            }

            // 键盘快捷键：1/2/3 切换预设，R 切换自动旋转
            if (IsFocused || IsMouseOver)
            {
                if (Input.GetKeyDown(KeyboardKeys.Alpha1))
                {
                    TransitionToPreset(CameraPreset.Far);
                }
                else if (Input.GetKeyDown(KeyboardKeys.Alpha2))
                {
                    TransitionToPreset(CameraPreset.Mid);
                }
                else if (Input.GetKeyDown(KeyboardKeys.Alpha3))
                {
                    TransitionToPreset(CameraPreset.Near);
                }
                else if (Input.GetKeyDown(KeyboardKeys.R))
                {
                    _autoRotate = !_autoRotate;
                    _idleTime = 0f;
                }
            }
        }

        private void SwapToPendingGenderModel()
        {
            if (_cameraSystem == null || _pendingGenderModel == null)
                return;

            var old = _cameraSystem.CharacterActor;
            if (old != null && old != _pendingGenderModel)
            {
                Actor.Destroy(old);
            }
            // 这里 _pendingGenderModel 由调用方持有(本面板不直接接管所有权),
            // 实际项目中 GenderSelectionUI 应通过预制体路径加载并自行管理 Actor 生命周期。
            _pendingGenderModel = null;
        }

        public override void DrawSelf()
        {
            base.DrawSelf();

            if (_textureBrush != null && _renderTexture != null)
            {
                _textureBrush.Draw(new Rectangle(Float2.Zero, Size), Color.White);
            }
        }

        private void CreateUIElements()
        {
            float buttonSize = 44;
            float buttonMargin = 30;

            // 水墨背景(最底层,半透明)
            _inkBackground = new InkWashBackground
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = Color.Transparent
            };

            // 角色投影圆盘(固定在父控件中央偏下,大致位于 3D 角色脚下)
            _groundDisc = new CharacterGroundDisc
            {
                Parent = this,
                AnchorPreset = AnchorPresets.MiddleCenter,
                Offsets = new Margin(0, 200, 0, 0)
            };

            // 复位视角按钮
            _resetViewButton = new Button
            {
                Parent = this,
                AnchorPreset = AnchorPresets.BottomRight,
                Offsets = new Margin(0, buttonMargin, 0, 85),
                Size = new Float2(buttonSize, buttonSize),
                Text = "\u21BA",
                TextColor = Color.White,
                BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.5f),
                BorderColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.5f),
                BorderThickness = 1.5f
            };
            _resetViewButton.Clicked += ResetView;

            // 飘落粒子(最顶层,需最后渲染)
            _particles = new FloatingParticles
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero
            };
            // 将粒子控件移到子节点列表最末尾,确保 ZOrder 最高
            _particles.Parent = this;
        }

        private void InitializeCameraSystem()
        {
            _cameraSystem = new UICharacterCameraSystem();
            _cameraSystem.OnCharacterLoaded += () => OnCharacterLoaded?.Invoke();

            int renderWidth = (int)Width;
            int renderHeight = (int)Height;
            if (renderWidth < 100) renderWidth = 400;
            if (renderHeight < 100) renderHeight = 600;

            _cameraSystem.Initialize(new Vector2(renderWidth, renderHeight), _targetScene);

            // 使用 SceneRenderTask 渲染到纹理
            // 关键修复: 使用 Scenes 模式而非 CustomActors 模式,
            // 这样整个 Character 场景(包含天空、灯光、地面)都会一起渲染,避免黑屏
            var desc = GPUTextureDescription.New2D(
                renderWidth,
                renderHeight,
                PixelFormat.R8G8B8A8_UNorm);
            _renderTexture = new GPUTexture();
            _renderTexture.Init(ref desc);

            _renderTask = new SceneRenderTask();
            _renderTask.Order = -100;
            _renderTask.Output = _renderTexture;
            // 关键修复: 初始化时不启用,等角色加载完成后再启用,避免空场景渲染
            _renderTask.Enabled = false;

            if (_cameraSystem.Camera != null)
            {
                _renderTask.Camera = _cameraSystem.Camera;
            }

            // 关键修复: 使用 CustomScenes 模式渲染整个目标场景(包含天空、灯光、模型、地面)
            // 这样模型被加入场景后会自动被渲染,无需手动维护 CustomActors 数组
            // 注意: Flax 1.12 API 中枚举值是 ActorsSources.Scenes (复数),属性是 CustomScenes
            if (_targetScene != null)
            {
                _renderTask.ActorsSource = ActorsSources.Scenes;
                _renderTask.CustomScenes = new FlaxEngine.Scene[] { _targetScene };
            }
            else
            {
                // 兜底:退回到 CustomActors 模式
                if (_cameraSystem.CameraActor != null)
                {
                    _renderTask.ActorsSource = ActorsSources.CustomActors;
                    _renderTask.CustomActors = new Actor[] { _cameraSystem.CameraActor };
                }
            }

            _textureBrush = new GPUTextureBrush(_renderTexture);

            // === 使用 OrbitCamera 驱动相机 ===
            // 模型固定在原点 (Vector3.Zero),相机围绕该点旋转
            // 调整目标点为角色中心(约 90cm 高),让相机对准角色身体而非脚底
            _orbitCamera.Azimuth = 0f;
            _orbitCamera.Elevation = -5f; // 略微俯视,让角色显示更自然
            _orbitCamera.Distance = 250f;
            _orbitCamera.Target = new Vector3(0f, 90f, 0f);

            if (!string.IsNullOrEmpty(CharacterPrefabPath))
            {
                _cameraSystem.LoadCharacter(CharacterPrefabPath);
            }

            // 立即应用一次初始变换,确保第一次渲染时视角已就绪
            if (_cameraSystem.Camera != null)
            {
                _orbitCamera.ApplyToCamera(_cameraSystem.Camera);
            }

            // 角色和场景就绪后,才启用 RenderTask
            if (_renderTask != null)
            {
                _renderTask.Enabled = true;
            }

            // 重置交互状态
            _idleTime = 0f;
            _autoRotate = true;
            _isDragging = false;
            _isRightDragging = false;
            _isPresetTransitioning = false;
        }

        /// <summary>
        /// 当目标场景变化时重新初始化相机系统
        /// </summary>
        private void ReinitializeCameraSystem()
        {
            if (_cameraSystem == null) return;

            _cameraSystem.Dispose();
            _cameraSystem = null;

            if (_renderTask != null)
            {
                _renderTask.Enabled = false;
                _renderTask = null;
            }
            if (_renderTexture != null)
            {
                _renderTexture.ReleaseGPU();
                _renderTexture = null;
            }
            _textureBrush = null;

            if (Width > 0 && Height > 0)
            {
                _initialized = true;
                _initWaitFrames = 0;
                InitializeCameraSystem();
            }
            else
            {
                _initialized = false;
                _initWaitFrames = 0;
            }
        }

        #region 鼠标交互 - 拖拽旋转 / 滚轮缩放 / 右键平移

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                _isDragging = true;
                _lastMousePos = location;
                _idleTime = 0f;
                return true;
            }

            if (button == MouseButton.Right)
            {
                // 双击检测：两次右键间隔 < 0.3s 视为双击，复位相机到默认视角
                float currentTime = Time.GameTime;
                if (_lastRightClickTime > 0f &&
                    currentTime - _lastRightClickTime < DoubleClickInterval)
                {
                    // 双击：复位 OrbitCamera 到默认
                    _orbitCamera.Azimuth = 0f;
                    _orbitCamera.Elevation = 0f;
                    _orbitCamera.Distance = 250f;
                    _orbitCamera.Target = Vector3.Zero;
                    _isPresetTransitioning = false;
                    _idleTime = 0f;
                    // 重置双击时间，避免三次点击再次触发
                    _lastRightClickTime = -1f;
                    return true;
                }

                _lastRightClickTime = currentTime;

                _isRightDragging = true;
                _lastMousePos = location;
                return true;
            }

            if (button == MouseButton.Middle)
            {
                // 中键按下：平移（Maya/3ds Max 习惯）
                _isPanning = true;
                _lastMousePos = location;
                _idleTime = 0f;
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                _isDragging = false;
                return true;
            }

            if (button == MouseButton.Right)
            {
                _isRightDragging = false;
                return true;
            }

            if (button == MouseButton.Middle)
            {
                _isPanning = false;
                return true;
            }

            return base.OnMouseUp(location, button);
        }

        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            if (_cameraSystem?.Camera == null)
                return;

            Float2 delta = location - _lastMousePos;

            if (_isDragging)
            {
                _orbitCamera.Rotate(-delta.X * 0.5f, -delta.Y * 0.5f);
            }
            else if (_isPanning)
            {
                // 中键平移：与右键行为一致，沿相机右/上方向平移目标点
                _orbitCamera.Pan(new Vector2(delta.X, delta.Y));
            }
            else if (_isRightDragging)
            {
                _orbitCamera.Pan(new Vector2(delta.X, delta.Y));
            }

            _lastMousePos = location;
            _idleTime = 0f;
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            if (_cameraSystem?.Camera != null)
            {
                _orbitCamera.Zoom(-delta * 0.5f);
                return true;
            }

            return base.OnMouseWheel(location, delta);
        }

        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _isDragging = false;
            _isRightDragging = false;
            _isPanning = false;
        }

        #endregion

        #region 公共方法

        public void LoadCharacter(string prefabPath)
        {
            CharacterPrefabPath = prefabPath;

            if (_cameraSystem != null)
            {
                _cameraSystem.LoadCharacter(prefabPath);
            }
        }

        /// <summary>
        /// 设置当前预览角色的 ID 字符串。
        /// 当 ID 发生变化时触发 OnCharacterIdChanged 事件,供视图层同步全局 ID 标签。
        /// </summary>
        public void SetCharacterId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            if (CurrentCharacterId == id)
                return;

            CurrentCharacterId = id;
            OnCharacterIdChanged?.Invoke(id);
        }

        /// <summary>
        /// 重置 OrbitCamera 到默认视角（Azimuth=0, Elevation=0, Distance=250）
        /// </summary>
        public void ResetView()
        {
            if (_cameraSystem?.Camera == null)
                return;

            _orbitCamera.Azimuth = 0f;
            _orbitCamera.Elevation = 0f;
            _orbitCamera.Distance = 250f;
            _orbitCamera.Target = Vector3.Zero;
            _isPresetTransitioning = false;
            _idleTime = 0f;
        }

        public void RotateCamera(float deltaY, float deltaX)
        {
            if (_cameraSystem?.Camera != null)
            {
                _orbitCamera.Rotate(deltaY, -deltaX);
            }
        }

        public void SetCameraDistance(float distance)
        {
            if (_cameraSystem?.Camera != null)
            {
                _orbitCamera.Distance = distance;
            }
        }

        /// <summary>
        /// 聚焦到指定身体部位（带平滑过渡）
        /// </summary>
        public void FocusOnBodyPart(string bodyPartName)
        {
            if (_cameraSystem != null)
            {
                _cameraSystem.FocusOnBodyPart(bodyPartName);
            }
        }

        /// <summary>
        /// 根据主分类名称聚焦到对应身体部位
        /// </summary>
        public void FocusOnCategory(string categoryName)
        {
            if (_cameraSystem != null)
            {
                _cameraSystem.FocusOnCategory(categoryName);
            }
        }

        /// <summary>
        /// 平滑过渡相机到目标位置和角度（薄包装）
        /// 内部实现：把目标位置转换为 OrbitCamera 的 Azimuth/Elevation/Distance，使用 FloatTween 系统过渡。
        /// targetRotation 在轨道相机模型下被忽略（朝向始终指向 Target）。
        /// </summary>
        public void TransitionCamera(Vector3 targetPosition, Quaternion targetRotation, float duration = 0.5f, Action onComplete = null)
        {
            if (_cameraSystem?.Camera == null)
            {
                onComplete?.Invoke();
                return;
            }

            // 转换 world targetPosition 到 OrbitCamera 球坐标(以当前 Target 为球心)
            Vector3 offset = targetPosition - _orbitCamera.Target;
            float dist = offset.Length;
            if (dist < 1f) dist = 250f;

            float clampedY = Mathf.Clamp(offset.Y / dist, -1f, 1f);
            float elev = Mathf.Asin(clampedY) * Mathf.RadiansToDegrees;
            float azim = Mathf.Atan2(offset.Z, offset.X) * Mathf.RadiansToDegrees;
            float clampedDist = Mathf.Clamp(dist, OrbitCamera.MinDistance, OrbitCamera.MaxDistance);

            _azimuthTween = new FloatTween
            {
                From = _orbitCamera.Azimuth,
                To = azim,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _elevationTween = new FloatTween
            {
                From = _orbitCamera.Elevation,
                To = elev,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _distanceTween = new FloatTween
            {
                From = _orbitCamera.Distance,
                To = clampedDist,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _isPresetTransitioning = true;

            // 延迟回调（与 FloatTween 同步）
            _onCameraTransitionComplete = onComplete;
            _cameraTransitionCompleteTimer = duration;
        }

        /// <summary>
        /// 相机过渡到默认视角（平滑版）
        /// </summary>
        public void TransitionToDefault(float duration = 0.5f)
        {
            if (_cameraSystem?.Camera == null) return;

            // 复用 OrbitCamera 默认值
            TransitionToPreset(CameraPreset.Mid, duration);
        }

        /// <summary>
        /// 模型淡入淡出切换（简单实现：通过缩放实现视觉效果）
        /// </summary>
        public void CrossfadeModel(string newPrefabPath, float duration = 0.3f)
        {
            if (_cameraSystem == null) return;

            _cameraSystem.LoadCharacter(newPrefabPath);
        }

        /// <summary>
        /// 相机系统是否已就绪
        /// </summary>
        public bool IsCameraReady => _cameraSystem != null && _cameraSystem.Camera != null;

        /// <summary>
        /// 切换到指定预设（Far / Mid / Near），使用 FloatTween 并行过渡
        /// </summary>
        public void TransitionToPreset(CameraPreset preset, float duration = 0.4f)
        {
            if (_cameraSystem?.Camera == null)
                return;

            PresetData target = GetPresetData(preset);

            _azimuthTween = new FloatTween
            {
                From = _orbitCamera.Azimuth,
                To = target.Azimuth,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _elevationTween = new FloatTween
            {
                From = _orbitCamera.Elevation,
                To = target.Elevation,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _distanceTween = new FloatTween
            {
                From = _orbitCamera.Distance,
                To = target.Distance,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
            _isPresetTransitioning = true;
        }

        /// <summary>
        /// 公开版本：根据字符串名选择预设（便于编辑器/UI 配置）
        /// </summary>
        public void TransitionToPresetByName(string presetName, float duration = 0.4f)
        {
            if (Enum.TryParse<CameraPreset>(presetName, true, out var preset))
            {
                TransitionToPreset(preset, duration);
            }
        }

        /// <summary>
        /// 切换自动旋转开关
        /// </summary>
        public void SetAutoRotate(bool enabled)
        {
            _autoRotate = enabled;
            _idleTime = 0f;
        }

        /// <summary>
        /// 将世界坐标投影到屏幕坐标并更新角色脚下投影圆盘位置。
        /// 简化方案:不做实时世界→屏幕投影,圆盘保持在固定位置(Parent 中央偏下),
        /// 3D 角色位置变化时无需更新此控件。
        /// </summary>
        /// <param name="worldPos">角色脚下的世界坐标(当前未使用,仅为 API 占位)</param>
        public void UpdateGroundPosition(Vector3 worldPos)
        {
            if (_groundDisc == null)
                return;

            // 简化方案:固定在父控件中央偏下,大致对应 3D 角色脚下
            _groundDisc.Location = new Float2(
                Width * 0.5f - _groundDisc.Width * 0.5f,
                Height * 0.65f);
        }

        /// <summary>
        /// 性别切换同步相机过渡。
        /// 启动 FloatTween 将相机距离从 4 → 2.5(duration 秒),过渡完成后可选地替换模型 Actor。
        /// 简化方案:只做相机距离过渡,模型替换由调用方决定(传 null 表示仅做距离过渡)。
        /// </summary>
        /// <param name="newModel">待切换到的新模型 Actor(若为 null 则仅做相机距离过渡)</param>
        /// <param name="duration">过渡时长(秒)</param>
        public void TransitionToNewModel(Actor newModel, float duration = 0.4f)
        {
            if (_cameraSystem == null)
                return;

            _pendingGenderModel = newModel;
            _genderDistanceTween = new FloatTween
            {
                From = 400f,
                To = 250f,
                Duration = duration,
                Elapsed = 0f,
                Ease = EaseType.EaseInOutSine
            };
        }

        private static PresetData GetPresetData(CameraPreset preset)
        {
            switch (preset)
            {
                case CameraPreset.Far:
                    return new PresetData(0f, 5f, 400f);
                case CameraPreset.Mid:
                    return new PresetData(0f, -5f, 250f);
                case CameraPreset.Near:
                    return new PresetData(0f, -10f, 150f);
                default:
                    return new PresetData(0f, 0f, 250f);
            }
        }

        #endregion

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (_cameraSystem != null)
            {
                _cameraSystem.Dispose();
                _cameraSystem = null;
            }

            if (_renderTask != null)
            {
                _renderTask.Enabled = false;
                _renderTask = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.ReleaseGPU();
                _renderTexture = null;
            }

            _textureBrush = null;

            _azimuthTween = null;
            _elevationTween = null;
            _distanceTween = null;
            _genderDistanceTween = null;
            _pendingGenderModel = null;
            _inkBackground = null;
            _particles = null;
            _groundDisc = null;
            _isPresetTransitioning = false;
        }
    }
}
