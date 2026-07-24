using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 3D 角色预览控件（水墨主题）。
    /// <para>
    /// 使用独立 <see cref="GPUTexture"/> + 离屏 <see cref="Camera"/> + 子 <see cref="Actor"/>
    /// 挂载 <see cref="AnimatedModel"/> 实现 UI 内嵌 3D 角色预览。通过
    /// <see cref="SceneRenderTask"/> 的 <c>ActorsSource = CustomActors</c> 仅渲染指定 Actor，
    /// 不污染主场景渲染输出。
    /// </para>
    /// <para>
    /// 默认模型取自 <see cref="EquipmentDatabase.DefaultBodyModelPath"/> /
    /// <see cref="EquipmentDatabase.DefaultBodyModelGuid"/>；动画图引用
    /// HundunWorldGame 中预加载使用的 AnimationGraph 资产。
    /// </para>
    /// <para>
    /// 支持鼠标左键水平拖拽旋转角色（约 0.01 弧度/像素）。
    /// 实现 <see cref="IInkPage"/> 接口，由外部布局系统调用 <see cref="RefreshLayout"/>。
    /// </para>
    /// </summary>
    public class CharacterPreview3D : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局 / 相机常量
        // =======================================================================

        /// <summary>默认控件宽度</summary>
        private const float DefaultWidth = 440f;

        /// <summary>默认控件高度</summary>
        private const float DefaultHeight = 600f;

        /// <summary>相机距离角色的距离（约 250 单位/cm，与 skm_uefn_mannequin 约180cm 身高匹配）</summary>
        private const float CameraDistance = 250f;

        /// <summary>相机高度偏移（cm，略高于角色中心，轻微俯视更自然）</summary>
        private const float CameraHeight = 120f;

        /// <summary>相机注视点高度（角色中心约 90cm，而非脚底）</summary>
        private const float LookAtHeight = 90f;

        /// <summary>相机视野角度（度）</summary>
        private const float CameraFieldOfView = 45f;

        /// <summary>相机近裁剪面</summary>
        private const float CameraNearPlane = 1f;

        /// <summary>相机远裁剪面</summary>
        private const float CameraFarPlane = 5000f;

        /// <summary>水平拖拽旋转灵敏度（弧度/像素，约 0.01）</summary>
        private const float DragYawSensitivity = 0.01f;

        /// <summary>模型俯仰校正（度）：UE 导出的 skm_uefn_mannequin 为 Z-up 轴向，在 Flax（Y-up）中直接加载会躺倒（头朝 -Z、正面朝 +Y）。绕 X 轴旋转 +90° 使头朝 +Y（上）、正面朝 +Z（相机）。注意方向为 +90°：-90° 会使头朝下倒立。</summary>
        private const float ModelPitchCorrection = 90f;

        /// <summary>角色模型资产 GUID（来自 HundunWorldGame.cs）</summary>
        private static readonly Guid CharacterAnimGraphGuid = new Guid("ceded67f4bb2623f40b4dcb493b0d419");

        /// <summary>角色 AnimationGraph 资产路径</summary>
        private const string CharacterAnimGraphPath = "Content/Character/Models/Animation Graph.flax";

        // ===================================================================
        // 渲染资源
        // =======================================================================

        /// <summary>离屏渲染目标纹理</summary>
        private GPUTexture _renderTexture;

        /// <summary>用于将 RenderTexture 绘制到 UI 的画笔</summary>
        private GPUTextureBrush _textureBrush;

        /// <summary>离屏渲染任务（仅渲染 CustomActors）</summary>
        private SceneRenderTask _renderTask;

        // ===================================================================
        // 场景 Actor
        // =======================================================================

        /// <summary>承载 Camera 的根 Actor（便于统一清理）</summary>
        private EmptyActor _cameraRoot;

        /// <summary>离屏相机</summary>
        private Camera _camera;

        /// <summary>承载 AnimatedModel 的根 Actor</summary>
        private EmptyActor _modelRoot;

        /// <summary>角色蒙皮模型组件</summary>
        private AnimatedModel _animatedModel;

        /// <summary>灯光根 Actor（承载三点光照，不随模型旋转）</summary>
        private EmptyActor _lightRoot;

        /// <summary>主光源（方向光）</summary>
        private DirectionalLight _keyLight;

        /// <summary>补光（方向光）</summary>
        private DirectionalLight _fillLight;

        /// <summary>轮廓光（方向光）</summary>
        private DirectionalLight _rimLight;

        // ===================================================================
        // 交互状态
        // =======================================================================

        /// <summary>鼠标左键是否正在拖拽</summary>
        private bool _isDragging;

        /// <summary>上一帧鼠标位置（用于计算水平增量）</summary>
        private Float2 _lastMousePosition;

        /// <summary>当前模型 Yaw 旋转（弧度）</summary>
        private float _modelYaw;

        // ===================================================================
        // 水墨微粒（少量环境粒子）
        // =======================================================================

        /// <summary>水墨微粒（无状态：位置/透明度由绝对时间推导，无需逐帧累积更新）</summary>
        private struct InkMote
        {
            public float XFrac;      // 基准水平位置（0~1，相对宽度）
            public float YFrac0;     // 初始垂直位置（0~1，相对高度）
            public float RiseSpeed;  // 上升漂移速度（每秒高度的比例）
            public float SwayAmp;    // 水平摆动幅度（像素）
            public float SwayFreq;   // 摆动频率（Hz）
            public float Phase;      // 随机相位
            public float Size;       // 粒子半径（像素）
            public float BaseAlpha;  // 基准不透明度
            public Color Tint;       // 粒子颜色
        }

        /// <summary>水墨微粒数组（少量，约 16 个）</summary>
        private InkMote[] _motes;

        /// <summary>微粒随机源（固定种子，多次打开观感一致）</summary>
        private readonly System.Random _moteRandom = new System.Random(20260723);

        /// <summary>是否已初始化渲染资源（避免重复初始化）</summary>
        private bool _initialized;

        /// <summary>透明背景模式开关（嵌入宿主容器时使用）</summary>
        private bool _transparentBackground;

        /// <summary>待应用的角色组件（场景/Actor 未就绪时暂存，初始化完成后重放）</summary>
        private CharacterAttributesComponent _pendingCharacter;

        /// <summary>
        /// 透明背景模式：跳过自身背景填充与描边，由宿主容器（如 ModelStage）
        /// 提供底色与边框，渲染纹理的透明区域透出宿主背景。
        /// </summary>
        public bool TransparentBackground
        {
            get => _transparentBackground;
            set
            {
                _transparentBackground = value;
                BackgroundColor = value ? Color.Transparent : InkWashTheme.BaseDefault;
            }
        }

        /// <summary>
        /// 是否已就绪（渲染资源与角色模型均已建立）。
        /// 宿主容器可据此隐藏 2D 占位提示，切换为真实 3D 预览。
        /// </summary>
        public bool IsReady => _initialized
                               && _renderTexture != null
                               && _animatedModel != null
                               && _animatedModel.SkinnedModel != null;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：默认尺寸 440x600，深墨黑背景，金色描边。
        /// 在构造时尝试初始化离屏渲染管线；若场景未就绪则推迟到 <see cref="RefreshLayout"/>。
        /// </summary>
        public CharacterPreview3D()
        {
            Size = new Float2(DefaultWidth, DefaultHeight);
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = true;
            AutoFocus = false;

            try
            {
                InitializeRenderTarget();
                InitializeActors();
                // 仅当 Actor 真正创建成功时才标记为已初始化，否则交由 RefreshLayout 重试
                _initialized = _cameraRoot != null && _modelRoot != null;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 初始化完成，_initialized={_initialized}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <summary>
        /// 基于当前控件实际尺寸刷新布局。
        /// 重建 <see cref="GPUTexture"/> 分辨率（匹配控件尺寸），
        /// 重新计算相机位置（保持角色居中、距离约 200、FOV 45 度）。
        /// </summary>
        public void RefreshLayout()
        {
            try
            {
                // 若未初始化或 Actor 未创建，且场景已就绪，则重试 InitializeActors
                if (!_initialized || _cameraRoot == null || _modelRoot == null)
                {
                    var scene = GetTargetScene();
                    if (scene != null)
                    {
                        FlaxEngine.Debug.Log("[CharacterPreview3D] RefreshLayout 触发 InitializeActors 重试");
                        InitializeActors();
                        if (_cameraRoot != null && _modelRoot != null)
                        {
                            _initialized = true;
                            FlaxEngine.Debug.Log("[CharacterPreview3D] RefreshLayout 重试 InitializeActors 成功");
                        }
                        else
                        {
                            FlaxEngine.Debug.LogWarning("[CharacterPreview3D] RefreshLayout 重试后 Actor 仍为 null");
                        }
                    }
                    else
                    {
                        FlaxEngine.Debug.LogWarning("[CharacterPreview3D] RefreshLayout 场景仍未就绪，跳过初始化");
                    }
                }

                RebuildRenderTarget();
                UpdateCameraTransform();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] RefreshLayout 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 绑定真实角色数据。读取组件所属 Actor 上的 <see cref="AnimatedModel"/>
        /// 的 <see cref="AnimatedModel.SkinnedModel"/> / <see cref="AnimatedModel.AnimationGraph"/>；
        /// 若组件为 null 或无可用模型，则使用默认模型。
        /// </summary>
        /// <param name="component">角色属性组件，可为 null</param>
        public void SetCharacter(CharacterAttributesComponent component)
        {
            try
            {
                // Actor 尚未创建（场景未就绪）：暂存角色数据，待 InitializeActors 完成后重放
                if (_animatedModel == null)
                {
                    _pendingCharacter = component;
                    FlaxEngine.Debug.Log("[CharacterPreview3D] SetCharacter：Actor 未就绪，已暂存角色数据待重放");
                    return;
                }

                SkinnedModel targetSkinnedModel = null;
                AnimationGraph targetAnimGraph = null;

                if (component != null && component.Actor != null)
                {
                    var source = component.Actor.GetChild<AnimatedModel>();
                    if (source != null)
                    {
                        if (source.SkinnedModel != null && source.SkinnedModel.IsLoaded)
                            targetSkinnedModel = source.SkinnedModel;
                        if (source.AnimationGraph != null && source.AnimationGraph.IsLoaded)
                            targetAnimGraph = source.AnimationGraph;
                    }
                }

                ApplyModelResources(targetSkinnedModel, targetAnimGraph);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] SetCharacter 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 初始化
        // =======================================================================

        /// <summary>
        /// 创建 <see cref="GPUTexture"/> 渲染目标与 <see cref="SceneRenderTask"/>。
        /// 初始分辨率匹配控件当前尺寸。
        /// </summary>
        private void InitializeRenderTarget()
        {
            int width = Mathf.Max(1, (int)Width);
            int height = Mathf.Max(1, (int)Height);

            var desc = GPUTextureDescription.New2D(width, height, PixelFormat.R8G8B8A8_UNorm);
            _renderTexture = new GPUTexture();
            if (!_renderTexture.Init(ref desc))
            {
                _textureBrush = new GPUTextureBrush(_renderTexture);

                _renderTask = new SceneRenderTask
                {
                    Order = -100,
                    Output = _renderTexture,
                    Enabled = false,
                };
                FlaxEngine.Debug.Log($"[CharacterPreview3D] RenderTexture 初始化成功 ({width}x{height})");
            }
            else
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] RenderTexture 初始化失败 (Init 返回非 0, {width}x{height})");
            }
        }

        /// <summary>
        /// 重建 RenderTexture 分辨率以匹配控件当前尺寸。
        /// 仅在尺寸有效且发生变化时执行。
        /// </summary>
        private void RebuildRenderTarget()
        {
            if (_renderTexture == null)
            {
                InitializeRenderTarget();
                return;
            }

            int width = Mathf.Max(1, (int)Width);
            int height = Mathf.Max(1, (int)Height);

            if (_renderTexture.Width == width && _renderTexture.Height == height)
                return;

            // Resize 要求纹理已创建
            _renderTexture.Resize(width, height, PixelFormat.Unknown);
        }

        /// <summary>
        /// 创建 Camera Actor 与 AnimatedModel Actor 并加入主场景，
        /// 通过 <see cref="ActorsSources.CustomActors"/> 仅渲染 <see cref="_modelRoot"/>。
        /// </summary>
        private void InitializeActors()
        {
            var scene = GetTargetScene();
            if (scene == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 无可用场景，Actor 创建推迟");
                return;
            }

            FlaxEngine.Debug.Log($"[CharacterPreview3D] 场景已就绪: {scene.Name}，开始创建 Actor");

            // ── 相机 Actor ──
            _cameraRoot = new EmptyActor { Name = "CharacterPreview3D_Camera" };
            Level.SpawnActor(_cameraRoot, scene);

            _camera = _cameraRoot.AddChild<Camera>();
            _camera.UsePerspective = true;
            _camera.FieldOfView = CameraFieldOfView;
            _camera.NearPlane = CameraNearPlane;
            _camera.FarPlane = CameraFarPlane;
            // 不启用音频监听，避免覆盖主相机
            // 注：Camera 类未公开 AudioListener 开关，依靠 CustomActors 渲染源隔离

            // ── 模型 Actor ──
            _modelRoot = new EmptyActor { Name = "CharacterPreview3D_Model" };
            Level.SpawnActor(_modelRoot, scene);
            _modelRoot.Position = Vector3.Zero;

            _animatedModel = _modelRoot.AddChild<AnimatedModel>();
            _animatedModel.SkinnedModel = null;
            _animatedModel.AnimationGraph = null;
            // 扶正模型：UE 导出模型为 Z-up 轴向，直接加载会躺倒（头朝 -Z、正面朝 +Y），绕 X 轴旋转 +90° 使头朝 +Y（上）、正面朝 +Z（相机）。
            // 应用于子节点 _animatedModel，与父节点 _modelRoot 上的转盘 Yaw 互不干扰（先扶正、再水平旋转）。
            _animatedModel.Orientation = Quaternion.Euler(ModelPitchCorrection, 0f, 0f);

            ApplyModelResources(null, null);

            // ── 灯光 Actor（三点光照，保证孤立渲染下模型可见） ──
            CreateLightingRig(scene);

            // ── 绑定渲染任务 ──
            _renderTask.Camera = _camera;
            _renderTask.ActorsSource = ActorsSources.CustomActors;
            // 关键修复：CustomActors 孤立渲染不包含场景原有灯光，
            // 必须将灯光组一并加入渲染列表，否则无光源导致渲染结果全黑
            _renderTask.CustomActors = new Actor[] { _modelRoot, _lightRoot };
            _renderTask.Enabled = true;

            UpdateCameraTransform();

            // 重放场景就绪前暂存的角色绑定
            if (_pendingCharacter != null)
            {
                var pending = _pendingCharacter;
                _pendingCharacter = null;
                SetCharacter(pending);
            }

            FlaxEngine.Debug.Log("[CharacterPreview3D] InitializeActors 成功");
        }

        /// <summary>
        /// 创建三点光照组并加入目标场景。
        /// <para>
        /// CustomActors 孤立渲染模式下，场景原有灯光不参与渲染，
        /// 必须将自建光源一并加入 <see cref="SceneRenderTask.CustomActors"/>，否则渲染结果一片漆黑。
        /// 主光/补光/轮廓光覆盖多个角度，确保模型在任意朝向下均清晰可见。
        /// </para>
        /// </summary>
        /// <param name="scene">目标场景</param>
        private void CreateLightingRig(FlaxEngine.Scene scene)
        {
            _lightRoot = new EmptyActor { Name = "CharacterPreview3D_Lights" };
            Level.SpawnActor(_lightRoot, scene);

            // 太阳主光：模拟自然阳光 —— 50° 高入射角、柔和亮度、微暖白，避免曝光过度
            _keyLight = _lightRoot.AddChild<DirectionalLight>();
            _keyLight.Name = "PreviewKeyLight";
            _keyLight.Orientation = Quaternion.Euler(-50f, -30f, 0f);
            _keyLight.Brightness = 1.5f;
            _keyLight.Color = new Color(1f, 0.96f, 0.90f);

            // 天空补光：模拟天光散射 —— 冷蓝紫、亮度约为主光一半，软化阴影降低对比
            _fillLight = _lightRoot.AddChild<DirectionalLight>();
            _fillLight.Name = "PreviewFillLight";
            _fillLight.Orientation = Quaternion.Euler(-60f, 150f, 0f);
            _fillLight.Brightness = 0.8f;
            _fillLight.Color = new Color(0.80f, 0.87f, 1f);

            // 轮廓光：后上方弱勾边，分离角色与深色背景（低亮度防过曝）
            _rimLight = _lightRoot.AddChild<DirectionalLight>();
            _rimLight.Name = "PreviewRimLight";
            _rimLight.Orientation = Quaternion.Euler(-35f, 180f, 0f);
            _rimLight.Brightness = 0.4f;
            _rimLight.Color = new Color(0.9f, 0.92f, 1f);

            FlaxEngine.Debug.Log("[CharacterPreview3D] 三点光照组已创建");
        }

        /// <summary>
        /// 获取目标场景：优先使用主场景，跳过过渡场景。
        /// </summary>
        /// <returns>可用的主场景，无则返回 null</returns>
        private static FlaxEngine.Scene GetTargetScene()
        {
            for (int i = Level.ScenesCount - 1; i >= 0; i--)
            {
                var scene = Level.GetScene(i);
                if (scene != null && scene.Name != "TransitionScene")
                    return scene;
            }
            return null;
        }

        /// <summary>
        /// 安全应用 SkinnedModel / AnimationGraph 到 <see cref="_animatedModel"/>。
        /// 若传入为 null，则回退到默认模型与默认 AnimationGraph。
        /// 遵循竞态安全协议：先停用组件、清空 AnimationGraph，再修改 SkinnedModel。
        /// </summary>
        /// <param name="skinnedModel">指定的 SkinnedModel，为 null 则使用默认</param>
        /// <param name="animGraph">指定的 AnimationGraph，为 null 则使用默认</param>
        private void ApplyModelResources(SkinnedModel skinnedModel, AnimationGraph animGraph)
        {
            if (_animatedModel == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] ApplyModelResources: _animatedModel 为 null");
                return;
            }

            // 回退到默认资产
            if (skinnedModel == null)
                skinnedModel = LoadDefaultSkinnedModel();
            if (animGraph == null)
                animGraph = LoadDefaultAnimationGraph();

            FlaxEngine.Debug.Log($"[CharacterPreview3D] ApplyModelResources: SkinnedModel={(skinnedModel != null ? skinnedModel.Path : "null")}, AnimationGraph={(animGraph != null ? animGraph.Path : "null")}");

            if (skinnedModel == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 无法获取 SkinnedModel，禁用 AnimatedModel");
                _animatedModel.IsActive = false;
                return;
            }

            // 竞态安全切换
            var originalUpdateMode = _animatedModel.UpdateMode;
            _animatedModel.UpdateMode = AnimatedModel.AnimationUpdateMode.Never;
            _animatedModel.IsActive = false;
            _animatedModel.AnimationGraph = null;

            if (_animatedModel.SkinnedModel != skinnedModel)
                _animatedModel.SkinnedModel = skinnedModel;

            _animatedModel.AnimationGraph = animGraph;

            try
            {
                _animatedModel.SetupSkinningData();
                _animatedModel.ResetAnimation();
                _animatedModel.UpdateAnimation();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CharacterPreview3D] 刷新 AnimatedModel 失败: {ex.Message}");
            }

            _animatedModel.UpdateMode = originalUpdateMode;
            // 直接激活，不再保留 wasActive 原状态（首次初始化时原状态为 false 会导致模型不渲染）
            _animatedModel.IsActive = true;
            FlaxEngine.Debug.Log("[CharacterPreview3D] AnimatedModel 已激活");
        }

        /// <summary>
        /// 加载默认 SkinnedModel（来自 <see cref="EquipmentDatabase"/>）。
        /// </summary>
        /// <returns>已加载的 SkinnedModel，或 null</returns>
        private static SkinnedModel LoadDefaultSkinnedModel()
        {
            try
            {
                var asset = Content.Load<SkinnedModel>(EquipmentDatabase.DefaultBodyModelGuid);
                if (asset != null && asset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] SkinnedModel 已加载 (GUID): {asset.Path}");
                    return asset;
                }
                if (asset != null)
                {
                    asset.WaitForLoaded(30000.0);
                    if (asset.IsLoaded)
                    {
                        FlaxEngine.Debug.Log($"[CharacterPreview3D] SkinnedModel 等待后加载完成 (GUID): {asset.Path}");
                        return asset;
                    }
                }

                var pathAsset = Content.LoadAsync<SkinnedModel>(EquipmentDatabase.DefaultBodyModelPath);
                if (pathAsset != null && pathAsset.WaitForLoaded(30000.0) && pathAsset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] SkinnedModel 已加载 (路径): {pathAsset.Path}");
                    return pathAsset;
                }
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 默认 SkinnedModel 加载失败：GUID 与路径均未就绪");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 加载默认 SkinnedModel 失败: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 加载默认 AnimationGraph（来自 HundunWorldGame 中的资产引用）。
        /// </summary>
        /// <returns>已加载的 AnimationGraph，或 null</returns>
        private static AnimationGraph LoadDefaultAnimationGraph()
        {
            try
            {
                var asset = Content.Load<AnimationGraph>(CharacterAnimGraphGuid);
                if (asset != null && asset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 已加载 (GUID): {asset.Path}");
                    return asset;
                }
                if (asset != null)
                {
                    asset.WaitForLoaded(30000.0);
                    if (asset.IsLoaded)
                    {
                        FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 等待后加载完成 (GUID): {asset.Path}");
                        return asset;
                    }
                }

                var pathAsset = Content.LoadAsync<AnimationGraph>(CharacterAnimGraphPath);
                if (pathAsset != null && pathAsset.WaitForLoaded(30000.0) && pathAsset.IsLoaded)
                {
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] AnimationGraph 已加载 (路径): {pathAsset.Path}");
                    return pathAsset;
                }
                FlaxEngine.Debug.LogWarning("[CharacterPreview3D] 默认 AnimationGraph 加载失败：GUID 与路径均未就绪");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 加载默认 AnimationGraph 失败: {ex.Message}");
            }
            return null;
        }

        // ===================================================================
        // 相机变换
        // =======================================================================

        /// <summary>
        /// 重新计算相机位置，保持角色居中，距离约 200 单位，FOV 45 度。
        /// 相机看向模型根节点原点，俯仰为 0（正面平视）。
        /// </summary>
        private void UpdateCameraTransform()
        {
            if (_cameraRoot == null || _camera == null)
                return;

            // 相机根节点置于模型前方（+Z 方向）并抬高，注视角色中心（约 90cm）而非脚底，
            // 确保约 180cm 角色完整入画（FOV 45、距离 250 时垂直视野约 207cm）。
            _cameraRoot.Position = new Vector3(0f, CameraHeight, CameraDistance);
            _cameraRoot.LookAt(new Vector3(0f, LookAtHeight, 0f));
        }

        // ===================================================================
        // 渲染
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 先绘制纯色背景占位（嵌入模式下跳过，由宿主容器提供底色）
            if (!_transparentBackground)
                Render2D.FillRectangle(bounds, InkWashTheme.BaseDefault);

            // 2. 绘制离屏 RenderTexture（若可用）
            if (_textureBrush != null && _renderTexture != null && _renderTexture.IsAllocated)
            {
                _textureBrush.Draw(bounds, Color.White);
            }

            // 3. 水墨微粒（少量环境粒子，漂浮于模型与背景之上）
            DrawInkMotes();

            // 4. 描边（金色，嵌入模式下跳过，由宿主容器提供边框）
            if (!_transparentBackground)
                Render2D.DrawRectangle(bounds, InkWashTheme.BorderGold, 1f);
        }

        // ===================================================================
        // 水墨微粒渲染
        // =======================================================================

        /// <summary>
        /// 初始化水墨微粒场（约 16 个，低密度不喧宾夺主）。
        /// 颜色取青玉 / 淡紫 / 淡金，与水墨青紫背景呼应。
        /// </summary>
        private void InitializeMotes()
        {
            const int count = 16;
            _motes = new InkMote[count];

            var tints = new[]
            {
                InkWashTheme.JadeBright,                    // 青玉
                new Color(0.70f, 0.62f, 0.92f, 1f),         // 淡紫
                new Color(0.90f, 0.86f, 0.74f, 1f),         // 淡金
            };

            for (int i = 0; i < count; i++)
            {
                _motes[i] = new InkMote
                {
                    XFrac = (float)_moteRandom.NextDouble(),
                    YFrac0 = (float)_moteRandom.NextDouble(),
                    RiseSpeed = 0.012f + (float)_moteRandom.NextDouble() * 0.028f, // 每秒 1.2%~4% 高度
                    SwayAmp = 5f + (float)_moteRandom.NextDouble() * 14f,
                    SwayFreq = 0.12f + (float)_moteRandom.NextDouble() * 0.30f,
                    Phase = (float)(_moteRandom.NextDouble() * Mathf.TwoPi),
                    Size = 1.5f + (float)_moteRandom.NextDouble() * 2.5f,
                    BaseAlpha = 0.20f + (float)_moteRandom.NextDouble() * 0.28f,
                    Tint = tints[i % tints.Length],
                };
            }
        }

        /// <summary>
        /// 绘制水墨微粒：缓慢上升并左右摇曳、透明度呼吸，
        /// 超出顶部后回绕到底部，形成自然的水墨氛围。
        /// </summary>
        private void DrawInkMotes()
        {
            if (_motes == null)
                InitializeMotes();

            float t = Time.GameTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                var m = _motes[i];

                // 垂直：随时间上升，归一化到 [0,1) 循环（超顶后回到底部）
                float yFrac = m.YFrac0 - m.RiseSpeed * t;
                yFrac -= Mathf.Floor(yFrac);

                // 水平：基准位置 + 正弦摇曳
                float x = m.XFrac * Width + Mathf.Sin(t * m.SwayFreq * Mathf.TwoPi + m.Phase) * m.SwayAmp;
                float y = yFrac * Height;

                // 透明度呼吸（缓慢脉动）
                float alpha = m.BaseAlpha * (0.55f + 0.45f * Mathf.Sin(t * 1.3f + m.Phase));
                if (alpha <= 0.02f)
                    continue;

                var core = new Color(m.Tint.R, m.Tint.G, m.Tint.B, alpha);
                InkRenderHelper.FillRadialGradient(new Float2(x, y), m.Size * 2.2f, core, Color.Transparent, 6);
            }
        }

        // ===================================================================
        // 鼠标交互（拖拽旋转）
        // =======================================================================

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (base.OnMouseDown(location, button))
                return true;

            if (button == MouseButton.Left)
            {
                _isDragging = true;
                _lastMousePosition = location;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (base.OnMouseUp(location, button))
                return true;

            if (_isDragging && button == MouseButton.Left)
            {
                _isDragging = false;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);

            if (!_isDragging)
                return;

            float deltaX = location.X - _lastMousePosition.X;
            _lastMousePosition = location;

            // 水平拖拽修改 AnimatedModel Actor 的 Orientation Yaw
            _modelYaw += deltaX * DragYawSensitivity;
            ApplyModelYaw();
        }

        /// <inheritdoc />
        public override void OnEndMouseCapture()
        {
            _isDragging = false;
            base.OnEndMouseCapture();
        }

        /// <summary>
        /// 将当前 <see cref="_modelYaw"/> 应用到 <see cref="_modelRoot"/>。
        /// </summary>
        private void ApplyModelYaw()
        {
            if (_modelRoot != null)
                _modelRoot.Orientation = Quaternion.Euler(0f, _modelYaw * Mathf.RadiansToDegrees, 0f);
        }

        // ===================================================================
        // 生命周期 & 清理
        // =======================================================================

        /// <inheritdoc />
        public override void OnDestroy()
        {
            try
            {
                if (_renderTask != null)
                {
                    _renderTask.Enabled = false;
                    _renderTask.CustomActors = null;
                    _renderTask.Camera = null;
                    FlaxEngine.Object.Destroy(_renderTask);
                    _renderTask = null;
                }

                _textureBrush = null;

                if (_renderTexture != null)
                {
                    _renderTexture.ReleaseGPU();
                    FlaxEngine.Object.Destroy(_renderTexture);
                    _renderTexture = null;
                }

                if (_modelRoot != null)
                {
                    FlaxEngine.Object.Destroy(_modelRoot);
                    _modelRoot = null;
                }

                if (_lightRoot != null)
                {
                    FlaxEngine.Object.Destroy(_lightRoot);
                    _lightRoot = null;
                }

                if (_cameraRoot != null)
                {
                    FlaxEngine.Object.Destroy(_cameraRoot);
                    _cameraRoot = null;
                }

                _animatedModel = null;
                _camera = null;
                _keyLight = null;
                _fillLight = null;
                _rimLight = null;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] OnDestroy 清理失败: {ex.Message}");
            }

            base.OnDestroy();
        }
    }
}
