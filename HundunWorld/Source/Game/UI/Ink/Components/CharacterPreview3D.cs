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

        /// <summary>垂直拖拽灵敏度（度/像素）——控制相机绕"角色左右轴"的俯仰查看角</summary>
        private const float DragPitchSensitivity = 0.12f;

        /// <summary>相机俯仰查看角下限（度）：禁止从下往上仰视（防穿帮/全身出框）</summary>
        private const float CameraPitchMinDeg = -18f;

        /// <summary>相机俯仰查看角上限（度）：限制俯视角度，保证全身完整入画</summary>
        private const float CameraPitchMaxDeg = 26f;

        /// <summary>
        /// 模型俯仰校正（度，绕 X 轴），与 <see cref="ModelYawCorrection"/> 组合使用。
        /// 实测（恒等姿态下截图）：当前资产原始导入为"上下颠倒的站立"（头朝 -Y、脚朝 +Y、面部朝向相机）。
        /// 经组合 Ry(180°)·Rx(180°) = 绕 Z 轴滚转 180°，头翻正且面部保持朝向玩家（相机 +Z 侧）。
        /// 若实测头部朝向异常，可单独微调本常数。
        /// </summary>
        private const float ModelPitchCorrection = 180f;

        /// <summary>
        /// 正面朝向校正（度，绕垂直轴）。
        /// 与 ModelPitchCorrection 组合：Rx(180°) 翻正后，Ry(180°) 将翻正过程中调整的
        /// 面部朝向旋回 +Z（相机侧）。组合结果 = 绕 Z 轴滚转 180°（头部翻正、面部不变）。
        /// </summary>
        private const float ModelYawCorrection = 180f;

        /// <summary>角色模型资产 GUID（来自 HundunWorldGame.cs）</summary>
        /// <remarks>
        /// 2026-09 修正：原写死的 ceded67f4bb2623f40b4dcb493b0d419 是**错误的 GUID**
        /// （Content/Character/Models/Animation Graph.flax 头部实际 ID 为
        /// ceded67f623f4bb2b4dcb44019d4b093），会导致 Content.Load 报
        /// "Invalid or missing asset (... AnimationGraph)"。现与资产真实 GUID 对齐。
        /// </remarks>
        private static readonly Guid CharacterAnimGraphGuid = new Guid("ceded67f623f4bb2b4dcb44019d4b093");

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
        // 背景天空（自然天空背景）
        // =======================================================================

        /// <summary>
        /// 场景内的 <see cref="Sky"/>（程序化大气 + 太阳）——被显式加入离屏渲染任务，
        /// 作为角色预览的自然天空背景。**归属场景，预览销毁时不可销毁它**。
        /// </summary>
        private Sky _sceneSky;

        /// <summary>场景天空是否已接入离屏渲染任务（避免重复接入）</summary>
        private bool _sceneSkyBound;

        /// <summary>
        /// UI 侧天空渐变的自定义回退开关：为 false 时，若场景内没有 Sky，
        /// 仍会绘制 UI 侧自然天空渐变，保证预览背景不是纯黑。
        /// </summary>
        public bool UseSkyBackdrop = true;

        /// <summary>天空渐变：天顶（上）</summary>
        private static readonly Color SkyZenith = new Color(0.27f, 0.45f, 0.76f, 1f);

        /// <summary>天空渐变：中段过渡</summary>
        private static readonly Color SkyMiddle = new Color(0.55f, 0.73f, 0.92f, 1f);

        /// <summary>天空渐变：地平线（下，接近天光白）</summary>
        private static readonly Color SkyHorizon = new Color(0.88f, 0.92f, 0.96f, 1f);

        /// <summary>天空渐变分段数（渲染为水平色带，48 段在任意面板高度下均无可见接缝）</summary>
        private const int SkyGradientBands = 48;

        /// <summary>天顶色→中段色的分界（0~1 的相对高度）</summary>
        private const float SkyMidStop = 0.62f;

        /// <summary>天空渐变背景的绘制色带缓存（尺寸变化时重建，避免每帧分配）</summary>
        private Rectangle[] _skyBandRects;
        private Color[] _skyBandColors;
        private float _skyBandHeight = -1f;
        private float _skyBandWidth = -1f;

        // ===================================================================
        // 交互状态
        // =======================================================================

        /// <summary>鼠标左键是否正在拖拽</summary>
        private bool _isDragging;

        /// <summary>上一帧鼠标位置（用于计算水平增量）</summary>
        private Float2 _lastMousePosition;

        /// <summary>最近一次 UI 鼠标移动事件时间（秒）——用于判定事件通道是否存活，决定轮询是否接管</summary>
        private float _lastEventMoveTime;

        /// <summary>诊断日志：拖拽启动是否已记录（仅一次）</summary>
        private bool _loggedDragStart;

        /// <summary>诊断日志：左右旋转生效是否已记录（仅一次）</summary>
        private bool _loggedYawApplied;

        /// <summary>骨骼基础姿态缓存（骨骼名 → 绑定姿态矩阵）：每帧在基准上叠加微小动态，避免累积漂移</summary>
        private readonly System.Collections.Generic.Dictionary<string, Matrix> _idleBasePose = new System.Collections.Generic.Dictionary<string, Matrix>();

        /// <summary>基础姿态是否已捕获</summary>
        private bool _idlePoseCaptured;

        /// <summary>诊断日志：待机动画已实际应用是否已记录（仅一次）</summary>
        private bool _loggedIdleApplied;

        /// <summary>待机动画目标骨骼（UE 人偶命名）；不存在的骨骼自动跳过</summary>
        private static readonly string[] IdleBoneNames = { "spine_01", "spine_02", "spine_03", "neck_01", "head", "upperarm_l", "upperarm_r" };

        /// <summary>当前模型 Yaw 旋转（弧度）</summary>
        private float _modelYaw;

        /// <summary>
        /// 相机俯仰查看角（度）——相机绕"角色左右轴"（过注视点的水平左右方向轴）旋转的偏移角，
        /// 限制在 [<see cref="CameraPitchMinDeg"/> , <see cref="CameraPitchMaxDeg"/>]，保证全身完整入画。
        /// </summary>
        private float _cameraPitchDeg;

        /// <summary>站立巡检计时（秒）——用于发现"模型躺倒"异常并自愈</summary>
        private float _uprightCheckTimer;

        /// <summary>是否已完成站立巡检（只自愈一次）</summary>
        private bool _uprightVerified;

        /// <summary>是否已完成自动取景（依据模型实际包围盒；未取景时使用原基线）</summary>
        private bool _framed;

        /// <summary>自动取景计时（秒）——等待绑定姿态生效后再测量包围盒</summary>
        private float _fitTimer;

        /// <summary>取景半径（取景中心到相机的距离）——自动取景前为原基线 251.8</summary>
        private float _fitDistance = Mathf.Sqrt((CameraHeight - LookAtHeight) * (CameraHeight - LookAtHeight) + CameraDistance * CameraDistance);

        /// <summary>取景中心高度（角色包围盒中心，脚底起算）——自动取景前为 LookAtHeight</summary>
        private float _fitCenterY = LookAtHeight;

        /// <summary>相机基线仰角（度）——略高于角色中心，轻微俯视更自然</summary>
        private const float CameraBaseElevationDeg = 6.8f;

        /// <summary>最近一次取景所用的模型资源（用于资源切换后自动重取景）</summary>
        private SkinnedModel _lastFramedModel;

        /// <summary>
        /// 模型局部居中偏移（水平居中 + 脚底贴地）。
        /// 关键：必须施加在**模型自身**（_animatedModel.Position）而非父节点——
        /// 父节点 _modelRoot 是 Yaw 自转的载体，其原点即旋转轴，须恰好穿过角色自身垂直中轴线。
        /// </summary>
        private Vector3 _modelLocalOffset;

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
            // 动画更新保障：离屏预览中模型不被主相机可见，必须开启"离屏也更新动画"，否则姿态不刷新
            _animatedModel.UpdateWhenOffscreen = true;
            // 程序化待机动画：订阅引擎动画求值完成事件（在其后叠加骨骼级微小动态，保证不被覆盖）
            _animatedModel.AnimationUpdated += ApplyProceduralIdle;
            // 姿态强制（扶正 + 正面朝向 + 居中偏移）：统一由 ApplyModelUpright 施加；
            // 与父节点 _modelRoot 上的转盘 Yaw 互不干扰（先扶正/朝向，再绕自身垂直轴旋转）。
            ApplyModelUpright();

            ApplyModelResources(null, null);

            // ── 灯光 Actor（三点光照，保证孤立渲染下模型可见） ──
            CreateLightingRig(scene);

            // ── 绑定渲染任务 ──
            _renderTask.Camera = _camera;
            _renderTask.ActorsSource = ActorsSources.CustomActors;
            // 关键修复：CustomActors 孤立渲染不包含场景原有灯光，
            // 必须将灯光组一并加入渲染列表，否则无光源导致渲染结果全黑
            _renderTask.CustomActors = new Actor[] { _modelRoot, _lightRoot };
            // ── 接入场景天空：CustomActors 孤立渲染同样不包含场景的 Sky/Skybox，
            //    必须把场景内的 Sky 显式加入渲染列表，离屏视图才会绘制自然天空背景 ──
            BindSceneSky(scene);
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

            // 光向约定：DirectionalLight 沿自身前向（+Z）照射；相机在 +Z 侧、角色正面朝 +Z，
            // 因此 yaw≈180° 的光照射角色正面，yaw≈0° 的光照背面。重排基准保证正面主体受光。

            // 主光：相机前上方偏左（yaw 160°、俯角 -45°）—— 正面主体受光，微暖白
            _keyLight = _lightRoot.AddChild<DirectionalLight>();
            _keyLight.Name = "PreviewKeyLight";
            _keyLight.Orientation = Quaternion.Euler(-45f, 160f, 0f);
            _keyLight.Brightness = 0.45f;
            _keyLight.Color = new Color(1f, 0.96f, 0.90f);

            // 补光：正面偏右、低角度（yaw 200°、俯角 -15°）—— 填补躯干/下部阴影，冷调天光
            _fillLight = _lightRoot.AddChild<DirectionalLight>();
            _fillLight.Name = "PreviewFillLight";
            _fillLight.Orientation = Quaternion.Euler(-15f, 200f, 0f);
            _fillLight.Brightness = 0.28f;
            _fillLight.Color = new Color(0.85f, 0.90f, 1f);

            // 轮廓光：背后上方（yaw 0°、俯角 -30°）—— 肩线/头部勾边，分离黑背景（弱，防过曝）
            _rimLight = _lightRoot.AddChild<DirectionalLight>();
            _rimLight.Name = "PreviewRimLight";
            _rimLight.Orientation = Quaternion.Euler(-30f, 0f, 0f);
            _rimLight.Brightness = 0.18f;
            _rimLight.Color = new Color(0.9f, 0.92f, 1f);

            FlaxEngine.Debug.Log("[CharacterPreview3D] 三点光照组已创建");
        }

        // ===================================================================
        // 背景天空
        // =======================================================================

        /// <summary>
        /// 把目标场景内的 <see cref="Sky"/> 显式接入离屏渲染任务，作为角色预览的**自然天空背景**。
        /// <para>
        /// 原因：<see cref="SceneRenderTask.ActorsSource"/> = <see cref="ActorsSources.CustomActors"/>
        /// 是"白名单"渲染，场景原有 Actor（含 Sky / Skybox）默认全部被排除，
        /// 因此离屏视图的背景原本只可能是清屏色（黑）。把场景 Sky 加进 <c>CustomActors</c> 后，
        /// 渲染器即可在该视图内绘制程序化大气与太阳，得到与游戏世界一致的自然天空。
        /// </para>
        /// <para>
        /// Sky Actor 属于场景（不是预览自建），因此这里**只引用、不创建、不销毁**——
        /// 避免污染主视图的天空来源（引擎每帧只取一个 Sky 渲染器）。
        /// </para>
        /// </summary>
        /// <param name="scene">Sky 所属场景</param>
        private void BindSceneSky(FlaxEngine.Scene scene)
        {
            if (_sceneSkyBound || _renderTask == null || scene == null)
                return;

            try
            {
                var sky = scene.FindActor<Sky>(false);
                if (sky == null)
                {
                    FlaxEngine.Debug.LogWarning(
                        "[CharacterPreview3D] 场景内未找到 Sky(Actor)，3D 层无法绘制引擎天空；" +
                        "将由 UI 侧自然天空渐变作为预览背景。");
                    return;
                }

                var current = _renderTask.CustomActors ?? new Actor[0];
                var merged = new Actor[current.Length + 1];
                Array.Copy(current, merged, current.Length);
                merged[merged.Length - 1] = sky;
                _renderTask.CustomActors = merged;

                _sceneSky = sky;
                _sceneSkyBound = true;
                FlaxEngine.Debug.Log(
                    $"[CharacterPreview3D] 已接入场景天空作为预览背景: {sky.Name}（渲染列表 Actor 数={merged.Length}）");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] 接入场景天空失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 绘制 UI 侧自然天空渐变背景（天顶蓝 → 中段淡蓝 → 地平线天光白，含一枚柔和日晕）。
        /// <para>
        /// 该层位于离屏 RenderTexture 之下：RenderTexture 已覆盖时不可见；
        /// 在渲染资源就绪前、或场景缺少 Sky 时，它就是预览面板的真实背景，
        /// 从而彻底消除"纯黑背景"。
        /// </para>
        /// </summary>
        /// <param name="bounds">控件本地矩形</param>
        private void DrawSkyBackdrop(Rectangle bounds)
        {
            if (!UseSkyBackdrop || bounds.Width <= 0f || bounds.Height <= 0f)
            {
                Render2D.FillRectangle(bounds, InkWashTheme.BaseDefault);
                return;
            }

            // 尺寸变化时重建色带缓存（每帧零分配）
            if (_skyBandRects == null || !Mathf.NearEqual(_skyBandWidth, bounds.Width) || !Mathf.NearEqual(_skyBandHeight, bounds.Height))
            {
                _skyBandRects = new Rectangle[SkyGradientBands];
                _skyBandColors = new Color[SkyGradientBands];
                _skyBandWidth = bounds.Width;
                _skyBandHeight = bounds.Height;

                for (int i = 0; i < SkyGradientBands; i++)
                {
                    float t = i / (float)SkyGradientBands;
                    float y0 = bounds.Height * t;
                    float y1 = bounds.Height * (i + 1) / SkyGradientBands;
                    _skyBandRects[i] = new Rectangle(bounds.X, bounds.Y + y0, bounds.Width, (y1 - y0) + 1f);
                    _skyBandColors[i] = t <= SkyMidStop
                        ? Color.Lerp(SkyZenith, SkyMiddle, t / SkyMidStop)
                        : Color.Lerp(SkyMiddle, SkyHorizon, (t - SkyMidStop) / (1f - SkyMidStop));
                }
            }

            for (int i = 0; i < SkyGradientBands; i++)
                Render2D.FillRectangle(_skyBandRects[i], _skyBandColors[i]);

            // 日晕：与场景 Sky 的太阳同侧（主光位于左上前方），柔化天际亮度过渡
            InkRenderHelper.FillRadialGradient(
                new Float2(bounds.X + bounds.Width * 0.32f, bounds.Y + bounds.Height * 0.08f),
                bounds.Width * 0.42f,
                new Color(1f, 0.99f, 0.94f, 0.30f),
                Color.Transparent,
                12);
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
        /// 安全应用 SkinnedModel 到 <see cref="_animatedModel"/>。
        /// 若传入为 null，则回退到默认模型；预览视图**不启用动画图**（仅展示绑定姿态站立）。
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
            // 预览视图：不启用动画图——只展示骨骼绑定姿态（自然站立、肢体完整），
            // 避免动画根姿态/缺失动画导致肢体错位、部件飞散与包围盒失真（2026-09 视觉修复）。
            animGraph = null;

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
        /// 重新计算相机位置：围绕角色取景中心（<see cref="_fitCenterY"/>）按 <see cref="_fitDistance"/> 半径
        /// 在 <see cref="_cameraPitchDeg"/> 限制范围内俯仰环视——角色全身完整入画、比例协调。
        /// 取景参数由 <see cref="TryAutoFrame"/> 依据模型实际包围盒计算（默认值等价于原基线）。
        /// </summary>
        private void UpdateCameraTransform()
        {
            if (_cameraRoot == null || _camera == null)
                return;

            float elevation = Mathf.DegreesToRadians * (CameraBaseElevationDeg + _cameraPitchDeg);
            _cameraRoot.Position = new Vector3(
                0f,
                _fitCenterY + _fitDistance * Mathf.Sin(elevation),
                _fitDistance * Mathf.Cos(elevation));
            _cameraRoot.LookAt(new Vector3(0f, _fitCenterY, 0f));
        }

        // ===================================================================
        // 渲染
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 先绘制自然天空背景（嵌入模式下跳过，由宿主容器提供底色）
            if (!_transparentBackground)
                DrawSkyBackdrop(bounds);

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

        /// <summary>
        /// 应用拖拽位移（UI 事件与输入轮询共用，同帧自动去重）：
        /// 水平拖拽 = 角色绕自身垂直轴左右旋转；垂直拖拽 = 相机小幅俯仰查看。
        /// </summary>
        private void ApplyDragMove(Float2 local)
        {
            float deltaX = local.X - _lastMousePosition.X;
            float deltaY = local.Y - _lastMousePosition.Y;
            _lastMousePosition = local;

            // 水平拖拽：角色绕自身垂直（左右对称）轴自转——仅角色旋转、保持站立，
            // 相机固定于正面框架，任意角度均完整显示全身。
            if (Mathf.Abs(deltaX) > 0.01f)
            {
                _modelYaw += deltaX * DragYawSensitivity;
                _modelYaw = WrapAngle(_modelYaw);
                ApplyModelYaw();
                if (!_loggedYawApplied)
                {
                    _loggedYawApplied = true;
                    FlaxEngine.Debug.Log($"[CharacterPreview3D] 左右旋转生效：dx={deltaX:F1}px, yaw={_modelYaw * Mathf.RadiansToDegrees:F1}°");
                }
            }

            // 垂直拖拽：相机绕"角色身体左右轴"（过注视点的水平轴）俯仰查看，
            // 角度限制在 [CameraPitchMinDeg, CameraPitchMaxDeg]，确保全身完整可见（不仰视、不俯穿）。
            if (Mathf.Abs(deltaY) > 0.01f)
            {
                _cameraPitchDeg = Mathf.Clamp(
                    _cameraPitchDeg - deltaY * DragPitchSensitivity,
                    CameraPitchMinDeg, CameraPitchMaxDeg);
                UpdateCameraTransform();
            }
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);

            if (button == MouseButton.Left)
            {
                _isDragging = true;
                _lastMousePosition = location;
                _lastEventMoveTime = Time.GameTime;
                if (!_loggedDragStart)
                {
                    _loggedDragStart = true;
                    FlaxEngine.Debug.Log("[CharacterPreview3D] 拖拽开始（UI 事件通道）");
                }
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            base.OnMouseUp(location, button);

            if (button == MouseButton.Left)
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

            _lastEventMoveTime = Time.GameTime; // 标记事件通道存活

            if (!_isDragging)
                return;

            ApplyDragMove(location);
        }

        /// <summary>将弧度角包裹到 (-π, π]，避免无限累积导致浮点精度损失。</summary>
        private static float WrapAngle(float radians)
        {
            while (radians > Mathf.Pi)
                radians -= Mathf.TwoPi;
            while (radians < -Mathf.Pi)
                radians += Mathf.TwoPi;
            return radians;
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

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 1. 拖拽输入（双通道保障左右旋转可用）：
            //    · UI 事件通道为主动力（OnMouseDown/Move/Up）；
            //    · 若事件通道失效（连续 0.3s 无 OnMouseMove 事件）且左键按住，则输入轮询接管，
            //      确保"左右拖拽 = 左右旋转"在任何情况下都能工作。
            bool leftDown = Input.GetMouseButton(MouseButton.Left);
            if (!leftDown)
            {
                _isDragging = false;
            }
            else
            {
                Float2 local = PointFromScreen(Input.MousePosition);
                bool overViewport = local.X >= 0f && local.Y >= 0f && local.X <= Width && local.Y <= Height;

                if (!_isDragging && overViewport)
                {
                    // 轮询兵底启动：事件通道失效时也能进入拖拽
                    _isDragging = true;
                    _lastMousePosition = local;
                    _lastEventMoveTime = Time.GameTime - 10f; // 允许轮询立即接管
                }
                else if (_isDragging)
                {
                    bool eventsAlive = Time.GameTime - _lastEventMoveTime < 0.3f;
                    if (!eventsAlive)
                        ApplyDragMove(local);
                }
            }

            // 2. 姿态强制（每帧重申）：角色必须保持站立——
            //    仅允许绕自身垂直（左右对称）轴的 Yaw 自转；扶正与自转姿态在此强制生效，
            //    任何外部（动画根运动/残留拖拽/资产轴向）造成的倾倒都会被立即纠正为站立。
            ApplyModelUpright();

            // 2b. 程序化待机动画（每帧驱动）：关键——无动画图时 AnimationUpdated 事件不会触发，
            //     必须在此直接驱动骨骼（基于捕获的基准姿态 × 微小动态，无漂移）。
            ApplyProceduralIdle();

            // 3. 站立巡检自愈：若动画状态的根姿态把角色放倒（高度小于前后厚度即判定躺倒），
            //    禁用动画图回退到站立绑定姿态（工装姿态仍为站立 T-pose），并告警一次。
            if (!_uprightVerified && _animatedModel != null)
            {
                _uprightCheckTimer += deltaTime;
                if (_uprightCheckTimer >= 1f)
                {
                    _uprightVerified = true;
                    if (_animatedModel.AnimationGraph != null)
                    {
                        var box = _animatedModel.Box;
                        if (box.Size.Y < box.Size.Z)
                        {
                            FlaxEngine.Debug.LogWarning(
                                $"[CharacterPreview3D] 检测到角色已躺倒（Y={box.Size.Y:F2} < Z={box.Size.Z:F2}），" +
                                "禁用动画图以回退站立姿态（保持全身可见与站立展示）。");
                            _animatedModel.AnimationGraph = null;
                        }
                    }
                }
            }

            // 4. 自动取景：模型就绪后按实际包围盒适配相机（全身完整、比例协调、永不裁切）；
            //    模型资源切换时自动重取景。
            if (_framed && _animatedModel != null && !ReferenceEquals(_animatedModel.SkinnedModel, _lastFramedModel))
            {
                _framed = false;
                _fitTimer = 0f;
            }
            TryAutoFrame(deltaTime);
        }

        /// <summary>
        /// 姿态强制：模型按常量校正组合（当前资产为恒等——原始导入即站立且正面朝玩家）
        /// + 居中偏移（施加在模型自身，保证旋转轴穿过自身中轴）
        /// + 自身垂直轴 Yaw 自转（左右拖动 = 左右旋转查看）。每帧重申，确保任意时刻姿态稳定、轴心正确。
        /// </summary>
        private void ApplyModelUpright()
        {
            if (_animatedModel != null)
            {
                // 校正组合：绕 X 轴 ModelPitchCorrection（扶正，当前 0）后，
                // 再绕垂直轴 ModelYawCorrection（正面朝向，当前 0）——两者均为常数，可快速校准。
                var upright = Quaternion.RotationY(Mathf.DegreesToRadians * ModelYawCorrection)
                              * Quaternion.RotationX(Mathf.DegreesToRadians * ModelPitchCorrection);
                _animatedModel.Orientation = upright;
                // 居中偏移施加在模型自身：父节点保持在原点 → Yaw 旋转轴 = 角色自身垂直中轴线（原地自转）。
                _animatedModel.Position = _modelLocalOffset;
            }
            ApplyModelYaw();
        }

        /// <summary>
        /// 程序化待机动画（基础角色动画）：在绑定姿态基础上叠加极小幅度的呼吸/摆动，
        /// 通过骨骼级 SetNodeTransformation 直接驱动（不依赖动画图资产）；
        /// 在 AnimationUpdated 事件中应用（引擎动画求值完成后），确保不被覆盖。
        /// </summary>
        private void ApplyProceduralIdle()
        {
            if (!_idlePoseCaptured || _animatedModel == null)
                return;

            float t = Time.GameTime;
            // 呼吸（脊椎三节微起仄）、头部轻摆、双臂微晃（左右反相）
            ApplyBoneIdle("spine_01", t, 0.35f, 0.60f, 0f);
            ApplyBoneIdle("spine_02", t, 0.35f, 1.10f, 0f);
            ApplyBoneIdle("spine_03", t, 0.35f, 0.70f, Mathf.Pi * 0.3f);
            ApplyBoneIdle("neck_01", t, 0.22f, 0.90f, Mathf.Pi * 0.5f);
            ApplyBoneIdle("head", t, 0.22f, 1.40f, Mathf.Pi * 1.7f);
            ApplyBoneIdle("upperarm_l", t, 0.30f, 2.20f, 0f);
            ApplyBoneIdle("upperarm_r", t, 0.30f, 2.20f, Mathf.Pi);

            if (!_loggedIdleApplied)
            {
                _loggedIdleApplied = true;
                FlaxEngine.Debug.Log($"[CharacterPreview3D] 待机动画已应用（骨骼 {_idleBasePose.Count} 根）");
            }
        }

        /// <summary>对单根骨骼应用微小摆动：基准矩阵 × 局部小旋转（绕骨骼自身原点，不会位移）。</summary>
        private void ApplyBoneIdle(string boneName, float t, float freq, float ampDeg, float phase)
        {
            if (!_idleBasePose.TryGetValue(boneName, out var baseM))
                return;

            float angle = Mathf.Sin(t * freq * Mathf.TwoPi + phase) * ampDeg;
            var delta = Quaternion.Euler(angle * 0.5f, angle, angle * 0.35f);
            _animatedModel.SetNodeTransformation(boneName, baseM * Matrix.RotationQuaternion(delta), false);
        }

        /// <summary>捕获基础骨骼姿态（模型就绪且绑定姿态稳定后执行一次；使待机动画每帧基于同一基准无漂移）。</summary>
        private void CaptureIdleBasePose()
        {
            if (_animatedModel == null || _animatedModel.SkinnedModel == null)
                return;

            _idlePoseCaptured = false;
            _idleBasePose.Clear();
            foreach (var name in IdleBoneNames)
            {
                _animatedModel.GetNodeTransformation(name, out Matrix m, false);
                // 全零矩阵 = 骨骼不存在（跳过）
                if (m.M11 != 0f || m.M44 != 0f)
                    _idleBasePose[name] = m;
            }
            _idlePoseCaptured = true;
            FlaxEngine.Debug.Log($"[CharacterPreview3D] 待机动画骨骼捕获：{_idleBasePose.Count}/{IdleBoneNames.Length}");
        }

        /// <summary>
        /// 自动取景：模型就绪后，依据实际包围盒：
        /// ① 水平居中 + 脚底贴地（使 Yaw 旋转轴落在角色自身垂直中轴线上）；
        /// ② 垂直/水平双向适配计算取景距离（含 18% 边距）——全身完整入画、比例协调、永不裁切；
        /// ③ 同步近/远裁剪面（适配从厘米到米级的不同资产尺度）。
        /// 只在模型就绪后执行一次；资源切换时由 Update 重置后重取景。
        /// </summary>
        private void TryAutoFrame(float deltaTime)
        {
            if (_framed || _animatedModel == null || _animatedModel.SkinnedModel == null || !_animatedModel.SkinnedModel.IsLoaded)
                return;

            _fitTimer += deltaTime;
            if (_fitTimer < 0.5f)
                return; // 等待绑定姿态（扶正/动画初始状态）生效后再测量

            var box = _animatedModel.Box;
            float h = box.Size.Y;
            if (h <= 0.001f)
                return; // 包围盒尚未就绪，下帧再试

            // ① 水平居中 + 脚底贴地：偏移施加在**模型自身**（_animatedModel.Position）上，
            //    父节点（Yaw 旋转载体）保持在原点——旋转轴恰好穿过角色自身垂直中轴（原地自转、不绕摆）。
            _modelRoot.Position = Vector3.Zero;
            _modelLocalOffset = new Vector3(-box.Center.X, -box.Minimum.Y, -box.Center.Z);
            ApplyModelUpright();

            // ② 双向适配取景距离（垂直 FOV，水平按控件宽高比换算；留 18% 边距）
            float fovRad = Mathf.DegreesToRadians * CameraFieldOfView;
            float halfTan = Mathf.Max(Mathf.Tan(fovRad * 0.5f), 0.01f);
            float aspect = Width > 4f && Height > 4f ? Width / Height : 1f;
            float horizontalHalf = Mathf.Max(box.Size.X, box.Size.Z) * 0.5f;
            float distV = (h * 0.5f) / halfTan;
            float distH = horizontalHalf / (halfTan * aspect);
            _fitDistance = Mathf.Max(distV, distH) * 1.18f;
            _fitCenterY = h * 0.5f;

            // ③ 裁剪面随取景距离自适应（防近平面裁切/远平面丢失）
            if (_camera != null)
            {
                _camera.NearPlane = Mathf.Max(0.01f, _fitDistance * 0.005f);
                _camera.FarPlane = _fitDistance * 20f;
            }

            _framed = true;
            _lastFramedModel = _animatedModel.SkinnedModel;
            UpdateCameraTransform();
            CaptureIdleBasePose(); // 基础姿态稳定后捕获（供程序化待机动画使用）

            FlaxEngine.Debug.Log(
                $"[CharacterPreview3D] 自动取景完成：身高={h:F2}, 宽={box.Size.X:F2}, 深={box.Size.Z:F2}, " +
                $"取景距离={_fitDistance:F1}, 视口={Width:F0}x{Height:F0}");
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

                if (_animatedModel != null)
                    _animatedModel.AnimationUpdated -= ApplyProceduralIdle;

                _animatedModel = null;
                _camera = null;
                _keyLight = null;
                _fillLight = null;
                _rimLight = null;
                // 场景天空仅解除引用：Sky Actor 归属场景，绝不可在此销毁
                _sceneSky = null;
                _sceneSkyBound = false;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterPreview3D] OnDestroy 清理失败: {ex.Message}");
            }

            base.OnDestroy();
        }
    }
}
