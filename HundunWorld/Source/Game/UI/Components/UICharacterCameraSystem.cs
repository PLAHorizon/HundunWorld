using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 身体部位相机预设 - 定义不同部位聚焦时的相机参数
    /// </summary>
    public struct BodyPartPreset
    {
        public string Name;
        public float Distance;      // 相机距离
        public float Height;        // 相机高度偏移
        public float LookAtHeight;  // 注视点高度
        public float RotationX;     // 俯仰角
        public float FOV;           // 视场角

        public BodyPartPreset(string name, float distance, float height, float lookAtHeight, float rotationX, float fov)
        {
            Name = name;
            Distance = distance;
            Height = height;
            LookAtHeight = lookAtHeight;
            RotationX = rotationX;
            FOV = fov;
        }
    }

    public class UICharacterCameraSystem : IDisposable
    {
        private Camera _camera;
        private Actor _cameraPivot;    // 相机挂载点
        private Actor _characterActor; // 角色固定在原点
        private FlaxEngine.Scene _targetScene;

        // 相机参数 (cm 单位,与模型尺寸匹配)
        private float _cameraDistance = 250f; // 250cm
        private float _cameraHeight = 120f;   // 120cm
        private float _cameraRotationY = 0.0f;
        private float _cameraRotationX = 10.0f;
        private float _lookAtHeight = 90f;    // 注视点高度 (模型中心约90cm)

        // 模型缩放 - 默认1.0f表示保持原始尺寸
        // UE导出的模型通常为cm单位（身高约180cm），不做缩放直接在场景中以cm显示
        // 如果模型实际尺寸偏大或偏小，可调整此值
        private float _modelScale = 1.0f;

        // 平滑过渡
        private float _targetDistance;
        private float _targetHeight;
        private float _targetLookAtHeight;
        private float _targetRotationX;
        private float _targetFOV = 45.0f;
        private float _transitionSpeed = 5.0f; // 过渡速度
        private bool _isTransitioning = false;

        // 鼠标交互
        private float _rotateSpeed = 0.3f;    // 旋转灵敏度
        private float _zoomSpeed = 0.05f;     // 缩放灵敏度
        private float _minDistance = 50f;      // 最小距离（脸部特写）
        private float _maxDistance = 450f;     // 最大距离（全身）

        private bool _isInitialized = false;

        // 静态缓存，避免同一资源被重复加载导致 registry 冲突
        private static readonly Dictionary<string, Asset> _assetCache = new Dictionary<string, Asset>();

        // 身体部位相机预设 (cm 单位)
        private static readonly Dictionary<string, BodyPartPreset> BodyPartPresets = new Dictionary<string, BodyPartPreset>
        {
            { "全身", new BodyPartPreset("全身", 250f, 120f, 90f, 10f, 45f) },
            { "脸部", new BodyPartPreset("脸部", 50f, 155f, 155f, 0f, 35f) },
            { "头部", new BodyPartPreset("头部", 70f, 160f, 150f, 5f, 38f) },
            { "上半身", new BodyPartPreset("上半身", 120f, 130f, 120f, 8f, 40f) },
            { "下半身", new BodyPartPreset("下半身", 150f, 60f, 60f, 5f, 42f) },
            { "发型", new BodyPartPreset("发型", 60f, 170f, 160f, 10f, 35f) },
            { "妆容", new BodyPartPreset("妆容", 45f, 155f, 155f, 0f, 32f) },
            { "体型", new BodyPartPreset("体型", 250f, 120f, 90f, 10f, 45f) },
        };

        // 主分类对应的默认相机部位
        private static readonly Dictionary<string, string> CategoryToBodyPart = new Dictionary<string, string>
        {
            { "捏脸", "脸部" },
            { "妆容", "妆容" },
            { "发型", "发型" },
            { "体型", "体型" },
        };

        public event Action OnCharacterLoaded;

        public Camera Camera => _camera;
        public Actor CameraActor => _cameraPivot;
        public Actor CharacterActor => _characterActor;

        /// <summary>
        /// 模型缩放因子。UE导出的模型通常单位为cm，需要缩小100倍。
        /// 默认0.01（缩小100倍），如果模型已经是m单位则设为1.0
        /// </summary>
        public float ModelScale
        {
            get => _modelScale;
            set
            {
                _modelScale = value;
                if (_characterActor != null)
                {
                    _characterActor.Scale = new Vector3(_modelScale, _modelScale, _modelScale);
                }
            }
        }

        public float CameraDistance
        {
            get => _cameraDistance;
            set
            {
                _cameraDistance = Mathf.Clamp(value, _minDistance, _maxDistance);
                _targetDistance = _cameraDistance;
                UpdateCameraPosition();
            }
        }

        public float CameraRotationY
        {
            get => _cameraRotationY;
            set
            {
                _cameraRotationY = value;
                UpdateCameraPosition();
            }
        }

        public float CameraRotationX
        {
            get => _cameraRotationX;
            set
            {
                _cameraRotationX = Mathf.Clamp(value, -45.0f, 60.0f);
                UpdateCameraPosition();
            }
        }

        /// <summary>
        /// 注视点高度（控制相机看向角色的哪个高度）
        /// </summary>
        public float LookAtHeight
        {
            get => _lookAtHeight;
            set
            {
                _lookAtHeight = value;
                _targetLookAtHeight = value;
                UpdateCameraPosition();
            }
        }

        /// <summary>
        /// 是否正在过渡动画中
        /// </summary>
        public bool IsTransitioning => _isTransitioning;

        public UICharacterCameraSystem()
        {
            // 初始化目标值为当前值
            _targetDistance = _cameraDistance;
            _targetHeight = _cameraHeight;
            _targetLookAtHeight = _lookAtHeight;
            _targetRotationX = _cameraRotationX;
        }

        public void Initialize(Vector2 renderTargetSize, FlaxEngine.Scene targetScene = null)
        {
            if (_isInitialized)
                return;

            _targetScene = targetScene;
            CreateCamera();

            _isInitialized = true;
        }

        /// <summary>
        /// 获取目标场景，优先使用指定的场景，否则查找 Character 场景，最后使用第一个场景
        /// </summary>
        private FlaxEngine.Scene GetTargetScene()
        {
            if (_targetScene != null)
                return _targetScene;

            for (int i = 0; i < Level.ScenesCount; i++)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name == "Character")
                    return scene;
            }

            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name != "TransitionScene")
                    return scene;
            }

            return null;
        }

        private void CreateCamera()
        {
            var targetScene = GetTargetScene();

            _cameraPivot = new EmptyActor();
            _cameraPivot.Name = "UICharacterCameraRoot";
            // 默认看向角色中心(0, 90, 0), 距离 250cm (模型约180cm高,中心在90cm)
            _cameraPivot.Position = new Vector3(0f, 120f, 250f);

            if (targetScene != null)
                Level.SpawnActor(_cameraPivot, targetScene);
            else
                Level.SpawnActor(_cameraPivot);

            _camera = _cameraPivot.AddChild<Camera>();
            _camera.Name = "UICharacterCamera";
            _camera.NearPlane = 1f;   // cm 单位下至少 1cm,避免精度问题
            _camera.FarPlane = 1000f;  // cm 单位下足够远
            _camera.FieldOfView = _targetFOV;
            _camera.UsePerspective = true;

            // 注意：不在此处设置 OverrideMainCamera，避免覆盖UI渲染管线
            // 由 CharacterPreviewPanel 通过 SceneRenderTask 渲染到纹理

            UpdateCameraPosition();

            Debug.Log($"[UICharacterCameraSystem] 相机创建完成，目标场景: {targetScene?.Name ?? "默认"}");
        }

        private void UpdateCameraPosition()
        {
            if (_cameraPivot == null)
                return;

            float radY = _cameraRotationY * Mathf.DegreesToRadians;
            float radX = _cameraRotationX * Mathf.DegreesToRadians;

            float camX = Mathf.Cos(radY) * Mathf.Cos(radX) * _cameraDistance;
            float camY = Mathf.Sin(radX) * _cameraDistance + _cameraHeight;
            float camZ = Mathf.Sin(radY) * Mathf.Cos(radX) * _cameraDistance;

            _cameraPivot.Position = new Vector3(camX, camY, camZ);
            // 默认看向角色中心(0, 90, 0)而非脚底(0, 0, 0),让角色显示更自然
            _cameraPivot.LookAt(new Vector3(0, 90f, 0));
        }

        public void LoadCharacter(string assetPath)
        {
            if (!_isInitialized)
            {
                Debug.LogError("UICharacterCameraSystem not initialized");
                return;
            }

            if (_characterActor != null)
            {
                Actor.Destroy(_characterActor);
                _characterActor = null;
            }

            try
            {
                Asset content;
                if (_assetCache.TryGetValue(assetPath, out content) && content != null)
                {
                    // 已缓存，直接使用
                }
                else
                {
                    content = Content.Load(assetPath);
                    if (content == null)
                    {
                        // 主路径（带扩展名）失败，尝试无扩展名回退路径
                        var extension = System.IO.Path.GetExtension(assetPath);
                        if (!string.IsNullOrEmpty(extension))
                        {
                            var fallbackPath = assetPath.Substring(0, assetPath.Length - extension.Length).Replace('\\', '/');
                            content = Content.Load(fallbackPath);
                        }
                    }
                    if (content != null)
                    {
                        _assetCache[assetPath] = content;
                    }
                }

                if (content != null)
                {
                    var targetScene = GetTargetScene();

                    if (content is Prefab prefab && prefab.IsLoaded)
                    {
                        _characterActor = PrefabManager.SpawnPrefab(prefab, Vector3.Zero, Quaternion.Identity);
                        _characterActor.Name = "UICharacter";
                        _characterActor.Position = Vector3.Zero;
                        // 应用模型缩放修正
                        _characterActor.Scale = new Vector3(_modelScale, _modelScale, _modelScale);
                        // 确保角色在正确的场景中 (PrefabManager.SpawnPrefab 已生成,只需确保场景归属)
                        if (targetScene != null && _characterActor.Parent == null)
                        {
                            _characterActor.Parent = targetScene;
                        }
                        UpdateCameraPosition();
                        OnCharacterLoaded?.Invoke();
                        Debug.Log($"[UICharacterCameraSystem] 角色预制体加载成功: {assetPath}, 缩放: {_modelScale}, 场景: {targetScene?.Name ?? "默认"}");
                        return;
                    }

                    if (content is SkinnedModel skinnedModel && skinnedModel.IsLoaded)
                    {
                        var animatedModel = new AnimatedModel
                        {
                            Name = "UICharacter",
                            Position = Vector3.Zero,
                            SkinnedModel = skinnedModel
                        };
                        // 应用模型缩放修正
                        animatedModel.Scale = new Vector3(_modelScale, _modelScale, _modelScale);
                        if (targetScene != null)
                            Level.SpawnActor(animatedModel, targetScene);
                        else
                            Level.SpawnActor(animatedModel);
                        _characterActor = animatedModel;
                        UpdateCameraPosition();
                        OnCharacterLoaded?.Invoke();
                        Debug.Log($"[UICharacterCameraSystem] 角色蒙皮模型加载成功: {assetPath}, 缩放: {_modelScale}, 场景: {targetScene?.Name ?? "默认"}");
                        return;
                    }

                    if (content is Model staticModelAsset && staticModelAsset.IsLoaded)
                    {
                        var staticModelActor = new StaticModel
                        {
                            Name = "UICharacter",
                            Position = Vector3.Zero,
                            Model = staticModelAsset
                        };
                        // 应用模型缩放修正
                        staticModelActor.Scale = new Vector3(_modelScale, _modelScale, _modelScale);
                        if (targetScene != null)
                            Level.SpawnActor(staticModelActor, targetScene);
                        else
                            Level.SpawnActor(staticModelActor);
                        _characterActor = staticModelActor;
                        UpdateCameraPosition();
                        OnCharacterLoaded?.Invoke();
                        Debug.Log($"[UICharacterCameraSystem] 角色静态模型加载成功: {assetPath}, 缩放: {_modelScale}, 场景: {targetScene?.Name ?? "默认"}");
                        return;
                    }

                    // 兜底:虽然加载成功但不是已知类型,记录类型信息
                    Debug.LogWarning($"[UICharacterCameraSystem] 资源已加载但类型不支持: {content.GetType().FullName}");
                }

                Debug.LogError($"[UICharacterCameraSystem] 无法加载角色资源: {assetPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UICharacterCameraSystem] 加载角色出错: {e.Message}");
            }
        }

        public void RotateCamera(float deltaY, float deltaX)
        {
            CameraRotationY += deltaY * _rotateSpeed;
            CameraRotationX += deltaX * _rotateSpeed;
        }

        /// <summary>
        /// 鼠标滚轮缩放 - 支持不同部位的缩放范围
        /// </summary>
        public void ZoomCamera(float delta)
        {
            float newDist = _cameraDistance - delta * _zoomSpeed * _cameraDistance;
            _cameraDistance = Mathf.Clamp(newDist, _minDistance, _maxDistance);
            _targetDistance = _cameraDistance;
            UpdateCameraPosition();
        }

        /// <summary>
        /// 聚焦到指定身体部位 - 带平滑过渡
        /// </summary>
        public void FocusOnBodyPart(string bodyPartName)
        {
            if (BodyPartPresets.TryGetValue(bodyPartName, out var preset))
            {
                _targetDistance = preset.Distance;
                _targetHeight = preset.Height;
                _targetLookAtHeight = preset.LookAtHeight;
                _targetRotationX = preset.RotationX;
                _targetFOV = preset.FOV;
                _isTransitioning = true;
                Debug.Log($"[UICharacterCameraSystem] 聚焦部位: {bodyPartName}, 距离: {preset.Distance}, FOV: {preset.FOV}");
            }
            else
            {
                Debug.LogWarning($"[UICharacterCameraSystem] 未知的身体部位: {bodyPartName}");
            }
        }

        /// <summary>
        /// 根据主分类名称聚焦到对应身体部位
        /// </summary>
        public void FocusOnCategory(string categoryName)
        {
            if (CategoryToBodyPart.TryGetValue(categoryName, out var bodyPart))
            {
                FocusOnBodyPart(bodyPart);
            }
            else
            {
                // 未知分类，默认全身
                FocusOnBodyPart("全身");
            }
        }

        /// <summary>
        /// 重置到默认全身视角
        /// </summary>
        public void ResetToDefault()
        {
            FocusOnBodyPart("全身");
            _cameraRotationY = 0;
        }

        /// <summary>
        /// 每帧更新 - 处理平滑过渡
        /// </summary>
        public void Update()
        {
            if (!_isInitialized)
                return;

            if (_isTransitioning)
            {
                float t = Mathf.Saturate(_transitionSpeed * Time.DeltaTime);

                _cameraDistance = Mathf.Lerp(_cameraDistance, _targetDistance, t);
                _cameraHeight = Mathf.Lerp(_cameraHeight, _targetHeight, t);
                _lookAtHeight = Mathf.Lerp(_lookAtHeight, _targetLookAtHeight, t);
                _cameraRotationX = Mathf.Lerp(_cameraRotationX, _targetRotationX, t);

                if (_camera != null)
                {
                    _camera.FieldOfView = Mathf.Lerp(_camera.FieldOfView, _targetFOV, t);
                }

                // 检查是否到达目标
                float distDiff = Mathf.Abs(_cameraDistance - _targetDistance);
                float heightDiff = Mathf.Abs(_cameraHeight - _targetHeight);
                float lookDiff = Mathf.Abs(_lookAtHeight - _targetLookAtHeight);
                float rotDiff = Mathf.Abs(_cameraRotationX - _targetRotationX);
                float fovDiff = _camera != null ? Mathf.Abs(_camera.FieldOfView - _targetFOV) : 0;

                if (distDiff < 0.001f && heightDiff < 0.001f && lookDiff < 0.001f && rotDiff < 0.1f && fovDiff < 0.1f)
                {
                    _cameraDistance = _targetDistance;
                    _cameraHeight = _targetHeight;
                    _lookAtHeight = _targetLookAtHeight;
                    _cameraRotationX = _targetRotationX;
                    if (_camera != null)
                        _camera.FieldOfView = _targetFOV;
                    _isTransitioning = false;
                }

                UpdateCameraPosition();
            }
        }

        public void Dispose()
        {
            if (_characterActor != null)
            {
                Actor.Destroy(_characterActor);
                _characterActor = null;
            }

            if (_cameraPivot != null)
            {
                Actor.Destroy(_cameraPivot);
                _cameraPivot = null;
                _camera = null;
            }

            _isInitialized = false;
        }
    }
}
